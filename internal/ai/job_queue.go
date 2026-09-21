package ai

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"sort"
	"strings"
	"sync"
	"time"

	"github.com/kataage/lumine/internal/domain"
)

var ErrAutoAnalyzeDisabled = errors.New("automatic AI analysis is disabled")

type AnalysisOutput struct {
	Engine       string `json:"engine"`
	ModelID      string `json:"modelId"`
	ModelVersion string `json:"modelVersion"`
	ResultJSON   string `json:"resultJson"`

	semanticResultStaged bool
	afterCommit          func()
}

func WithDurableSemanticResult(output AnalysisOutput, afterCommit func()) AnalysisOutput {
	output.semanticResultStaged = true
	output.afterCommit = afterCommit
	return output
}

func (o AnalysisOutput) runAfterCommit() {
	if o.afterCommit != nil {
		o.afterCommit()
	}
}

type AnalysisHandler func(context.Context, domain.AIJob) (AnalysisOutput, error)

type AnalysisJobRepository interface {
	Enqueue(assetID int64, capability domain.AICapability, source domain.AIJobSource, priority int, maxAttempts int) (domain.AIJob, bool, error)
	EnqueueBatch(assetIDs []int64, capability domain.AICapability, source domain.AIJobSource, priority int, maxAttempts int) (int, error)
	ClaimNext(capabilities []domain.AICapability) (*domain.AIJob, error)
	CompleteJob(jobID int64, engine, modelID, modelVersion, resultJSON string) error
	CompleteSemanticJob(jobID int64, engine, modelID, modelVersion, resultJSON string) error
	FailOrRequeue(jobID int64, message string) (bool, error)
	CancelJob(jobID int64) error
	RetryJob(jobID int64) error
	RequeueInterrupted(jobID int64, message string) error
	RecoverInterrupted() (int64, error)
	MarkStaleForModel(capability domain.AICapability, engine, modelID, modelVersion string) (int64, error)
	MarkStaleForAssets(capability domain.AICapability, assetIDs []int64) (int64, error)
	ListNeedingAnalysis(libraryID int64, capability domain.AICapability, engine, modelID, modelVersion string, afterID int64, limit int) ([]int64, error)
	GetByAsset(assetID int64) ([]domain.AIAnalysis, error)
	GetJob(id int64) (*domain.AIJob, error)
	ListJobs(limit int) ([]domain.AIJob, error)
}

type analysisJobDiagnosticsRepository interface {
	AIJobDiagnosticsCounts(capability domain.AICapability, longRunningBefore time.Time) (domain.AIJobDiagnosticsCounts, error)
}

type activeAIJob struct {
	capability domain.AICapability
	cancel     context.CancelFunc
}

type JobQueueStatus struct {
	Started            bool     `json:"started"`
	Workers            int      `json:"workers"`
	ActiveCount        int      `json:"activeCount"`
	ActiveCapabilities []string `json:"activeCapabilities"`
	PausedCapabilities []string `json:"pausedCapabilities"`
}

type JobQueue struct {
	repo     AnalysisJobRepository
	settings SettingsProvider
	workers  int

	mu       sync.Mutex
	handlers map[domain.AICapability]AnalysisHandler
	active   map[int64]activeAIJob
	paused   map[domain.AICapability]int
	started     bool
	startupHeld bool
	uiPaused    bool
	cancel      context.CancelFunc
	wake     chan struct{}
	wg       sync.WaitGroup

	claimMu sync.Mutex

	semanticDiagnostics *SemanticDiagnostics
}

func NewJobQueue(repo AnalysisJobRepository, settings SettingsProvider, workers int) *JobQueue {
	if workers <= 0 {
		workers = 1
	}
	return &JobQueue{
		repo:     repo,
		settings: settings,
		workers:  workers,
		handlers: make(map[domain.AICapability]AnalysisHandler),
		active:   make(map[int64]activeAIJob),
		paused:   make(map[domain.AICapability]int),
		wake:     make(chan struct{}, workers),
	}
}

