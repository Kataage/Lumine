package db

import (
	"os"
	"testing"
	"time"

	"github.com/kataage/lumine/internal/domain"
)

func openAIAnalysisTestDB(t *testing.T) *DB {
	t.Helper()
	dir, err := os.MkdirTemp("", "lumine-ai-analysis-*")
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = os.RemoveAll(dir) })

	database, err := Open(dir)
	if err != nil {
		t.Fatalf("Open: %v", err)
	}
	t.Cleanup(func() { _ = database.Close() })
	return database
}

func createAIRepoTestAsset(t *testing.T, database *DB, folder, name string) int64 {
	t.Helper()

	lib, err := NewLibraryRepo(database).Create("AI Test", folder)
	if err != nil {
		t.Fatalf("create library: %v", err)
	}
	id, err := NewAssetRepo(database).Create(&domain.Asset{
		LibraryID:   lib.ID,
		FolderPath:  folder,
		FileName:    name,
		FilePath:    folder + "/" + name,
		Extension:   ".png",
		FileSize:    123,
		ThumbStatus: domain.ThumbStatusNone,
		StatusLabel: domain.StatusUnsorted,
	})
	if err != nil {
		t.Fatalf("create asset: %v", err)
	}
	return id
}

func TestAIAnalysisRepoLifecycleAndProvenance(t *testing.T) {
	database := openAIAnalysisTestDB(t)
	repo := NewAIAnalysisRepo(database)
	assetID := createAIRepoTestAsset(t, database, "/tmp/ai-repo-lifecycle", "asset.png")

	job, created, err := repo.Enqueue(
		assetID,
		domain.AICapabilitySemanticSearch,
		domain.AIJobSourceManual,
		1,
		3,
	)
	if err != nil {
		t.Fatalf("Enqueue: %v", err)
	}
	if !created {
		t.Fatal("first enqueue should create a job")
	}

	sameJob, created, err := repo.Enqueue(
		assetID,
		domain.AICapabilitySemanticSearch,
		domain.AIJobSourceManual,
		50,
		3,
	)
	if err != nil {
		t.Fatalf("deduplicated Enqueue: %v", err)
	}
	if created {
		t.Fatal("second enqueue should reuse the active job")
	}
	if sameJob.ID != job.ID || sameJob.Priority != 50 {
		t.Fatalf("deduplicated job mismatch: got %+v, original %+v", sameJob, job)
	}

	claimed, err := repo.ClaimNext([]domain.AICapability{domain.AICapabilitySemanticSearch})
	if err != nil {
		t.Fatalf("ClaimNext: %v", err)
	}
	if claimed == nil || claimed.ID != job.ID {
		t.Fatalf("unexpected claimed job: %+v", claimed)
	}
	if claimed.Status != domain.AIJobRunning || claimed.AttemptCount != 1 {
		t.Fatalf("claimed job state mismatch: %+v", claimed)
	}

	if err := repo.CompleteJob(
		claimed.ID,
		"dummy-engine",
		"embedding-model",
		"1.0.0",
		`{"vector":[0.1,0.2]}`,
	); err != nil {
		t.Fatalf("CompleteJob: %v", err)
	}

	analyses, err := repo.GetByAsset(assetID)
	if err != nil {
		t.Fatalf("GetByAsset: %v", err)
	}
	if len(analyses) != 1 {
		t.Fatalf("expected one analysis row, got %d", len(analyses))
	}
	analysis := analyses[0]
	if analysis.State != domain.AIAnalysisReady {
		t.Fatalf("analysis state = %s, want ready", analysis.State)
	}
	if analysis.Engine != "dummy-engine" ||
		analysis.ModelID != "embedding-model" ||
		analysis.ModelVersion != "1.0.0" {
		t.Fatalf("analysis provenance mismatch: %+v", analysis)
	}
	if analysis.ResultJSON != `{"vector":[0.1,0.2]}` {
		t.Fatalf("unexpected result json: %s", analysis.ResultJSON)
	}

	changed, err := repo.MarkStaleForModel(
		domain.AICapabilitySemanticSearch,
		"dummy-engine",
		"embedding-model",
		"1.0.0",
	)
	if err != nil {
		t.Fatalf("MarkStaleForModel same model: %v", err)
	}
	if changed != 0 {
		t.Fatalf("same model should not mark results stale, changed=%d", changed)
	}

	changed, err = repo.MarkStaleForModel(
		domain.AICapabilitySemanticSearch,
		"dummy-engine",
		"embedding-model",
		"2.0.0",
	)
	if err != nil {
		t.Fatalf("MarkStaleForModel newer model: %v", err)
	}
	if changed != 1 {
		t.Fatalf("new model should mark one result stale, changed=%d", changed)
	}
	analyses, _ = repo.GetByAsset(assetID)
	if analyses[0].State != domain.AIAnalysisStale {
		t.Fatalf("analysis state = %s, want stale", analyses[0].State)
	}
}

