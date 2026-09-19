package llamacpp

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/kataage/lumine/internal/ai"
)

func TestAdvancedCandidateManifestsAreSwappableAndValid(t *testing.T) {
	manifests := AdvancedVisionCandidateManifests()
	if len(manifests) < 2 {
		t.Fatalf("candidate manifests = %d, want at least 2", len(manifests))
	}
	for _, manifest := range manifests {
		if manifest.Engine != AdvancedEngineID {
			t.Fatalf("%s engine = %q", manifest.ID, manifest.Engine)
		}
		if err := ai.ValidateManifest(manifest); err != nil {
			t.Fatalf("%s ValidateManifest: %v", manifest.ID, err)
		}
		roles := map[string]bool{}
		for _, file := range manifest.Files {
			roles[file.Role] = true
		}
		if !roles["model"] || !roles["mmproj"] {
			t.Fatalf("%s missing model/mmproj roles: %+v", manifest.ID, manifest.Files)
		}
	}
}

func TestAdvancedEngineIDIsIndependentFromLightweight(t *testing.T) {
	if EngineID == AdvancedEngineID {
		t.Fatal("Advanced Vision must use a separate engine id")
	}
	if NewEngine(nil).ID() != EngineID {
		t.Fatal("Lightweight engine id mismatch")
	}
	if NewAdvancedEngine(nil).ID() != AdvancedEngineID {
		t.Fatal("Advanced engine id mismatch")
	}
}

func TestAdvancedFilePaths(t *testing.T) {
	paths, err := advancedFilePaths(map[string]any{
		"filePaths": []any{" a.png ", "b.png"},
	})
	if err != nil {
		t.Fatalf("advancedFilePaths: %v", err)
	}
	if strings.Join(paths, ",") != "a.png,b.png" {
		t.Fatalf("paths = %v", paths)
	}
	if _, err := advancedFilePaths(map[string]any{"filePaths": "bad"}); err == nil {
		t.Fatal("non-array filePaths should fail")
	}
	if _, err := advancedFilePaths(map[string]any{}); err == nil {
		t.Fatal("missing images should fail")
	}
}

func TestParseAdvancedVisionChatResponse(t *testing.T) {
	resultJSON, _ := json.Marshal(AdvancedVisionResult{
		Summary:            " person ",
		Subjects:           []string{" person ", ""},
		Environment:        " room ",
		Composition:        " centered ",
		Viewpoint:          " low angle ",
		Actions:            []string{" standing "},
		Relationships:      []string{},
		Context:            " indoor ",
		Differences:        []string{},
		Commonalities:      []string{},
		ReversePromptHints: []string{" blue hair "},
		VisibleText:        []string{"LUMINE"},
		Notes:              []string{},
	})
	envelope, _ := json.Marshal(map[string]any{
		"choices": []any{
			map[string]any{"message": map[string]any{"content": "```json\n" + string(resultJSON) + "\n```"}},
		},
		"usage": map[string]any{"completion_tokens": 51},
	})
	result, tokens, err := parseAdvancedVisionChatResponse(envelope)
	if err != nil {
		t.Fatalf("parseAdvancedVisionChatResponse: %v", err)
	}
	if result.Summary != "person" || result.Viewpoint != "low angle" {
		t.Fatalf("unexpected result: %+v", result)
	}
	if len(result.Subjects) != 1 || result.Subjects[0] != "person" {
		t.Fatalf("subjects = %v", result.Subjects)
	}
	if tokens != 51 {
		t.Fatalf("tokens = %d, want 51", tokens)
	}
}

func TestAdvancedVisionJSONSchemaStableFields(t *testing.T) {
	schema := advancedVisionJSONSchema()
	required, ok := schema["required"].([]string)
	if !ok {
		t.Fatalf("required type = %T", schema["required"])
	}
	want := []string{
		"summary", "subjects", "environment", "composition", "viewpoint",
		"actions", "relationships", "context", "differences", "commonalities",
		"reversePromptHints", "visibleText", "notes",
	}
	if strings.Join(required, ",") != strings.Join(want, ",") {
		t.Fatalf("required = %v", required)
	}
}

func TestManifestServerArgsProtectRuntimeControls(t *testing.T) {
	allowed := ai.ModelManifest{Parameters: map[string]string{
		"serverArgsJson": "[\"--reasoning\",\"off\"]",
	}}
	args, err := manifestServerArgs(allowed)
	if err != nil {
		t.Fatalf("allowed server args: %v", err)
	}
	if strings.Join(args, ",") != "--reasoning,off" {
		t.Fatalf("allowed args = %v", args)
	}

	blocked := ai.ModelManifest{Parameters: map[string]string{
		"serverArgsJson": "[\"--host\",\"0.0.0.0\"]",
	}}
	if _, err := manifestServerArgs(blocked); err == nil {
		t.Fatal("overriding host must fail")
	}

	blockedEquals := ai.ModelManifest{Parameters: map[string]string{
		"serverArgsJson": "[\"--host=0.0.0.0\"]",
	}}
	if _, err := manifestServerArgs(blockedEquals); err == nil {
		t.Fatal("equals-form host override must fail")
	}
}

func TestBuildLlamaServerArgsCPUAndGPU(t *testing.T) {
	cpu := buildLlamaServerArgs("model.gguf", "mmproj.gguf", 1234, 8192, 8, false, nil)
	joinedCPU := strings.Join(cpu, " ")
	if !strings.Contains(joinedCPU, "--no-mmproj-offload") || !strings.Contains(joinedCPU, "-ngl 0") {
		t.Fatalf("CPU args = %s", joinedCPU)
	}

	gpu := buildLlamaServerArgs("model.gguf", "mmproj.gguf", 1234, 8192, 8, true, nil)
	joinedGPU := strings.Join(gpu, " ")
	if strings.Contains(joinedGPU, "--no-mmproj-offload") || !strings.Contains(joinedGPU, "-ngl 99") {
		t.Fatalf("GPU args = %s", joinedGPU)
	}
}

func TestResolveVLMModelPathsUsesRoles(t *testing.T) {
	root := t.TempDir()
	modelPath := filepath.Join(root, "main.gguf")
	mmprojPath := filepath.Join(root, "projector.gguf")
	if err := os.WriteFile(modelPath, []byte("m"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(mmprojPath, []byte("p"), 0o644); err != nil {
		t.Fatal(err)
	}
	installed := ai.InstalledModel{
		RootDir: root,
		Manifest: ai.ModelManifest{
			ID: "advanced-test",
			Files: []ai.ModelFile{
				{Path: "main.gguf", Role: "model"},
				{Path: "projector.gguf", Role: "mmproj"},
			},
		},
	}
	gotModel, gotMMProj, err := resolveVLMModelPaths(installed)
	if err != nil {
		t.Fatalf("resolveVLMModelPaths: %v", err)
	}
	if gotModel != modelPath || gotMMProj != mmprojPath {
		t.Fatalf("resolved %q %q", gotModel, gotMMProj)
	}
}