func (q *JobQueue) SetSemanticDiagnostics(diagnostics *SemanticDiagnostics) {
	q.mu.Lock()
	q.semanticDiagnostics = diagnostics
	q.mu.Unlock()
}

func (q *JobQueue) SemanticDiagnosticsSnapshot() SemanticPipelineDiagnosticsSnapshot {
	q.mu.Lock()
	diagnostics := q.semanticDiagnostics
	activeWorkers := len(q.active)
	workers := q.workers
	q.mu.Unlock()

	snapshot := SemanticPipelineDiagnosticsSnapshot{CollectedAt: time.Now()}
	if diagnostics != nil {
		snapshot = diagnostics.Snapshot()
	}
	snapshot.ActiveWorkers = activeWorkers
	snapshot.WorkerCount = workers

	if repo, ok := q.repo.(analysisJobDiagnosticsRepository); ok {
		counts, err := repo.AIJobDiagnosticsCounts(
			domain.AICapabilitySemanticSearch,
			time.Now().Add(-5*time.Minute),
		)
		if err == nil {
			snapshot.QueueDepth = counts.Queued
			snapshot.RunningJobs = counts.Running
			snapshot.LongRunningJobs = counts.LongRunning
		}
	}
	return snapshot
}

func (q *JobQueue) Status() JobQueueStatus {
	q.mu.Lock()
	defer q.mu.Unlock()

	activeSet := make(map[string]struct{})
	for _, job := range q.active {
		activeSet[string(job.capability)] = struct{}{}
	}
	active := make([]string, 0, len(activeSet))
	for capability := range activeSet {
		active = append(active, capability)
	}
	paused := make([]string, 0, len(q.paused))
	for capability, count := range q.paused {
		if count > 0 {
			paused = append(paused, string(capability))
		}
	}
	sort.Strings(active)
	sort.Strings(paused)

	return JobQueueStatus{
		Started:            q.started,
		Workers:            q.workers,
		ActiveCount:        len(q.active),
		ActiveCapabilities: active,
		PausedCapabilities: paused,
	}
}

func (q *JobQueue) RegisterHandler(capability domain.AICapability, handler AnalysisHandler) error {
	if !domain.IsModelBackedAICapability(capability) {
		return fmt.Errorf("unsupported AI job capability %q", capability)
	}
	if handler == nil {
		return errors.New("AI analysis handler is required")
	}

	q.mu.Lock()
	q.handlers[capability] = handler
	q.mu.Unlock()
	q.signal()
	return nil
}

func (q *JobQueue) Start(parent context.Context) error {
	if q.repo == nil {
		return errors.New("AI job repository is required")
	}

	q.mu.Lock()
	if q.started {
		q.mu.Unlock()
		return nil
	}
	ctx, cancel := context.WithCancel(parent)
	q.started = true
	q.cancel = cancel
	q.mu.Unlock()

	recovered, err := q.repo.RecoverInterrupted()
	if err != nil {
		cancel()
		q.mu.Lock()
		q.started = false
		q.cancel = nil
		q.mu.Unlock()
		return err
	}
	if recovered > 0 {
		slog.Info("recovered interrupted AI jobs", "count", recovered)
	}

	for i := 0; i < q.workers; i++ {
		q.wg.Add(1)
		go q.worker(ctx, i)
	}
	q.signal()
	return nil
}

func (q *JobQueue) Stop(ctx context.Context) error {
	q.mu.Lock()
	if !q.started {
		q.mu.Unlock()
		return nil
	}
	cancel := q.cancel
	q.started = false
	q.cancel = nil
	q.mu.Unlock()

	cancel()

	done := make(chan struct{})
	go func() {
		q.wg.Wait()
		close(done)
	}()

	select {
	case <-done:
		return nil
	case <-ctx.Done():
		return ctx.Err()
	}
}

