package commands

import (
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
	"github.com/kataage/lumine/internal/infrastructure/scanner"
)

func setupTestDB(t *testing.T) *db.DB {
	t.Helper()
	dir, err := os.MkdirTemp("", "lumine-cmd-test-*")
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { os.RemoveAll(dir) })

	database, err := db.Open(dir)
	if err != nil {
		t.Fatalf("Open db: %v", err)
	}
	t.Cleanup(func() { database.Close() })
	return database
}

func setupCommands(t *testing.T) *AppCommands {
	t.Helper()
	database := setupTestDB(t)
	scanSvc := scanner.NewScanner(
		db.NewAssetRepo(database),
		db.NewLibraryRepo(database),
		db.NewJobLogRepo(database),
	)
	scanSvc.SetSettingRepo(db.NewAppSettingRepo(database))
	return New(database, scanSvc)
}

func createTestLibrary(t *testing.T, cmd *AppCommands, name, rootPath string) *LibraryDTO {
	t.Helper()
	lib := cmd.AddLibrary(name, rootPath)
	if lib == nil {
		t.Fatal("AddLibrary returned nil")
	}
	return lib
}

func makeAsset(libraryID int64, folder, name string) *domain.Asset {
	return &domain.Asset{
		LibraryID:   libraryID,
		FolderPath:  folder,
		FileName:    name,
		FilePath:    filepath.Join(folder, name),
		Extension:   filepath.Ext(name),
		FileSize:    1024,
		ThumbStatus: domain.ThumbStatusNone,
		Rating:      0,
		StatusLabel: domain.StatusUnsorted,
		CreatedAtFS: time.Now(),
		ModifiedAtFS: time.Now(),
	}
}

func TestListLibraries(t *testing.T) {
	cmd := setupCommands(t)

	libs := cmd.ListLibraries()
	if len(libs) != 0 {
		t.Errorf("expected 0 libraries, got %d", len(libs))
	}

	createTestLibrary(t, cmd, "Lib1", "/tmp/lib1")
	createTestLibrary(t, cmd, "Lib2", "/tmp/lib2")

	libs = cmd.ListLibraries()
	if len(libs) != 2 {
		t.Errorf("expected 2 libraries, got %d", len(libs))
	}
}

func TestEnableDisableLibrary(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "TestLib", "/tmp/test")

	if !lib.IsEnabled {
		t.Error("new library should be enabled by default")
	}

	err := cmd.DisableLibrary(lib.ID)
	if err != nil {
		t.Fatalf("DisableLibrary: %v", err)
	}

	libs := cmd.ListLibraries()
	if libs[0].IsEnabled {
		t.Error("library should be disabled")
	}

	err = cmd.EnableLibrary(lib.ID)
	if err != nil {
		t.Fatalf("EnableLibrary: %v", err)
	}

	libs = cmd.ListLibraries()
	if !libs[0].IsEnabled {
		t.Error("library should be enabled")
	}
}

func TestUpdateLibrary(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "OldName", "/tmp/old")

	updated := cmd.UpdateLibrary(lib.ID, "NewName", "/tmp/new")
	if updated == nil {
		t.Fatal("UpdateLibrary returned nil")
	}
	if updated.Name != "NewName" {
		t.Errorf("expected NewName, got %s", updated.Name)
	}
	if updated.RootPath != "/tmp/new" {
		t.Errorf("expected /tmp/new, got %s", updated.RootPath)
	}
}

func TestRemoveLibrary(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "ToDelete", "/tmp/del")

	err := cmd.RemoveLibrary(lib.ID)
	if err != nil {
		t.Fatalf("RemoveLibrary: %v", err)
	}

	libs := cmd.ListLibraries()
	if len(libs) != 0 {
		t.Errorf("expected 0 libraries after delete, got %d", len(libs))
	}
}

