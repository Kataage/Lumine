package commands

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/kataage/lumine/internal/promptprofile"
)

func TestExtractTaggerTagsHandlesCommonShapes(t *testing.T) {
	raw := `{
		"generalTags": ["1girl", "white hair"],
		"characters": [{"tag":"heroine"}],
		"rating": "safe",
		"scores": {"ignored": 0.9}
	}`
	got := extractTaggerTags(raw)
	joined := strings.Join(got, ",")
	for _, want := range []string{"1girl", "white hair", "heroine", "safe"} {
		if !strings.Contains(joined, want) {
			t.Fatalf("tags %v missing %q", got, want)
		}
	}
}

func TestFallbackImagePromptRespectsProfileFamily(t *testing.T) {
	manual, _ := json.Marshal(map[string]any{"tags": []string{"1girl", "white hair"}})
	vision, _ := json.Marshal(LightweightVisionResult{
		DetailedCaption: "A woman standing in a neon city",
		Composition:     "upper body",
		Background:      "night city",
	})
	sources := []ImagePromptSourceDTO{
		{Kind: "manual_tags", State: "ready", DataJSON: string(manual)},
		{Kind: "lightweight_vision", State: "ready", DataJSON: string(vision)},
	}

	illustrious, _ := promptprofile.BuiltIn(promptprofile.IllustriousID)
	il := fallbackImagePrompt(illustrious, sources)
	if !strings.Contains(il.Positive, "1girl") || !strings.Contains(il.Positive, "masterpiece") {
		t.Fatalf("Illustrious fallback = %q", il.Positive)
	}

	flux, _ := promptprofile.BuiltIn(promptprofile.FluxID)
	fluxResult := fallbackImagePrompt(flux, sources)
	if !strings.Contains(fluxResult.Positive, "A woman standing in a neon city") {
		t.Fatalf("FLUX fallback = %q", fluxResult.Positive)
	}
	if strings.Contains(fluxResult.Positive, "masterpiece") {
		t.Fatalf("FLUX fallback inherited tag-style quality boilerplate: %q", fluxResult.Positive)
	}
}

func TestSourceContextPreservesProvenance(t *testing.T) {
	context := sourceContext([]ImagePromptSourceDTO{{
		Kind: "tagger", Label: "Tagger", State: "ready",
		Engine: "onnx", ModelID: "tagger-model", ModelVersion: "1",
		DataJSON: `{"tags":["1girl"]}`,
	}})
	sources, ok := context["sources"].([]map[string]any)
	if !ok || len(sources) != 1 {
		t.Fatalf("context sources = %#v", context["sources"])
	}
	if sources[0]["engine"] != "onnx" || sources[0]["modelId"] != "tagger-model" {
		t.Fatalf("provenance missing: %#v", sources[0])
	}
}

func TestBoundedSourceJSONLimitsLargeEvidence(t *testing.T) {
	raw := strings.Repeat("x", 25000)
	bounded := boundedSourceJSON(raw)
	if len([]rune(bounded)) >= len([]rune(raw)) {
		t.Fatal("large source was not bounded")
	}
	if !strings.Contains(bounded, "truncated") {
		t.Fatalf("bounded source lacks truncation marker: %q", bounded[:80])
	}
}