func (q *JobQueue) Enqueue(
	assetID int64,
	capability domain.AICapability,
	priority int,
	automatic bool,
) (domain.AIJob, bool, error) {
	if !domain.IsModelBackedAICapability(capability) {
		return domain.AIJob{}, false, fmt.Errorf("unsupported AI job capability %q", capability)
	}
	settings, err := q.currentSettings()
	if err != nil {
		return domain.AIJob{}, false, err
	}
	if !settings.CapabilityEnabled(capability) {
		return domain.AIJob{}, false, ErrCapabilityDisabled
	}

	source := domain.AIJobSourceManual
	if automatic {
		if !settings.CapabilityEnabled(domain.AICapabilityAutoAnalyze) {
			return domain.AIJob{}, false, ErrAutoAnalyzeDisabled
		}
		source = domain.AIJobSourceAutomatic
	}

	if priority > 1000 {
		priority = 1000
	}
	if priority < -1000 {
		priority = -1000
	}

	job, created, err := q.repo.Enqueue(assetID, capability, source, priority, 3)
	if err != nil {
		return domain.AIJob{}, false, err
	}
	q.signal()
	return job, created, nil
}

func (q *JobQueue) EnqueueMany(
	assetIDs []int64,
	capability domain.AICapability,
	priority int,
	automatic bool,
) (int, error) {
	if len(assetIDs) == 0 {
		return 0, nil
	}
	if !domain.IsModelBackedAICapability(capability) {
		return 0, fmt.Errorf("unsupported AI job capability %q", capability)
	}
	settings, err := q.currentSettings()
	if err != nil {
		return 0, err
	}
	if !settings.CapabilityEnabled(capability) {
		return 0, ErrCapabilityDisabled
	}

	source := domain.AIJobSourceManual
	if automatic {
		if !settings.CapabilityEnabled(domain.AICapabilityAutoAnalyze) {
			return 0, ErrAutoAnalyzeDisabled
		}
		source = domain.AIJobSourceAutomatic
	}
	if priority > 1000 {
		priority = 1000
	}
	if priority < -1000 {
		priority = -1000
	}

	created, err := q.repo.EnqueueBatch(assetIDs, capability, source, priority, 3)
	if err != nil {
		return 0, err
	}
	q.signal()
	return created, nil
}

func (q *JobQueue) Cancel(jobID int64) error {
	if err := q.repo.CancelJob(jobID); err != nil {
		return err
	}

	q.mu.Lock()
	active, ok := q.active[jobID]
	q.mu.Unlock()
	if ok {
		active.cancel()
	}
	return nil
}

func (q *JobQueue) Retry(jobID int64) error {
	if err := q.repo.RetryJob(jobID); err != nil {
		return err
	}
	q.signal()
	return nil
}

func (q *JobQueue) SetStartupHold(held bool) {
	q.mu.Lock()
	if q.startupHeld == held {
		q.mu.Unlock()
		return
	}
	q.startupHeld = held
	q.mu.Unlock()
	if !held {
		q.signal()
	}
}

func (q *JobQueue) InteractiveUIActive() bool {
	q.mu.Lock()
	defer q.mu.Unlock()
	return q.uiPaused
}

func (q *JobQueue) SetInteractiveUIActive(active bool) {
	q.mu.Lock()
	if q.uiPaused == active {
		q.mu.Unlock()
		return
	}
	q.uiPaused = active
	diagnostics := q.semanticDiagnostics
	q.mu.Unlock()
	if diagnostics != nil {
		diagnostics.SetViewerActive(active)
	}

	// Viewer activity is a throttle, not a stop-the-world barrier. Existing
	// inference is allowed to finish so expensive preprocessing/GPU work is not
	// repeatedly thrown away while the user scrolls. While active, only worker 0
	// is permitted to claim new jobs; the remaining workers park until idle.
	q.signal()
}

