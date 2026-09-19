package ai

import (
	"context"
	"errors"
	"fmt"
	"sync"
	"time"

	"github.com/kataage/lumine/internal/domain"
)

var (
	ErrCapabilityDisabled = errors.New("AI capability is disabled")
	ErrEngineNotRegistered = errors.New("AI engine is not registered")
	ErrRuntimeNotLoaded = errors.New("AI runtime is not loaded")
)

type runtimeSession struct {
	opMu        sync.Mutex
	closing     bool
	cleanupOnly bool
	engine      Engine
	model    InstalledModel
	options  LoadOptions
	state    RuntimeState
	lastErr  string
}

type Manager struct {
	store      *ModelStore
	settings       SettingsProvider
	modelActivated func(domain.AICapability, InstalledModel) error

	mu          sync.Mutex
	lifecycleMu sync.Mutex
	factories   map[string]EngineFactory
	sessions    map[domain.AICapability]*runtimeSession
}

func NewManager(root string, settings SettingsProvider) *Manager {
	return &Manager{
		store:     NewModelStore(root),
		settings:  settings,
		factories: make(map[string]EngineFactory),
		sessions:  make(map[domain.AICapability]*runtimeSession),
	}
}

func (m *Manager) Store() *ModelStore {
	return m.store
}

func (m *Manager) SetModelActivatedHook(hook func(domain.AICapability, InstalledModel) error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.modelActivated = hook
}

func (m *Manager) RegisterEngine(engineID string, factory EngineFactory) error {
	if !safeComponentPattern.MatchString(engineID) {
		return fmt.Errorf("invalid engine id %q", engineID)
	}
	if factory == nil {
		return errors.New("engine factory is required")
	}

	m.mu.Lock()
	defer m.mu.Unlock()
	m.factories[engineID] = factory
	return nil
}

func (m *Manager) InstallModel(ctx context.Context, manifest ModelManifest, progress ProgressFunc) (InstalledModel, error) {
	if err := lockContextMutex(ctx, &m.lifecycleMu); err != nil {
		return InstalledModel{}, fmt.Errorf("wait for AI lifecycle: %w", err)
	}
	defer m.lifecycleMu.Unlock()
	if m.modelInUse(manifest.ID, manifest.Version) {
		return InstalledModel{}, fmt.Errorf("model %s@%s is currently loaded", manifest.ID, manifest.Version)
	}
	return m.store.Install(ctx, manifest, progress)
}

func (m *Manager) UpdateModel(ctx context.Context, manifest ModelManifest, progress ProgressFunc) (InstalledModel, error) {
	if err := lockContextMutex(ctx, &m.lifecycleMu); err != nil {
		return InstalledModel{}, fmt.Errorf("wait for AI lifecycle: %w", err)
	}
	defer m.lifecycleMu.Unlock()
	if m.modelInUse(manifest.ID, manifest.Version) {
		return InstalledModel{}, fmt.Errorf("model %s@%s is currently loaded", manifest.ID, manifest.Version)
	}
	return m.store.Update(ctx, manifest, progress)
}

func (m *Manager) VerifyModel(modelID, version string) (InstalledModel, error) {
	return m.store.Verify(modelID, version)
}

func (m *Manager) ListInstalledModels() ([]InstalledModelInfo, error) {
	return m.store.List()
}

func (m *Manager) RemoveModel(modelID, version string) error {
	return m.RemoveModelContext(context.Background(), modelID, version)
}

func (m *Manager) RemoveModelContext(ctx context.Context, modelID, version string) error {
	if err := lockContextMutex(ctx, &m.lifecycleMu); err != nil {
		return fmt.Errorf("wait for AI lifecycle: %w", err)
	}
	defer m.lifecycleMu.Unlock()
	m.mu.Lock()
	for capability, session := range m.sessions {
		if session.model.Manifest.ID == modelID && session.model.Manifest.Version == version {
			m.mu.Unlock()
			return fmt.Errorf("model %s@%s is loaded by %s", modelID, version, capability)
		}
	}
	m.mu.Unlock()
	return m.store.Remove(modelID, version)
}

