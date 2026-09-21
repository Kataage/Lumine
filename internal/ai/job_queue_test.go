package ai

import (
	"context"
	"errors"
	"fmt"
	"os"
	"sync"
	"testing"
	"time"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
)


type busyThenCompleteRepo struct {
	*db.AIAnalysisRepo
	mu              sync.Mutex
	remainingBusy   int
	semanticCalls   int
}

type queueSQLiteBusyError struct {
	code int
}

func (e queueSQLiteBusyError) Error() string { return "simulated sqlite busy" }
func (e queueSQLiteBusyError) Code() int     { return e.code }

func (r *busyThenCompleteRepo) CompleteSemanticJob(
	jobID int64,
	engine string,
	modelID string,
	modelVersion string,
	resultJSON string,
) error {
	r.mu.Lock()
	r.semanticCalls++
	if r.remainingBusy > 0 {
		r.remainingBusy--
		r.mu.Unlock()
		return queueSQLiteBusyError{code: 517}
	}
	r.mu.Unlock()
	return r.AIAnalysisRepo.CompleteSemanticJob(jobID, engine, modelID, modelVersion, resultJSON)
}

func (r *busyThenCompleteRepo) semanticCallCount() int {
	r.mu.Lock()
	defer r.mu.Unlock()
	return r.semanticCalls
}

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

func TestJobQueueCapabilityWorkPendingTracksDurableBacklog(t *testing.T) {
	_, repo, assetID := setupAIQueueTest(t)
	settings := domain.AISettings{Enabled: true, SemanticSearch: true}
	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		return settings, nil
	}, 1)

	if queue.CapabilityWorkPending(domain.AICapabilitySemanticSearch) {
		t.Fatal("empty Semantic queue reported pending work")
	}
	job, _, err := queue.Enqueue(assetID, domain.AICapabilitySemanticSearch, 0, false)
	if err != nil {
		t.Fatal(err)
	}
	if !queue.CapabilityWorkPending(domain.AICapabilitySemanticSearch) {
		t.Fatal("queued Semantic job was not observed")
	}

	claimed, err := repo.ClaimNext([]domain.AICapability{domain.AICapabilitySemanticSearch})
	if err != nil || claimed == nil || claimed.ID != job.ID {
		t.Fatalf("claim Semantic job: job=%+v err=%v", claimed, err)
	}
	if !queue.CapabilityWorkPending(domain.AICapabilitySemanticSearch) {
		t.Fatal("running Semantic job was not observed")
	}
	if err := repo.CompleteJob(job.ID, "engine", "model", "1", "{}"); err != nil {
		t.Fatal(err)
	}
	if queue.CapabilityWorkPending(domain.AICapabilitySemanticSearch) {
		t.Fatal("completed Semantic job still reported pending work")
	}
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