func (q *JobQueue) PauseCapabilityForForeground(capability domain.AICapability) func() {
	q.mu.Lock()
	q.paused[capability]++
	active := make(map[int64]activeAIJob)
	for id, job := range q.active {
		if job.capability == capability {
			active[id] = job
		}
	}
	q.mu.Unlock()

	// Preserve an active background job without consuming its retry budget.
	for id, job := range active {
		if err := q.repo.RequeueInterrupted(id, "yielded to a foreground AI request"); err != nil {
			slog.Debug("failed to requeue AI job for foreground request", "job", id, "error", err)
			continue
		}
		job.cancel()
	}

	var once sync.Once
	return func() {
		once.Do(func() {
			q.mu.Lock()
			if count := q.paused[capability]; count <= 1 {
				delete(q.paused, capability)
			} else {
				q.paused[capability] = count - 1
			}
			q.mu.Unlock()
			q.signal()
		})
	}
}

func (q *JobQueue) ApplySettings(settings domain.AISettings) error {
	q.mu.Lock()
	active := make(map[int64]activeAIJob)
	for id, job := range q.active {
		if !settings.CapabilityEnabled(job.capability) {
			active[id] = job
		}
	}
	q.mu.Unlock()

	var combined error
	for id, job := range active {
		if err := q.repo.RequeueInterrupted(id, "paused because AI capability was disabled"); err != nil {
			combined = errors.Join(combined, err)
			continue
		}
		job.cancel()
	}
	if combined == nil {
		q.signal()
	}
	return combined
}

func (q *JobQueue) MarkStaleForModel(
	capability domain.AICapability,
	engine string,
	modelID string,
	modelVersion string,
) (int64, error) {
	return q.repo.MarkStaleForModel(capability, engine, modelID, modelVersion)
}

func (q *JobQueue) MarkStaleForAssets(
	capability domain.AICapability,
	assetIDs []int64,
) (int64, error) {
	return q.repo.MarkStaleForAssets(capability, assetIDs)
}

func (q *JobQueue) HandleModelActivated(capability domain.AICapability, model InstalledModel) error {
	version := model.Manifest.Version
	if revision := strings.TrimSpace(model.Manifest.Parameters["analysis_revision"]); revision != "" {
		version += "+" + revision
	}
	_, err := q.MarkStaleForModel(
		capability,
		model.Manifest.Engine,
		model.Manifest.ID,
		version,
	)
	return err
}

func (q *JobQueue) ListNeedingAnalysis(
	libraryID int64,
	capability domain.AICapability,
	engine string,
	modelID string,
	modelVersion string,
	afterID int64,
	limit int,
) ([]int64, error) {
	return q.repo.ListNeedingAnalysis(libraryID, capability, engine, modelID, modelVersion, afterID, limit)
}

func (q *JobQueue) GetAnalysesByAsset(assetID int64) ([]domain.AIAnalysis, error) {
	return q.repo.GetByAsset(assetID)
}

func (q *JobQueue) ListJobs(limit int) ([]domain.AIJob, error) {
	return q.repo.ListJobs(limit)
}

func (q *JobQueue) worker(ctx context.Context, workerID int) {
	defer q.wg.Done()
	ticker := time.NewTicker(time.Second)
	defer ticker.Stop()

	for {
		if ctx.Err() != nil {
			return
		}

		job, err := q.claimNext(workerID)
		if err != nil {
			slog.Error("failed to claim AI job", "error", err)
			if !q.wait(ctx, ticker.C) {
				return
			}
			continue
		}
		if job == nil {
			if !q.wait(ctx, ticker.C) {
				return
			}
			continue
		}

		q.processJob(ctx, *job, workerID)
	}
}

func (q *JobQueue) claimNext(workerID int) (*domain.AIJob, error) {
	capabilities, err := q.runnableCapabilities(workerID)
	if err != nil {
		return nil, err
	}
	if len(capabilities) == 0 {
		return nil, nil
	}

	// SQLite has one writer. Serialising claims inside this process avoids
	// multiple workers selecting the same queued row before its state update.
	q.claimMu.Lock()
	defer q.claimMu.Unlock()
	return q.repo.ClaimNext(capabilities)
}

