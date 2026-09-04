package commands

import (
	"os"
	"path/filepath"
	"testing"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
	"github.com/kataage/lumine/internal/infrastructure/scanner"
)

func newDeleteTestCommands(t *testing.T) (*AppCommands, *db.DB, *domain.Library) {
	t.Helper()
	database, err := db.Open(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	libraryRepo := db.NewLibraryRepo(database)
	root := t.TempDir()
	library, err := libraryRepo.Create("delete-test", root)
	if err != nil {
		database.Close()
		t.Fatal(err)
	}
	scanSvc := scanner.NewScanner(db.NewAssetRepo(database), libraryRepo, db.NewJobLogRepo(database))
	return New(database, scanSvc), database, library
}

func TestDeleteAssetFilesRemovesOriginalAndDatabaseRowOnce(t *testing.T) {
	commands, database, library := newDeleteTestCommands(t)
	defer database.Close()

	path := filepath.Join(library.RootPath, "delete-me.png")
	if err := os.WriteFile(path, []byte("image-data"), 0600); err != nil {
		t.Fatal(err)
	}
	id, err := commands.assetRepo.Create(&domain.Asset{
		LibraryID:   library.ID,
		FolderPath:  library.RootPath,
		FileName:    filepath.Base(path),
		FilePath:    path,
		Extension:   ".png",
		FileSize:    10,
		ThumbStatus: domain.ThumbStatusNone,
		StatusLabel: domain.StatusUnsorted,
	})
	if err != nil {
		t.Fatal(err)
	}

	result := commands.DeleteAssetFiles([]int64{id, id})
	if result.DeletedCount != 1 || result.FailedCount != 0 || len(result.DeletedIDs) != 1 {
		t.Fatalf("delete result = %+v", result)
	}
	if _, err := os.Stat(path); !os.IsNotExist(err) {
		t.Fatalf("original file still exists or stat failed unexpectedly: %v", err)
	}
	asset, err := commands.assetRepo.GetByID(id)
	if err != nil {
		t.Fatal(err)
	}
	if asset != nil {
		t.Fatal("asset row still exists after file deletion")
	}
}

func TestDeleteAssetFilesCleansStaleMissingFileRow(t *testing.T) {
	commands, database, library := newDeleteTestCommands(t)
	defer database.Close()

	path := filepath.Join(library.RootPath, "already-missing.png")
	id, err := commands.assetRepo.Create(&domain.Asset{
		LibraryID:   library.ID,
		FolderPath:  library.RootPath,
		FileName:    filepath.Base(path),
		FilePath:    path,
		Extension:   ".png",
		ThumbStatus: domain.ThumbStatusNone,
		StatusLabel: domain.StatusUnsorted,
	})
	if err != nil {
		t.Fatal(err)
	}

	result := commands.DeleteAssetFiles([]int64{id})
	if result.DeletedCount != 1 || result.FailedCount != 0 {
		t.Fatalf("delete result = %+v", result)
	}
	asset, err := commands.assetRepo.GetByID(id)
	if err != nil {
		t.Fatal(err)
	}
	if asset != nil {
		t.Fatal("stale asset row still exists")
	}
}
