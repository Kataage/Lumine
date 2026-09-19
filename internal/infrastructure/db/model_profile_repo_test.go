package db

import (
	"testing"

	"github.com/kataage/lumine/internal/domain"
)

func TestModelProfileRepoCRUD(t *testing.T) {
	database := openAIAnalysisTestDB(t)
	repo := NewModelProfileRepo(database)

	created, err := repo.Create(&domain.ModelProfile{
		ID:                   "custom-test-profile",
		Name:                 "My IL Profile",
		Family:               "illustrious",
		CheckpointName:       "my-checkpoint.safetensors",
		PromptStyle:          "danbooru tags",
		QualityTags:          []string{"masterpiece", "best quality"},
		NegativePromptPolicy: "focused",
		TagOrder:             []string{"subject", "quality"},
		TriggerWords:         []string{"trigger_a"},
		LoRATriggerSyntax:    "<lora:{name}:{weight}>",
		WeightSyntax:         "({text}:{weight})",
		SystemGuidance:       "keep triggers",
		Notes:                "test",
	})
	if err != nil {
		t.Fatalf("Create: %v", err)
	}
	if created.BuiltIn || len(created.QualityTags) != 2 {
		t.Fatalf("created = %+v", created)
	}

	created.Name = "Updated"
	created.TriggerWords = []string{"one", "two"}
	updated, err := repo.Update(created)
	if err != nil {
		t.Fatalf("Update: %v", err)
	}
	if updated.Name != "Updated" || len(updated.TriggerWords) != 2 {
		t.Fatalf("updated = %+v", updated)
	}

	list, err := repo.List()
	if err != nil {
		t.Fatal(err)
	}
	if len(list) != 1 || list[0].ID != created.ID {
		t.Fatalf("list = %+v", list)
	}

	if err := repo.Delete(created.ID); err != nil {
		t.Fatal(err)
	}
	value, err := repo.Get(created.ID)
	if err != nil {
		t.Fatal(err)
	}
	if value != nil {
		t.Fatalf("deleted profile still exists: %+v", value)
	}
}