func (q *JobQueue) runnableCapabilities(workerID int) ([]domain.AICapability, error) {
	settings, err := q.currentSettings()
	if err != nil {
		return nil, err
	}

	q.mu.Lock()
	defer q.mu.Unlock()
	if q.startupHeld {
		return nil, nil
	}
	// Keep one worker alive during interactive viewer activity. This preserves
	// forward progress for long-running library analysis without letting the
	// full worker pool contend with thumbnail decode/composition.
	if q.uiPaused && workerID > 0 {
		return nil, nil
	}
	capabilities := make([]domain.AICapability, 0, len(q.handlers))
	for capability := range q.handlers {
		if settings.CapabilityEnabled(capability) && q.paused[capability] == 0 {
			capabilities = append(capabilities, capability)
		}
	}
	return capabilities, nil
}

func (q *JobQueue) processJob(parent context.Context, job domain.AIJob, workerID int) {
	q.mu.Lock()
	handler := q.handlers[job.Capability]
	diagnostics := q.semanticDiagnostics
	q.mu.Unlock()
	if handler == nil {
		_, _ = q.repo.FailOrRequeue(job.ID, "no handler registered for AI capability")
		return
	}

	jobCtx, cancel := context.WithCancel(parent)
	q.mu.Lock()
	q.active[job.ID] = activeAIJob{capability: job.Capability, cancel: cancel}
	paused := q.startupHeld || q.paused[job.Capability] > 0
	q.mu.Unlock()

	cleanup := func() {
		cancel()
		q.mu.Lock()
		delete(q.active, job.ID)
		q.mu.Unlock()
	}

	if paused {
		if err := q.repo.RequeueInterrupted(job.ID, "yielded to foreground viewer or AI work"); err != nil {
			slog.Error("failed to yield claimed AI job to foreground request", "job", job.ID, "error", err)
		}
		cleanup()
		return
	}

	latest, err := q.repo.GetJob(job.ID)
	if err != nil {
		cleanup()
		slog.Error("failed to re-read claimed AI job", "job", job.ID, "error", err)
		return
	}
	if latest == nil || latest.Status == domain.AIJobCancelled {
		cleanup()
		return
	}

	trace := (*SemanticJobTrace)(nil)
	if diagnostics != nil {
		trace = diagnostics.StartJob(*latest, workerID)
		jobCtx = WithSemanticJobTrace(jobCtx, trace)
	}

	output, handlerErr := handler(jobCtx, job)
	cleanup()

	if handlerErr == nil {
		stopCompletion := MeasureSemanticStage(jobCtx, SemanticStageCompletionPersistence)
		completeErr := q.completeOutputWithRetry(parent, job, output, diagnostics)
		stopCompletion()
		if completeErr != nil {
			if trace != nil {
				trace.RecordError(SemanticStageCompletionPersistence, completeErr)
			}
			if diagnostics != nil {
				diagnostics.RecordCompletionFailure(completeErr)
				diagnostics.FinishJob(trace, "completion_failed", completeErr)
			}
			slog.Error("failed to complete AI job", "job", job.ID, "error", completeErr)
			return
		}
		output.runAfterCommit()
		if diagnostics != nil {
			diagnostics.FinishJob(trace, "success", nil)
		}
		return
	}

	current, readErr := q.repo.GetJob(job.ID)
	if readErr != nil {
		if trace != nil {
			trace.RecordError("failure_state_read", readErr)
		}
		if diagnostics != nil {
			diagnostics.FinishJob(trace, "failure_state_read", readErr)
		}
		slog.Error("failed to read failed AI job", "job", job.ID, "error", readErr)
		return
	}
	if current == nil || current.Status == domain.AIJobCancelled {
		if diagnostics != nil {
			diagnostics.FinishJob(trace, "cancelled", handlerErr)
		}
		return
	}
	if current.Status == domain.AIJobQueued && errors.Is(handlerErr, context.Canceled) {
		// Foreground preemption requeues the durable job before cancelling the
		// active handler. Classify that as cancellation/yield rather than a
		// failure so diagnostics can quantify discarded decode/ORT work.
		if diagnostics != nil {
			diagnostics.FinishJob(trace, "cancelled", handlerErr)
		}
		return
	}

	if parent.Err() != nil {
		requeueErr := q.repo.RequeueInterrupted(job.ID, "interrupted by app shutdown")
		if requeueErr != nil {
			if trace != nil {
				trace.RecordError("shutdown_requeue", requeueErr)
			}
			slog.Error("failed to preserve interrupted AI job", "job", job.ID, "error", requeueErr)
		}
		if diagnostics != nil {
			diagnostics.FinishJob(trace, "interrupted", errors.Join(handlerErr, requeueErr))
		}
		return
	}

	requeued, err := q.repo.FailOrRequeue(job.ID, handlerErr.Error())
	if err != nil {
		if trace != nil {
			trace.RecordError("failure_persistence", err)
		}
		if diagnostics != nil {
			diagnostics.FinishJob(trace, "failure_persistence", errors.Join(handlerErr, err))
		}
		slog.Error("failed to record AI job failure", "job", job.ID, "error", err)
		return
	}
	if requeued {
		if diagnostics != nil {
			diagnostics.RecordRetry()
			diagnostics.FinishJob(trace, "retry", handlerErr)
		}
		q.signal()
		return
	}
	if diagnostics != nil {
		diagnostics.FinishJob(trace, "failed", handlerErr)
	}
}