func TestExcludedDirs(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "TestLib", "/tmp/test")

	dirs := cmd.GetExcludedDirs(lib.ID)
	if dirs != nil {
		t.Errorf("expected nil dirs, got %v", dirs)
	}

	err := cmd.SetExcludedDirs(lib.ID, []string{"/tmp/test/node_modules", "/tmp/test/.git"})
	if err != nil {
		t.Fatalf("SetExcludedDirs: %v", err)
	}

	dirs = cmd.GetExcludedDirs(lib.ID)
	if len(dirs) != 2 {
		t.Errorf("expected 2 dirs, got %d", len(dirs))
	}
	if dirs[0] != "/tmp/test/node_modules" {
		t.Errorf("expected /tmp/test/node_modules, got %s", dirs[0])
	}
}

func TestSupportedExtensions(t *testing.T) {
	cmd := setupCommands(t)

	exts := cmd.GetSupportedExtensions()
	if len(exts) == 0 {
		t.Error("expected some default extensions")
	}

	err := cmd.SetSupportedExtensions([]string{".jpg", ".png", ".raw"})
	if err != nil {
		t.Fatalf("SetSupportedExtensions: %v", err)
	}
}

func TestAssetRating(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "TestLib", "/tmp/test")

	assetRepo := db.NewAssetRepo(cmd.db)
	assetID, _ := assetRepo.Create(makeAsset(lib.ID, "/tmp/test", "test.png"))

	err := cmd.UpdateAssetRating(assetID, 4)
	if err != nil {
		t.Fatalf("UpdateAssetRating: %v", err)
	}

	asset, _ := assetRepo.GetByID(assetID)
	if asset.Rating != 4 {
		t.Errorf("expected rating 4, got %d", asset.Rating)
	}
}

func TestAssetStatus(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "TestLib", "/tmp/test")

	assetRepo := db.NewAssetRepo(cmd.db)
	assetID, _ := assetRepo.Create(makeAsset(lib.ID, "/tmp/test", "test.png"))

	err := cmd.UpdateAssetStatus(assetID, "reviewed")
	if err != nil {
		t.Fatalf("UpdateAssetStatus: %v", err)
	}

	asset, _ := assetRepo.GetByID(assetID)
	if asset.StatusLabel != domain.StatusReviewed {
		t.Errorf("expected reviewed, got %s", asset.StatusLabel)
	}
}

func TestAssetFavorite(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "TestLib", "/tmp/test")

	assetRepo := db.NewAssetRepo(cmd.db)
	assetID, _ := assetRepo.Create(makeAsset(lib.ID, "/tmp/test", "test.png"))

	err := cmd.ToggleAssetFavorite(assetID, true)
	if err != nil {
		t.Fatalf("ToggleAssetFavorite: %v", err)
	}

	asset, _ := assetRepo.GetByID(assetID)
	if !asset.IsFavorite {
		t.Error("expected favorite to be true")
	}
}

func TestAssetColorLabel(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "TestLib", "/tmp/test")

	assetRepo := db.NewAssetRepo(cmd.db)
	assetID, _ := assetRepo.Create(makeAsset(lib.ID, "/tmp/test", "test.png"))

	err := cmd.UpdateAssetColorLabel(assetID, "red")
	if err != nil {
		t.Fatalf("UpdateAssetColorLabel: %v", err)
	}

	asset, _ := assetRepo.GetByID(assetID)
	if asset.ColorLabel != "red" {
		t.Errorf("expected red, got %s", asset.ColorLabel)
	}
}

