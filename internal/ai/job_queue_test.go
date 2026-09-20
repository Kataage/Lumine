package ai

import (
	"context"
	"errors"
	"fmt"
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
			return AnalysisOutput{}, fmt.Errorf("handler asset = %d, want %d", job.AssetID, assetID)
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


func TestJobQueueForegroundPauseYieldsAndResumesCapability(t *testing.T) {
	_, repo, assetID := setupAIQueueTest(t)

	settings := domain.AISettings{
		Enabled:        true,
		SemanticSearch: true,
	}
	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		return settings, nil
	}, 1)

	started := make(chan int, 2)
	attempt := 0
	if err := queue.RegisterHandler(domain.AICapabilitySemanticSearch, func(
		ctx context.Context,
		job domain.AIJob,
	) (AnalysisOutput, error) {
		attempt++
		started <- attempt
		if attempt == 1 {
			<-ctx.Done()
			return AnalysisOutput{}, ctx.Err()
		}
		return AnalysisOutput{
			Engine:       "engine",
			ModelID:      "model",
			ModelVersion: "1",
			ResultJSON:   "{}",
		}, nil
	}); err != nil {
		t.Fatal(err)
	}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	if err := queue.Start(ctx); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		stopCtx, stopCancel := context.WithTimeout(context.Background(), time.Second)
		defer stopCancel()
		_ = queue.Stop(stopCtx)
	})

	job, _, err := queue.Enqueue(assetID, domain.AICapabilitySemanticSearch, 0, false)
	if err != nil {
		t.Fatal(err)
	}
	select {
	case got := <-started:
		if got != 1 {
			t.Fatalf("first handler attempt = %d", got)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("semantic job did not start")
	}
	waitForAIJobStatus(t, repo, job.ID, domain.AIJobRunning)

	resume := queue.PauseCapabilityForForeground(domain.AICapabilitySemanticSearch)
	waitForAIJobStatus(t, repo, job.ID, domain.AIJobQueued)

	select {
	case got := <-started:
		t.Fatalf("semantic job restarted while foreground pause was active: attempt %d", got)
	case <-time.After(150 * time.Millisecond):
	}

	resume()
	select {
	case got := <-started:
		if got != 2 {
			t.Fatalf("resumed handler attempt = %d, want 2", got)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("semantic job did not resume")
	}
	waitForAIJobStatus(t, repo, job.ID, domain.AIJobCompleted)
}


func TestJobQueueInteractiveUIYieldsAndResumesWork(t *testing.T) {
	_, repo, assetID := setupAIQueueTest(t)

	settings := domain.AISettings{
		Enabled:        true,
		SemanticSearch: true,
		AutoAnalyze:    true,
	}
	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		return settings, nil
	}, 1)

	started := make(chan int, 2)
	attempt := 0
	if err := queue.RegisterHandler(domain.AICapabilitySemanticSearch, func(
		ctx context.Context,
		job domain.AIJob,
	) (AnalysisOutput, error) {
		attempt++
		started <- attempt
		if attempt == 1 {
			<-ctx.Done()
			return AnalysisOutput{}, ctx.Err()
		}
		return AnalysisOutput{
			Engine:       "engine",
			ModelID:      "model",
			ModelVersion: "1",
			ResultJSON:   "{}",
		}, nil
	}); err != nil {
		t.Fatal(err)
	}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	if err := queue.Start(ctx); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		stopCtx, stopCancel := context.WithTimeout(context.Background(), time.Second)
		defer stopCancel()
		_ = queue.Stop(stopCtx)
	})

	job, _, err := queue.Enqueue(assetID, domain.AICapabilitySemanticSearch, -100, true)
	if err != nil {
		t.Fatal(err)
	}

	select {
	case got := <-started:
		if got != 1 {
			t.Fatalf("first handler attempt = %d", got)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("AI job did not start")
	}
	waitForAIJobStatus(t, repo, job.ID, domain.AIJobRunning)

	queue.SetInteractiveUIActive(true)
	requeued := waitForAIJobStatus(t, repo, job.ID, domain.AIJobQueued)
	if requeued.AttemptCount != 0 {
		t.Fatalf("viewer yield consumed retry budget: %+v", requeued)
	}

	select {
	case got := <-started:
		t.Fatalf("AI job restarted while viewer was active: attempt %d", got)
	case <-time.After(150 * time.Millisecond):
	}

	queue.SetInteractiveUIActive(false)
	select {
	case got := <-started:
		if got != 2 {
			t.Fatalf("resumed handler attempt = %d, want 2", got)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("AI job did not resume after viewer became idle")
	}
	waitForAIJobStatus(t, repo, job.ID, domain.AIJobCompleted)
}


func TestJobQueueStartupHoldPreventsClaimUntilRestoreCompletes(t *testing.T) {
	_, repo, assetID := setupAIQueueTest(t)

	settings := domain.AISettings{
		Enabled:        true,
		SemanticSearch: true,
	}
	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		return settings, nil
	}, 1)

	started := make(chan struct{}, 1)
	if err := queue.RegisterHandler(domain.AICapabilitySemanticSearch, func(
		ctx context.Context,
		job domain.AIJob,
	) (AnalysisOutput, error) {
		started <- struct{}{}
		return AnalysisOutput{
			Engine:       "engine",
			ModelID:      "model",
			ModelVersion: "1",
			ResultJSON:   "{}",
		}, nil
	}); err != nil {
		t.Fatal(err)
	}

	queue.SetStartupHold(true)
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	if err := queue.Start(ctx); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		stopCtx, stopCancel := context.WithTimeout(context.Background(), time.Second)
		defer stopCancel()
		_ = queue.Stop(stopCtx)
	})

	job, _, err := queue.Enqueue(assetID, domain.AICapabilitySemanticSearch, 0, false)
	if err != nil {
		t.Fatal(err)
	}

	select {
	case <-started:
		t.Fatal("job started while startup hold was active")
	case <-time.After(200 * time.Millisecond):
	}
	held, err := repo.GetJob(job.ID)
	if err != nil {
		t.Fatal(err)
	}
	if held.Status != domain.AIJobQueued || held.AttemptCount != 0 {
		t.Fatalf("startup hold must preserve queued job without consuming attempts: %+v", held)
	}

	queue.SetStartupHold(false)
	select {
	case <-started:
	case <-time.After(5 * time.Second):
		t.Fatal("job did not start after startup hold was released")
	}
	waitForAIJobStatus(t, repo, job.ID, domain.AIJobCompleted)
}


