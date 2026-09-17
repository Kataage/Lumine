package db

import (
	"os"
	"testing"

	"github.com/kataage/lumine/internal/domain"
)

func TestCreativeWorkflowRoundTrip(t *testing.T) {
	dir, err := os.MkdirTemp("", "lumine-creative-*")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(dir)

	database, err := Open(dir)
	if err != nil {
		t.Fatalf("Open failed: %v", err)
	}
	defer database.Close()

	for _, table := range []string{"works", "work_assets", "generation_groups", "generation_group_assets", "asset_relations", "work_posts"} {
		var count int
		if err := database.QueryRow("SELECT COUNT(*) FROM " + table).Scan(&count); err != nil {
			t.Fatalf("creative table %s unavailable: %v", table, err)
		}
	}

	library, err := NewLibraryRepo(database).Create("Creative", "/tmp/lumine-creative")
	if err != nil {
		t.Fatal(err)
	}
	assetRepo := NewAssetRepo(database)
	createAsset := func(name string) int64 {
		t.Helper()
		id, createErr := assetRepo.Create(&domain.Asset{
			LibraryID: library.ID, FolderPath: "/tmp/lumine-creative", FileName: name,
			FilePath: "/tmp/lumine-creative/" + name, Extension: ".png",
			ThumbStatus: domain.ThumbStatusNone, StatusLabel: domain.StatusUnsorted,
		})
		if createErr != nil {
			t.Fatalf("create asset %s: %v", name, createErr)
		}
		return id
	}
	assetA := createAsset("base.png")
	assetB := createAsset("variation.png")
	assetC := createAsset("upscale.png")

	repo := NewCreativeRepo(database)
	work, err := repo.CreateWork("夏祭りフブキ", "作品単位のテスト", []int64{assetA, assetB})
	if err != nil {
		t.Fatalf("CreateWork: %v", err)
	}
	if work.CoverAssetID == nil || *work.CoverAssetID != assetA {
		t.Fatalf("expected first asset as cover, got %+v", work.CoverAssetID)
	}
	if err := repo.AddAssetsToWork(work.ID, []int64{assetC, assetA}); err != nil {
		t.Fatalf("AddAssetsToWork: %v", err)
	}
	workAssets, err := repo.GetWorkAssetIDs(work.ID)
	if err != nil || len(workAssets) != 3 || workAssets[0] != assetA || workAssets[1] != assetB || workAssets[2] != assetC {
		t.Fatalf("unexpected work order: %v err=%v", workAssets, err)
	}

	group, err := repo.CreateGenerationGroup(&domain.GenerationGroup{
		WorkID: &work.ID, Name: "浴衣 seed variations", Prompt: "1girl, yukata", NegativePrompt: "low quality",
		ModelName: "test-xl", Sampler: "euler", Scheduler: "normal", Steps: 24, CFGScale: 5.5, Notes: "same prompt",
	}, []int64{assetA, assetB})
	if err != nil {
		t.Fatalf("CreateGenerationGroup: %v", err)
	}
	if group.WorkID == nil || *group.WorkID != work.ID || group.Prompt != "1girl, yukata" {
		t.Fatalf("group metadata not preserved: %+v", group)
	}
	if err := repo.AddAssetsToGenerationGroup(group.ID, []int64{assetC}); err != nil {
		t.Fatalf("AddAssetsToGenerationGroup: %v", err)
	}
	groupAssets, err := repo.GetGenerationGroupAssetIDs(group.ID)
	if err != nil || len(groupAssets) != 3 || groupAssets[2] != assetC {
		t.Fatalf("unexpected group assets: %v err=%v", groupAssets, err)
	}

	relation, err := repo.CreateRelation(assetA, assetC, "upscale", "2x upscale")
	if err != nil {
		t.Fatalf("CreateRelation: %v", err)
	}
	if relation.ParentAssetID != assetA || relation.ChildAssetID != assetC || relation.RelationType != "upscale" {
		t.Fatalf("unexpected relation: %+v", relation)
	}
	relations, err := repo.GetRelationsByAsset(assetC)
	if err != nil || len(relations) != 1 || relations[0].ID != relation.ID {
		t.Fatalf("relation lookup failed: %+v err=%v", relations, err)
	}
	if _, err := repo.CreateRelation(assetA, assetA, "derived", ""); err == nil {
		t.Fatal("expected self relation to be rejected")
	}
}
