package llamacpp

import (
	"strings"
	"testing"

	"github.com/kataage/lumine/internal/ai"
)

func testMultimodalInstalledModel(params map[string]string) ai.InstalledModel {
	return ai.InstalledModel{
		Manifest: ai.ModelManifest{
			ID:          "test-vlm",
			Version:     "1",
			Engine:      EngineID,
			DisplayName: "test",
			License:     "MIT",
			RuntimeParameters: params,
			Files: []ai.ModelFile{
				{Path: "model.gguf", URL: "https://example.invalid/model.gguf", SHA256: strings.Repeat("a", 64), SizeBytes: 1},
				{Path: "mmproj.gguf", URL: "https://example.invalid/mmproj.gguf", SHA256: strings.Repeat("b", 64), SizeBytes: 1},
			},
		},
		RootDir: "C:/models/test-vlm/1",
	}
}

func TestParseModelRuntimeConfig(t *testing.T) {
	model := testMultimodalInstalledModel(map[string]string{
		"mainFile": "model.gguf",
		"mmprojFile": "mmproj.gguf",
		"context": "8192",
		"threads": "6",
		"maxTokens": "1024",
		"reasoning": "off",
	})
	config, err := parseModelRuntimeConfig(model)
	if err != nil {
		t.Fatalf("parseModelRuntimeConfig: %v", err)
	}
	if config.context != 8192 || config.threads != 6 || config.maxTokens != 1024 || config.reasoning != "off" {
		t.Fatalf("unexpected config: %+v", config)
	}

	cpu := strings.Join(config.serverArgs(12345, false), " ")
	if !strings.Contains(cpu, "--no-mmproj-offload") || !strings.Contains(cpu, "-ngl 0") || !strings.Contains(cpu, "--reasoning off") {
		t.Fatalf("unexpected CPU args: %s", cpu)
	}
	gpu := strings.Join(config.serverArgs(12345, true), " ")
	if strings.Contains(gpu, "--no-mmproj-offload") || !strings.Contains(gpu, "-ngl 99") {
		t.Fatalf("unexpected GPU args: %s", gpu)
	}
}

func TestParseModelRuntimeConfigRejectsUnsafeOrUnknownValues(t *testing.T) {
	model := testMultimodalInstalledModel(map[string]string{"unknownArg": "--host 0.0.0.0"})
	if _, err := parseModelRuntimeConfig(model); err == nil {
		t.Fatal("unknown runtime parameter should fail")
	}

	model = testMultimodalInstalledModel(map[string]string{
		"mainFile": "other.gguf",
		"mmprojFile": "mmproj.gguf",
	})
	if _, err := parseModelRuntimeConfig(model); err == nil {
		t.Fatal("runtime file not declared by manifest should fail")
	}

	model = testMultimodalInstalledModel(map[string]string{
		"mainFile": "model.gguf",
		"mmprojFile": "mmproj.gguf",
		"context": "999999",
	})
	if _, err := parseModelRuntimeConfig(model); err == nil {
		t.Fatal("oversized context should fail")
	}
}

func TestAdvancedVisionCandidateManifestsValidate(t *testing.T) {
	candidates := AdvancedVisionCandidateManifests()
	if len(candidates) != 5 {
		t.Fatalf("candidate count = %d, want 5", len(candidates))
	}
	seen := map[string]bool{}
	for _, candidate := range candidates {
		manifest := candidate.Manifest
		if seen[manifest.ID] {
			t.Fatalf("duplicate candidate id %s", manifest.ID)
		}
		seen[manifest.ID] = true
		if err := ai.ValidateManifest(manifest); err != nil {
			t.Fatalf("candidate %s manifest: %v", manifest.ID, err)
		}
		if _, err := parseModelRuntimeConfig(ai.InstalledModel{Manifest: manifest}); err != nil {
			t.Fatalf("candidate %s runtime config: %v", manifest.ID, err)
		}
	}
}