func TestJobQueueModelActivationHonorsAnalysisRevision(t *testing.T) {
	_, repo, assetID := setupAIQueueTest(t)

	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		return domain.DefaultAISettings(), nil
	}, 1)

	job, _, err := repo.Enqueue(
		assetID,
		domain.AICapabilitySemanticSearch,
		domain.AIJobSourceManual,
		0,
		3,
	)
	if err != nil {
		t.Fatal(err)
	}
	claimed, err := repo.ClaimNext([]domain.AICapability{domain.AICapabilitySemanticSearch})
	if err != nil || claimed == nil {
		t.Fatalf("claim old semantic job: job=%+v err=%v", claimed, err)
	}
	if err := repo.CompleteJob(job.ID, "siglip2-onnx", "siglip2", "base-version", "{}"); err != nil {
		t.Fatal(err)
	}

	model := InstalledModel{Manifest: ModelManifest{
		Engine:  "siglip2-onnx",
		ID:      "siglip2",
		Version: "base-version",
		Parameters: map[string]string{
			"analysis_revision": "preprocess-v2",
		},
	}}
	if err := queue.HandleModelActivated(domain.AICapabilitySemanticSearch, model); err != nil {
		t.Fatal(err)
	}
	analyses, err := repo.GetByAsset(assetID)
	if err != nil {
		t.Fatal(err)
	}
	if len(analyses) != 1 || analyses[0].State != domain.AIAnalysisStale {
		t.Fatalf("old semantic analysis was not invalidated: %+v", analyses)
	}

	job, _, err = repo.Enqueue(
		assetID,
		domain.AICapabilitySemanticSearch,
		domain.AIJobSourceManual,
		0,
		3,
	)
	if err != nil {
		t.Fatal(err)
	}
	claimed, err = repo.ClaimNext([]domain.AICapability{domain.AICapabilitySemanticSearch})
	if err != nil || claimed == nil {
		t.Fatalf("claim revised semantic job: job=%+v err=%v", claimed, err)
	}
	if err := repo.CompleteJob(
		job.ID,
		"siglip2-onnx",
		"siglip2",
		"base-version+preprocess-v2",
		"{}",
	); err != nil {
		t.Fatal(err)
	}
	if err := queue.HandleModelActivated(domain.AICapabilitySemanticSearch, model); err != nil {
		t.Fatal(err)
	}
	analyses, err = repo.GetByAsset(assetID)
	if err != nil {
		t.Fatal(err)
	}
	if len(analyses) != 1 || analyses[0].State != domain.AIAnalysisReady {
		t.Fatalf("matching analysis revision was incorrectly invalidated: %+v", analyses)
	}
}
