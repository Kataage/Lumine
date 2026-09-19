package commands

import (
	"encoding/json"
	"testing"

	"github.com/kataage/lumine/internal/ai"
)

func TestLightweightVisionResultFromResponse(t *testing.T) {
	result, err := lightweightVisionResultFromResponse(ai.InferenceResponse{
		Payload: map[string]any{
			"shortCaption": " person ",
			"detailedCaption": " person in a room ",
			"subject": " person ",
			"background": " room ",
			"composition": " centered ",
			"viewpoint": " eye level ",
			"visibleText": []any{"LUMINE", "", 123},
			"completionTokens": float64(37),
		},
	})
	if err != nil {
		t.Fatalf("lightweightVisionResultFromResponse: %v", err)
	}
	if result.SchemaVersion != 1 {
		t.Fatalf("schema version = %d", result.SchemaVersion)
	}
	if result.ShortCaption != "person" || result.Viewpoint != "eye level" {
		t.Fatalf("unexpected result: %+v", result)
	}
	if len(result.VisibleText) != 1 || result.VisibleText[0] != "LUMINE" {
		t.Fatalf("visible text = %v", result.VisibleText)
	}
	if result.CompletionTokens != 37 {
		t.Fatalf("completion tokens = %d", result.CompletionTokens)
	}
	if len(result.Notes) == 0 {
		t.Fatal("result should explain uncalibrated confidence")
	}

	encoded, err := json.Marshal(result)
	if err != nil {
		t.Fatal(err)
	}
	var roundTrip LightweightVisionResult
	if err := json.Unmarshal(encoded, &roundTrip); err != nil {
		t.Fatal(err)
	}
	if roundTrip.Subject != "person" {
		t.Fatalf("round trip subject = %q", roundTrip.Subject)
	}
}

func TestLightweightVisionResultRejectsEmptyPayload(t *testing.T) {
	if _, err := lightweightVisionResultFromResponse(ai.InferenceResponse{
		Payload: map[string]any{},
	}); err == nil {
		t.Fatal("empty analysis should fail")
	}
}
