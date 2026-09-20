package db

import (
	"database/sql"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"testing"

	"github.com/kataage/lumine/internal/domain"
)

func TestOpenAndMigrate(t *testing.T) {
	dir, err := os.MkdirTemp("", "lumine-test-*")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(dir)

	database, err := Open(dir)
	if err != nil {
		t.Fatalf("Open failed: %v", err)
	}
	defer database.Close()

	var count int
	err = database.QueryRow("SELECT COUNT(*) FROM libraries").Scan(&count)
	if err != nil {
		t.Fatalf("query libraries: %v", err)
	}
	if count != 0 {
		t.Errorf("expected 0 libraries, got %d", count)
	}

	tables := []string{"assets", "asset_notes", "tags", "asset_tags", "post_targets", "post_accounts", "posts", "post_destinations", "post_assets", "job_logs", "app_settings", "ai_asset_analysis", "ai_jobs", "ai_tag_suggestions", "ai_semantic_embeddings", "custom_model_profiles", "prompt_projects", "prompt_project_assets", "prompt_variants", "prompt_versions", "asset_generation_metadata", "lora_trigger_knowledge"}
	for _, table := range tables {
		var tc int
		err := database.QueryRow("SELECT COUNT(*) FROM " + table).Scan(&tc)
		if err != nil {
			t.Errorf("table %s not accessible: %v", table, err)
		}
	}
}

func TestLibraryCRUD(t *testing.T) {
	dir, err := os.MkdirTemp("", "lumine-test-*")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(dir)

	database, err := Open(dir)
	if err != nil {
		t.Fatalf("Open failed: %v", err)
	}
	defer database.Close()

	repo := NewLibraryRepo(database)

	lib, err := repo.Create("TestLib", "/tmp/images")
	if err != nil {
		t.Fatalf("Create failed: %v", err)
	}
	if lib.ID == 0 {
		t.Error("expected non-zero ID")
	}
	if lib.Name != "TestLib" {
		t.Errorf("expected name TestLib, got %s", lib.Name)
	}

	libs, err := repo.List()
	if err != nil {
		t.Fatalf("List failed: %v", err)
	}
	if len(libs) != 1 {
		t.Errorf("expected 1 library, got %d", len(libs))
	}

	got, err := repo.GetByID(lib.ID)
	if err != nil {
		t.Fatalf("GetByID failed: %v", err)
	}
	if got.Name != "TestLib" {
		t.Errorf("expected name TestLib, got %s", got.Name)
	}

	err = repo.UpdateLastScanned(lib.ID)
	if err != nil {
		t.Fatalf("UpdateLastScanned failed: %v", err)
	}
	updated, _ := repo.GetByID(lib.ID)
	if updated.LastScannedAt == nil {
		t.Error("expected last_scanned_at to be set")
	}

	err = repo.Delete(lib.ID)
	if err != nil {
		t.Fatalf("Delete failed: %v", err)
	}
	libs, _ = repo.List()
	if len(libs) != 0 {
		t.Errorf("expected 0 libraries after delete, got %d", len(libs))
	}
}

func TestAssetCRUD(t *testing.T) {
	dir, _ := os.MkdirTemp("", "lumine-test-*")
	defer os.RemoveAll(dir)

	database, _ := Open(dir)
	defer database.Close()

	libRepo := NewLibraryRepo(database)
	assetRepo := NewAssetRepo(database)

	lib, _ := libRepo.Create("TestLib", "/tmp/img")

	asset := &domain.Asset{
		LibraryID:   lib.ID,
		FolderPath:  "/tmp/img",
		FileName:    "test.png",
		FilePath:    "/tmp/img/test.png",
		Extension:   ".png",
		FileSize:    1024,
		ThumbStatus: domain.ThumbStatusQueued,
		Rating:      0,
		StatusLabel: domain.StatusUnsorted,
	}

	id, err := assetRepo.Create(asset)
	if err != nil {
		t.Fatalf("Create asset failed: %v", err)
	}
	if id == 0 {
		t.Error("expected non-zero asset ID")
	}

	got, err := assetRepo.GetByID(id)
	if err != nil {
		t.Fatalf("GetByID failed: %v", err)
	}
	if got.FileName != "test.png" {
		t.Errorf("expected filename test.png, got %s", got.FileName)
	}

	byPath, err := assetRepo.GetByFilePath("/tmp/img/test.png")
	if err != nil {
		t.Fatalf("GetByFilePath failed: %v", err)
	}
	if byPath == nil {
		t.Error("expected to find asset by path")
	}

	got.Rating = 5
	err = assetRepo.Update(got)
	if err != nil {
		t.Fatalf("Update failed: %v", err)
	}
	updated, _ := assetRepo.GetByID(id)
	if updated.Rating != 5 {
		t.Errorf("expected rating 5, got %d", updated.Rating)
	}
}

