package ai

import (
	"context"
	"net/http"
	"net/http/httptest"
	"sync"
	"testing"
	"time"

	"github.com/kataage/lumine/internal/domain"
)

type dummyEngine struct {
	mu       sync.Mutex
	loaded   bool
	unloaded bool
	options  LoadOptions
}

func (e *dummyEngine) ID() string { return "dummy" }

func (e *dummyEngine) Load(_ context.Context, _ InstalledModel, options LoadOptions) error {
	e.mu.Lock()
	defer e.mu.Unlock()
	e.loaded = true
	e.options = options
	return nil
}

func (e *dummyEngine) Infer(_ context.Context, request InferenceRequest) (InferenceResponse, error) {
	e.mu.Lock()
	defer e.mu.Unlock()
	if !e.loaded || e.unloaded {
		return InferenceResponse{}, ErrRuntimeNotLoaded
	}
	return InferenceResponse{Payload: request.Payload}, nil
}

func (e *dummyEngine) Unload(_ context.Context) error {
	e.mu.Lock()
	defer e.mu.Unlock()
	e.unloaded = true
	return nil
}

func TestManagerFullLifecycleAndSettingsGate(t *testing.T) {
	data := []byte("dummy runtime model")
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write(data)
	}))
	defer server.Close()

	settings := domain.AISettings{
		Enabled:         true,
		SemanticSearch:  true,
		GPUAcceleration: false,
	}
	manager := NewManager(t.TempDir(), func() (domain.AISettings, error) {
		return settings, nil
	})

	var lastEngine *dummyEngine
	if err := manager.RegisterEngine("dummy", func() Engine {
		lastEngine = &dummyEngine{}
		return lastEngine
	}); err != nil {
		t.Fatalf("RegisterEngine: %v", err)
	}

	manifest := testManifest(server.URL, data)
	if _, err := manager.InstallModel(context.Background(), manifest, nil); err != nil {
		t.Fatalf("InstallModel: %v", err)
	}
	if _, err := manager.VerifyModel(manifest.ID, manifest.Version); err != nil {
		t.Fatalf("VerifyModel: %v", err)
	}

	if err := manager.Load(
		context.Background(),
		domain.AICapabilitySemanticSearch,
		manifest.ID,
		manifest.Version,
		LoadOptions{AllowGPU: true},
	); err != nil {
		t.Fatalf("Load: %v", err)
	}
	if lastEngine == nil || !lastEngine.loaded {
		t.Fatal("dummy engine was not loaded")
	}
	if lastEngine.options.AllowGPU {
		t.Fatal("runtime must force CPU-only when GPU acceleration setting is off")
	}

	status := manager.Status(domain.AICapabilitySemanticSearch)
	if status.State != RuntimeStateReady {
		t.Fatalf("status = %s, want ready", status.State)
	}

	request := InferenceRequest{
		Operation: "echo",
		Payload:   map[string]any{"prompt": "hello"},
	}
	response, err := manager.Infer(context.Background(), domain.AICapabilitySemanticSearch, request)
	if err != nil {
		t.Fatalf("Infer: %v", err)
	}
	if response.Payload["prompt"] != "hello" {
		t.Fatalf("unexpected inference response: %+v", response.Payload)
	}

	if err := manager.RemoveModel(manifest.ID, manifest.Version); err == nil {
		t.Fatal("loaded model must not be removable")
	}

	if err := manager.Unload(context.Background(), domain.AICapabilitySemanticSearch); err != nil {
		t.Fatalf("Unload: %v", err)
	}
	if !lastEngine.unloaded {
		t.Fatal("explicit unload must tear down the active runtime")
	}

	// Reload once more so the settings transition itself is also proven to
	// unload an active runtime.
	if err := manager.Load(
		context.Background(),
		domain.AICapabilitySemanticSearch,
		manifest.ID,
		manifest.Version,
		LoadOptions{},
	); err != nil {
		t.Fatalf("reload before settings change: %v", err)
	}
	reloadedEngine := lastEngine

	settings.Enabled = false
	if err := manager.ApplySettings(context.Background(), settings); err != nil {
		t.Fatalf("ApplySettings: %v", err)
	}
	if !reloadedEngine.unloaded {
		t.Fatal("disabling AI must unload the active runtime")
	}
	if status := manager.Status(domain.AICapabilitySemanticSearch); status.State != RuntimeStateDisabled {
		t.Fatalf("status after global disable = %s, want disabled", status.State)
	}
	if _, err := manager.Infer(context.Background(), domain.AICapabilitySemanticSearch, request); err != ErrCapabilityDisabled {
		t.Fatalf("Infer while disabled error = %v, want %v", err, ErrCapabilityDisabled)
	}

	settings.Enabled = true
	if err := manager.RemoveModel(manifest.ID, manifest.Version); err != nil {
		t.Fatalf("RemoveModel after unload: %v", err)
	}
}

