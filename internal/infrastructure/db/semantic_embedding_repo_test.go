package db

import (
	"math"
	"testing"

	"github.com/kataage/lumine/internal/domain"
)

func createSemanticTestAsset(t *testing.T, database *DB, libraryID int64, folder, name string) int64 {
	t.Helper()
	id, err := NewAssetRepo(database).Create(&domain.Asset{
		LibraryID:   libraryID,
		FolderPath:  folder,
		FileName:    name,
		FilePath:    folder + "/" + name,
		Extension:   ".png",
		FileSize:    100,
		ThumbStatus: domain.ThumbStatusNone,
		StatusLabel: domain.StatusUnsorted,
	})
	if err != nil {
		t.Fatal(err)
	}
	return id
}

func markSemanticReady(t *testing.T, database *DB, assetID int64, engine, model, version string) {
	t.Helper()
	_, err := database.Exec(`
		INSERT INTO ai_asset_analysis (
			asset_id, capability, state, engine, model_id, model_version, result_json, analyzed_at
		) VALUES (?, 'semantic_search', 'ready', ?, ?, ?, '{}', CURRENT_TIMESTAMP)
		ON CONFLICT(asset_id, capability) DO UPDATE SET
			state = 'ready',
			engine = excluded.engine,
			model_id = excluded.model_id,
			model_version = excluded.model_version
	`, assetID, engine, model, version)
	if err != nil {
		t.Fatal(err)
	}
}

func TestSemanticEmbeddingUpsertNormalizesAndSearches(t *testing.T) {
	database := openAIAnalysisTestDB(t)
	lib, err := NewLibraryRepo(database).Create("Semantic", "/tmp/semantic")
	if err != nil {
		t.Fatal(err)
	}
	repo := NewSemanticEmbeddingRepo(database)

	first := createSemanticTestAsset(t, database, lib.ID, "/tmp/semantic/a", "first.png")
	second := createSemanticTestAsset(t, database, lib.ID, "/tmp/semantic/a", "second.png")
	third := createSemanticTestAsset(t, database, lib.ID, "/tmp/semantic/b", "third.png")

	for _, id := range []int64{first, second, third} {
		markSemanticReady(t, database, id, "siglip2-onnx", "siglip2-base", "1")
	}
	if err := repo.Upsert(first, "siglip2-onnx", "siglip2-base", "1", []float32{10, 0}); err != nil {
		t.Fatal(err)
	}
	if err := repo.Upsert(second, "siglip2-onnx", "siglip2-base", "1", []float32{1, 1}); err != nil {
		t.Fatal(err)
	}
	if err := repo.Upsert(third, "siglip2-onnx", "siglip2-base", "1", []float32{-1, 0}); err != nil {
		t.Fatal(err)
	}

	stored, err := repo.Get(first)
	if err != nil {
		t.Fatal(err)
	}
	if stored == nil || len(stored.Vector) != 2 {
		t.Fatalf("unexpected stored embedding: %+v", stored)
	}
	if math.Abs(float64(stored.Vector[0]-1)) > 1e-6 || math.Abs(float64(stored.Vector[1])) > 1e-6 {
		t.Fatalf("vector was not normalized: %+v", stored.Vector)
	}

	result, err := repo.Search([]float32{2, 0}, SemanticSearchQuery{
		LibraryID:    lib.ID,
		Engine:       "siglip2-onnx",
		ModelID:      "siglip2-base",
		ModelVersion: "1",
		Limit:        10,
	})
	if err != nil {
		t.Fatal(err)
	}
	if result.TotalCount != 3 || len(result.Hits) != 3 {
		t.Fatalf("unexpected search result: %+v", result)
	}
	if result.Hits[0].AssetID != first || result.Hits[1].AssetID != second || result.Hits[2].AssetID != third {
		t.Fatalf("cosine order mismatch: %+v", result.Hits)
	}
}