func TestAssetListWithFilters(t *testing.T) {
	dir, _ := os.MkdirTemp("", "lumine-test-*")
	defer os.RemoveAll(dir)

	database, _ := Open(dir)
	defer database.Close()

	libRepo := NewLibraryRepo(database)
	assetRepo := NewAssetRepo(database)

	lib, _ := libRepo.Create("TestLib", "/tmp/img")

	for i := 0; i < 10; i++ {
		ext := ".png"
		if i%2 == 0 {
			ext = ".jpg"
		}
		asset := &domain.Asset{
			LibraryID:   lib.ID,
			FolderPath:  "/tmp/img",
			FileName:    "img" + itoa(i) + ext,
			FilePath:    "/tmp/img/img" + itoa(i) + ext,
			Extension:   ext,
			FileSize:    int64(i * 100),
			ThumbStatus: domain.ThumbStatusNone,
			Rating:      i % 5,
			StatusLabel: domain.StatusUnsorted,
		}
		assetRepo.Create(asset)
	}

	result, err := assetRepo.List(AssetQuery{LibraryID: lib.ID, Limit: 5})
	if err != nil {
		t.Fatalf("List failed: %v", err)
	}
	if result.TotalCount != 10 {
		t.Errorf("expected total 10, got %d", result.TotalCount)
	}
	if len(result.Assets) != 5 {
		t.Errorf("expected 5 assets, got %d", len(result.Assets))
	}

	pngResult, err := assetRepo.List(AssetQuery{LibraryID: lib.ID, Extension: ".png"})
	if err != nil {
		t.Fatalf("List with extension filter failed: %v", err)
	}
	if len(pngResult.Assets) != 5 {
		t.Errorf("expected 5 png assets, got %d", len(pngResult.Assets))
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

func TestAssetNoteCRUD(t *testing.T) {
	dir, _ := os.MkdirTemp("", "lumine-test-*")
	defer os.RemoveAll(dir)

	database, _ := Open(dir)
	defer database.Close()

	libRepo := NewLibraryRepo(database)
	assetRepo := NewAssetRepo(database)
	noteRepo := NewAssetNoteRepo(database)

	lib, _ := libRepo.Create("TestLib", "/tmp/img")
	assetID, _ := assetRepo.Create(&domain.Asset{
		LibraryID:   lib.ID,
		FolderPath:  "/tmp/img",
		FileName:    "test.png",
		FilePath:    "/tmp/img/test.png",
		Extension:   ".png",
		FileSize:    1024,
		ThumbStatus: domain.ThumbStatusNone,
		Rating:      0,
		StatusLabel: domain.StatusUnsorted,
	})

	err := noteRepo.Upsert(assetID, "hello world")
	if err != nil {
		t.Fatalf("Upsert failed: %v", err)
	}

	note, err := noteRepo.GetByAssetID(assetID)
	if err != nil {
		t.Fatalf("GetByAssetID failed: %v", err)
	}
	if note == nil || note.Content != "hello world" {
		t.Errorf("expected 'hello world', got %v", note)
	}

	err = noteRepo.Upsert(assetID, "updated")
	if err != nil {
		t.Fatalf("Upsert update failed: %v", err)
	}
	note, _ = noteRepo.GetByAssetID(assetID)
	if note.Content != "updated" {
		t.Errorf("expected 'updated', got %s", note.Content)
	}
}

func TestTagCRUD(t *testing.T) {
	dir, _ := os.MkdirTemp("", "lumine-test-*")
	defer os.RemoveAll(dir)

	database, _ := Open(dir)
	defer database.Close()

	libRepo := NewLibraryRepo(database)
	assetRepo := NewAssetRepo(database)
	tagRepo := NewTagRepo(database)

	lib, _ := libRepo.Create("TestLib", "/tmp/img")
	assetID, _ := assetRepo.Create(&domain.Asset{
		LibraryID:   lib.ID,
		FolderPath:  "/tmp/img",
		FileName:    "test.png",
		FilePath:    "/tmp/img/test.png",
		Extension:   ".png",
		FileSize:    1024,
		ThumbStatus: domain.ThumbStatusNone,
		Rating:      0,
		StatusLabel: domain.StatusUnsorted,
	})

	tag1, err := tagRepo.Create("character", "#ff0000")
	if err != nil {
		t.Fatalf("Create tag failed: %v", err)
	}
	tag2, _ := tagRepo.Create("landscape", "#00ff00")

	err = tagRepo.SetAssetTags(assetID, []int64{tag1.ID, tag2.ID})
	if err != nil {
		t.Fatalf("SetAssetTags failed: %v", err)
	}

	tags, err := tagRepo.GetByAssetID(assetID)
	if err != nil {
		t.Fatalf("GetByAssetID failed: %v", err)
	}
	if len(tags) != 2 {
		t.Errorf("expected 2 tags, got %d", len(tags))
	}

	err = tagRepo.SetAssetTags(assetID, []int64{tag1.ID})
	if err != nil {
		t.Fatalf("SetAssetTags update failed: %v", err)
	}
	tags, _ = tagRepo.GetByAssetID(assetID)
	if len(tags) != 1 {
		t.Errorf("expected 1 tag after update, got %d", len(tags))
	}
}


func TestSemanticBackfillReorderMigrationRetiresOnlyBackgroundSemanticWork(t *testing.T) {
	dir := t.TempDir()
	dbPath := filepath.Join(dir, "lumine.db")
	raw, err := sql.Open("sqlite", dbPath+"?_pragma=foreign_keys(1)")
	if err != nil {
		t.Fatal(err)
	}
	defer raw.Close()

	if _, err := raw.Exec(`
		CREATE TABLE IF NOT EXISTS _migrations (
			id INTEGER PRIMARY KEY,
			name TEXT NOT NULL UNIQUE,
			applied_at DATETIME DEFAULT CURRENT_TIMESTAMP
		)
	`); err != nil {
		t.Fatal(err)
	}

	entries, err := migrationsFS.ReadDir("migrations")
	if err != nil {
		t.Fatal(err)
	}
	var names []string
	for _, entry := range entries {
		if strings.HasSuffix(entry.Name(), ".sql") && entry.Name() < "017_" {
			names = append(names, entry.Name())
		}
	}
	sort.Strings(names)
	for _, name := range names {
		content, err := migrationsFS.ReadFile("migrations/" + name)
		if err != nil {
			t.Fatal(err)
		}
		if _, err := raw.Exec(string(content)); err != nil {
			t.Fatalf("apply pre-017 migration %s: %v", name, err)
		}
		if _, err := raw.Exec("INSERT INTO _migrations (name) VALUES (?)", name); err != nil {
			t.Fatalf("record pre-017 migration %s: %v", name, err)
		}
	}

	database := &DB{raw}
	lib, err := NewLibraryRepo(database).Create("Migration", "/tmp/migration")
	if err != nil {
		t.Fatal(err)
	}
	assetRepo := NewAssetRepo(database)
	makeAsset := func(name string) int64 {
		t.Helper()
		id, err := assetRepo.Create(&domain.Asset{
			LibraryID:   lib.ID,
			FolderPath:  "/tmp/migration",
			FileName:    name,
			FilePath:    "/tmp/migration/" + name,
			Extension:   ".png",
			FileSize:    1,
			ThumbStatus: domain.ThumbStatusNone,
			StatusLabel: domain.StatusUnsorted,
		})
		if err != nil {
			t.Fatal(err)
		}
		return id
	}
	backgroundQueuedAsset := makeAsset("background-queued.png")
	backgroundRunningAsset := makeAsset("background-running.png")
	foregroundAsset := makeAsset("foreground.png")

	repo := NewAIAnalysisRepo(database)
	backgroundQueued, _, err := repo.Enqueue(
		backgroundQueuedAsset,
		domain.AICapabilitySemanticSearch,
		domain.AIJobSourceAutomatic,
		-100,
		3,
	)
	if err != nil {
		t.Fatal(err)
	}
	backgroundRunning, _, err := repo.Enqueue(
		backgroundRunningAsset,
		domain.AICapabilitySemanticSearch,
		domain.AIJobSourceAutomatic,
		-100,
		3,
	)
	if err != nil {
		t.Fatal(err)
	}
	foreground, _, err := repo.Enqueue(
		foregroundAsset,
		domain.AICapabilitySemanticSearch,
		domain.AIJobSourceAutomatic,
		250,
		3,
	)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := raw.Exec(
		"UPDATE ai_jobs SET status = 'running', started_at = CURRENT_TIMESTAMP WHERE id = ?",
		backgroundRunning.ID,
	); err != nil {
		t.Fatal(err)
	}
	if _, err := raw.Exec(
		"UPDATE ai_asset_analysis SET state = 'running' WHERE asset_id = ? AND capability = 'semantic_search'",
		backgroundRunningAsset,
	); err != nil {
		t.Fatal(err)
	}

	if err := migrate(raw); err != nil {
		t.Fatalf("apply migration 017: %v", err)
	}

	for _, jobID := range []int64{backgroundQueued.ID, backgroundRunning.ID} {
		job, err := repo.GetJob(jobID)
		if err != nil {
			t.Fatal(err)
		}
		if job == nil || job.Status != domain.AIJobCancelled {
			t.Fatalf("legacy background semantic job %d = %+v, want cancelled", jobID, job)
		}
	}
	foregroundJob, err := repo.GetJob(foreground.ID)
	if err != nil {
		t.Fatal(err)
	}
	if foregroundJob == nil || foregroundJob.Status != domain.AIJobQueued {
		t.Fatalf("foreground semantic job changed by migration: %+v", foregroundJob)
	}

	for _, assetID := range []int64{backgroundQueuedAsset, backgroundRunningAsset} {
		analyses, err := repo.GetByAsset(assetID)
		if err != nil {
			t.Fatal(err)
		}
		if len(analyses) != 1 || analyses[0].State != domain.AIAnalysisStale {
			t.Fatalf("legacy background analysis asset %d = %+v, want stale", assetID, analyses)
		}
	}
	foregroundAnalyses, err := repo.GetByAsset(foregroundAsset)
	if err != nil {
		t.Fatal(err)
	}
	if len(foregroundAnalyses) != 1 || foregroundAnalyses[0].State != domain.AIAnalysisQueued {
		t.Fatalf("foreground analysis changed by migration: %+v", foregroundAnalyses)
	}
}
