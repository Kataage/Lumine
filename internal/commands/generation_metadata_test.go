package commands

import (
	"testing"

	"github.com/kataage/lumine/internal/domain"
)

func TestInferModelProfileFromCheckpointHeuristics(t *testing.T) {
	cases := map[string]string{
		"my_illustrious_xl.safetensors": "illustrious-xl",
		"NoobAI-XL-vPred.safetensors":    "noobai-xl",
		"ponyDiffusionV6XL.safetensors":  "pony-xl",
		"flux1-dev.safetensors":          "flux",
		"sd15-anime.ckpt":                "sd15",
		"generic_sdxl.safetensors":       "sdxl-generic",
	}
	commands := &AppCommands{}
	for checkpoint, want := range cases {
		// No DB is required for built-in heuristic matching.
		got := commands.inferModelProfileFromCheckpoint(checkpoint)
		if got != want {
			t.Fatalf("%q => %q, want %q", checkpoint, got, want)
		}
	}
}

func TestGenerationMetadataPresentHandlesPartialHistory(t *testing.T) {
	if generationMetadataPresent(domain.GenerationMetadata{
		SchemaVersion: 1,
		Checkpoint: "model.safetensors",
	}, map[string]string{}) == false {
		t.Fatal("checkpoint-only metadata should be reusable")
	}
}
