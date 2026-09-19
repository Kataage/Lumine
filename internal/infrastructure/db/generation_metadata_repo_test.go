package db

import (
	"os"
	"testing"

	"github.com/kataage/lumine/internal/domain"
)

func TestGenerationMetadataRepoAndLoRAKnowledge(t *testing.T) {
	dir, err := os.MkdirTemp("", "lumine-generation-meta-*")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(dir)
	database, err := Open(dir)
	if err != nil {
		t.Fatal(err)
	}
	defer database.Close()

	library, err := NewLibraryRepo(database).Create("Meta", "/tmp/meta")
	if err != nil {
		t.Fatal(err)
	}
	assetID, err := NewAssetRepo(database).Create(&domain.Asset{
		LibraryID: library.ID,
		FolderPath: "/tmp/meta",
		FileName: "image.png",
		FilePath: "/tmp/meta/image.png",
		Extension: ".png",
		FileSize: 123,
		ThumbStatus: domain.ThumbStatusNone,
		StatusLabel: domain.StatusUnsorted,
	})
	if err != nil {
		t.Fatal(err)
	}

	repo := NewGenerationMetadataRepo(database)
	if err := repo.Upsert(domain.StoredGenerationMetadata{
		AssetID: assetID,
		SchemaVersion: 1,
		ParserVersion: 1,
		SourceFormat: "png",
		FileSize: 123,
		ModifiedAtFS: "2026-09-19T00:00:00Z",
		RawJSON: `{"prompt":"{}"}`,
		NormalizedJSON: `{"schemaVersion":1,"sourceFormat":"png","loras":[]}`,
	}); err != nil {
		t.Fatal(err)
	}
	stored, err := repo.Get(assetID)
	if err != nil {
		t.Fatal(err)
	}
	if stored == nil || stored.SourceFormat != "png" || stored.FileSize != 123 {
		t.Fatalf("stored = %+v", stored)
	}

	if err := repo.MergeLoRATriggers("styles/foo.safetensors", []string{"trigger_a", "trigger_b"}); err != nil {
		t.Fatal(err)
	}
	if err := repo.MergeLoRATriggers("foo.safetensors", []string{"trigger_b", "trigger_c"}); err != nil {
		t.Fatal(err)
	}
	triggers, err := repo.GetLoRATriggers("FOO.SAFETENSORS")
	if err != nil {
		t.Fatal(err)
	}
	if len(triggers) != 3 {
		t.Fatalf("triggers = %v", triggers)
	}
}
