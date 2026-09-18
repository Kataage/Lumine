package ai

import (
	"context"
	"errors"
	"os"
	"testing"
	"time"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
)

func setupAIQueueTest(t *testing.T) (*db.DB, *db.AIAnalysisRepo, int64) {
	t.Helper()
	dir, err := os.MkdirTemp("", "lumine-ai-queue-*")
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = os.RemoveAll(dir) })

	database, err := db.Open(dir)
	if err != nil {
		t.Fatalf("open db: %v", err)
	}
	t.Cleanup(func() { _ = database.Close() })

	lib, err := db.NewLibraryRepo(database).Create("Queue Test", "/tmp/ai-queue")
	if err != nil {
		t.Fatal(err)
	}
	assetID, err := db.NewAssetRepo(database).Create(&domain.Asset{
		LibraryID:   lib.ID,
		FolderPath:  "/tmp/ai-queue",
		FileName:    "queue.png",
		FilePath:    "/tmp/ai-queue/queue.png",
		Extension:   ".png",
		FileSize:    100,
		ThumbStatus: domain.ThumbStatusNone,
		StatusLabel: domain.StatusUnsorted,
	})
	if err != nil {
		t.Fatal(err)
	}

	return database, db.NewAIAnalysisRepo(database), assetID
}

func waitForAIJobStatus(t *testing.T, repo *db.AIAnalysisRepo, jobID int64, want domain.AIJobStatus) *domain.AIJob {
	t.Helper()
	deadline := time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) {
		job, err := repo.GetJob(jobID)
		if err != nil {
			t.Fatalf("GetJob: %v", err)
		}
		if job != nil && job.Status == want {
			return job
		}
		time.Sleep(20 * time.Millisecond)
	}
	job, _ := repo.GetJob(jobID)
	t.Fatalf("job %d did not reach %s; latest=%+v", jobID, want, job)
	return nil
}

func TestJobQueueAutomaticAnalysisRequiresOptIn(t *testing.T) {
	_, repo, assetID := setupAIQueueTest(t)

	settings := domain.AISettings{
		Enabled:        true,
		SemanticSearch: true,
		AutoAnalyze:    false,
	}
	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		return settings, nil
	}, 1)

	_, _, err := queue.Enqueue(assetID, domain.AICapabilitySemanticSearch, 0, true)
	if !errors.Is(err, ErrAutoAnalyzeDisabled) {
		t.Fatalf("automatic enqueue error = %v, want %v", err, ErrAutoAnalyzeDisabled)
	}

	job, created, err := queue.Enqueue(assetID, domain.AICapabilitySemanticSearch, 0, false)
	if err != nil {
		t.Fatalf("manual enqueue: %v", err)
	}
	if !created || job.Source != domain.AIJobSourceManual {
		t.Fatalf("unexpected manual job: created=%v job=%+v", created, job)
	}

	settings.Enabled = false
	_, _, err = queue.Enqueue(assetID, domain.AICapabilitySemanticSearch, 0, false)
	if !errors.Is(err, ErrCapabilityDisabled) {
		t.Fatalf("disabled feature enqueue error = %v, want %v", err, ErrCapabilityDisabled)
	}
}

func TestJobQueueProcessesAndPersistsModelProvenance(t *testing.T) {
	_, repo, assetID := setupAIQueueTest(t)

	settings := domain.AISettings{
		Enabled:        true,
		SemanticSearch: true,
	}
	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		return settings, nil
	}, 1)

	if err := queue.RegisterHandler(domain.AICapabilitySemanticSearch, func(
		ctx context.Context,
		job domain.AIJob,
	) (AnalysisOutput, error) {
		if job.AssetID != assetID {
			t.Fatalf("handler asset = %d, want %d", job.AssetID, assetID)
		}
		return AnalysisOutput{
			Engine:       "test-engine",
			ModelID:      "test-embedding",
			ModelVersion: "3.2.1",
			ResultJSON:   `{"embedding":"stored"}`,
		}, nil
	}); err != nil {
		t.Fatalf("RegisterHandler: %v", err)
	}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	if err := queue.Start(ctx); err != nil {
		t.Fatalf("Start: %v", err)
	}
	t.Cleanup(func() {
		stopCtx, stopCancel := context.WithTimeout(context.Background(), time.Second)
		defer stopCancel()
		_ = queue.Stop(stopCtx)
	})

	job, _, err := queue.Enqueue(assetID, domain.AICapabilitySemanticSearch, 20, false)
	if err != nil {
		t.Fatalf("Enqueue: %v", err)
	}
	waitForAIJobStatus(t, repo, job.ID, domain.AIJobCompleted)

	analyses, err := repo.GetByAsset(assetID)
	if err != nil {
		t.Fatalf("GetByAsset: %v", err)
	}
	if len(analyses) != 1 {
		t.Fatalf("analysis count = %d, want 1", len(analyses))
	}
	got := analyses[0]
	if got.State != domain.AIAnalysisReady ||
		got.Engine != "test-engine" ||
		got.ModelID != "test-embedding" ||
		got.ModelVersion != "3.2.1" {
		t.Fatalf("persisted analysis provenance mismatch: %+v", got)
	}
}