func TestAIAnalysisRepoRetryCancelAndRecovery(t *testing.T) {
	database := openAIAnalysisTestDB(t)
	repo := NewAIAnalysisRepo(database)
	assetID := createAIRepoTestAsset(t, database, "/tmp/ai-repo-retry", "retry.png")

	job, _, err := repo.Enqueue(
		assetID,
		domain.AICapabilityTagger,
		domain.AIJobSourceManual,
		0,
		2,
	)
	if err != nil {
		t.Fatalf("Enqueue: %v", err)
	}

	claimed, err := repo.ClaimNext([]domain.AICapability{domain.AICapabilityTagger})
	if err != nil || claimed == nil {
		t.Fatalf("first ClaimNext: job=%+v err=%v", claimed, err)
	}
	requeued, err := repo.FailOrRequeue(claimed.ID, "temporary failure")
	if err != nil {
		t.Fatalf("first FailOrRequeue: %v", err)
	}
	if !requeued {
		t.Fatal("first failure should be retried")
	}

	claimed, err = repo.ClaimNext([]domain.AICapability{domain.AICapabilityTagger})
	if err != nil || claimed == nil {
		t.Fatalf("second ClaimNext: job=%+v err=%v", claimed, err)
	}
	if claimed.AttemptCount != 2 {
		t.Fatalf("attempt count = %d, want 2", claimed.AttemptCount)
	}
	requeued, err = repo.FailOrRequeue(claimed.ID, "permanent failure")
	if err != nil {
		t.Fatalf("second FailOrRequeue: %v", err)
	}
	if requeued {
		t.Fatal("last allowed failure should leave the job failed")
	}

	failed, err := repo.GetJob(job.ID)
	if err != nil {
		t.Fatalf("GetJob failed: %v", err)
	}
	if failed == nil || failed.Status != domain.AIJobFailed || failed.LastError != "permanent failure" {
		t.Fatalf("unexpected failed job: %+v", failed)
	}

	if err := repo.RetryJob(job.ID); err != nil {
		t.Fatalf("RetryJob: %v", err)
	}
	retried, _ := repo.GetJob(job.ID)
	if retried.Status != domain.AIJobQueued || retried.AttemptCount != 0 {
		t.Fatalf("manual retry should reset attempts: %+v", retried)
	}

	claimed, err = repo.ClaimNext([]domain.AICapability{domain.AICapabilityTagger})
	if err != nil || claimed == nil {
		t.Fatalf("claim retried job: job=%+v err=%v", claimed, err)
	}
	if err := repo.CancelJob(claimed.ID); err != nil {
		t.Fatalf("CancelJob: %v", err)
	}
	cancelled, _ := repo.GetJob(claimed.ID)
	if cancelled.Status != domain.AIJobCancelled || !cancelled.CancelRequested {
		t.Fatalf("unexpected cancelled job: %+v", cancelled)
	}
	analyses, _ := repo.GetByAsset(assetID)
	if len(analyses) != 1 || analyses[0].State != domain.AIAnalysisStale {
		t.Fatalf("cancelled analysis should be stale: %+v", analyses)
	}

	recoveryAssetID := createAIRepoTestAsset(t, database, "/tmp/ai-repo-recovery", "recovery.png")
	recoveryJob, _, err := repo.Enqueue(
		recoveryAssetID,
		domain.AICapabilityLightweightVision,
		domain.AIJobSourceManual,
		0,
		3,
	)
	if err != nil {
		t.Fatalf("enqueue recovery job: %v", err)
	}
	recoveryClaim, err := repo.ClaimNext([]domain.AICapability{domain.AICapabilityLightweightVision})
	if err != nil || recoveryClaim == nil {
		t.Fatalf("claim recovery job: job=%+v err=%v", recoveryClaim, err)
	}
	if recoveryClaim.AttemptCount != 1 {
		t.Fatalf("recovery claim attempts = %d, want 1", recoveryClaim.AttemptCount)
	}

	count, err := repo.RecoverInterrupted()
	if err != nil {
		t.Fatalf("RecoverInterrupted: %v", err)
	}
	if count != 1 {
		t.Fatalf("recovered jobs = %d, want 1", count)
	}
	recovered, _ := repo.GetJob(recoveryJob.ID)
	if recovered.Status != domain.AIJobQueued || recovered.AttemptCount != 0 {
		t.Fatalf("restart recovery should requeue without consuming retry budget: %+v", recovered)
	}
}