func TestBulkOperations(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "TestLib", "/tmp/test")

	assetRepo := db.NewAssetRepo(cmd.db)
	var ids []int64
	for i := 0; i < 5; i++ {
		id, _ := assetRepo.Create(makeAsset(lib.ID, "/tmp/test", "img"+itoa(i)+".png"))
		ids = append(ids, id)
	}

	if err := cmd.BulkUpdateRating(ids, 3); err != nil {
		t.Fatalf("BulkUpdateRating: %v", err)
	}
	if err := cmd.BulkUpdateStatus(ids, "candidate"); err != nil {
		t.Fatalf("BulkUpdateStatus: %v", err)
	}
	if err := cmd.BulkUpdateFavorite(ids, true); err != nil {
		t.Fatalf("BulkUpdateFavorite: %v", err)
	}
	if err := cmd.BulkUpdateColorLabel(ids, "blue"); err != nil {
		t.Fatalf("BulkUpdateColorLabel: %v", err)
	}

	for _, id := range ids {
		a, _ := assetRepo.GetByID(id)
		if a.Rating != 3 {
			t.Errorf("asset %d: expected rating 3, got %d", id, a.Rating)
		}
		if a.StatusLabel != domain.StatusCandidate {
			t.Errorf("asset %d: expected candidate, got %s", id, a.StatusLabel)
		}
		if !a.IsFavorite {
			t.Errorf("asset %d: expected favorite", id)
		}
		if a.ColorLabel != "blue" {
			t.Errorf("asset %d: expected blue, got %s", id, a.ColorLabel)
		}
	}
}

func TestAssetNote(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "TestLib", "/tmp/test")

	assetRepo := db.NewAssetRepo(cmd.db)
	assetID, _ := assetRepo.Create(makeAsset(lib.ID, "/tmp/test", "test.png"))

	err := cmd.UpdateAssetNote(assetID, "test memo content")
	if err != nil {
		t.Fatalf("UpdateAssetNote: %v", err)
	}

	dto := cmd.GetAssetDetail(assetID)
	if dto == nil {
		t.Fatal("GetAssetDetail returned nil")
	}
	if dto.NoteContent != "test memo content" {
		t.Errorf("expected 'test memo content', got %s", dto.NoteContent)
	}
}

func TestTagsCRUD(t *testing.T) {
	cmd := setupCommands(t)

	tags := cmd.ListTags()
	if len(tags) != 0 {
		t.Errorf("expected 0 tags, got %d", len(tags))
	}

	tag := cmd.CreateTag("character", "#ff0000")
	if tag == nil {
		t.Fatal("CreateTag returned nil")
	}
	if tag.Name != "character" {
		t.Errorf("expected character, got %s", tag.Name)
	}

	tags = cmd.ListTags()
	if len(tags) != 1 {
		t.Errorf("expected 1 tag, got %d", len(tags))
	}

	if err := cmd.DeleteTag(tag.ID); err != nil {
		t.Fatalf("DeleteTag: %v", err)
	}

	tags = cmd.ListTags()
	if len(tags) != 0 {
		t.Errorf("expected 0 tags after delete, got %d", len(tags))
	}
}

func TestPostsCRUD(t *testing.T) {
	cmd := setupCommands(t)

	posts := cmd.ListPosts(0, 50)
	if len(posts) != 0 {
		t.Errorf("expected 0 posts, got %d", len(posts))
	}

	p := cmd.CreatePostDraft("Test Post", "body content", "#art")
	if p == nil {
		t.Fatal("CreatePostDraft returned nil")
	}
	if p.Title != "Test Post" {
		t.Errorf("expected Test Post, got %s", p.Title)
	}
	if p.Status != "draft" {
		t.Errorf("expected draft, got %s", p.Status)
	}

	updated := cmd.UpdatePost(p.ID, "Updated Title", "new body", "#updated", "scheduled")
	if updated == nil {
		t.Fatal("UpdatePost returned nil")
	}
	if updated.Title != "Updated Title" {
		t.Errorf("expected Updated Title, got %s", updated.Title)
	}
	if updated.Status != "scheduled" {
		t.Errorf("expected scheduled, got %s", updated.Status)
	}

	posts = cmd.ListPosts(0, 50)
	if len(posts) != 1 {
		t.Errorf("expected 1 post, got %d", len(posts))
	}

	if err := cmd.DeletePost(p.ID); err != nil {
		t.Fatalf("DeletePost: %v", err)
	}

	posts = cmd.ListPosts(0, 50)
	if len(posts) != 0 {
		t.Errorf("expected 0 posts after delete, got %d", len(posts))
	}
}

