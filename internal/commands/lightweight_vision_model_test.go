package commands

import (
	"errors"
	"os"
	"path/filepath"
	"testing"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/ai/llamacpp"
	"github.com/kataage/lumine/internal/domain"
)

func setupLightweightVisionCommands(t *testing.T) (*AppCommands, *ai.Manager, *llamacpp.RuntimeStore) {
	t.Helper()
	cmd := setupCommands(t)
	modelRoot := filepath.Join(t.TempDir(), "models")
	runtimeRoot := filepath.Join(t.TempDir(), "runtimes")
	manager := ai.NewManager(modelRoot, cmd.GetAISettings)
	cmd.SetAIManager(manager)
	runtimeStore := llamacpp.NewRuntimeStore(runtimeRoot)
	cmd.SetLlamaRuntimeStore(runtimeStore)
	return cmd, manager, runtimeStore
}

func TestLightweightVisionInfoStartsUninstalledWithoutCreatingAssets(t *testing.T) {
	cmd, manager, runtimeStore := setupLightweightVisionCommands(t)

	info := cmd.GetDefaultLightweightVisionModelInfo()
	if info.Installed || info.LlamaRuntime.Installed {
		t.Fatalf("fresh Lightweight Vision should be uninstalled: %+v", info)
	}
	if info.Runtime.State != ai.RuntimeStateDisabled {
		t.Fatalf("runtime state = %s, want disabled", info.Runtime.State)
	}
	if _, err := os.Stat(manager.Store().Root()); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("querying model info must not create model storage: %v", err)
	}
	if _, err := os.Stat(runtimeStore.Root()); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("querying runtime info must not create runtime storage: %v", err)
	}
}

func TestRestoreLightweightVisionNeverDownloadsMissingAssets(t *testing.T) {
	cmd, manager, runtimeStore := setupLightweightVisionCommands(t)
	if _, err := cmd.SetAISettings(domain.AISettings{
		Enabled:           true,
		LightweightVision: true,
	}); err != nil {
		t.Fatalf("SetAISettings: %v", err)
	}

	if err := cmd.RestoreDefaultLightweightVisionModel(); err != nil {
		t.Fatalf("RestoreDefaultLightweightVisionModel: %v", err)
	}
	if _, err := os.Stat(manager.Store().Root()); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("restore must not create/download model data: %v", err)
	}
	if _, err := os.Stat(runtimeStore.Root()); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("restore must not create/download runtime data: %v", err)
	}
}

func TestLoadLightweightVisionRequiresFeatureOptIn(t *testing.T) {
	cmd, _, _ := setupLightweightVisionCommands(t)
	err := cmd.LoadDefaultLightweightVisionModel()
	if !errors.Is(err, ErrLightweightVisionDisabled) {
		t.Fatalf("LoadDefaultLightweightVisionModel error = %v, want %v", err, ErrLightweightVisionDisabled)
	}
}

func TestLightweightVisionPinnedInfoMatchesManifests(t *testing.T) {
	cmd, _, _ := setupLightweightVisionCommands(t)
	info := cmd.GetDefaultLightweightVisionModelInfo()
	model := llamacpp.DefaultVisionModelManifest()
	runtimeManifest := llamacpp.DefaultRuntimeManifest()

	if info.ID != model.ID ||
		info.Version != model.Version ||
		info.Engine != model.Engine ||
		info.SizeBytes != model.SizeBytes {
		t.Fatalf("model info mismatch: %+v vs %+v", info, model)
	}
	if info.LlamaRuntime.ID != runtimeManifest.ID ||
		info.LlamaRuntime.Version != runtimeManifest.Version ||
		info.LlamaRuntime.SizeBytes != runtimeManifest.SizeBytes {
		t.Fatalf("runtime info mismatch: %+v vs %+v", info.LlamaRuntime, runtimeManifest)
	}
}
