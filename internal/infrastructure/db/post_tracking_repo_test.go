package db

import (
	"os"
	"testing"

	"github.com/kataage/lumine/internal/domain"
)

func TestPostTrackingRecordRoundTrip(t *testing.T) {
	dir, err := os.MkdirTemp("", "lumine-post-tracking-*")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(dir)

	database, err := Open(dir)
	if err != nil {
		t.Fatalf("Open failed: %v", err)
	}
	defer database.Close()

	libraryRepo := NewLibraryRepo(database)
	assetRepo := NewAssetRepo(database)
	postRepo := NewPostRepo(database)
	targetRepo := NewPostTargetRepo(database)
	accountRepo := NewPostAccountRepo(database)

	library, err := libraryRepo.Create("Images", "/tmp/lumine-post-images")
	if err != nil {
		t.Fatalf("create library: %v", err)
	}

	createAsset := func(name string) int64 {
		t.Helper()
		id, createErr := assetRepo.Create(&domain.Asset{
			LibraryID:   library.ID,
			FolderPath:  "/tmp/lumine-post-images",
			FileName:    name,
			FilePath:    "/tmp/lumine-post-images/" + name,
			Extension:   ".png",
			FileSize:    1024,
			ThumbStatus: domain.ThumbStatusNone,
			StatusLabel: domain.StatusUnsorted,
		})
		if createErr != nil {
			t.Fatalf("create asset %s: %v", name, createErr)
		}
		return id
	}

	assetA := createAsset("a.png")
	assetB := createAsset("b.png")

	targetID, err := targetRepo.Create("Pixiv", "pixiv")
	if err != nil {
		t.Fatalf("create target: %v", err)
	}
	accountID, err := accountRepo.Create(targetID, "main", "kataage")
	if err != nil {
		t.Fatalf("create account: %v", err)
	}

	postID, err := postRepo.CreateTrackingRecord(PostRecordCreate{
		Title:                "テスト投稿",
		Body:                 "投稿時点のキャプション",
		Hashtags:             "白上フブキ\n浴衣",
		PlatformMetadataJSON: `{"ageRestriction":"all","aiGenerated":true}`,
		AssetIDs:             []int64{assetA, assetB, assetA},
		TargetID:             targetID,
		AccountID:            accountID,
		ExternalPostID:       "123",
		ExternalURL:          "https://example.invalid/post/123",
	})
	if err != nil {
		t.Fatalf("CreateTrackingRecord: %v", err)
	}
	if postID == 0 {
		t.Fatal("expected non-zero post ID")
	}

	records, err := postRepo.ListTrackingRecords(0, 20)
	if err != nil {
		t.Fatalf("ListTrackingRecords: %v", err)
	}
	if len(records) != 1 {
		t.Fatalf("expected 1 record, got %d", len(records))
	}
	record := records[0]
	if record.ID != postID {
		t.Errorf("expected post ID %d, got %d", postID, record.ID)
	}
	if record.TargetName != "Pixiv" || record.TargetKind != "pixiv" {
		t.Errorf("unexpected target: %s/%s", record.TargetName, record.TargetKind)
	}
	if record.AccountDisplay != "main" || record.AccountIdentifier != "kataage" {
		t.Errorf("unexpected account: %s/%s", record.AccountDisplay, record.AccountIdentifier)
	}
	if record.Body != "投稿時点のキャプション" || record.Hashtags != "白上フブキ\n浴衣" {
		t.Errorf("publication copy was not preserved: body=%q hashtags=%q", record.Body, record.Hashtags)
	}
	if record.PlatformMetadataJSON != `{"ageRestriction":"all","aiGenerated":true}` {
		t.Errorf("unexpected platform metadata: %s", record.PlatformMetadataJSON)
	}
	if record.ExternalPostID != "123" || record.ExternalURL != "https://example.invalid/post/123" {
		t.Errorf("unexpected external reference: id=%s url=%s", record.ExternalPostID, record.ExternalURL)
	}
	if len(record.AssetIDs) != 2 || record.AssetIDs[0] != assetA || record.AssetIDs[1] != assetB {
		t.Errorf("expected deduplicated ordered asset IDs [%d %d], got %v", assetA, assetB, record.AssetIDs)
	}
	if record.PublishedAt == nil {
		t.Error("expected PublishedAt to be set")
	}

	byAsset, err := postRepo.GetTrackingRecordsByAsset(assetB)
	if err != nil {
		t.Fatalf("GetTrackingRecordsByAsset: %v", err)
	}
	if len(byAsset) != 1 || byAsset[0].ID != postID {
		t.Fatalf("expected record %d for asset %d, got %+v", postID, assetB, byAsset)
	}
}

func TestPostTrackingRejectsMismatchedAccount(t *testing.T) {
	dir, err := os.MkdirTemp("", "lumine-post-tracking-*")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(dir)

	database, err := Open(dir)
	if err != nil {
		t.Fatalf("Open failed: %v", err)
	}
	defer database.Close()

	library, _ := NewLibraryRepo(database).Create("Images", "/tmp/lumine-mismatch")
	assetID, err := NewAssetRepo(database).Create(&domain.Asset{
		LibraryID:   library.ID,
		FolderPath:  "/tmp/lumine-mismatch",
		FileName:    "a.png",
		FilePath:    "/tmp/lumine-mismatch/a.png",
		Extension:   ".png",
		ThumbStatus: domain.ThumbStatusNone,
		StatusLabel: domain.StatusUnsorted,
	})
	if err != nil {
		t.Fatalf("create asset: %v", err)
	}

	targetRepo := NewPostTargetRepo(database)
	accountRepo := NewPostAccountRepo(database)
	targetA, _ := targetRepo.Create("Pixiv", "pixiv")
	targetB, _ := targetRepo.Create("X", "twitter")
	accountB, _ := accountRepo.Create(targetB, "x-main", "@kataage")

	if _, err := NewPostRepo(database).CreateTrackingRecord(PostRecordCreate{
		Title: "bad", AssetIDs: []int64{assetID}, TargetID: targetA, AccountID: accountB,
	}); err == nil {
		t.Fatal("expected mismatched target/account to be rejected")
	}
}