func (m *Manager) Load(
	ctx context.Context,
	capability domain.AICapability,
	modelID string,
	version string,
	options LoadOptions,
) error {
	// Runtime lifecycle changes are rare but expensive. Serialize them so
	// startup restore, Settings actions, and model switching cannot construct
	// two engines for the same capability at once or race DLL extraction. The
	// wait itself must honor ctx so shutdown cannot hang before reaching an
	// engine-level timeout.
	if err := lockContextMutex(ctx, &m.lifecycleMu); err != nil {
		return fmt.Errorf("wait for AI lifecycle: %w", err)
	}
	defer m.lifecycleMu.Unlock()

	settings, err := m.currentSettings()
	if err != nil {
		return err
	}
	if !settings.CapabilityEnabled(capability) {
		return ErrCapabilityDisabled
	}
	options.AllowGPU = options.AllowGPU && settings.GPUAcceleration

	model, err := m.store.Verify(modelID, version)
	if err != nil {
		return err
	}

	m.mu.Lock()
	factory := m.factories[model.Manifest.Engine]
	current := m.sessions[capability]
	if current != nil &&
		current.model.Manifest.ID == model.Manifest.ID &&
		current.model.Manifest.Version == model.Manifest.Version &&
		current.model.Manifest.Engine == model.Manifest.Engine &&
		current.options.AllowGPU == options.AllowGPU &&
		(current.state == RuntimeStateReady || current.state == RuntimeStateRunning) {
		m.mu.Unlock()
		return nil
	}
	m.mu.Unlock()
	if factory == nil {
		return fmt.Errorf("%w: %s", ErrEngineNotRegistered, model.Manifest.Engine)
	}

	if current != nil {
		if err := m.unloadLocked(ctx, capability); err != nil {
			return fmt.Errorf("unload current runtime: %w", err)
		}
	}

	engine := factory()
	if engine == nil {
		return errors.New("engine factory returned nil")
	}
	if engine.ID() != model.Manifest.Engine {
		return fmt.Errorf("engine factory returned %q for manifest engine %q", engine.ID(), model.Manifest.Engine)
	}

	session := &runtimeSession{
		engine:  engine,
		model:   model,
		options: options,
		state:   RuntimeStateReady,
	}

	// Do not publish a healthy session until Load succeeds. If cleanup of a
	// partially loaded engine fails, however, retain it as an error session so
	// later shutdown/unload can retry instead of orphaning a runtime.
	if err := engine.Load(ctx, model, options); err != nil {
		loadErr := fmt.Errorf("load engine %s: %w", engine.ID(), err)
		if cleanupErr := unloadEngineBounded(engine); cleanupErr != nil {
			session.state = RuntimeStateError
			session.cleanupOnly = true
			session.lastErr = errors.Join(loadErr, cleanupErr).Error()
			m.mu.Lock()
			m.sessions[capability] = session
			m.mu.Unlock()
			return errors.Join(loadErr, fmt.Errorf("cleanup failed load: %w", cleanupErr))
		}
		return loadErr
	}

	m.mu.Lock()
	hook := m.modelActivated
	m.mu.Unlock()
	if hook != nil {
		if err := hook(capability, model); err != nil {
			activateErr := fmt.Errorf("activate model metadata for %s: %w", capability, err)
			if cleanupErr := unloadEngineBounded(engine); cleanupErr != nil {
				session.state = RuntimeStateError
				session.cleanupOnly = true
				session.lastErr = errors.Join(activateErr, cleanupErr).Error()
				m.mu.Lock()
				m.sessions[capability] = session
				m.mu.Unlock()
				return errors.Join(activateErr, fmt.Errorf("cleanup failed activation: %w", cleanupErr))
			}
			return activateErr
		}
	}

	m.mu.Lock()
	m.sessions[capability] = session
	m.mu.Unlock()
	return nil
}

func lockContextMutex(ctx context.Context, mu *sync.Mutex) error {
	if ctx == nil {
		ctx = context.Background()
	}
	if mu.TryLock() {
		return nil
	}

	ticker := time.NewTicker(10 * time.Millisecond)
	defer ticker.Stop()
	for {
		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-ticker.C:
			if mu.TryLock() {
				return nil
			}
		}
	}
}

func lockRuntimeSession(ctx context.Context, session *runtimeSession) error {
	return lockContextMutex(ctx, &session.opMu)
}

func unloadEngineBounded(engine Engine) error {
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	return engine.Unload(ctx)
}

func (m *Manager) Infer(
	ctx context.Context,
	capability domain.AICapability,
	request InferenceRequest,
) (InferenceResponse, error) {
	allowed, err := m.capabilityAllowed(capability)
	if err != nil {
		return InferenceResponse{}, err
	}
	if !allowed {
		_ = m.Unload(ctx, capability)
		return InferenceResponse{}, ErrCapabilityDisabled
	}

	// Never wait for the per-runtime operation lock while holding Manager.mu.
	// Doing so deadlocks when the currently-running inference needs Manager.mu
	// to publish its result before it can release opMu.
	m.mu.Lock()
	session := m.sessions[capability]
	if session == nil || session.closing || session.cleanupOnly {
		m.mu.Unlock()
		return InferenceResponse{}, ErrRuntimeNotLoaded
	}
	m.mu.Unlock()

	if err := lockRuntimeSession(ctx, session); err != nil {
		return InferenceResponse{}, fmt.Errorf("wait for %s runtime: %w", capability, err)
	}
	defer session.opMu.Unlock()

	// Unload may have won the race between taking the session pointer and
	// acquiring opMu. Revalidate ownership before invoking the engine.
	m.mu.Lock()
	if current := m.sessions[capability]; current != session || session.closing || session.cleanupOnly {
		m.mu.Unlock()
		return InferenceResponse{}, ErrRuntimeNotLoaded
	}
	session.state = RuntimeStateRunning
	m.mu.Unlock()

	response, inferErr := session.engine.Infer(ctx, request)

	m.mu.Lock()
	if current := m.sessions[capability]; current == session && !session.closing {
		switch {
		case inferErr == nil:
			session.state = RuntimeStateReady
			session.lastErr = ""
		case errors.Is(inferErr, context.Canceled), errors.Is(inferErr, context.DeadlineExceeded):
			// Caller cancellation is normal control flow (search replacement,
			// foreground priority, shutdown) and must not poison the runtime.
			session.state = RuntimeStateReady
			session.lastErr = ""
		default:
			session.state = RuntimeStateError
			session.lastErr = inferErr.Error()
		}
	}
	m.mu.Unlock()

	if inferErr != nil {
		return InferenceResponse{}, inferErr
	}
	return response, nil
}