func TestManagerRejectsLoadWhenFeatureIsOff(t *testing.T) {
	settings := domain.AISettings{Enabled: true, SemanticSearch: false}
	manager := NewManager(t.TempDir(), func() (domain.AISettings, error) {
		return settings, nil
	})

	if err := manager.Load(
		context.Background(),
		domain.AICapabilitySemanticSearch,
		"missing",
		"1",
		LoadOptions{},
	); err != ErrCapabilityDisabled {
		t.Fatalf("Load error = %v, want %v", err, ErrCapabilityDisabled)
	}
}


func TestManagerCallsModelActivationHookBeforePublishingRuntime(t *testing.T) {
	data := []byte("model activation hook")
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write(data)
	}))
	defer server.Close()

	settings := domain.AISettings{
		Enabled:        true,
		SemanticSearch: true,
	}
	manager := NewManager(t.TempDir(), func() (domain.AISettings, error) {
		return settings, nil
	})

	var engine *dummyEngine
	if err := manager.RegisterEngine("dummy", func() Engine {
		engine = &dummyEngine{}
		return engine
	}); err != nil {
		t.Fatalf("RegisterEngine: %v", err)
	}

	manifest := testManifest(server.URL, data)
	if _, err := manager.InstallModel(context.Background(), manifest, nil); err != nil {
		t.Fatalf("InstallModel: %v", err)
	}

	var called bool
	manager.SetModelActivatedHook(func(capability domain.AICapability, model InstalledModel) error {
		called = true
		if capability != domain.AICapabilitySemanticSearch {
			t.Fatalf("hook capability = %s, want %s", capability, domain.AICapabilitySemanticSearch)
		}
		if model.Manifest.ID != manifest.ID || model.Manifest.Version != manifest.Version {
			t.Fatalf("hook model mismatch: %+v", model.Manifest)
		}
		if status := manager.Status(capability); status.State != RuntimeStateModelNotInstalled {
			t.Fatalf("runtime must not be published before activation hook succeeds: %+v", status)
		}
		return nil
	})

	if err := manager.Load(
		context.Background(),
		domain.AICapabilitySemanticSearch,
		manifest.ID,
		manifest.Version,
		LoadOptions{},
	); err != nil {
		t.Fatalf("Load: %v", err)
	}
	if !called {
		t.Fatal("model activation hook was not called")
	}
	if engine == nil || !engine.loaded {
		t.Fatal("engine should be loaded")
	}
	if status := manager.Status(domain.AICapabilitySemanticSearch); status.State != RuntimeStateReady {
		t.Fatalf("runtime status after hook = %+v", status)
	}
}


type slowLifecycleEngine struct {
	active *int
	max    *int
	mu     *sync.Mutex
}

func (e *slowLifecycleEngine) ID() string { return "dummy" }

func (e *slowLifecycleEngine) Load(_ context.Context, _ InstalledModel, _ LoadOptions) error {
	e.mu.Lock()
	*e.active = *e.active + 1
	if *e.active > *e.max {
		*e.max = *e.active
	}
	e.mu.Unlock()

	time.Sleep(60 * time.Millisecond)

	e.mu.Lock()
	*e.active = *e.active - 1
	e.mu.Unlock()
	return nil
}

func (e *slowLifecycleEngine) Infer(_ context.Context, request InferenceRequest) (InferenceResponse, error) {
	return InferenceResponse{Payload: request.Payload}, nil
}

func (e *slowLifecycleEngine) Unload(_ context.Context) error { return nil }

