package llamacpp

import (
	"testing"

	"github.com/kataage/lumine/internal/ai"
)

func TestPromptCandidateManifestsArePinnedUniqueAndValid(t *testing.T) {
	tests := []struct {
		name       string
		manifest   ai.ModelManifest
		wantID     string
		wantSHA256 string
		wantSize   int64
	}{
		{
			name:       "NeoHorse",
			manifest:   PromptNeoHorseManifest(),
			wantID:     PromptNeoHorseModelID,
			wantSHA256: "0900451ccd4f0a65bf0e8d3f2c224eff02ae9c961cb064de8b87e8a2118af3c2",
			wantSize:   2783446464,
		},
		{
			name:       "Spark-X2.5",
			manifest:   PromptSparkX25Manifest(),
			wantID:     PromptSparkX25ModelID,
			wantSHA256: "8700b9cb4be2ddf1a6b4a9230f7b9d030aeb923d0528301bc546bb5a74f21cd7",
			wantSize:   2600224384,
		},
		{
			name:       "Qwen3.5 reference",
			manifest:   PromptReferenceQwen35Manifest(),
			wantID:     PromptReferenceQwen35ModelID,
			wantSHA256: "f8e45572b9cc35161d4772b09bccfd383fe0bb03fc6d69b40a9138731302290b",
			wantSize:   2385660192,
		},
	}

	seen := map[string]bool{}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			manifest := tt.manifest
			if manifest.ID != tt.wantID {
				t.Fatalf("id = %q, want %q", manifest.ID, tt.wantID)
			}
			if seen[manifest.ID] {
				t.Fatalf("duplicate id %q", manifest.ID)
			}
			seen[manifest.ID] = true
			if manifest.Engine != PromptEngineID {
				t.Fatalf("engine = %q", manifest.Engine)
			}
			if manifest.Version == "" || manifest.Version == "main" {
				t.Fatalf("version must be immutable: %q", manifest.Version)
			}
			if manifest.SizeBytes != tt.wantSize {
				t.Fatalf("size = %d, want %d", manifest.SizeBytes, tt.wantSize)
			}
			if len(manifest.Files) != 1 || manifest.Files[0].Role != "model" {
				t.Fatalf("files = %+v", manifest.Files)
			}
			if manifest.Files[0].SHA256 != tt.wantSHA256 {
				t.Fatalf("unexpected model sha256: %s", manifest.Files[0].SHA256)
			}
			if manifest.Files[0].SizeBytes != tt.wantSize {
				t.Fatalf("file size = %d, want %d", manifest.Files[0].SizeBytes, tt.wantSize)
			}
			if err := ai.ValidateManifest(manifest); err != nil {
				t.Fatalf("ValidateManifest: %v", err)
			}
		})
	}

	manifests := PromptEngineCandidateManifests()
	if len(manifests) != len(tests) {
		t.Fatalf("candidate count = %d, want %d", len(manifests), len(tests))
	}
	for index, tt := range tests {
		if manifests[index].ID != tt.wantID {
			t.Fatalf("candidate[%d] = %q, want %q", index, manifests[index].ID, tt.wantID)
		}
	}
}

func TestPromptCandidateThinkingPolicies(t *testing.T) {
	if got := PromptNeoHorseManifest().Parameters["disableThinking"]; got != "true" {
		t.Fatalf("NeoHorse disableThinking = %q", got)
	}
	if got := PromptReferenceQwen35Manifest().Parameters["disableThinking"]; got != "true" {
		t.Fatalf("Qwen reference disableThinking = %q", got)
	}
	if got := PromptSparkX25Manifest().Parameters["disableThinking"]; got != "" {
		t.Fatalf("Spark disableThinking = %q; Spark benchmark profile keeps native thinking", got)
	}
	if got := PromptSparkX25Manifest().Parameters["maxTokens"]; got != "2048" {
		t.Fatalf("Spark maxTokens = %q", got)
	}
}
