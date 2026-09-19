package db

import (
	"os"
	"testing"

	"github.com/kataage/lumine/internal/domain"
)

func TestPromptProjectHistoryAndSoftDelete(t *testing.T) {
	dir, err := os.MkdirTemp("", "lumine-prompt-project-*")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(dir)
	database, err := Open(dir)
	if err != nil {
		t.Fatal(err)
	}
	defer database.Close()

	repo := NewPromptProjectRepo(database)
	project, err := repo.Create(&domain.PromptProject{
		Title:           "Test project",
		Idea:            "idea",
		TargetProfileID: "illustrious-xl",
		Characters:      []string{"heroine"},
		LoRAs: []domain.PromptLoRA{{
			Name: "style", Weight: 0.8, TriggerWords: []string{"trigger"},
		}},
	})
	if err != nil {
		t.Fatal(err)
	}
	variant, err := repo.CreateVariant(project.ID, "Main")
	if err != nil {
		t.Fatal(err)
	}
	v1, err := repo.CreateVersion(&domain.PromptVersion{
		VariantID: variant.ID, Positive: "v1", Negative: "bad", Source: "manual",
		ProfileID: "illustrious-xl", ProfileSnapshotJSON: `{"id":"illustrious-xl"}`,
	})
	if err != nil {
		t.Fatal(err)
	}
	v2, err := repo.CreateVersion(&domain.PromptVersion{
		VariantID: variant.ID, ParentVersionID: &v1.ID, Positive: "v2",
		Source: "llm", ChangeInstruction: "improve", AIEngine: "llamacpp-prompt",
		AIModelID: "model", AIModelVersion: "1",
	})
	if err != nil {
		t.Fatal(err)
	}
	versions, err := repo.ListVersions(variant.ID)
	if err != nil {
		t.Fatal(err)
	}
	if len(versions) != 2 || versions[0].ID != v2.ID || versions[1].ID != v1.ID {
		t.Fatalf("versions = %+v", versions)
	}
	if versions[0].ParentVersionID == nil || *versions[0].ParentVersionID != v1.ID {
		t.Fatalf("parent version missing: %+v", versions[0])
	}

	branch, err := repo.CreateVariant(project.ID, "Branch")
	if err != nil {
		t.Fatal(err)
	}
	if _, err := repo.CreateVersion(&domain.PromptVersion{
		VariantID: branch.ID, ParentVersionID: &v1.ID, Positive: "branch", Source: "derived",
	}); err != nil {
		t.Fatal(err)
	}

	if err := repo.SetDeleted(project.ID, true); err != nil {
		t.Fatal(err)
	}
	if active, _ := repo.GetProject(project.ID, false); active != nil {
		t.Fatalf("deleted project leaked into active lookup: %+v", active)
	}
	if deleted, _ := repo.GetProject(project.ID, true); deleted == nil || deleted.DeletedAt == nil {
		t.Fatalf("soft-deleted project unavailable: %+v", deleted)
	}
	if err := repo.SetDeleted(project.ID, false); err != nil {
		t.Fatal(err)
	}
	if restored, _ := repo.GetProject(project.ID, false); restored == nil || restored.DeletedAt != nil {
		t.Fatalf("project was not restored: %+v", restored)
	}
}

func TestPromptVersionCannotCrossProjects(t *testing.T) {
	dir, _ := os.MkdirTemp("", "lumine-prompt-cross-*")
	defer os.RemoveAll(dir)
	database, err := Open(dir)
	if err != nil {
		t.Fatal(err)
	}
	defer database.Close()
	repo := NewPromptProjectRepo(database)

	first, _ := repo.Create(&domain.PromptProject{Title: "First"})
	second, _ := repo.Create(&domain.PromptProject{Title: "Second"})
	firstVariant, _ := repo.CreateVariant(first.ID, "Main")
	secondVariant, _ := repo.CreateVariant(second.ID, "Main")
	parent, _ := repo.CreateVersion(&domain.PromptVersion{VariantID: firstVariant.ID, Positive: "a", Source: "manual"})
	if _, err := repo.CreateVersion(&domain.PromptVersion{
		VariantID: secondVariant.ID, ParentVersionID: &parent.ID, Positive: "b", Source: "derived",
	}); err == nil {
		t.Fatal("cross-project parent version should fail")
	}
}
