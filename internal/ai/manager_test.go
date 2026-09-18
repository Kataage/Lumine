package ai

import (
	"context"
	"net/http"
	"net/http/httptest"
	"sync"
	"testing"

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