func TestAIAnalysisRepoBatchPriorityAndCapabilityClaim(t *testing.T) {
	database := openAIAnalysisTestDB(t)
	repo := NewAIAnalysisRepo(database)

	lib, err := NewLibraryRepo(database).Create("AI Batch", "/tmp/ai-batch")
	if err != nil {
		t.Fatal(err)
	}
	assetRepo := NewAssetRepo(database)
	var ids []int64
	for i, name := range []string{"a.png", "b.png", "c.png"} {
		id, err := assetRepo.Create(&domain.Asset{
			LibraryID:   lib.ID,
			FolderPath:  "/tmp/ai-batch",
			FileName:    name,
			FilePath:    "/tmp/ai-batch/" + name,
			Extension:   ".png",
			FileSize:    int64(i + 1),
			ThumbStatus: domain.ThumbStatusNone,
			StatusLabel: domain.StatusUnsorted,
		})
		if err != nil {
			t.Fatal(err)
		}
		ids = append(ids, id)
	}

	created, err := repo.EnqueueBatch(
		ids[:2],
		domain.AICapabilitySemanticSearch,
		domain.AIJobSourceManual,
		10,
		3,
	)
	if err != nil {
		t.Fatalf("EnqueueBatch: %v", err)
	}
	if created != 2 {
		t.Fatalf("batch created %d jobs, want 2", created)
	}
	_, _, err = repo.Enqueue(
		ids[2],
		domain.AICapabilityTagger,
		domain.AIJobSourceManual,
		100,
		3,
	)
	if err != nil {
		t.Fatal(err)
	}

	claimed, err := repo.ClaimNext([]domain.AICapability{domain.AICapabilitySemanticSearch})
	if err != nil {
		t.Fatalf("ClaimNext semantic only: %v", err)
	}
	if claimed == nil || claimed.Capability != domain.AICapabilitySemanticSearch {
		t.Fatalf("wrong capability claimed: %+v", claimed)
	}

	claimedTagger, err := repo.ClaimNext([]domain.AICapability{domain.AICapabilityTagger})
	if err != nil {
		t.Fatalf("ClaimNext tagger: %v", err)
	}
	if claimedTagger == nil || claimedTagger.AssetID != ids[2] {
		t.Fatalf("expected high-priority tagger job for third asset: %+v", claimedTagger)
	}
}