func TestJobQueueDisablingCapabilityRequeuesActiveWork(t *testing.T) {
	_, repo, assetID := setupAIQueueTest(t)

	settings := domain.AISettings{
		Enabled:         true,
		AdvancedVision:  true,
		GPUAcceleration: true,
	}
	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		return settings, nil
	}, 1)

	started := make(chan struct{}, 1)
	cancelled := make(chan struct{}, 1)
	if err := queue.RegisterHandler(domain.AICapabilityAdvancedVision, func(
		ctx context.Context,
		job domain.AIJob,
	) (AnalysisOutput, error) {
		started <- struct{}{}
		<-ctx.Done()
		cancelled <- struct{}{}
		return AnalysisOutput{}, ctx.Err()
	}); err != nil {
		t.Fatalf("RegisterHandler: %v", err)
	}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	if err := queue.Start(ctx); err != nil {
		t.Fatalf("Start: %v", err)
	}
	t.Cleanup(func() {
		stopCtx, stopCancel := context.WithTimeout(context.Background(), time.Second)
		defer stopCancel()
		_ = queue.Stop(stopCtx)
	})

	job, _, err := queue.Enqueue(assetID, domain.AICapabilityAdvancedVision, 0, false)
	if err != nil {
		t.Fatalf("Enqueue: %v", err)
	}

	select {
	case <-started:
	case <-time.After(5 * time.Second):
		t.Fatal("handler did not start")
	}
	waitForAIJobStatus(t, repo, job.ID, domain.AIJobRunning)

	settings.AdvancedVision = false
	if err := queue.ApplySettings(settings); err != nil {
		t.Fatalf("ApplySettings: %v", err)
	}

	select {
	case <-cancelled:
	case <-time.After(5 * time.Second):
		t.Fatal("handler was not cancelled")
	}
	requeued := waitForAIJobStatus(t, repo, job.ID, domain.AIJobQueued)
	if requeued.AttemptCount != 0 {
		t.Fatalf("settings pause should not consume retry budget: %+v", requeued)
	}
}

func TestJobQueueStartRecoversInterruptedJobs(t *testing.T) {
	_, repo, assetID := setupAIQueueTest(t)

	job, _, err := repo.Enqueue(
		assetID,
		domain.AICapabilityTagger,
		domain.AIJobSourceManual,
		0,
		3,
	)
	if err != nil {
		t.Fatal(err)
	}
	claimed, err := repo.ClaimNext([]domain.AICapability{domain.AICapabilityTagger})
	if err != nil || claimed == nil {
		t.Fatalf("ClaimNext: job=%+v err=%v", claimed, err)
	}

	settings := domain.AISettings{Enabled: true, Tagger: true}
	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		return settings, nil
	}, 1)

	// No handler is registered, so Start should recover the old running row to
	// queued and leave it durable rather than trying to execute it.
	ctx, cancel := context.WithCancel(context.Background())
	if err := queue.Start(ctx); err != nil {
		t.Fatalf("Start: %v", err)
	}
	cancel()
	stopCtx, stopCancel := context.WithTimeout(context.Background(), time.Second)
	defer stopCancel()
	if err := queue.Stop(stopCtx); err != nil {
		t.Fatalf("Stop: %v", err)
	}

	recovered, err := repo.GetJob(job.ID)
	if err != nil {
		t.Fatal(err)
	}
	if recovered.Status != domain.AIJobQueued || recovered.AttemptCount != 0 {
		t.Fatalf("unexpected recovered job: %+v", recovered)
	}
}