func TestPostTargetsCRUD(t *testing.T) {
	cmd := setupCommands(t)

	targets := cmd.ListPostTargets()
	if len(targets) != 0 {
		t.Errorf("expected 0 targets, got %d", len(targets))
	}

	target := cmd.CreatePostTarget("Twitter", "twitter")
	if target == nil {
		t.Fatal("CreatePostTarget returned nil")
	}
	if target.Name != "Twitter" {
		t.Errorf("expected Twitter, got %s", target.Name)
	}

	targets = cmd.ListPostTargets()
	if len(targets) != 1 {
		t.Errorf("expected 1 target, got %d", len(targets))
	}

	if err := cmd.DeletePostTarget(target.ID); err != nil {
		t.Fatalf("DeletePostTarget: %v", err)
	}
}

func TestPostAccountsCRUD(t *testing.T) {
	cmd := setupCommands(t)

	target := cmd.CreatePostTarget("Pixiv", "pixiv")
	if target == nil {
		t.Fatal("CreatePostTarget returned nil")
	}

	accounts := cmd.ListPostAccounts()
	if len(accounts) != 0 {
		t.Errorf("expected 0 accounts, got %d", len(accounts))
	}

	account := cmd.CreatePostAccount(target.ID, "MyAccount", "@myaccount")
	if account == nil {
		t.Fatal("CreatePostAccount returned nil")
	}
	if account.DisplayName != "MyAccount" {
		t.Errorf("expected MyAccount, got %s", account.DisplayName)
	}

	accounts = cmd.ListPostAccounts()
	if len(accounts) != 1 {
		t.Errorf("expected 1 account, got %d", len(accounts))
	}

	if err := cmd.DeletePostAccount(account.ID); err != nil {
		t.Fatalf("DeletePostAccount: %v", err)
	}
}

func TestSettings(t *testing.T) {
	cmd := setupCommands(t)

	val := cmd.GetSetting("nonexistent")
	if val != "" {
		t.Errorf("expected empty string for nonexistent key, got %s", val)
	}

	if err := cmd.SetSetting("theme", `"dark"`); err != nil {
		t.Fatalf("SetSetting: %v", err)
	}

	val = cmd.GetSetting("theme")
	if val != `"dark"` {
		t.Errorf("expected \"dark\", got %s", val)
	}

	if err := cmd.SetSetting("theme", `"light"`); err != nil {
		t.Fatalf("SetSetting update: %v", err)
	}

	val = cmd.GetSetting("theme")
	if val != `"light"` {
		t.Errorf("expected \"light\", got %s", val)
	}
}

func TestGetAppBootstrap(t *testing.T) {
	cmd := setupCommands(t)
	createTestLibrary(t, cmd, "Lib1", "/tmp/lib1")
	cmd.CreateTag("tag1", "#ff0000")

	bootstrap := cmd.GetAppBootstrap()
	if bootstrap == nil {
		t.Fatal("GetAppBootstrap returned nil")
	}

	if _, ok := bootstrap["libraries"]; !ok {
		t.Error("bootstrap missing libraries key")
	}
	if _, ok := bootstrap["tags"]; !ok {
		t.Error("bootstrap missing tags key")
	}
	if _, ok := bootstrap["settings"]; !ok {
		t.Error("bootstrap missing settings key")
	}
}

