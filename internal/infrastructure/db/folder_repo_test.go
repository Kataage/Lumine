package db

import (
	"os"
	"testing"
)

func TestFolderRepoUsesTypedTimestamps(t *testing.T) {
	dir, err := os.MkdirTemp("", "lumine-folder-test-*")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(dir)

	database, err := Open(dir)
	if err != nil {
		t.Fatalf("Open failed: %v", err)
	}
	defer database.Close()

	library, err := NewLibraryRepo(database).Create("Folders", "/tmp/folders")
	if err != nil {
		t.Fatalf("Create library: %v", err)
	}

	repo := NewFolderRepo(database)
	if err := repo.UpsertFolder(library.ID, "/tmp/folders/child", "/tmp/folders"); err != nil {
		t.Fatalf("UpsertFolder: %v", err)
	}

	folders, err := repo.GetTreeByLibrary(library.ID)
	if err != nil {
		t.Fatalf("GetTreeByLibrary: %v", err)
	}
	if len(folders) != 1 {
		t.Fatalf("expected 1 folder, got %d", len(folders))
	}
	if folders[0].CreatedAt.IsZero() {
		t.Error("CreatedAt should be a typed, non-zero time.Time")
	}
	if folders[0].UpdatedAt.IsZero() {
		t.Error("UpdatedAt should be a typed, non-zero time.Time")
	}
}