func (m *Manager) Unload(ctx context.Context, capability domain.AICapability) error {
	if err := lockContextMutex(ctx, &m.lifecycleMu); err != nil {
		return fmt.Errorf("wait for AI lifecycle: %w", err)
	}
	defer m.lifecycleMu.Unlock()
	return m.unloadLocked(ctx, capability)
}

func (m *Manager) unloadLocked(ctx context.Context, capability domain.AICapability) error {
	m.mu.Lock()
	session := m.sessions[capability]
	if session == nil {
		m.mu.Unlock()
		return nil
	}
	if session.closing {
		m.mu.Unlock()
		return nil
	}
	session.closing = true
	m.mu.Unlock()

	if err := lockRuntimeSession(ctx, session); err != nil {
		m.mu.Lock()
		if current := m.sessions[capability]; current == session {
			session.closing = false
		}
		m.mu.Unlock()
		return fmt.Errorf("wait to unload engine %s: %w", session.engine.ID(), err)
	}
	defer session.opMu.Unlock()

	if err := session.engine.Unload(ctx); err != nil {
		unloadErr := fmt.Errorf("unload engine %s: %w", session.engine.ID(), err)
		m.mu.Lock()
		if current := m.sessions[capability]; current == session {
			session.closing = false
			session.cleanupOnly = true
			session.state = RuntimeStateError
			session.lastErr = unloadErr.Error()
		}
		m.mu.Unlock()
		return unloadErr
	}

	m.mu.Lock()
	if current := m.sessions[capability]; current == session {
		delete(m.sessions, capability)
	}
	m.mu.Unlock()
	return nil
}

func (m *Manager) ApplySettings(ctx context.Context, settings domain.AISettings) error {
	m.mu.Lock()
	var toUnload []domain.AICapability
	for capability, session := range m.sessions {
		if !settings.CapabilityEnabled(capability) || (session.options.AllowGPU && !settings.GPUAcceleration) {
			toUnload = append(toUnload, capability)
		}
	}
	m.mu.Unlock()

	var combined error
	for _, capability := range toUnload {
		if err := m.Unload(ctx, capability); err != nil {
			combined = errors.Join(combined, err)
		}
	}
	return combined
}

func (m *Manager) Status(capability domain.AICapability) RuntimeStatus {
	allowed, err := m.capabilityAllowed(capability)
	if err != nil {
		return RuntimeStatus{Capability: capability, State: RuntimeStateError, Error: err.Error()}
	}
	if !allowed {
		return RuntimeStatus{Capability: capability, State: RuntimeStateDisabled}
	}

	m.mu.Lock()
	defer m.mu.Unlock()
	session := m.sessions[capability]
	if session == nil {
		return RuntimeStatus{Capability: capability, State: RuntimeStateModelNotInstalled}
	}
	return RuntimeStatus{
		Capability: capability,
		State:      session.state,
		ModelID:    session.model.Manifest.ID,
		Version:    session.model.Manifest.Version,
		Engine:     session.model.Manifest.Engine,
		Error:      session.lastErr,
	}
}

func (m *Manager) Close(ctx context.Context) error {
	m.mu.Lock()
	capabilities := make([]domain.AICapability, 0, len(m.sessions))
	for capability := range m.sessions {
		capabilities = append(capabilities, capability)
	}
	m.mu.Unlock()

	var combined error
	for _, capability := range capabilities {
		if err := m.Unload(ctx, capability); err != nil {
			combined = errors.Join(combined, err)
		}
	}
	return combined
}

func (m *Manager) capabilityAllowed(capability domain.AICapability) (bool, error) {
	settings, err := m.currentSettings()
	if err != nil {
		return false, err
	}
	return settings.CapabilityEnabled(capability), nil
}

func (m *Manager) modelInUse(modelID, version string) bool {
	m.mu.Lock()
	defer m.mu.Unlock()
	for _, session := range m.sessions {
		if session.model.Manifest.ID == modelID && session.model.Manifest.Version == version {
			return true
		}
	}
	return false
}

func (m *Manager) currentSettings() (domain.AISettings, error) {
	if m.settings == nil {
		return domain.DefaultAISettings(), nil
	}
	return m.settings()
}
