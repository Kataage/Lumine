package db

import (
	"testing"

	"github.com/kataage/lumine/internal/domain"
)

func TestAdvancedVisionRunRepoLifecycle(t *testing.T) {
	database := openAIAnalysisTestDB(t)
	repo := NewAdvancedVisionRunRepo(database)

	lib, err := NewLibraryRepo(database).Create("Advanced Vision Test", "/tmp/advanced-vision")
	if err != nil {
		t.Fatal(err)
	}
	assetRepo := NewAssetRepo(database)
	createAsset := func(name string) int64 {
		id, err := assetRepo.Create(&domain.Asset{
			LibraryID:   lib.ID,
			FolderPath:  "/tmp/advanced-vision",
			FileName:    name,
			FilePath:    "/tmp/advanced-vision/" + name,
			Extension:   ".png",
			FileSize:    123,
			ThumbStatus: domain.ThumbStatusNone,
			StatusLabel: domain.StatusUnsorted,
		})
		if err != nil {
			t.Fatal(err)
		}
		return id
	}
	first := createAsset("first.png")
	second := createAsset("second.png")

	run, err := repo.CreateRunning(
		"compare_images",
		"違いを詳しく",
		"llamacpp-advanced-vlm",
		"model",
		"version",
		[]int64{first, second},
	)
	if err != nil {
		t.Fatalf("CreateRunning: %v", err)
	}
	if run.State != domain.AdvancedVisionRunRunning {
		t.Fatalf("state = %s", run.State)
	}
	if len(run.AssetIDs) != 2 || run.AssetIDs[0] != first || run.AssetIDs[1] != second {
		t.Fatalf("asset order = %v", run.AssetIDs)
	}

	if err := repo.Complete(run.ID, `{"schemaVersion":1,"summary":"done"}`); err != nil {
		t.Fatalf("Complete: %v", err)
	}
	completed, err := repo.GetByID(run.ID)
	if err != nil {
		t.Fatal(err)
	}
	if completed == nil || completed.State != domain.AdvancedVisionRunReady || completed.CompletedAt == nil {
		t.Fatalf("completed run = %+v", completed)
	}

	bySecond, err := repo.ListByAsset(second, 10)
	if err != nil {
		t.Fatal(err)
	}
	if len(bySecond) != 1 || bySecond[0].ID != run.ID {
		t.Fatalf("ListByAsset = %+v", bySecond)
	}

	failed, err := repo.CreateRunning(
		"analyze_deep", "", "engine", "model", "version", []int64{first},
	)
	if err != nil {
		t.Fatal(err)
	}
	if err := repo.Fail(failed.ID, "runtime failed"); err != nil {
		t.Fatal(err)
	}
	failed, err = repo.GetByID(failed.ID)
	if err != nil {
		t.Fatal(err)
	}
	if failed == nil || failed.State != domain.AdvancedVisionRunFailed || failed.ErrorMessage != "runtime failed" {
		t.Fatalf("failed run = %+v", failed)
	}
}
