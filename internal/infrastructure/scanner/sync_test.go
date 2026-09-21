package scanner

import (
	"fmt"
	"image"
	"image/color"
	"image/png"
	"os"
	"path/filepath"
	"testing"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
)

func writeTestPNG(t *testing.T, path string) {
	t.Helper()
	file, err := os.Create(path)
	if err != nil {
		t.Fatal(err)
	}
	defer file.Close()
	img := image.NewRGBA(image.Rect(0, 0, 2, 2))
	img.Set(0, 0, color.RGBA{R: 255, A: 255})
	if err := png.Encode(file, img); err != nil {
		t.Fatal(err)
	}
}

func TestSyncLibraryDetectsFilesystemDeltaWithoutJobLogs(t *testing.T) {
	database, err := db.Open(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	defer database.Close()

	assetRepo := db.NewAssetRepo(database)
	libraryRepo := db.NewLibraryRepo(database)
	scanner := NewScanner(assetRepo, libraryRepo, db.NewJobLogRepo(database))
	scanner.SetFolderRepo(db.NewFolderRepo(database))

	root := t.TempDir()
	library, err := libraryRepo.Create("sync-test", root)
	if err != nil {
		t.Fatal(err)
	}
	imagePath := filepath.Join(root, "new.png")
	writeTestPNG(t, imagePath)

	first, err := scanner.SyncLibrary(library, nil)
	if err != nil {
		t.Fatal(err)
	}
	if first.AddedCount != 1 || !first.Changed {
		t.Fatalf("first sync = %+v, want one addition", first)
	}

	asset, err := assetRepo.GetByFilePath(imagePath)
	if err != nil || asset == nil {
		t.Fatalf("get added asset: %v, asset=%v", err, asset)
	}
	fullAsset, err := assetRepo.GetByID(asset.ID)
	if err != nil || fullAsset == nil {
		t.Fatalf("get full asset: %v, asset=%v", err, fullAsset)
	}
	fullAsset.Rating = 4
	fullAsset.StatusLabel = domain.StatusCandidate
	fullAsset.IsFavorite = true
	fullAsset.ColorLabel = "blue"
	if err := assetRepo.Update(fullAsset); err != nil {
		t.Fatal(err)
	}

	file, err := os.OpenFile(imagePath, os.O_APPEND|os.O_WRONLY, 0600)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := file.Write([]byte{0}); err != nil {
		file.Close()
		t.Fatal(err)
	}
	if err := file.Close(); err != nil {
		t.Fatal(err)
	}

	updated, err := scanner.SyncLibrary(library, nil)
	if err != nil {
		t.Fatal(err)
	}
	if updated.UpdatedCount != 1 || !updated.Changed {
		t.Fatalf("updated sync = %+v, want one update", updated)
	}
	preserved, err := assetRepo.GetByID(asset.ID)
	if err != nil || preserved == nil {
		t.Fatalf("get updated asset: %v, asset=%v", err, preserved)
	}
	if preserved.Rating != 4 || preserved.StatusLabel != domain.StatusCandidate || !preserved.IsFavorite || preserved.ColorLabel != "blue" {
		t.Fatalf("user-managed state changed during sync: %+v", preserved)
	}

	second, err := scanner.SyncLibrary(library, nil)
	if err != nil {
		t.Fatal(err)
	}
	if second.Changed || second.SkippedCount != 1 {
		t.Fatalf("second sync = %+v, want unchanged/skipped", second)
	}

	if err := os.Remove(imagePath); err != nil {
		t.Fatal(err)
	}
	third, err := scanner.SyncLibrary(library, nil)
	if err != nil {
		t.Fatal(err)
	}
	if third.RemovedCount != 1 || !third.Changed {
		t.Fatalf("third sync = %+v, want one removal", third)
	}

	remaining, err := assetRepo.GetAllFilePathsMap(library.ID)
	if err != nil {
		t.Fatal(err)
	}
	if len(remaining) != 0 {
		t.Fatalf("remaining assets = %d, want 0", len(remaining))
	}

	var jobLogs int
	if err := database.QueryRow("SELECT COUNT(*) FROM job_logs").Scan(&jobLogs); err != nil {
		t.Fatal(err)
	}
	if jobLogs != 0 {
		t.Fatalf("silent sync created %d job log rows, want 0", jobLogs)
	}
}


func TestSyncLibraryYieldsToViewerAndDefersDestructiveCleanup(t *testing.T) {
	database, err := db.Open(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	defer database.Close()

	assetRepo := db.NewAssetRepo(database)
	libraryRepo := db.NewLibraryRepo(database)
	scanner := NewScanner(assetRepo, libraryRepo, db.NewJobLogRepo(database))
	scanner.SetFolderRepo(db.NewFolderRepo(database))

	root := t.TempDir()
	library, err := libraryRepo.Create("sync-yield", root)
	if err != nil {
		t.Fatal(err)
	}

	// Seed a DB-only asset. A yielded/partial background walk must never infer
	// that this row was deleted from disk, because it did not finish observing
	// the filesystem.
	missingPath := filepath.Join(root, "missing.png")
	missingID, err := assetRepo.Create(&domain.Asset{
		LibraryID:   library.ID,
		FolderPath:  root,
		FileName:    "missing.png",
		FilePath:    missingPath,
		Extension:   ".png",
		FileSize:    10,
		ThumbStatus: domain.ThumbStatusNone,
		StatusLabel: domain.StatusUnsorted,
	})
	if err != nil {
		t.Fatal(err)
	}

	const files = 260
	for i := 0; i < files; i++ {
		path := filepath.Join(root, fmt.Sprintf("%03d.png", i))
		if err := os.WriteFile(path, []byte("not-a-real-png"), 0600); err != nil {
			t.Fatal(err)
		}
	}

	// Sync batches new assets at 250. Trigger Viewer activity from the first
	// committed batch, which deterministically starts *after* the walk began.
	yieldTriggered := false
	scanner.SetAssetChangeHandler(func(_ []int64) {
		if yieldTriggered {
			return
		}
		yieldTriggered = true
		scanner.SetInteractiveUIActive(true)
	})

	first, err := scanner.SyncLibrary(library, nil)
	if err != nil {
		t.Fatal(err)
	}
	if !yieldTriggered || !first.Yielded {
		t.Fatalf("sync did not yield to newly-started Viewer activity: %+v", first)
	}
	if first.ScannedCount < 250 || first.ScannedCount >= files {
		t.Fatalf("yielded scan count = %d, want a partial walk after first batch", first.ScannedCount)
	}
	if first.AddedCount != 250 || first.RemovedCount != 0 {
		t.Fatalf("yielded sync changed unexpected rows: %+v", first)
	}
	if asset, err := assetRepo.GetByID(missingID); err != nil || asset == nil {
		t.Fatalf("partial sync deleted unseen DB asset: asset=%+v err=%v", asset, err)
	}

	// Once Viewer work is idle, a clean retry must converge: remaining files
	// are discovered and only then is the genuinely missing row removed.
	scanner.SetAssetChangeHandler(nil)
	scanner.SetInteractiveUIActive(false)
	second, err := scanner.SyncLibrary(library, nil)
	if err != nil {
		t.Fatal(err)
	}
	if second.Yielded {
		t.Fatalf("idle retry unexpectedly yielded: %+v", second)
	}
	if second.AddedCount != files-250 || second.RemovedCount != 1 || !second.Changed {
		t.Fatalf("idle retry did not converge: %+v", second)
	}
	if asset, err := assetRepo.GetByID(missingID); err != nil || asset != nil {
		t.Fatalf("clean retry did not remove missing DB asset: asset=%+v err=%v", asset, err)
	}
	all, err := assetRepo.GetSyncFilePathsMap(library.ID)
	if err != nil {
		t.Fatal(err)
	}
	if len(all) != files {
		t.Fatalf("final asset count = %d, want %d", len(all), files)
	}
}

func TestSyncLibraryDoesNotStartWhileViewerIsActive(t *testing.T) {
	database, err := db.Open(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	defer database.Close()

	assetRepo := db.NewAssetRepo(database)
	libraryRepo := db.NewLibraryRepo(database)
	scanner := NewScanner(assetRepo, libraryRepo, db.NewJobLogRepo(database))
	root := t.TempDir()
	library, err := libraryRepo.Create("sync-active", root)
	if err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(root, "new.png"), []byte("x"), 0600); err != nil {
		t.Fatal(err)
	}

	scanner.SetInteractiveUIActive(true)
	result, err := scanner.SyncLibrary(library, nil)
	if err != nil {
		t.Fatal(err)
	}
	if !result.Yielded || result.ScannedCount != 0 || result.Changed {
		t.Fatalf("active Viewer should prevent background sync start: %+v", result)
	}
	all, err := assetRepo.GetSyncFilePathsMap(library.ID)
	if err != nil {
		t.Fatal(err)
	}
	if len(all) != 0 {
		t.Fatalf("Viewer-blocked sync created %d assets", len(all))
	}
}