func (q *JobQueue) completeOutputWithRetry(
	ctx context.Context,
	job domain.AIJob,
	output AnalysisOutput,
	diagnostics *SemanticDiagnostics,
) error {
	if ctx == nil {
		ctx = context.Background()
	}
	delay := 5 * time.Millisecond
	const maxDelay = 250 * time.Millisecond

	for {
		var err error
		if output.semanticResultStaged && job.Capability == domain.AICapabilitySemanticSearch {
			err = q.repo.CompleteSemanticJob(
				job.ID,
				output.Engine,
				output.ModelID,
				output.ModelVersion,
				output.ResultJSON,
			)
		} else {
			err = q.repo.CompleteJob(
				job.ID,
				output.Engine,
				output.ModelID,
				output.ModelVersion,
				output.ResultJSON,
			)
		}
		if err == nil {
			return nil
		}
		code := sqliteErrorCode(err)
		if code&0xff != 5 {
			return err
		}
		if diagnostics != nil {
			diagnostics.RecordCompletionFailure(err)
		}

		timer := time.NewTimer(delay)
		select {
		case <-ctx.Done():
			if !timer.Stop() {
				<-timer.C
			}
			return fmt.Errorf("AI result finalization interrupted: %w", ctx.Err())
		case <-timer.C:
		}
		if delay < maxDelay {
			delay *= 2
			if delay > maxDelay {
				delay = maxDelay
			}
		}
	}
}

func (q *JobQueue) wait(ctx context.Context, tick <-chan time.Time) bool {
	select {
	case <-ctx.Done():
		return false
	case <-q.wake:
		return true
	case <-tick:
		return true
	}
}

func (q *JobQueue) signal() {
	// Wake the whole pool. A single-token wake channel was appropriate for the
	// original one-worker queue but makes additional workers sleep until their
	// one-second polling tick, defeating burst concurrency.
	for i := 0; i < q.workers; i++ {
		select {
		case q.wake <- struct{}{}:
		default:
			return
		}
	}
}

func (q *JobQueue) currentSettings() (domain.AISettings, error) {
	if q.settings == nil {
		return domain.DefaultAISettings(), nil
	}
	return q.settings()
}
