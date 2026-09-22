package llamacpp

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/kataage/lumine/internal/ai"
)

func TestPromptEngineIDIsIndependent(t *testing.T) {
	if PromptEngineID == EngineID || PromptEngineID == AdvancedEngineID {
		t.Fatal("Prompt Engine must use a separate engine id")
	}
	if NewPromptEngine(nil).ID() != PromptEngineID {
		t.Fatal("Prompt Engine id mismatch")
	}
}

func TestResolveTextModelPathRequiresSingleModelRole(t *testing.T) {
	root := t.TempDir()
	path := filepath.Join(root, "prompt.gguf")
	if err := os.WriteFile(path, []byte("model"), 0o644); err != nil {
		t.Fatal(err)
	}
	installed := ai.InstalledModel{
		RootDir: root,
		Manifest: ai.ModelManifest{
			Files: []ai.ModelFile{{Path: "prompt.gguf", Role: "model"}},
		},
	}
	got, err := resolveTextModelPath(installed)
	if err != nil {
		t.Fatalf("resolveTextModelPath: %v", err)
	}
	if got != path {
		t.Fatalf("path = %q, want %q", got, path)
	}

	installed.Manifest.Files = append(installed.Manifest.Files, ai.ModelFile{Path: "other.gguf", Role: "model"})
	if _, err := resolveTextModelPath(installed); err == nil {
		t.Fatal("duplicate model roles should fail")
	}
}

func TestBuildPromptServerArgsDoesNotUseMMProj(t *testing.T) {
	cpu := buildPromptServerArgs("model.gguf", 1234, 8192, 8, false, nil)
	joined := strings.Join(cpu, " ")
	if strings.Contains(joined, "mmproj") {
		t.Fatalf("text-only args unexpectedly contain mmproj: %s", joined)
	}
	if !strings.Contains(joined, "-ngl 0") {
		t.Fatalf("CPU args = %s", joined)
	}

	gpu := buildPromptServerArgs("model.gguf", 1234, 8192, 8, true, nil)
	joinedGPU := strings.Join(gpu, " ")
	if !strings.Contains(joinedGPU, "-ngl auto") ||
		!strings.Contains(joinedGPU, "--fit on") ||
		!strings.Contains(joinedGPU, "--fit-target 1024") {
		t.Fatalf("GPU args = %v", gpu)
	}
}

func TestPromptInferenceRequestValidation(t *testing.T) {
	valid := []ai.InferenceRequest{
		{Operation: PromptOperationIdea, Payload: map[string]any{"idea": "idea"}},
		{Operation: PromptOperationImprove, Payload: map[string]any{"positive": "1girl"}},
		{Operation: PromptOperationConvert, Payload: map[string]any{"positive": "1girl", "targetProfile": "FLUX"}},
		{Operation: PromptOperationEdit, Payload: map[string]any{"positive": "1girl", "instruction": "change background"}},
	}
	for _, request := range valid {
		if err := validatePromptInferenceRequest(request); err != nil {
			t.Fatalf("%s should be valid: %v", request.Operation, err)
		}
	}
	if err := validatePromptInferenceRequest(ai.InferenceRequest{Operation: PromptOperationIdea}); err == nil {
		t.Fatal("idea without input should fail")
	}
	if err := validatePromptInferenceRequest(ai.InferenceRequest{Operation: "chat"}); err == nil {
		t.Fatal("general chat operation must not be accepted")
	}
}

func TestParsePromptEnvelopeAndResult(t *testing.T) {
	resultJSON, _ := json.Marshal(PromptResult{
		Positive:    " 1girl, white hair ",
		Negative:    " lowres ",
		Characters:  []string{" heroine ", ""},
		LoRAs:       []string{" <lora:test:0.8> "},
		Composition: " upper body ",
		Notes:       []string{" keep outfit "},
	})
	envelope, _ := json.Marshal(map[string]any{
		"choices": []any{
			map[string]any{
				"message": map[string]any{
					"content": "[thinking]\n[End thinking]\n\x60\x60\x60json\n" + string(resultJSON) + "\n\x60\x60\x60",
				},
			},
		},
		"usage": map[string]any{"completion_tokens": 42},
	})
	raw, tokens, err := parsePromptChatEnvelope(envelope)
	if err != nil {
		t.Fatalf("parsePromptChatEnvelope: %v", err)
	}
	result, err := parsePromptResult(raw)
	if err != nil {
		t.Fatalf("parsePromptResult: %v", err)
	}
	result.normalize()
	if result.Positive != "1girl, white hair" || result.Composition != "upper body" {
		t.Fatalf("result = %+v", result)
	}
	if len(result.Characters) != 1 || result.Characters[0] != "heroine" {
		t.Fatalf("characters = %v", result.Characters)
	}
	if tokens != 42 {
		t.Fatalf("tokens = %d, want 42", tokens)
	}
}

func TestPromptJSONSchemaStableFields(t *testing.T) {
	schema := promptJSONSchema()
	required, ok := schema["required"].([]string)
	if !ok {
		t.Fatalf("required type = %T", schema["required"])
	}
	want := []string{"positive", "negative", "characters", "loras", "composition", "notes"}
	if strings.Join(required, ",") != strings.Join(want, ",") {
		t.Fatalf("required = %v", required)
	}
}

func TestPromptResultValidationRequiresPositive(t *testing.T) {
	if err := validatePromptResult(PromptResult{}); err == nil {
		t.Fatal("empty positive must fail")
	}
	if err := validatePromptResult(PromptResult{Positive: "1girl"}); err != nil {
		t.Fatalf("valid result: %v", err)
	}
}
