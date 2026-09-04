package scanner

import (
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