func TestListAssets(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "TestLib", "/tmp/test")

	assetRepo := db.NewAssetRepo(cmd.db)
	for i := 0; i < 15; i++ {
		assetRepo.Create(makeAsset(lib.ID, "/tmp/test", "img"+itoa(i)+".png"))
	}

	resp := cmd.ListAssets(AssetListRequest{
		LibraryID: lib.ID,
		Offset:    0,
		Limit:     10,
	})
	if resp == nil {
		t.Fatal("ListAssets returned nil")
	}
	if resp.TotalCount != 15 {
		t.Errorf("expected total 15, got %d", resp.TotalCount)
	}
	if len(resp.Assets) != 10 {
		t.Errorf("expected 10 assets, got %d", len(resp.Assets))
	}

	resp2 := cmd.ListAssets(AssetListRequest{
		LibraryID: lib.ID,
		Offset:    10,
		Limit:     10,
	})
	if len(resp2.Assets) != 5 {
		t.Errorf("expected 5 assets on page 2, got %d", len(resp2.Assets))
	}
}

func TestAssetDetailWithTags(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "TestLib", "/tmp/test")

	assetRepo := db.NewAssetRepo(cmd.db)
	assetID, _ := assetRepo.Create(makeAsset(lib.ID, "/tmp/test", "test.png"))

	tag1 := cmd.CreateTag("character", "#ff0000")
	tag2 := cmd.CreateTag("landscape", "#00ff00")

	if err := cmd.SetAssetTags(assetID, []int64{tag1.ID, tag2.ID}); err != nil {
		t.Fatalf("SetAssetTags: %v", err)
	}

	detail := cmd.GetAssetDetail(assetID)
	if detail == nil {
		t.Fatal("GetAssetDetail returned nil")
	}
	if len(detail.Tags) != 2 {
		t.Errorf("expected 2 tags, got %d", len(detail.Tags))
	}

	if err := cmd.SetAssetTags(assetID, []int64{tag1.ID}); err != nil {
		t.Fatalf("SetAssetTags remove: %v", err)
	}

	detail = cmd.GetAssetDetail(assetID)
	if len(detail.Tags) != 1 {
		t.Errorf("expected 1 tag after removal, got %d", len(detail.Tags))
	}
}

func TestAttachAssetsToPost(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "TestLib", "/tmp/test")

	assetRepo := db.NewAssetRepo(cmd.db)
	var assetIDs []int64
	for i := 0; i < 3; i++ {
		id, _ := assetRepo.Create(makeAsset(lib.ID, "/tmp/test", "img"+itoa(i)+".png"))
		assetIDs = append(assetIDs, id)
	}

	post := cmd.CreatePostDraft("Test Post", "", "")
	if post == nil {
		t.Fatal("CreatePostDraft returned nil")
	}

	if err := cmd.AttachAssetsToPost(post.ID, assetIDs); err != nil {
		t.Fatalf("AttachAssetsToPost: %v", err)
	}

	posts := cmd.GetPostsByAsset(assetIDs[0])
	if len(posts) != 1 {
		t.Errorf("expected 1 post for asset, got %d", len(posts))
	}
}

func TestMoveAssets(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "TestLib", "/tmp/test")

	srcDir, _ := os.MkdirTemp("", "lumine-move-src-*")
	dstDir, _ := os.MkdirTemp("", "lumine-move-dst-*")
	defer os.RemoveAll(srcDir)
	defer os.RemoveAll(dstDir)

	srcFile := filepath.Join(srcDir, "test.png")
	if err := os.WriteFile(srcFile, []byte("test data"), 0644); err != nil {
		t.Fatal(err)
	}

	assetRepo := db.NewAssetRepo(cmd.db)
	assetID, _ := assetRepo.Create(&domain.Asset{
		LibraryID:   lib.ID,
		FolderPath:  srcDir,
		FileName:    "test.png",
		FilePath:    srcFile,
		Extension:   ".png",
		FileSize:    9,
		ThumbStatus: domain.ThumbStatusNone,
		StatusLabel: domain.StatusUnsorted,
	})

	result := cmd.MoveAssets(MoveRequest{
		AssetIDs:         []int64{assetID},
		DestinationFolder: dstDir,
		ConflictPolicy:   "skip",
	})
	if result == nil {
		t.Fatal("MoveAssets returned nil")
	}
	if result.MovedCount != 1 {
		t.Errorf("expected 1 moved, got moved=%d skipped=%d failed=%d errors=%v",
			result.MovedCount, result.SkippedCount, result.FailedCount, result.Errors)
	}

	if _, err := os.Stat(filepath.Join(dstDir, "test.png")); err != nil {
		t.Errorf("destination file not found: %v", err)
	}
	if _, err := os.Stat(srcFile); err == nil {
		t.Error("source file should have been removed")
	}
}

