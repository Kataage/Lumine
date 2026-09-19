package commands

import (
	"testing"
)

func TestPromptVersionSourceValidation(t *testing.T) {
	for _, source := range []string{"manual", "llm", "vlm", "tagger", "derived", "metadata"} {
		if !validPromptVersionSource(source) {
			t.Fatalf("%s should be valid", source)
		}
	}
	if validPromptVersionSource("chat") {
		t.Fatal("arbitrary source should not be accepted")
	}
}

func TestNormalizeJSONObject(t *testing.T) {
	value, err := normalizeJSONObject("")
	if err != nil || value != "{}" {
		t.Fatalf("empty JSON = %q, %v", value, err)
	}
	if _, err := normalizeJSONObject("[]"); err == nil {
		t.Fatal("array metadata must fail")
	}
	value, err = normalizeJSONObject(`{ "seed": 42, "size": [1024, 1024] }`)
	if err != nil {
		t.Fatal(err)
	}
	if value == "" {
		t.Fatal("normalized JSON is empty")
	}
}
