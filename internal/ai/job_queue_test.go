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
	if err := queue.ApplySettings(settings); err != nil {
		t.Fatalf("apply disabled settings: %v", err)
	}
	_, _, err = queue.Enqueue(assetID, domain.AICapabilitySemanticSearch, 0, false)
	if !errors.Is(err, ErrCapabilityDisabled) {
		t.Fatalf("disabled feature enqueue error = %v, want %v", err, ErrCapabilityDisabled)
	}
}

func TestJobQueueCachesSettingsAcrossIdleWorkerPollsAndAcceptsPushUpdates(t *testing.T) {
	_, repo, _ := setupAIQueueTest(t)

	settings := domain.AISettings{
		Enabled:        true,
		SemanticSearch: true,
	}
	var callsMu sync.Mutex
	providerCalls := 0
	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		callsMu.Lock()
		providerCalls++
		callsMu.Unlock()
		return settings, nil
	}, 4)

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

	// Workers wake once per second when idle. Before settings caching, a
	// four-worker queue could therefore read persisted settings roughly four
	// times per second. Wait across more than one tick and require one provider
	// read for the whole queue lifetime.
	time.Sleep(1200 * time.Millisecond)
	callsMu.Lock()
	gotCalls := providerCalls
	callsMu.Unlock()
	if gotCalls != 1 {
		t.Fatalf("settings provider calls after idle polling = %d, want 1", gotCalls)
	}

	updated := settings
	updated.SemanticSearch = false
	if err := queue.ApplySettings(updated); err != nil {
		t.Fatal(err)
	}
	got, err := queue.currentSettings()
	if err != nil {
		t.Fatal(err)
	}
	if got.SemanticSearch {
		t.Fatal("pushed settings update did not replace cached snapshot")
	}
	callsMu.Lock()
	gotCalls = providerCalls
	callsMu.Unlock()
	if gotCalls != 1 {
		t.Fatalf("push update unexpectedly re-read settings provider: calls=%d", gotCalls)
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


func TestJobQueueBoundsSerializedCapabilityAndKeepsIndependentRuntimeMoving(t *testing.T) {
	database, repo, firstAssetID := setupAIQueueTest(t)
	assetRepo := db.NewAssetRepo(database)
	firstAsset, err := assetRepo.GetByID(firstAssetID)
	if err != nil || firstAsset == nil {
		t.Fatalf("get first asset: asset=%+v err=%v", firstAsset, err)
	}
	createAsset := func(name string) int64 {
		t.Helper()
		next := *firstAsset
		next.ID = 0
		next.FileName = name
		next.FilePath = "/tmp/ai-queue/" + name
		id, err := assetRepo.Create(&next)
		if err != nil {
			t.Fatal(err)
		}
		return id
	}
	secondSemanticAssetID := createAsset("semantic-2.png")
	thirdSemanticAssetID := createAsset("semantic-3.png")

	settings := domain.AISettings{
		Enabled:        true,
		SemanticSearch: true,
		Tagger:         true,
		AdvancedVision: true,
	}
	// Three generic workers: the admission limit for one serialized capability
	// is two, leaving one worker for an independent runtime.
	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		return settings, nil
	}, 3)

	semanticStarted := make(chan int64, 3)
	semanticRelease := make(chan struct{})
	if err := queue.RegisterHandler(domain.AICapabilitySemanticSearch, func(
		ctx context.Context,
		job domain.AIJob,
	) (AnalysisOutput, error) {
		semanticStarted <- job.ID
		select {
		case <-ctx.Done():
			return AnalysisOutput{}, ctx.Err()
		case <-semanticRelease:
		}
		return AnalysisOutput{
			Engine:       "semantic-engine",
			ModelID:      "semantic-model",
			ModelVersion: "1",
			ResultJSON:   "{}",
		}, nil
	}); err != nil {
		t.Fatal(err)
	}

	independentStarted := make(chan domain.AICapability, 2)
	independentRelease := make(chan struct{}, 2)
	registerIndependent := func(capability domain.AICapability) {
		t.Helper()
		if err := queue.RegisterHandler(capability, func(
			ctx context.Context,
			job domain.AIJob,
		) (AnalysisOutput, error) {
			independentStarted <- capability
			select {
			case <-ctx.Done():
				return AnalysisOutput{}, ctx.Err()
			case <-independentRelease:
			}
			return AnalysisOutput{
				Engine:       string(capability) + "-engine",
				ModelID:      string(capability) + "-model",
				ModelVersion: "1",
				ResultJSON:   "{}",
			}, nil
		}); err != nil {
			t.Fatal(err)
		}
	}
	registerIndependent(domain.AICapabilityTagger)
	registerIndependent(domain.AICapabilityAdvancedVision)

	// Queue before Start so claim ordering is deterministic across the serialized
	// claimMu: two highest-priority Semantic jobs consume the bounded admission,
	// then the highest-priority *available* independent capability is chosen.
	semantic1, _, err := queue.Enqueue(firstAssetID, domain.AICapabilitySemanticSearch, 100, false)
	if err != nil {
		t.Fatal(err)
	}
	semantic2, _, err := queue.Enqueue(secondSemanticAssetID, domain.AICapabilitySemanticSearch, 90, false)
	if err != nil {
		t.Fatal(err)
	}
	semantic3, _, err := queue.Enqueue(thirdSemanticAssetID, domain.AICapabilitySemanticSearch, 85, false)
	if err != nil {
		t.Fatal(err)
	}
	taggerJob, _, err := queue.Enqueue(firstAssetID, domain.AICapabilityTagger, 80, false)
	if err != nil {
		t.Fatal(err)
	}
	visionJob, _, err := queue.Enqueue(firstAssetID, domain.AICapabilityAdvancedVision, 70, false)
	if err != nil {
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

	for i := 0; i < 2; i++ {
		select {
		case <-semanticStarted:
		case <-time.After(5 * time.Second):
			t.Fatal("two-stage Semantic pipeline did not fill its bounded admission")
		}
	}
	select {
	case extra := <-semanticStarted:
		t.Fatalf("third Semantic job consumed all generic capacity: job=%d", extra)
	case <-time.After(150 * time.Millisecond):
	}

	select {
	case capability := <-independentStarted:
		if capability != domain.AICapabilityTagger {
			t.Fatalf("available priority order chose %s, want tagger", capability)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("independent Tagger runtime made no progress while Semantic was saturated")
	}

	thirdState, err := repo.GetJob(semantic3.ID)
	if err != nil {
		t.Fatal(err)
	}
	if thirdState.Status != domain.AIJobQueued || thirdState.AttemptCount != 0 {
		t.Fatalf("third Semantic job should remain durably queued outside admission: %+v", thirdState)
	}

	// Free the independent worker. Semantic remains admission-saturated, so the
	// next available capability must be Advanced Vision rather than semantic3.
	independentRelease <- struct{}{}
	waitForAIJobStatus(t, repo, taggerJob.ID, domain.AIJobCompleted)
	select {
	case capability := <-independentStarted:
		if capability != domain.AICapabilityAdvancedVision {
			t.Fatalf("next available capability = %s, want advanced_vision", capability)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("Advanced Vision did not progress while Semantic remained saturated")
	}

	close(semanticRelease)
	independentRelease <- struct{}{}
	waitForAIJobStatus(t, repo, semantic1.ID, domain.AIJobCompleted)
	waitForAIJobStatus(t, repo, semantic2.ID, domain.AIJobCompleted)
	waitForAIJobStatus(t, repo, semantic3.ID, domain.AIJobCompleted)
	waitForAIJobStatus(t, repo, visionJob.ID, domain.AIJobCompleted)
}

func TestJobQueueSameCapabilityPipelineIsBoundedButStillOverlaps(t *testing.T) {
	database, repo, firstAssetID := setupAIQueueTest(t)
	assetRepo := db.NewAssetRepo(database)
	firstAsset, err := assetRepo.GetByID(firstAssetID)
	if err != nil || firstAsset == nil {
		t.Fatalf("get first asset: asset=%+v err=%v", firstAsset, err)
	}
	ids := []int64{firstAssetID}
	for n := 2; n <= 3; n++ {
		next := *firstAsset
		next.ID = 0
		next.FileName = fmt.Sprintf("queue-%d.png", n)
		next.FilePath = fmt.Sprintf("/tmp/ai-queue/queue-%d.png", n)
		id, err := assetRepo.Create(&next)
		if err != nil {
			t.Fatal(err)
		}
		ids = append(ids, id)
	}

	settings := domain.AISettings{Enabled: true, SemanticSearch: true}
	queue := NewJobQueue(repo, func() (domain.AISettings, error) {
		return settings, nil
	}, 4)

	started := make(chan int64, 3)
	release := make(chan struct{})
	if err := queue.RegisterHandler(domain.AICapabilitySemanticSearch, func(
		ctx context.Context,
		job domain.AIJob,
	) (AnalysisOutput, error) {
		started <- job.ID
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

	jobs := make([]domain.AIJob, 0, len(ids))
	for _, assetID := range ids {
		job, _, err := queue.Enqueue(assetID, domain.AICapabilitySemanticSearch, 0, false)
		if err != nil {
			t.Fatal(err)
		}
		jobs = append(jobs, job)
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

	for i := 0; i < 2; i++ {
		select {
		case <-started:
		case <-time.After(5 * time.Second):
			t.Fatal("Semantic pipeline did not allow one look-ahead handler")
		}
	}
	select {
	case extra := <-started:
		t.Fatalf("Semantic admission exceeded two handlers: job=%d", extra)
	case <-time.After(150 * time.Millisecond):
	}

	close(release)
	for _, job := range jobs {
		waitForAIJobStatus(t, repo, job.ID, domain.AIJobCompleted)
	}
}

func TestJobQueueInteractiveUIThrottlesWithoutCancellingActiveWork(t *testing.T) {
	_, repo, assetID := setupAIQueueTest(t)

	settings := domain.AISettings{
		Enabled:        true,
		SemanticSearch: true,
		AutoAnalyze:    true,
		Tagger:         true,
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

	if err := queue.RegisterHandler(domain.AICapabilityTagger, func(
		ctx context.Context,
		job domain.AIJob,
	) (AnalysisOutput, error) {
		return AnalysisOutput{
			Engine:       "tagger-engine",
			ModelID:      "tagger-model",
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