func TestSemanticSearchExcludesStaleAndWrongModel(t *testing.T) {
	database := openAIAnalysisTestDB(t)
	lib, err := NewLibraryRepo(database).Create("Semantic", "/tmp/semantic-stale")
	if err != nil {
		t.Fatal(err)
	}
	repo := NewSemanticEmbeddingRepo(database)
	ready := createSemanticTestAsset(t, database, lib.ID, "/tmp/semantic-stale", "ready.png")
	stale := createSemanticTestAsset(t, database, lib.ID, "/tmp/semantic-stale", "stale.png")
	wrong := createSemanticTestAsset(t, database, lib.ID, "/tmp/semantic-stale", "wrong.png")

	markSemanticReady(t, database, ready, "engine", "model", "2")
	markSemanticReady(t, database, stale, "engine", "model", "2")
	markSemanticReady(t, database, wrong, "engine", "other", "2")
	if _, err := database.Exec(`
		UPDATE ai_asset_analysis SET state = 'stale'
		WHERE asset_id = ? AND capability = 'semantic_search'
	`, stale); err != nil {
		t.Fatal(err)
	}

	for _, item := range []struct {
		id      int64
		modelID string
	}{
		{ready, "model"},
		{stale, "model"},
		{wrong, "other"},
	} {
		if err := repo.Upsert(item.id, "engine", item.modelID, "2", []float32{1, 0}); err != nil {
			t.Fatal(err)
		}
	}

	result, err := repo.Search([]float32{1, 0}, SemanticSearchQuery{
		LibraryID:    lib.ID,
		Engine:       "engine",
		ModelID:      "model",
		ModelVersion: "2",
		Limit:        10,
	})
	if err != nil {
		t.Fatal(err)
	}
	if result.TotalCount != 1 || len(result.Hits) != 1 || result.Hits[0].AssetID != ready {
		t.Fatalf("stale/wrong-model embeddings leaked into search: %+v", result)
	}
}

func TestSemanticSearchFiltersAndSimilarExclusion(t *testing.T) {
	database := openAIAnalysisTestDB(t)
	lib, err := NewLibraryRepo(database).Create("Semantic", "/tmp/semantic-filter")
	if err != nil {
		t.Fatal(err)
	}
	repo := NewSemanticEmbeddingRepo(database)
	assetRepo := NewAssetRepo(database)
	first := createSemanticTestAsset(t, database, lib.ID, "/tmp/semantic-filter/a", "first.png")
	second := createSemanticTestAsset(t, database, lib.ID, "/tmp/semantic-filter/a/sub", "second.png")
	third := createSemanticTestAsset(t, database, lib.ID, "/tmp/semantic-filter/b", "third.png")

	value, _ := assetRepo.GetByID(second)
	value.Rating = 5
	if err := assetRepo.Update(value); err != nil {
		t.Fatal(err)
	}

	for _, id := range []int64{first, second, third} {
		markSemanticReady(t, database, id, "engine", "model", "1")
		if err := repo.Upsert(id, "engine", "model", "1", []float32{1, 0}); err != nil {
			t.Fatal(err)
		}
	}

	result, err := repo.Search([]float32{1, 0}, SemanticSearchQuery{
		LibraryID:    lib.ID,
		FolderPath:   "/tmp/semantic-filter/a",
		Recurse:      true,
		Rating:       5,
		Engine:       "engine",
		ModelID:      "model",
		ModelVersion: "1",
		ExcludeID:    first,
		Limit:        10,
	})
	if err != nil {
		t.Fatal(err)
	}
	if result.TotalCount != 1 || result.Hits[0].AssetID != second {
		t.Fatalf("scope/filter/exclusion mismatch: %+v", result)
	}
}

func TestSemanticEmbeddingRejectsInvalidVectors(t *testing.T) {
	database := openAIAnalysisTestDB(t)
	repo := NewSemanticEmbeddingRepo(database)
	if err := repo.Upsert(1, "engine", "model", "1", nil); err == nil {
		t.Fatal("empty vector should fail")
	}
	if err := repo.Upsert(1, "engine", "model", "1", []float32{0, 0}); err == nil {
		t.Fatal("zero vector should fail")
	}
}