func TestJobQueueWorkersProcessJobsConcurrently(t *testing.T) {
	database, repo, firstAssetID := setupAIQueueTest(t)
	firstAsset, err := db.NewAssetRepo(database).GetByID(firstAssetID)
	if err != nil || firstAsset == nil {
		t.Fatalf("get first asset: asset=%+v err=%v", firstAsset, err)
	}
	secondAsset := *firstAsset
	secondAsset.ID = 0
	secondAsset.FileName = "queue-2.png"
	secondAsset.FilePath = "/tmp/ai-queue/queue-2.png"
	secondAssetID, err := db.NewAssetRepo(database).Create(&secondAsset)
	if err != nil {
		t.Fatal(err)
	}

	settings := domain.AISettings{
		Enabled:        true,
		SemanticSearch: true,
	}
	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		return settings, nil
	}, 2)

	started := make(chan struct{}, 2)
	release := make(chan struct{})
	var mu sync.Mutex
	active := 0
	maxActive := 0
	if err := queue.RegisterHandler(domain.AICapabilitySemanticSearch, func(
		ctx context.Context,
		job domain.AIJob,
	) (AnalysisOutput, error) {
		mu.Lock()
		active++
		if active > maxActive {
			maxActive = active
		}
		mu.Unlock()
		started <- struct{}{}
		select {
		case <-ctx.Done():
			return AnalysisOutput{}, ctx.Err()
		case <-release:
		}
		mu.Lock()
		active--
		mu.Unlock()
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

	firstJob, _, err := queue.Enqueue(firstAssetID, domain.AICapabilitySemanticSearch, 0, false)
	if err != nil {
		t.Fatal(err)
	}
	secondJob, _, err := queue.Enqueue(secondAssetID, domain.AICapabilitySemanticSearch, 0, false)
	if err != nil {
		t.Fatal(err)
	}

	for i := 0; i < 2; i++ {
		select {
		case <-started:
		case <-time.After(5 * time.Second):
			t.Fatal("two workers did not process jobs concurrently")
		}
	}
	mu.Lock()
	observedMax := maxActive
	mu.Unlock()
	if observedMax < 2 {
		t.Fatalf("max concurrent handlers = %d, want at least 2", observedMax)
	}

	close(release)
	waitForAIJobStatus(t, repo, firstJob.ID, domain.AIJobCompleted)
	waitForAIJobStatus(t, repo, secondJob.ID, domain.AIJobCompleted)
}

func TestJobQueueInteractiveUIThrottlesWithoutCancellingActiveWork(t *testing.T) {
	_, repo, assetID := setupAIQueueTest(t)

	settings := domain.AISettings{
		Enabled:        true,
		SemanticSearch: true,
		AutoAnalyze:    true,
	}
	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		return settings, nil
	}, 2)

	started := make(chan struct{}, 1)
	release := make(chan struct{})
	if err := queue.RegisterHandler(domain.AICapabilitySemanticSearch, func(
		ctx context.Context,
		job domain.AIJob,
	) (AnalysisOutput, error) {
		started <- struct{}{}
		select {
		case <-ctx.Done():
			return AnalysisOutput{}, ctx.Err()
		case <-release:
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
	case <-started:
	case <-time.After(5 * time.Second):
		t.Fatal("AI job did not start")
	}
	waitForAIJobStatus(t, repo, job.ID, domain.AIJobRunning)

	queue.SetInteractiveUIActive(true)
	time.Sleep(150 * time.Millisecond)
	stillRunning, err := repo.GetJob(job.ID)
	if err != nil {
		t.Fatal(err)
	}
	if stillRunning.Status != domain.AIJobRunning || stillRunning.AttemptCount != 1 {
		t.Fatalf("viewer throttle should not cancel/requeue active work: %+v", stillRunning)
	}

	worker0, err := queue.runnableCapabilities(0)
	if err != nil {
		t.Fatal(err)
	}
	worker1, err := queue.runnableCapabilities(1)
	if err != nil {
		t.Fatal(err)
	}
	if len(worker0) == 0 {
		t.Fatal("primary worker should remain runnable during viewer activity")
	}
	if len(worker1) != 0 {
		t.Fatalf("extra worker should be throttled during viewer activity: %v", worker1)
	}

	close(release)
	waitForAIJobStatus(t, repo, job.ID, domain.AIJobCompleted)
	queue.SetInteractiveUIActive(false)
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


func TestJobQueueRetriesSemanticFinalizationWithoutRerunningHandler(t *testing.T) {
	database, baseRepo, assetID := setupAIQueueTest(t)
	semanticRepo := db.NewSemanticEmbeddingRepo(database)
	repo := &busyThenCompleteRepo{
		AIAnalysisRepo: baseRepo,
		remainingBusy:  2,
	}

	settings := domain.AISettings{
		Enabled:        true,
		SemanticSearch: true,
	}
	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		return settings, nil
	}, 1)
	diagnostics := NewSemanticDiagnostics()
	diagnostics.SetEnabled(true)
	queue.SetSemanticDiagnostics(diagnostics)

	var handlerMu sync.Mutex
	handlerCalls := 0
	afterCommitCalls := 0
	if err := queue.RegisterHandler(domain.AICapabilitySemanticSearch, func(
		ctx context.Context,
		job domain.AIJob,
	) (AnalysisOutput, error) {
		handlerMu.Lock()
		handlerCalls++
		handlerMu.Unlock()

		const resultJSON = `{"dimensions":2,"kind":"image_embedding"}`
		if err := semanticRepo.StageAnalysisResult(
			ctx,
			job.ID,
			job.AssetID,
			"siglip2-onnx",
			"siglip2",
			"revision-1",
			resultJSON,
			[]float32{1, 0},
		); err != nil {
			return AnalysisOutput{}, err
		}
		return WithDurableSemanticResult(AnalysisOutput{
			Engine:       "siglip2-onnx",
			ModelID:      "siglip2",
			ModelVersion: "revision-1",
			ResultJSON:   resultJSON,
		}, func() {
			handlerMu.Lock()
			afterCommitCalls++
			handlerMu.Unlock()
		}), nil
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
	waitForAIJobStatus(t, baseRepo, job.ID, domain.AIJobCompleted)

	handlerMu.Lock()
	gotHandlerCalls := handlerCalls
	gotAfterCommitCalls := afterCommitCalls
	handlerMu.Unlock()
	if gotHandlerCalls != 1 {
		t.Fatalf("handler/inference calls = %d, want 1", gotHandlerCalls)
	}
	if gotAfterCommitCalls != 1 {
		t.Fatalf("after-commit calls = %d, want 1", gotAfterCommitCalls)
	}
	if got := repo.semanticCallCount(); got != 3 {
		t.Fatalf("semantic finalization calls = %d, want 3 (2 busy + success)", got)
	}
	snapshot := diagnostics.Snapshot()
	if snapshot.FailedCompletionCount < 2 || snapshot.SQLiteBusySnapshotCount < 2 {
		t.Fatalf("transient finalization failures were not observable: %+v", snapshot)
	}

	ready, err := semanticRepo.GetReady(assetID)
	if err != nil {
		t.Fatal(err)
	}
	if ready == nil || ready.ModelVersion != "revision-1" {
		t.Fatalf("semantic embedding not durably ready after retry: %+v", ready)
	}
}
