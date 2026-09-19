package commands

import (
	"testing"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/domain"
)

func TestTaggerResultFromResponseFiltersAndDeduplicates(t *testing.T) {
	response := ai.InferenceResponse{Payload: map[string]any{
		"generalThreshold":   0.35,
		"characterThreshold": 0.80,
		"ratingThreshold":    0.0,
		"generalTags": []any{
			map[string]any{"name": "1girl", "score": 0.95},
			map[string]any{"name": "1girl", "score": 0.91},
			map[string]any{"name": "solo", "score": 0.70},
			map[string]any{"name": "low_score", "score": 0.20},
		},
		"characterTags": []any{
			map[string]any{"name": "test_character", "score": 0.88},
			map[string]any{"name": "weak_character", "score": 0.40},
		},
		"ratingScores": []any{
			map[string]any{"name": "explicit", "score": 0.82},
			map[string]any{"name": "questionable", "score": 0.12},
		},
	}}

	result, err := taggerResultFromResponse(response)
	if err != nil {
		t.Fatalf("taggerResultFromResponse: %v", err)
	}
	if len(result.GeneralTags) != 2 {
		t.Fatalf("general tags = %+v, want 2 filtered/deduplicated tags", result.GeneralTags)
	}
	if result.GeneralTags[0].Name != "1girl" || result.GeneralTags[0].Score != 0.95 {
		t.Fatalf("unexpected top general tag: %+v", result.GeneralTags[0])
	}
	if len(result.CharacterTags) != 1 || result.CharacterTags[0].Name != "test_character" {
		t.Fatalf("unexpected character tags: %+v", result.CharacterTags)
	}
	if result.Rating != "explicit" {
		t.Fatalf("rating = %q, want explicit", result.Rating)
	}

	suggestions := taggerSuggestionsFromResult(result)
	var general, character, rating int
	for _, suggestion := range suggestions {
		switch suggestion.Kind {
		case domain.AITagSuggestionGeneral:
			general++
			if suggestion.Threshold != 0.35 {
				t.Fatalf("general threshold = %v", suggestion.Threshold)
			}
		case domain.AITagSuggestionCharacter:
			character++
			if suggestion.Threshold != 0.80 {
				t.Fatalf("character threshold = %v", suggestion.Threshold)
			}
		case domain.AITagSuggestionRating:
			rating++
		}
	}
	if general != 2 || character != 1 || rating != 2 {
		t.Fatalf("suggestion counts general=%d character=%d rating=%d", general, character, rating)
	}
}

func TestTaggerResultFromResponseRejectsInvalidScores(t *testing.T) {
	_, err := taggerResultFromResponse(ai.InferenceResponse{Payload: map[string]any{
		"generalTags": []any{
			map[string]any{"name": "broken", "score": 1.5},
		},
	}})
	if err == nil {
		t.Fatal("expected invalid score to fail")
	}
}
