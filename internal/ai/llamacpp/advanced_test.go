package llamacpp

import (
	"encoding/json"
	"strings"
	"testing"
)

func TestParseAdvancedVisionChatResponse(t *testing.T) {
	content := AdvancedVisionResult{
		Summary: " scene ",
		Subjects: []string{"adult person"},
		Environment: " room ",
		Composition: " centered ",
		Viewpoint: " low angle ",
		Actions: []string{"standing"},
		Relationships: []string{},
		Context: "test",
		Differences: nil,
		Commonalities: nil,
		ReversePromptHints: []string{"low angle"},
		VisibleText: []string{"LUMINE"},
		Notes: nil,
	}
	contentJSON, _ := json.Marshal(content)
	envelope, _ := json.Marshal(map[string]any{
		"choices": []any{
			map[string]any{"message": map[string]any{"content": string(contentJSON)}},
		},
		"usage": map[string]any{"completion_tokens": 123},
	})
	result, tokens, err := parseAdvancedVisionChatResponse(envelope)
	if err != nil {
		t.Fatalf("parseAdvancedVisionChatResponse: %v", err)
	}
	if result.Summary != "scene" || result.Viewpoint != "low angle" {
		t.Fatalf("unexpected result: %+v", result)
	}
	if result.Differences == nil || result.Commonalities == nil || result.Notes == nil {
		t.Fatalf("array fields should normalize to empty slices: %+v", result)
	}
	if tokens != 123 {
		t.Fatalf("tokens = %d, want 123", tokens)
	}
}

func TestAdvancedVisionSchemaAndPrompt(t *testing.T) {
	schema := advancedVisionJSONSchema()
	required, ok := schema["required"].([]string)
	if !ok || len(required) < 10 {
		t.Fatalf("unexpected required schema: %#v", schema["required"])
	}
	prompt := advancedVisionPrompt("compare", "日本語で違いを説明", 2)
	for _, expected := range []string{"Compare", "日本語で違いを説明", "2 images", "adult-only"} {
		if !strings.Contains(prompt, expected) {
			t.Fatalf("prompt missing %q: %s", expected, prompt)
		}
	}
}

func TestInferenceFilePaths(t *testing.T) {
	paths, err := inferenceFilePaths([]any{"a.png", "b.png"})
	if err != nil {
		t.Fatal(err)
	}
	if len(paths) != 2 {
		t.Fatalf("paths = %v", paths)
	}
	if _, err := inferenceFilePaths([]any{"a.png", 1}); err == nil {
		t.Fatal("non-string path should fail")
	}
	if _, err := inferenceFilePaths([]string{"a.png", ""}); err == nil {
		t.Fatal("empty path should fail")
	}
}
