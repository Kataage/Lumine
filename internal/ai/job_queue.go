package ai

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
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
}

type AnalysisHandler func(context.Context, domain.AIJob) (AnalysisOutput, error)

type AnalysisJobRepository interface {
	Enqueue(assetID int64, capability domain.AICapability, source domain.AIJobSource, priority int, maxAttempts int) (domain.AIJob, bool, error)
	EnqueueBatch(assetIDs []int64, capability domain.AICapability, source domain.AIJobSource, priority int, maxAttempts int) (int, error)
	ClaimNext(capabilities []domain.AICapability) (*domain.AIJob, error)
	CompleteJob(jobID int64, engine, modelID, modelVersion, resultJSON string) error
	FailOrRequeue(jobID int64, message string) (bool, error)
	CancelJob(jobID int64) error
	RetryJob(jobID int64) error
	RequeueInterrupted(jobID int64, message string) error
	RecoverInterrupted() (int64, error)
	MarkStaleForModel(capability domain.AICapability, engine, modelID, modelVersion string) (int64, error)
	GetByAsset(assetID int64) ([]domain.AIAnalysis, error)
	GetJob(id int64) (*domain.AIJob, error)
	ListJobs(limit int) ([]domain.AIJob, error)
}

type activeAIJob struct {
	capability domain.AICapability
	cancel     context.CancelFunc
}

type JobQueue struct {
	repo     AnalysisJobRepository
	settings SettingsProvider
	workers  int

	mu       sync.Mutex
	handlers map[domain.AICapability]AnalysisHandler
	active   map[int64]activeAIJob
	started  bool
	cancel   context.CancelFunc
	wake     chan struct{}
	wg       sync.WaitGroup

	claimMu sync.Mutex
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
		wake:     make(chan struct{}, 1),
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
		go q.worker(ctx)
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

func (q *JobQueue) HandleModelActivated(capability domain.AICapability, model InstalledModel) error {
	_, err := q.MarkStaleForModel(
		capability,
		model.Manifest.Engine,
		model.Manifest.ID,
		model.Manifest.Version,
	)
	return err
}

func (q *JobQueue) GetAnalysesByAsset(assetID int64) ([]domain.AIAnalysis, error) {
	return q.repo.GetByAsset(assetID)
}

func (q *JobQueue) ListJobs(limit int) ([]domain.AIJob, error) {
	return q.repo.ListJobs(limit)
}

func (q *JobQueue) worker(ctx context.Context) {
	defer q.wg.Done()
	ticker := time.NewTicker(time.Second)
	defer ticker.Stop()

	for {
		if ctx.Err() != nil {
			return
		}

		job, err := q.claimNext()
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

		q.processJob(ctx, *job)
	}
}

func (q *JobQueue) claimNext() (*domain.AIJob, error) {
	capabilities, err := q.runnableCapabilities()
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

func (q *JobQueue) runnableCapabilities() ([]domain.AICapability, error) {
	settings, err := q.currentSettings()
	if err != nil {
		return nil, err
	}

	q.mu.Lock()
	defer q.mu.Unlock()
	capabilities := make([]domain.AICapability, 0, len(q.handlers))
	for capability := range q.handlers {
		if settings.CapabilityEnabled(capability) {
			capabilities = append(capabilities, capability)
		}
	}
	return capabilities, nil
}

func (q *JobQueue) processJob(parent context.Context, job domain.AIJob) {
	q.mu.Lock()
	handler := q.handlers[job.Capability]
	q.mu.Unlock()
	if handler == nil {
		_, _ = q.repo.FailOrRequeue(job.ID, "no handler registered for AI capability")
		return
	}

	jobCtx, cancel := context.WithCancel(parent)
	q.mu.Lock()
	q.active[job.ID] = activeAIJob{capability: job.Capability, cancel: cancel}
	q.mu.Unlock()

	cleanup := func() {
		cancel()
		q.mu.Lock()
		delete(q.active, job.ID)
		q.mu.Unlock()
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

	output, handlerErr := handler(jobCtx, job)
	cleanup()

	if handlerErr == nil {
		if err := q.repo.CompleteJob(job.ID, output.Engine, output.ModelID, output.ModelVersion, output.ResultJSON); err != nil {
			slog.Error("failed to complete AI job", "job", job.ID, "error", err)
		}
		return
	}

	current, readErr := q.repo.GetJob(job.ID)
	if readErr != nil {
		slog.Error("failed to read failed AI job", "job", job.ID, "error", readErr)
		return
	}
	if current == nil || current.Status == domain.AIJobCancelled {
		return
	}

	if parent.Err() != nil {
		if err := q.repo.RequeueInterrupted(job.ID, "interrupted by app shutdown"); err != nil {
			slog.Error("failed to preserve interrupted AI job", "job", job.ID, "error", err)
		}
		return
	}

	requeued, err := q.repo.FailOrRequeue(job.ID, handlerErr.Error())
	if err != nil {
		slog.Error("failed to record AI job failure", "job", job.ID, "error", err)
		return
	}
	if requeued {
		q.signal()
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
	select {
	case q.wake <- struct{}{}:
	default:
	}
}

func (q *JobQueue) currentSettings() (domain.AISettings, error) {
	if q.settings == nil {
		return domain.DefaultAISettings(), nil
	}
	return q.settings()
}
