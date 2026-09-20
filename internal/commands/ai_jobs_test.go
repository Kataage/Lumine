package commands

import (
	"context"
	"errors"
	"testing"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
)

func attachTestAIJobQueue(t *testing.T, cmd *AppCommands) *ai.JobQueue {
	t.Helper()
	queue := ai.NewJobQueue(db.NewAIAnalysisRepo(cmd.db), cmd.GetAISettings, 1)
	cmd.SetAIJobQueue(queue)
	return queue
}

func enableSemanticSearchForTest(t *testing.T, cmd *AppCommands) {
	t.Helper()
	_, err := cmd.SetAISettings(domain.AISettings{
		Enabled:        true,
		SemanticSearch: true,
	})
	if err != nil {
		t.Fatalf("SetAISettings: %v", err)
	}
}

func TestReanalyzeAssetFolderAndLibraryScopes(t *testing.T) {
	cmd := setupCommands(t)
	attachTestAIJobQueue(t, cmd)
	enableSemanticSearchForTest(t, cmd)

	lib := createTestLibrary(t, cmd, "AI Scope", "/tmp/ai-scope")
	assetRepo := db.NewAssetRepo(cmd.db)

	create := func(folder, name string) int64 {
		t.Helper()
		id, err := assetRepo.Create(makeAsset(lib.ID, folder, name))
		if err != nil {
			t.Fatalf("create asset %s: %v", name, err)
		}
		return id
	}

	rootAsset := create("/tmp/ai-scope/folder-a", "root.png")
	subAsset := create("/tmp/ai-scope/folder-a/sub", "sub.png")
	otherAsset := create("/tmp/ai-scope/folder-b", "other.png")

	created, err := cmd.ReanalyzeAssets(
		[]int64{rootAsset},
		string(domain.AICapabilitySemanticSearch),
		100,
	)
	if err != nil {
		t.Fatalf("ReanalyzeAssets: %v", err)
	}
	if created != 1 {
		t.Fatalf("ReanalyzeAssets created %d, want 1", created)
	}

	created, err = cmd.ReanalyzeFolder(
		lib.ID,
		"/tmp/ai-scope/folder-a",
		true,
		string(domain.AICapabilitySemanticSearch),
		50,
	)
	if err != nil {
		t.Fatalf("ReanalyzeFolder: %v", err)
	}
	if created != 1 {
		t.Fatalf("recursive folder reanalysis should add only sub asset; created=%d", created)
	}

	created, err = cmd.ReanalyzeLibrary(
		lib.ID,
		string(domain.AICapabilitySemanticSearch),
		10,
	)
	if err != nil {
		t.Fatalf("ReanalyzeLibrary: %v", err)
	}
	if created != 1 {
		t.Fatalf("library reanalysis should add only remaining asset; created=%d", created)
	}

	jobs, err := cmd.ListAIJobs(100)
	if err != nil {
		t.Fatalf("ListAIJobs: %v", err)
	}
	if len(jobs) != 3 {
		t.Fatalf("expected 3 deduplicated jobs, got %d", len(jobs))
	}

	found := map[int64]bool{}
	for _, job := range jobs {
		found[job.AssetID] = true
	}
	for _, id := range []int64{rootAsset, subAsset, otherAsset} {
		if !found[id] {
			t.Fatalf("asset %d missing from queued jobs", id)
		}
	}
}

func TestExplicitSemanticPriorityEnqueueDoesNotRequireAutoAnalyze(t *testing.T) {
	cmd := setupCommands(t)
	queue := attachTestAIJobQueue(t, cmd)
	_ = queue
	enableSemanticSearchForTest(t, cmd)

	lib := createTestLibrary(t, cmd, "Semantic Explicit", "/tmp/semantic-explicit")
	assetRepo := db.NewAssetRepo(cmd.db)
	first, err := assetRepo.Create(makeAsset(lib.ID, "/tmp/semantic-explicit", "first.png"))
	if err != nil {
		t.Fatal(err)
	}
	second, err := assetRepo.Create(makeAsset(lib.ID, "/tmp/semantic-explicit", "second.png"))
	if err != nil {
		t.Fatal(err)
	}

	// AutoAnalyze is deliberately false. Viewer-order assets should still be
	// promoted when the user explicitly executes Semantic Search.
	cmd.rememberSemanticPriorityAssets([]int64{second, first})
	created, err := cmd.enqueueRememberedSemanticPriorityAssets(
		context.Background(),
		ai.RuntimeStatus{
			State:   ai.RuntimeStateReady,
			Engine:  "siglip2-onnx",
			ModelID: "siglip2-test",
			Version: "1",
		},
	)
	if err != nil {
		t.Fatalf("explicit semantic priority enqueue: %v", err)
	}
	if created != 2 {
		t.Fatalf("created %d semantic jobs, want 2", created)
	}

	jobs, err := cmd.ListAIJobs(10)
	if err != nil {
		t.Fatal(err)
	}
	if len(jobs) != 2 {
		t.Fatalf("job count = %d, want 2", len(jobs))
	}
	for _, job := range jobs {
		if job.Capability != domain.AICapabilitySemanticSearch {
			t.Fatalf("unexpected capability: %+v", job)
		}
		if job.Source != domain.AIJobSourceManual {
			t.Fatalf("explicit search job source = %s, want manual", job.Source)
		}
		if job.Priority != 250 {
			t.Fatalf("explicit search job priority = %d, want 250", job.Priority)
		}
	}
}

func TestReanalysisRespectsFeatureFlagsAndValidation(t *testing.T) {
	cmd := setupCommands(t)
	attachTestAIJobQueue(t, cmd)

	lib := createTestLibrary(t, cmd, "AI Disabled", "/tmp/ai-disabled")
	assetID, err := db.NewAssetRepo(cmd.db).Create(makeAsset(lib.ID, "/tmp/ai-disabled", "disabled.png"))
	if err != nil {
		t.Fatal(err)
	}

	_, err = cmd.ReanalyzeAssets(
		[]int64{assetID},
		string(domain.AICapabilitySemanticSearch),
		0,
	)
	if !errors.Is(err, ai.ErrCapabilityDisabled) {
		t.Fatalf("disabled reanalysis error = %v, want %v", err, ai.ErrCapabilityDisabled)
	}

	enableSemanticSearchForTest(t, cmd)

	if _, err := cmd.ReanalyzeAssets([]int64{assetID}, "not-a-capability", 0); err == nil {
		t.Fatal("invalid capability should be rejected")
	}
	if _, err := cmd.ReanalyzeFolder(
		lib.ID,
		"",
		true,
		string(domain.AICapabilitySemanticSearch),
		0,
	); err == nil {
		t.Fatal("empty folder path should be rejected")
	}
}
