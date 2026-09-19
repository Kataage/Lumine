package commands

import (
	"errors"
	"testing"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/ai/llamacpp"
)

func TestValidateAdvancedVisionRequest(t *testing.T) {
	cases := []struct {
		name      string
		operation string
		ids       []int64
		wantErr   bool
	}{
		{"deep one", llamacpp.AdvancedOperationDeep, []int64{1}, false},
		{"deep many", llamacpp.AdvancedOperationDeep, []int64{1, 2}, true},
		{"reverse one", llamacpp.AdvancedOperationReversePromptSupport, []int64{1}, false},
		{"compare two", llamacpp.AdvancedOperationCompare, []int64{1, 2}, false},
		{"compare one", llamacpp.AdvancedOperationCompare, []int64{1}, true},
		{"compare duplicate", llamacpp.AdvancedOperationCompare, []int64{1, 1}, true},
		{"unknown", "unknown", []int64{1}, true},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			err := validateAdvancedVisionRequest(tc.operation, tc.ids, "")
			if (err != nil) != tc.wantErr {
				t.Fatalf("error = %v, wantErr=%v", err, tc.wantErr)
			}
		})
	}
}

func TestAdvancedVisionResultFromResponse(t *testing.T) {
	result, err := advancedVisionResultFromResponse(ai.InferenceResponse{
		Payload: map[string]any{
			"summary":            " scene ",
			"subjects":           []string{"adult woman"},
			"environment":        " room ",
			"composition":        " centered ",
			"viewpoint":          " low angle ",
			"actions":            []string{"standing"},
			"relationships":      []string{},
			"context":            " illustration ",
			"differences":        []string{},
			"commonalities":      []string{},
			"reversePromptHints": []string{"low angle"},
			"visibleText":        []string{"LUMINE"},
			"notes":              []string{},
			"completionTokens":   float64(42),
		},
	})
	if err != nil {
		t.Fatalf("advancedVisionResultFromResponse: %v", err)
	}
	if result.SchemaVersion != 1 || result.Summary != "scene" || result.Viewpoint != "low angle" {
		t.Fatalf("unexpected result: %+v", result)
	}
	if result.CompletionTokens != 42 {
		t.Fatalf("completion tokens = %d", result.CompletionTokens)
	}
}

func TestAdvancedVisionResultRejectsEmptyPayload(t *testing.T) {
	if _, err := advancedVisionResultFromResponse(ai.InferenceResponse{Payload: map[string]any{}}); err == nil {
		t.Fatal("empty Advanced Vision response should fail")
	}
}

func TestAdvancedVisionStatusExposesReplaceableCandidates(t *testing.T) {
	cmd := setupCommands(t)
	status := cmd.GetAdvancedVisionStatus()
	if len(status.Models) < 2 {
		t.Fatalf("candidate count = %d, want at least 2", len(status.Models))
	}
	seen := map[string]bool{}
	for _, model := range status.Models {
		seen[model.ID] = true
	}
	if !seen[llamacpp.AdvancedQwen3VL2BModelID] || !seen[llamacpp.AdvancedMiniCPM46ModelID] {
		t.Fatalf("missing candidates: %v", seen)
	}
}

func TestRunAdvancedVisionRequiresFeatureOptIn(t *testing.T) {
	cmd := setupCommands(t)
	manager := ai.NewManager(t.TempDir(), cmd.GetAISettings)
	cmd.SetAIManager(manager)
	_, err := cmd.RunAdvancedVision(llamacpp.AdvancedOperationDeep, []int64{1}, "")
	if !errors.Is(err, ErrAdvancedVisionDisabled) {
		t.Fatalf("RunAdvancedVision error = %v, want %v", err, ErrAdvancedVisionDisabled)
	}
}
