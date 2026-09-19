package db

import (
	"testing"

	"github.com/kataage/lumine/internal/domain"
)

func TestAITagSuggestionRepoReviewLifecycle(t *testing.T) {
	database := openAIAnalysisTestDB(t)
	repo := NewAITagSuggestionRepo(database)
	tagRepo := NewTagRepo(database)
	assetID := createAIRepoTestAsset(t, database, "/tmp/ai-tagger-review", "asset.png")

	manual, err := tagRepo.Create("manual_tag", "#123456")
	if err != nil {
		t.Fatalf("create manual tag: %v", err)
	}
	if err := tagRepo.SetAssetTags(assetID, []int64{manual.ID}); err != nil {
		t.Fatalf("assign manual tag: %v", err)
	}

	suggestions := []domain.AITagSuggestion{
		{Kind: domain.AITagSuggestionGeneral, Name: "1girl", Confidence: 0.97, Threshold: 0.35},
		{Kind: domain.AITagSuggestionCharacter, Name: "test_character", Confidence: 0.91, Threshold: 0.85},
		{Kind: domain.AITagSuggestionRating, Name: "explicit", Confidence: 0.88, Threshold: 0},
	}
	if err := repo.ReplaceForAsset(assetID, "tagger-engine", "tagger-model", "1.0", suggestions); err != nil {
		t.Fatalf("ReplaceForAsset: %v", err)
	}

	got, err := repo.ListByAsset(assetID)
	if err != nil {
		t.Fatalf("ListByAsset: %v", err)
	}
	if len(got) != 3 {
		t.Fatalf("suggestion count = %d, want 3", len(got))
	}

	var generalID, ratingID int64
	for _, value := range got {
		switch value.Kind {
		case domain.AITagSuggestionGeneral:
			generalID = value.ID
		case domain.AITagSuggestionRating:
			ratingID = value.ID
		}
		if value.State != domain.AITagSuggestionPending {
			t.Fatalf("initial suggestion state = %q, want pending", value.State)
		}
		if value.Engine != "tagger-engine" || value.ModelID != "tagger-model" || value.ModelVersion != "1.0" {
			t.Fatalf("suggestion provenance mismatch: %+v", value)
		}
	}

	accepted, err := repo.Accept(generalID)
	if err != nil {
		t.Fatalf("Accept general: %v", err)
	}
	if accepted == nil || accepted.Name != "1girl" {
		t.Fatalf("accepted tag = %+v, want 1girl", accepted)
	}

	if acceptedRating, err := repo.Accept(ratingID); err != nil {
		t.Fatalf("Accept rating: %v", err)
	} else if acceptedRating != nil {
		t.Fatalf("rating acceptance must not create a normal tag: %+v", acceptedRating)
	}

	assigned, err := tagRepo.GetByAssetID(assetID)
	if err != nil {
		t.Fatalf("GetByAssetID: %v", err)
	}
	names := map[string]bool{}
	for _, tag := range assigned {
		names[tag.Name] = true
	}
	if !names["manual_tag"] || !names["1girl"] {
		t.Fatalf("manual/accepted tags were not preserved: %+v", assigned)
	}
	if names["explicit"] {
		t.Fatalf("content rating must not be inserted into normal tags: %+v", assigned)
	}
}

func TestAITagSuggestionRepoBulkAndReanalysis(t *testing.T) {
	database := openAIAnalysisTestDB(t)
	repo := NewAITagSuggestionRepo(database)
	tagRepo := NewTagRepo(database)
	assetID := createAIRepoTestAsset(t, database, "/tmp/ai-tagger-bulk", "asset.png")

	first := []domain.AITagSuggestion{
		{Kind: domain.AITagSuggestionGeneral, Name: "solo", Confidence: 0.95, Threshold: 0.35},
		{Kind: domain.AITagSuggestionCharacter, Name: "old_character", Confidence: 0.89, Threshold: 0.85},
		{Kind: domain.AITagSuggestionRating, Name: "general", Confidence: 0.99, Threshold: 0},
	}
	if err := repo.ReplaceForAsset(assetID, "engine", "model", "1", first); err != nil {
		t.Fatal(err)
	}
	if _, err := repo.AcceptAll(assetID); err != nil {
		t.Fatalf("AcceptAll: %v", err)
	}

	assigned, err := tagRepo.GetByAssetID(assetID)
	if err != nil {
		t.Fatal(err)
	}
	if len(assigned) != 2 {
		t.Fatalf("accepted normal tag count = %d, want 2", len(assigned))
	}

	second := []domain.AITagSuggestion{
		{Kind: domain.AITagSuggestionGeneral, Name: "upper_body", Confidence: 0.86, Threshold: 0.35},
		{Kind: domain.AITagSuggestionRating, Name: "sensitive", Confidence: 0.72, Threshold: 0},
	}
	if err := repo.ReplaceForAsset(assetID, "engine", "model", "2", second); err != nil {
		t.Fatalf("ReplaceForAsset reanalysis: %v", err)
	}
	got, err := repo.ListByAsset(assetID)
	if err != nil {
		t.Fatal(err)
	}
	if len(got) != 2 {
		t.Fatalf("reanalysis suggestions = %d, want 2", len(got))
	}
	for _, value := range got {
		if value.ModelVersion != "2" || value.State != domain.AITagSuggestionPending {
			t.Fatalf("unexpected reanalysis suggestion: %+v", value)
		}
	}

	assignedAfter, err := tagRepo.GetByAssetID(assetID)
	if err != nil {
		t.Fatal(err)
	}
	if len(assignedAfter) != 2 {
		t.Fatalf("reanalysis removed accepted user tags: %+v", assignedAfter)
	}

	if err := repo.RejectAll(assetID); err != nil {
		t.Fatalf("RejectAll: %v", err)
	}
	rejected, _ := repo.ListByAsset(assetID)
	for _, value := range rejected {
		if value.State != domain.AITagSuggestionRejected {
			t.Fatalf("suggestion not rejected: %+v", value)
		}
	}
}