func TestManagerSerializesConcurrentLoads(t *testing.T) {
	data := []byte("concurrent lifecycle model")
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write(data)
	}))
	defer server.Close()

	settings := domain.AISettings{Enabled: true, SemanticSearch: true}
	manager := NewManager(t.TempDir(), func() (domain.AISettings, error) {
		return settings, nil
	})

	var mu sync.Mutex
	active := 0
	maxConcurrent := 0
	if err := manager.RegisterEngine("dummy", func() Engine {
		return &slowLifecycleEngine{active: &active, max: &maxConcurrent, mu: &mu}
	}); err != nil {
		t.Fatal(err)
	}

	manifest := testManifest(server.URL, data)
	if _, err := manager.InstallModel(context.Background(), manifest, nil); err != nil {
		t.Fatal(err)
	}

	start := make(chan struct{})
	errs := make(chan error, 2)
	for i := 0; i < 2; i++ {
		go func() {
			<-start
			errs <- manager.Load(
				context.Background(),
				domain.AICapabilitySemanticSearch,
				manifest.ID,
				manifest.Version,
				LoadOptions{},
			)
		}()
	}
	close(start)

	for i := 0; i < 2; i++ {
		if err := <-errs; err != nil {
			t.Fatalf("concurrent Load: %v", err)
		}
	}

	mu.Lock()
	got := maxConcurrent
	mu.Unlock()
	if got != 1 {
		t.Fatalf("concurrent engine loads = %d, want 1", got)
	}
}


func TestManagerLoadIsIdempotentForReadySameRuntime(t *testing.T) {
	data := []byte("idempotent runtime model")
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write(data)
	}))
	defer server.Close()

	settings := domain.AISettings{Enabled: true, SemanticSearch: true}
	manager := NewManager(t.TempDir(), func() (domain.AISettings, error) {
		return settings, nil
	})

	loadCount := 0
	unloadCount := 0
	type countedEngine struct{ dummyEngine }
	var current *countedEngine

	if err := manager.RegisterEngine("dummy", func() Engine {
		current = &countedEngine{}
		return current
	}); err != nil {
		t.Fatal(err)
	}

	manifest := testManifest(server.URL, data)
	if _, err := manager.InstallModel(context.Background(), manifest, nil); err != nil {
		t.Fatal(err)
	}

	// Wrap the factory with explicit counters by replacing it after installation.
	if err := manager.RegisterEngine("dummy", func() Engine {
		engine := &countedEngine{}
		current = engine
		return &countingEngine{
			inner: engine,
			onLoad: func() { loadCount++ },
			onUnload: func() { unloadCount++ },
		}
	}); err != nil {
		t.Fatal(err)
	}

	for i := 0; i < 2; i++ {
		if err := manager.Load(
			context.Background(),
			domain.AICapabilitySemanticSearch,
			manifest.ID,
			manifest.Version,
			LoadOptions{},
		); err != nil {
			t.Fatalf("Load %d: %v", i+1, err)
		}
	}

	if loadCount != 1 {
		t.Fatalf("engine Load calls = %d, want 1", loadCount)
	}
	if unloadCount != 0 {
		t.Fatalf("engine Unload calls = %d, want 0", unloadCount)
	}
	if current == nil || !current.loaded {
		t.Fatal("ready runtime should remain loaded")
	}
}

type countingEngine struct {
	inner    Engine
	onLoad   func()
	onUnload func()
}

func (e *countingEngine) ID() string { return e.inner.ID() }
func (e *countingEngine) Load(ctx context.Context, model InstalledModel, options LoadOptions) error {
	if e.onLoad != nil {
		e.onLoad()
	}
	return e.inner.Load(ctx, model, options)
}
func (e *countingEngine) Infer(ctx context.Context, request InferenceRequest) (InferenceResponse, error) {
	return e.inner.Infer(ctx, request)
}
func (e *countingEngine) Unload(ctx context.Context) error {
	if e.onUnload != nil {
		e.onUnload()
	}
	return e.inner.Unload(ctx)
}


type blockingInferenceEngine struct {
	dummyEngine
	started chan struct{}
	release chan struct{}
	once    sync.Once
}

func (e *blockingInferenceEngine) Infer(ctx context.Context, request InferenceRequest) (InferenceResponse, error) {
	e.once.Do(func() { close(e.started) })
	select {
	case <-e.release:
	case <-ctx.Done():
		return InferenceResponse{}, ctx.Err()
	}
	return InferenceResponse{Payload: request.Payload}, nil
}

