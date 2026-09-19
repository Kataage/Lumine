package commands

import (
	"testing"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/domain"
)

func TestRuntimeStatusForInstalledModelReportsNotLoaded(t *testing.T) {
	manifest := ai.ModelManifest{
		ID:      "model-a",
		Version: "1",
		Engine:  "engine-a",
	}
	status := runtimeStatusForInstalledModel(
		ai.RuntimeStatus{
			Capability: domain.AICapabilitySemanticSearch,
			State:      ai.RuntimeStateModelNotInstalled,
		},
		manifest,
		true,
	)

	if status.State != ai.RuntimeStateNotLoaded {
		t.Fatalf("state = %s, want %s", status.State, ai.RuntimeStateNotLoaded)
	}
	if status.ModelID != manifest.ID || status.Version != manifest.Version || status.Engine != manifest.Engine {
		t.Fatalf("runtime provenance mismatch: %+v", status)
	}
}

func TestRuntimeStatusForInstalledModelDoesNotMaskMissingModel(t *testing.T) {
	manifest := ai.ModelManifest{ID: "model-a", Version: "1", Engine: "engine-a"}
	status := runtimeStatusForInstalledModel(
		ai.RuntimeStatus{
			Capability: domain.AICapabilitySemanticSearch,
			State:      ai.RuntimeStateModelNotInstalled,
		},
		manifest,
		false,
	)

	if status.State != ai.RuntimeStateModelNotInstalled {
		t.Fatalf("state = %s, want %s", status.State, ai.RuntimeStateModelNotInstalled)
	}
	if status.ModelID != "" || status.Version != "" || status.Engine != "" {
		t.Fatalf("missing model unexpectedly gained provenance: %+v", status)
	}
}

func TestRuntimeStatusForInstalledModelPreservesDisabledAndError(t *testing.T) {
	manifest := ai.ModelManifest{ID: "model-a", Version: "1", Engine: "engine-a"}
	for _, input := range []ai.RuntimeStatus{
		{Capability: domain.AICapabilitySemanticSearch, State: ai.RuntimeStateDisabled},
		{Capability: domain.AICapabilitySemanticSearch, State: ai.RuntimeStateError, Error: "boom"},
	} {
		got := runtimeStatusForInstalledModel(input, manifest, true)
		if got.State != input.State || got.Error != input.Error {
			t.Fatalf("status changed unexpectedly: got=%+v want=%+v", got, input)
		}
	}
}
