package commands

import "testing"

func TestCommandsReturnErrorsForMissingEntities(t *testing.T) {
	cmd := setupCommands(t)

	if _, err := cmd.UpdateLibrary(999999, "missing", "/tmp/missing"); err == nil {
		t.Error("UpdateLibrary should return an error for a missing library")
	}
	if _, err := cmd.GetAssetDetail(999999); err == nil {
		t.Error("GetAssetDetail should return an error for a missing asset")
	}
	if _, err := cmd.UpdatePost(999999, "missing", "", "", "draft"); err == nil {
		t.Error("UpdatePost should return an error for a missing post")
	}
}

func TestCreatePostRecordRejectsInvalidPublishedAt(t *testing.T) {
	cmd := setupCommands(t)

	if _, err := cmd.CreatePostRecord(PostRecordRequest{
		AssetIDs:    []int64{1},
		TargetID:    1,
		AccountID:   1,
		PublishedAt: "not-a-date",
	}); err == nil {
		t.Error("CreatePostRecord should return an error for invalid publishedAt")
	}
}