func newLoadedBlockingManager(t *testing.T) (*Manager, *blockingInferenceEngine, ModelManifest) {
	t.Helper()

	data := []byte("blocking inference model")
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = w.Write(data)
	}))
	t.Cleanup(server.Close)

	settings := domain.AISettings{Enabled: true, SemanticSearch: true}
	manager := NewManager(t.TempDir(), func() (domain.AISettings, error) {
		return settings, nil
	})
	engine := &blockingInferenceEngine{
		started: make(chan struct{}),
		release: make(chan struct{}),
	}
	if err := manager.RegisterEngine("dummy", func() Engine { return engine }); err != nil {
		t.Fatal(err)
	}
	manifest := testManifest(server.URL, data)
	if _, err := manager.InstallModel(context.Background(), manifest, nil); err != nil {
		t.Fatal(err)
	}
	if err := manager.Load(
		context.Background(),
		domain.AICapabilitySemanticSearch,
		manifest.ID,
		manifest.Version,
		LoadOptions{},
	); err != nil {
		t.Fatal(err)
	}
	return manager, engine, manifest
}

func TestManagerConcurrentInferenceDoesNotDeadlock(t *testing.T) {
	manager, engine, _ := newLoadedBlockingManager(t)

	firstDone := make(chan error, 1)
	go func() {
		_, err := manager.Infer(
			context.Background(),
			domain.AICapabilitySemanticSearch,
			InferenceRequest{Operation: "first"},
		)
		firstDone <- err
	}()

	select {
	case <-engine.started:
	case <-time.After(time.Second):
		t.Fatal("first inference did not start")
	}

	secondDone := make(chan error, 1)
	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
		defer cancel()
		_, err := manager.Infer(
			ctx,
			domain.AICapabilitySemanticSearch,
			InferenceRequest{Operation: "second"},
		)
		secondDone <- err
	}()

	// Give the second call enough time to reach the runtime-operation wait. In
	// the old implementation it held Manager.mu here, deadlocking the first
	// inference when it tried to publish completion.
	time.Sleep(75 * time.Millisecond)
	close(engine.release)

	for name, done := range map[string]<-chan error{
		"first":  firstDone,
		"second": secondDone,
	} {
		select {
		case err := <-done:
			if err != nil {
				t.Fatalf("%s inference: %v", name, err)
			}
		case <-time.After(3 * time.Second):
			t.Fatalf("%s inference deadlocked", name)
		}
	}
}

func TestManagerUnloadHonorsContextWhileInferenceIsRunning(t *testing.T) {
	manager, engine, _ := newLoadedBlockingManager(t)

	inferDone := make(chan error, 1)
	go func() {
		_, err := manager.Infer(
			context.Background(),
			domain.AICapabilitySemanticSearch,
			InferenceRequest{Operation: "blocking"},
		)
		inferDone <- err
	}()
	select {
	case <-engine.started:
	case <-time.After(time.Second):
		t.Fatal("inference did not start")
	}

	ctx, cancel := context.WithTimeout(context.Background(), 100*time.Millisecond)
	defer cancel()
	started := time.Now()
	err := manager.Unload(ctx, domain.AICapabilitySemanticSearch)
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("Unload error = %v, want context deadline", err)
	}
	if elapsed := time.Since(started); elapsed > time.Second {
		t.Fatalf("Unload ignored context timeout: %s", elapsed)
	}

	// A timed-out unload must keep the session tracked instead of orphaning a
	// live engine that shutdown can no longer reach.
	status := manager.Status(domain.AICapabilitySemanticSearch)
	if status.State != RuntimeStateRunning {
		t.Fatalf("runtime after timed-out unload = %s, want running", status.State)
	}

	close(engine.release)
	select {
	case err := <-inferDone:
		if err != nil {
			t.Fatalf("blocking inference: %v", err)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("blocking inference did not finish")
	}

	if err := manager.Unload(context.Background(), domain.AICapabilitySemanticSearch); err != nil {
		t.Fatalf("final unload: %v", err)
	}
}


func TestManagerCancelledInferenceDoesNotPoisonRuntime(t *testing.T) {
	manager, engine, _ := newLoadedBlockingManager(t)

	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() {
		_, err := manager.Infer(
			ctx,
			domain.AICapabilitySemanticSearch,
			InferenceRequest{Operation: "cancel-me"},
		)
		done <- err
	}()

	select {
	case <-engine.started:
	case <-time.After(time.Second):
		t.Fatal("inference did not start")
	}
	cancel()

	select {
	case err := <-done:
		if !errors.Is(err, context.Canceled) {
			t.Fatalf("Infer error = %v, want context.Canceled", err)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("cancelled inference did not return")
	}

	if status := manager.Status(domain.AICapabilitySemanticSearch); status.State != RuntimeStateReady {
		t.Fatalf("runtime after caller cancellation = %+v, want ready", status)
	}
}
