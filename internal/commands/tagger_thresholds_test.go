package commands

import (
	"os"
	"testing"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/infrastructure/db"
)

func openTaggerThresholdTestCommands(t *testing.T) *AppCommands {
	t.Helper()
	dir, err := os.MkdirTemp("", "lumine-tagger-thresholds-*")
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = os.RemoveAll(dir) })

	database, err := db.Open(dir)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = database.Close() })

	return &AppCommands{settingRepo: db.NewAppSettingRepo(database)}
}

func floatPtr(value float64) *float64 { return &value }

func TestTaggerThresholdOverridesPersistence(t *testing.T) {
	cmd := openTaggerThresholdTestCommands(t)

	if got, err := cmd.GetTaggerThresholdOverridesJSON(); err != nil {
		t.Fatal(err)
	} else if got != `{"general":null,"character":null,"rating":null}` {
		t.Fatalf("default overrides = %s", got)
	}

	if err := cmd.SetTaggerThresholdOverridesJSON(`{"general":0.2,"character":0.75,"rating":null}`); err != nil {
		t.Fatal(err)
	}
	value, err := cmd.getTaggerThresholdOverrides()
	if err != nil {
		t.Fatal(err)
	}
	if value.General == nil || *value.General != 0.2 {
		t.Fatalf("general override = %+v", value.General)
	}
	if value.Character == nil || *value.Character != 0.75 {
		t.Fatalf("character override = %+v", value.Character)
	}
	if value.Rating != nil {
		t.Fatalf("rating override = %+v, want nil", value.Rating)
	}

	if err := cmd.SetTaggerThresholdOverridesJSON(`{"general":1.1}`); err == nil {
		t.Fatal("out-of-range threshold must fail")
	}
}

func TestTaggerThresholdOverridesPayloadAndFiltering(t *testing.T) {
	overrides := taggerThresholdOverrides{
		General:   floatPtr(0.5),
		Character: floatPtr(0.9),
		Rating:    floatPtr(0.4),
	}
	payload := map[string]any{"filePath": "test.png"}
	addTaggerThresholdOverridesToPayload(payload, overrides)
	if payload["generalThreshold"] != 0.5 ||
		payload["characterThreshold"] != 0.9 ||
		payload["ratingThreshold"] != 0.4 {
		t.Fatalf("unexpected Tagger threshold payload: %+v", payload)
	}

	result, err := taggerResultFromResponse(ai.InferenceResponse{Payload: map[string]any{
		"generalThreshold":   0.1,
		"characterThreshold": 0.1,
		"ratingThreshold":    0.1,
		"generalTags": []any{
			map[string]any{"name": "high", "score": 0.8},
			map[string]any{"name": "low", "score": 0.3},
		},
		"characterTags": []any{
			map[string]any{"name": "character_high", "score": 0.95},
			map[string]any{"name": "character_low", "score": 0.7},
		},
		"ratingScores": []any{
			map[string]any{"name": "explicit", "score": 0.7},
			map[string]any{"name": "questionable", "score": 0.2},
		},
	}})
	if err != nil {
		t.Fatal(err)
	}
	applyTaggerThresholdOverrides(&result, overrides)
	if result.GeneralThreshold != 0.5 || len(result.GeneralTags) != 1 || result.GeneralTags[0].Name != "high" {
		t.Fatalf("unexpected general threshold result: %+v", result)
	}
	if result.CharacterThreshold != 0.9 || len(result.CharacterTags) != 1 {
		t.Fatalf("unexpected character threshold result: %+v", result)
	}
	if result.RatingThreshold != 0.4 || len(result.RatingScores) != 1 || result.Rating != "explicit" {
		t.Fatalf("unexpected rating threshold result: %+v", result)
	}
}