func TestAIAnalysisRepoListsOnlyAssetsNeedingCurrentModel(t *testing.T) {
	database := openAIAnalysisTestDB(t)
	repo := NewAIAnalysisRepo(database)

	lib, err := NewLibraryRepo(database).Create("Vision Backfill", "/tmp/vision-backfill")
	if err != nil {
		t.Fatal(err)
	}
	assetRepo := NewAssetRepo(database)
	var ids []int64
	for _, name := range []string{"ready.png", "stale.png", "missing.png"} {
		id, err := assetRepo.Create(&domain.Asset{
			LibraryID: lib.ID,
			FolderPath: "/tmp/vision-backfill",
			FileName: name,
			FilePath: "/tmp/vision-backfill/" + name,
			Extension: ".png",
			FileSize: 10,
			ThumbStatus: domain.ThumbStatusNone,
			StatusLabel: domain.StatusUnsorted,
		})
		if err != nil {
			t.Fatal(err)
		}
		ids = append(ids, id)
	}

	readyJob, _, err := repo.Enqueue(ids[0], domain.AICapabilityLightweightVision, domain.AIJobSourceManual, 0, 3)
	if err != nil { t.Fatal(err) }
	if _, err := repo.ClaimNext([]domain.AICapability{domain.AICapabilityLightweightVision}); err != nil { t.Fatal(err) }
	if err := repo.CompleteJob(readyJob.ID, "engine", "model", "1", `{"shortCaption":"ready"}`); err != nil { t.Fatal(err) }

	staleJob, _, err := repo.Enqueue(ids[1], domain.AICapabilityLightweightVision, domain.AIJobSourceManual, 0, 3)
	if err != nil { t.Fatal(err) }
	if _, err := repo.ClaimNext([]domain.AICapability{domain.AICapabilityLightweightVision}); err != nil { t.Fatal(err) }
	if err := repo.CompleteJob(staleJob.ID, "engine", "model", "0", `{"shortCaption":"old"}`); err != nil { t.Fatal(err) }

	needing, err := repo.ListNeedingAnalysis(
		lib.ID,
		domain.AICapabilityLightweightVision,
		"engine",
		"model",
		"1",
		0,
		100,
	)
	if err != nil {
		t.Fatalf("ListNeedingAnalysis: %v", err)
	}
	if len(needing) != 2 || needing[0] != ids[1] || needing[1] != ids[2] {
		t.Fatalf("needing = %v, want [%d %d]", needing, ids[1], ids[2])
	}
}


func TestAIAnalysisRepoForegroundSemanticClaimPreservesEnqueueOrder(t *testing.T) {
	database := openAIAnalysisTestDB(t)
	repo := NewAIAnalysisRepo(database)

	lib, err := NewLibraryRepo(database).Create("Semantic foreground", "/tmp/semantic-foreground")
	if err != nil {
		t.Fatal(err)
	}
	assetRepo := NewAssetRepo(database)
	create := func(name string, modified time.Time) int64 {
		t.Helper()
		id, err := assetRepo.Create(&domain.Asset{
			LibraryID:    lib.ID,
			FolderPath:   "/tmp/semantic-foreground",
			FileName:     name,
			FilePath:     "/tmp/semantic-foreground/" + name,
			Extension:    ".png",
			FileSize:     1,
			ThumbStatus:  domain.ThumbStatusNone,
			StatusLabel:  domain.StatusUnsorted,
			ModifiedAtFS: modified,
		})
		if err != nil {
			t.Fatal(err)
		}
		return id
	}

	firstVisible := create("first-visible.png", time.Date(2024, 1, 1, 0, 0, 0, 0, time.UTC))
	secondVisible := create("second-visible.png", time.Date(2026, 1, 1, 0, 0, 0, 0, time.UTC))

	for _, id := range []int64{firstVisible, secondVisible} {
		if _, _, err := repo.Enqueue(
			id,
			domain.AICapabilitySemanticSearch,
			domain.AIJobSourceAutomatic,
			250,
			3,
		); err != nil {
			t.Fatal(err)
		}
	}

	first, err := repo.ClaimNext([]domain.AICapability{domain.AICapabilitySemanticSearch})
	if err != nil {
		t.Fatal(err)
	}
	if first == nil || first.AssetID != firstVisible {
		t.Fatalf("foreground semantic order changed: got %+v want first visible asset %d", first, firstVisible)
	}
}

