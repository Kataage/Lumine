package llamacpp

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestParseVisionChatResponse(t *testing.T) {
	content := map[string]any{
		"shortCaption": " person ",
		"detailedCaption": " a person in a room ",
		"subject": " person ",
		"background": " room ",
		"composition": " centered ",
		"viewpoint": " eye level ",
		"visibleText": []string{"LUMINE"},
	}
	contentJSON, _ := json.Marshal(content)
	envelope, _ := json.Marshal(map[string]any{
		"choices": []any{
			map[string]any{"message": map[string]any{"content": "```json\n" + string(contentJSON) + "\n```"}},
		},
		"usage": map[string]any{"completion_tokens": 42},
	})
	result, tokens, err := parseVisionChatResponse(envelope)
	if err != nil {
		t.Fatalf("parseVisionChatResponse: %v", err)
	}
	if result.ShortCaption != "person" || result.Viewpoint != "eye level" {
		t.Fatalf("unexpected normalized result: %+v", result)
	}
	if tokens != 42 {
		t.Fatalf("completion tokens = %d, want 42", tokens)
	}
}

func TestParseVisionChatResponseContentArray(t *testing.T) {
	resultJSON := `{"shortCaption":"a","detailedCaption":"b","subject":"c","background":"d","composition":"e","viewpoint":"f","visibleText":[]}`
	envelope, _ := json.Marshal(map[string]any{
		"choices": []any{
			map[string]any{"message": map[string]any{
				"content": []any{
					map[string]any{"type": "text", "text": resultJSON},
				},
			}},
		},
	})
	if _, _, err := parseVisionChatResponse(envelope); err != nil {
		t.Fatalf("array content should parse: %v", err)
	}
}

func TestFileDataURIRejectsNonImageAndEncodesImage(t *testing.T) {
	dir := t.TempDir()
	textPath := filepath.Join(dir, "bad.txt")
	if err := os.WriteFile(textPath, []byte("plain text"), 0o644); err != nil {
		t.Fatal(err)
	}
	if _, err := fileDataURI(textPath); err == nil {
		t.Fatal("non-image input should be rejected")
	}

	pngPath := filepath.Join(dir, "tiny.png")
	pngHeader := []byte{0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00}
	if err := os.WriteFile(pngPath, pngHeader, 0o644); err != nil {
		t.Fatal(err)
	}
	uri, err := fileDataURI(pngPath)
	if err != nil {
		t.Fatalf("fileDataURI: %v", err)
	}
	if !strings.HasPrefix(uri, "data:image/png;base64,") {
		t.Fatalf("unexpected data URI: %s", uri)
	}
}

func TestVisionJSONSchemaRequiresStableContract(t *testing.T) {
	schema := visionJSONSchema()
	required, ok := schema["required"].([]string)
	if !ok {
		t.Fatalf("required schema type = %T", schema["required"])
	}
	want := []string{"shortCaption","detailedCaption","subject","background","composition","viewpoint","visibleText"}
	if strings.Join(required, ",") != strings.Join(want, ",") {
		t.Fatalf("required fields = %v", required)
	}
}