func TestMoveAssetsConflictSkip(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "TestLib", "/tmp/test")

	srcDir, _ := os.MkdirTemp("", "lumine-move-src2-*")
	dstDir, _ := os.MkdirTemp("", "lumine-move-dst2-*")
	defer os.RemoveAll(srcDir)
	defer os.RemoveAll(dstDir)

	srcFile := filepath.Join(srcDir, "existing.png")
	os.WriteFile(srcFile, []byte("src data"), 0644)
	dstFile := filepath.Join(dstDir, "existing.png")
	os.WriteFile(dstFile, []byte("dst data"), 0644)

	assetRepo := db.NewAssetRepo(cmd.db)
	assetID, _ := assetRepo.Create(&domain.Asset{
		LibraryID:   lib.ID,
		FolderPath:  srcDir,
		FileName:    "existing.png",
		FilePath:    srcFile,
		Extension:   ".png",
		FileSize:    8,
		ThumbStatus: domain.ThumbStatusNone,
		StatusLabel: domain.StatusUnsorted,
	})

	result := cmd.MoveAssets(MoveRequest{
		AssetIDs:         []int64{assetID},
		DestinationFolder: dstDir,
		ConflictPolicy:   "skip",
	})
	if result.SkippedCount != 1 {
		t.Errorf("expected 1 skipped, got %d", result.SkippedCount)
	}

	dstContent, _ := os.ReadFile(dstFile)
	if string(dstContent) != "dst data" {
		t.Error("destination file should not have been overwritten")
	}
}

func TestMoveAssetsConflictRename(t *testing.T) {
	cmd := setupCommands(t)
	lib := createTestLibrary(t, cmd, "TestLib", "/tmp/test")

	srcDir, _ := os.MkdirTemp("", "lumine-move-src3-*")
	dstDir, _ := os.MkdirTemp("", "lumine-move-dst3-*")
	defer os.RemoveAll(srcDir)
	defer os.RemoveAll(dstDir)

	srcFile := filepath.Join(srcDir, "dup.png")
	os.WriteFile(srcFile, []byte("src data"), 0644)
	dstFile := filepath.Join(dstDir, "dup.png")
	os.WriteFile(dstFile, []byte("dst data"), 0644)

	assetRepo := db.NewAssetRepo(cmd.db)
	assetID, _ := assetRepo.Create(&domain.Asset{
		LibraryID:   lib.ID,
		FolderPath:  srcDir,
		FileName:    "dup.png",
		FilePath:    srcFile,
		Extension:   ".png",
		FileSize:    8,
		ThumbStatus: domain.ThumbStatusNone,
		StatusLabel: domain.StatusUnsorted,
	})

	result := cmd.MoveAssets(MoveRequest{
		AssetIDs:         []int64{assetID},
		DestinationFolder: dstDir,
		ConflictPolicy:   "rename",
	})
	if result.MovedCount != 1 {
		t.Errorf("expected 1 moved with rename, got moved=%d", result.MovedCount)
	}

	renamedPath := filepath.Join(dstDir, "dup (1).png")
	if _, err := os.Stat(renamedPath); err != nil {
		t.Errorf("renamed file not found: %v", err)
	}
}

func itoa(n int) string {
	if n == 0 {
		return "0"
	}
	s := ""
	for n > 0 {
		s = string(rune('0'+n%10)) + s
		n /= 10
	}
	return s
}
