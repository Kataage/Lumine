package ai

import (
	"context"
	"errors"
	"fmt"
	"strconv"
	"sync"
	"sync/atomic"
	"time"

	"github.com/kataage/lumine/internal/domain"
)

const (
	SemanticStageFileOpen              = "file_open"
	SemanticStageFileRead              = "file_read"
	SemanticStageDecode                = "decode"
	SemanticStagePreprocess            = "preprocess"
	SemanticStageRuntimeLockWait       = "runtime_lock_wait"
	SemanticStageORTRunLockWait        = "ort_run_lock_wait"
	SemanticStageTensorSetup           = "tensor_setup"
	SemanticStageORTRun                = "ort_run"
	SemanticStageEmbeddingPersistence  = "embedding_persistence"
	SemanticStageCompletionPersistence = "completion_persistence"
	SemanticStageIndexLockWait         = "index_lock_wait"
	SemanticStageIndexUpdate           = "index_update"

	semanticDiagnosticsRecentLimit = 64
)

// SemanticStageTimings contains the per-stage wall-clock time recorded for one
// Semantic image attempt. Values are milliseconds so the Wails/JSON surface is
// easy to inspect without leaking time.Duration's nanosecond representation.
type SemanticStageTimings struct {
	FileOpenMs              float64 `json:"fileOpenMs"`
	FileReadMs              float64 `json:"fileReadMs"`
	DecodeMs                float64 `json:"decodeMs"`
	PreprocessMs            float64 `json:"preprocessMs"`
	RuntimeLockWaitMs       float64 `json:"runtimeLockWaitMs"`
	ORTRunLockWaitMs        float64 `json:"ortRunLockWaitMs"`
	TensorSetupMs           float64 `json:"tensorSetupMs"`
	ORTRunMs                float64 `json:"ortRunMs"`
	EmbeddingPersistenceMs  float64 `json:"embeddingPersistenceMs"`
	CompletionPersistenceMs float64 `json:"completionPersistenceMs"`
	IndexLockWaitMs         float64 `json:"indexLockWaitMs"`
	IndexUpdateMs           float64 `json:"indexUpdateMs"`
}

type SemanticJobTraceSnapshot struct {
	JobID            int64                `json:"jobId"`
	AssetID          int64                `json:"assetId"`
	Attempt          int                  `json:"attempt"`
	WorkerID         int                  `json:"workerId"`
	StartedAt        time.Time            `json:"startedAt"`
	FinishedAt       time.Time            `json:"finishedAt"`
	QueueWaitMs      float64              `json:"queueWaitMs"`
	ClaimToReadyMs   float64              `json:"claimToReadyMs"`
	EnqueueToReadyMs float64              `json:"enqueueToReadyMs"`
	Stages           SemanticStageTimings `json:"stages"`
	Outcome          string               `json:"outcome"`
	ErrorStage       string               `json:"errorStage,omitempty"`
	Error            string               `json:"error,omitempty"`
	SQLiteCode       int                  `json:"sqliteCode,omitempty"`
}

type SemanticPipelineDiagnosticsSnapshot struct {
	Enabled                 bool                       `json:"enabled"`
	CollectedAt             time.Time                  `json:"collectedAt"`
	ResetAt                 time.Time                  `json:"resetAt"`
	ReadyLastMinute         int                        `json:"readyLastMinute"`
	OrtRunsLastMinute       int                        `json:"ortRunsLastMinute"`
	RetryCount              uint64                     `json:"retryCount"`
	ReInferenceCount        uint64                     `json:"reInferenceCount"`
	DiscardedAfterCancel    uint64                     `json:"discardedAfterCancel"`
	FailedCompletionCount   uint64                     `json:"failedCompletionCount"`
	SQLiteBusyCount         uint64                     `json:"sqliteBusyCount"`
	SQLiteBusySnapshotCount uint64                     `json:"sqliteBusySnapshotCount"`
	SQLiteErrorCodes        map[string]uint64          `json:"sqliteErrorCodes"`
	SnapshotAttempts        uint64                     `json:"snapshotAttempts"`
	SnapshotSuccesses       uint64                     `json:"snapshotSuccesses"`
	SnapshotDiscarded       uint64                     `json:"snapshotDiscarded"`
	SnapshotFailures        uint64                     `json:"snapshotFailures"`
	SnapshotBytes           uint64                     `json:"snapshotBytes"`
	SnapshotDurationMs      float64                    `json:"snapshotDurationMs"`
	ViewerActive            bool                       `json:"viewerActive"`
	ViewerActiveForMs       float64                    `json:"viewerActiveForMs"`
	ViewerActiveTotalMs     float64                    `json:"viewerActiveTotalMs"`
	QueueDepth              int64                      `json:"queueDepth"`
	RunningJobs             int64                      `json:"runningJobs"`
	LongRunningJobs         int64                      `json:"longRunningJobs"`
	ActiveWorkers           int                        `json:"activeWorkers"`
	WorkerCount             int                        `json:"workerCount"`
	ExecutionProvider       string                     `json:"executionProvider,omitempty"`
	AdapterID               *int                       `json:"adapterId,omitempty"`
	RecentJobs              []SemanticJobTraceSnapshot `json:"recentJobs"`
}

type semanticTraceContextKey struct{}

type SemanticJobTrace struct {
	mu         sync.Mutex
	collector  *SemanticDiagnostics
	job        domain.AIJob
	workerID   int
	startedAt  time.Time
	queueWait  time.Duration
	stages     map[string]time.Duration
	ortRan     bool
	errorStage string
	errorText  string
	sqliteCode int
}

type SemanticDiagnostics struct {
	enabled atomic.Bool

	mu      sync.Mutex
	resetAt time.Time

	readyTimes              []time.Time
	ortRunTimes             []time.Time
	retryCount              uint64
	reInferenceCount        uint64
	discardedAfterCancel    uint64
	failedCompletionCount   uint64
	sqliteBusyCount         uint64
	sqliteBusySnapshotCount uint64
	sqliteErrorCodes        map[int]uint64

	snapshotAttempts  uint64
	snapshotSuccesses uint64
	snapshotDiscarded uint64
	snapshotFailures  uint64
	snapshotBytes     uint64
	snapshotDuration  time.Duration

	viewerActive      bool
	viewerActiveSince time.Time
	viewerActiveTotal time.Duration

	recent []SemanticJobTraceSnapshot
}

func NewSemanticDiagnostics() *SemanticDiagnostics {
	return &SemanticDiagnostics{
		resetAt:          time.Now(),
		sqliteErrorCodes: make(map[int]uint64),
		recent:           make([]SemanticJobTraceSnapshot, 0, semanticDiagnosticsRecentLimit),
	}
}

func (d *SemanticDiagnostics) SetEnabled(enabled bool) {
	if d == nil {
		return
	}
	d.enabled.Store(enabled)
}

func (d *SemanticDiagnostics) Enabled() bool {
	return d != nil && d.enabled.Load()
}

func (d *SemanticDiagnostics) Reset() {
	if d == nil {
		return
	}
	now := time.Now()
	d.mu.Lock()
	d.resetAt = now
	d.readyTimes = nil
	d.ortRunTimes = nil
	d.retryCount = 0
	d.reInferenceCount = 0
	d.discardedAfterCancel = 0
	d.failedCompletionCount = 0
	d.sqliteBusyCount = 0
	d.sqliteBusySnapshotCount = 0
	d.sqliteErrorCodes = make(map[int]uint64)
	d.snapshotAttempts = 0
	d.snapshotSuccesses = 0
	d.snapshotDiscarded = 0
	d.snapshotFailures = 0
	d.snapshotBytes = 0
	d.snapshotDuration = 0
	d.viewerActiveTotal = 0
	if d.viewerActive {
		d.viewerActiveSince = now
	} else {
		d.viewerActiveSince = time.Time{}
	}
	d.recent = d.recent[:0]
	d.mu.Unlock()
}

func (d *SemanticDiagnostics) StartJob(job domain.AIJob, workerID int) *SemanticJobTrace {
	if !d.Enabled() || job.Capability != domain.AICapabilitySemanticSearch {
		return nil
	}
	startedAt := time.Now()
	queueWait := startedAt.Sub(job.CreatedAt)
	if queueWait < 0 {
		queueWait = 0
	}
	d.mu.Lock()
	if job.AttemptCount > 1 {
		d.reInferenceCount++
	}
	d.mu.Unlock()
	return &SemanticJobTrace{
		collector: d,
		job:       job,
		workerID:  workerID,
		startedAt: startedAt,
		queueWait: queueWait,
		stages:    make(map[string]time.Duration),
	}
}

func WithSemanticJobTrace(ctx context.Context, trace *SemanticJobTrace) context.Context {
	if ctx == nil {
		ctx = context.Background()
	}
	if trace == nil {
		return ctx
	}
	return context.WithValue(ctx, semanticTraceContextKey{}, trace)
}

func SemanticJobTraceFromContext(ctx context.Context) *SemanticJobTrace {
	if ctx == nil {
		return nil
	}
	trace, _ := ctx.Value(semanticTraceContextKey{}).(*SemanticJobTrace)
	return trace
}

func MeasureSemanticStage(ctx context.Context, stage string) func() {
	trace := SemanticJobTraceFromContext(ctx)
	if trace == nil {
		return func() {}
	}
	started := time.Now()
	return func() {
		trace.AddStage(stage, time.Since(started))
	}
}

func (t *SemanticJobTrace) AddStage(stage string, elapsed time.Duration) {
	if t == nil || elapsed < 0 {
		return
	}
	t.mu.Lock()
	t.stages[stage] += elapsed
	t.mu.Unlock()
}

func (t *SemanticJobTrace) MarkOrtRun() {
	if t == nil || t.collector == nil {
		return
	}
	t.mu.Lock()
	t.ortRan = true
	t.mu.Unlock()
	t.collector.recordOrtRun(time.Now())
}

func (t *SemanticJobTrace) RecordError(stage string, err error) {
	if t == nil || err == nil {
		return
	}
	code := sqliteErrorCode(err)
	t.mu.Lock()
	if t.errorStage == "" {
		t.errorStage = stage
		t.errorText = err.Error()
		t.sqliteCode = code
	}
	t.mu.Unlock()
}

func RecordSemanticTraceError(ctx context.Context, stage string, err error) {
	if trace := SemanticJobTraceFromContext(ctx); trace != nil {
		trace.RecordError(stage, err)
		if trace.collector != nil {
			trace.collector.RecordSQLiteError(err)
		}
	}
}

func (d *SemanticDiagnostics) RecordRetry() {
	if !d.Enabled() {
		return
	}
	d.mu.Lock()
	d.retryCount++
	d.mu.Unlock()
}

func (d *SemanticDiagnostics) RecordCompletionFailure(err error) {
	if !d.Enabled() {
		return
	}
	d.mu.Lock()
	d.failedCompletionCount++
	d.mu.Unlock()
	if code := sqliteErrorCode(err); code != 0 {
		d.recordSQLiteCode(code)
	}
}

func (d *SemanticDiagnostics) RecordSQLiteError(err error) {
	if !d.Enabled() {
		return
	}
	if code := sqliteErrorCode(err); code != 0 {
		d.recordSQLiteCode(code)
	}
}

func (d *SemanticDiagnostics) FinishJob(trace *SemanticJobTrace, outcome string, err error) {
	if d == nil || trace == nil {
		return
	}
	finishedAt := time.Now()
	if err != nil {
		trace.RecordError(outcome, err)
	}

	trace.mu.Lock()
	ortRan := trace.ortRan
	snapshot := SemanticJobTraceSnapshot{
		JobID:            trace.job.ID,
		AssetID:          trace.job.AssetID,
		Attempt:          trace.job.AttemptCount,
		WorkerID:         trace.workerID,
		StartedAt:        trace.startedAt,
		FinishedAt:       finishedAt,
		QueueWaitMs:      durationMillis(trace.queueWait),
		ClaimToReadyMs:   durationMillis(finishedAt.Sub(trace.startedAt)),
		EnqueueToReadyMs: durationMillis(finishedAt.Sub(trace.job.CreatedAt)),
		Stages:           stagesToSnapshot(trace.stages),
		Outcome:          outcome,
		ErrorStage:       trace.errorStage,
		Error:            trace.errorText,
		SQLiteCode:       trace.sqliteCode,
	}
	trace.mu.Unlock()

	d.mu.Lock()
	if outcome == "success" {
		d.readyTimes = append(d.readyTimes, finishedAt)
	}
	if outcome == "cancelled" && ortRan {
		d.discardedAfterCancel++
	}
	d.recent = append(d.recent, snapshot)
	if len(d.recent) > semanticDiagnosticsRecentLimit {
		d.recent = append([]SemanticJobTraceSnapshot(nil), d.recent[len(d.recent)-semanticDiagnosticsRecentLimit:]...)
	}
	d.trimMinuteLocked(finishedAt)
	d.mu.Unlock()
}

func (d *SemanticDiagnostics) SetViewerActive(active bool) {
	if d == nil {
		return
	}
	now := time.Now()
	d.mu.Lock()
	if d.viewerActive == active {
		d.mu.Unlock()
		return
	}
	if d.viewerActive && !d.viewerActiveSince.IsZero() {
		d.viewerActiveTotal += now.Sub(d.viewerActiveSince)
	}
	d.viewerActive = active
	if active {
		d.viewerActiveSince = now
	} else {
		d.viewerActiveSince = time.Time{}
	}
	d.mu.Unlock()
}

func (d *SemanticDiagnostics) BeginSnapshot() func(outcome string, bytes int64) {
	if !d.Enabled() {
		return func(string, int64) {}
	}
	started := time.Now()
	d.mu.Lock()
	d.snapshotAttempts++
	d.mu.Unlock()
	return func(outcome string, bytes int64) {
		elapsed := time.Since(started)
		d.mu.Lock()
		d.snapshotDuration += elapsed
		if bytes > 0 {
			d.snapshotBytes += uint64(bytes)
		}
		switch outcome {
		case "success":
			d.snapshotSuccesses++
		case "discarded", "generation_changed", "yielded":
			d.snapshotDiscarded++
		default:
			d.snapshotFailures++
		}
		d.mu.Unlock()
	}
}

func (d *SemanticDiagnostics) Snapshot() SemanticPipelineDiagnosticsSnapshot {
	now := time.Now()
	if d == nil {
		return SemanticPipelineDiagnosticsSnapshot{CollectedAt: now}
	}
	d.mu.Lock()
	d.trimMinuteLocked(now)
	viewerActiveFor := time.Duration(0)
	viewerTotal := d.viewerActiveTotal
	if d.viewerActive && !d.viewerActiveSince.IsZero() {
		viewerActiveFor = now.Sub(d.viewerActiveSince)
		viewerTotal += viewerActiveFor
	}
	codes := make(map[string]uint64, len(d.sqliteErrorCodes))
	for code, count := range d.sqliteErrorCodes {
		codes[strconv.Itoa(code)] = count
	}
	recent := append([]SemanticJobTraceSnapshot(nil), d.recent...)
	snapshot := SemanticPipelineDiagnosticsSnapshot{
		Enabled:                 d.Enabled(),
		CollectedAt:             now,
		ResetAt:                 d.resetAt,
		ReadyLastMinute:         len(d.readyTimes),
		OrtRunsLastMinute:       len(d.ortRunTimes),
		RetryCount:              d.retryCount,
		ReInferenceCount:        d.reInferenceCount,
		DiscardedAfterCancel:    d.discardedAfterCancel,
		FailedCompletionCount:   d.failedCompletionCount,
		SQLiteBusyCount:         d.sqliteBusyCount,
		SQLiteBusySnapshotCount: d.sqliteBusySnapshotCount,
		SQLiteErrorCodes:        codes,
		SnapshotAttempts:        d.snapshotAttempts,
		SnapshotSuccesses:       d.snapshotSuccesses,
		SnapshotDiscarded:       d.snapshotDiscarded,
		SnapshotFailures:        d.snapshotFailures,
		SnapshotBytes:           d.snapshotBytes,
		SnapshotDurationMs:      durationMillis(d.snapshotDuration),
		ViewerActive:            d.viewerActive,
		ViewerActiveForMs:       durationMillis(viewerActiveFor),
		ViewerActiveTotalMs:     durationMillis(viewerTotal),
		RecentJobs:              recent,
	}
	d.mu.Unlock()
	return snapshot
}

func (d *SemanticDiagnostics) recordOrtRun(at time.Time) {
	if !d.Enabled() {
		return
	}
	d.mu.Lock()
	d.ortRunTimes = append(d.ortRunTimes, at)
	d.trimMinuteLocked(at)
	d.mu.Unlock()
}

func (d *SemanticDiagnostics) recordSQLiteCode(code int) {
	if !d.Enabled() || code == 0 {
		return
	}
	d.mu.Lock()
	d.sqliteErrorCodes[code]++
	if code&0xff == 5 {
		d.sqliteBusyCount++
	}
	if code == 517 {
		d.sqliteBusySnapshotCount++
	}
	d.mu.Unlock()
}

func (d *SemanticDiagnostics) trimMinuteLocked(now time.Time) {
	cutoff := now.Add(-time.Minute)
	trimTimes := func(values []time.Time) []time.Time {
		index := 0
		for index < len(values) && values[index].Before(cutoff) {
			index++
		}
		if index == 0 {
			return values
		}
		return append(values[:0], values[index:]...)
	}
	d.readyTimes = trimTimes(d.readyTimes)
	d.ortRunTimes = trimTimes(d.ortRunTimes)
}

func sqliteErrorCode(err error) int {
	if err == nil {
		return 0
	}
	var coder interface{ Code() int }
	if errors.As(err, &coder) {
		return coder.Code()
	}
	return 0
}

func stagesToSnapshot(stages map[string]time.Duration) SemanticStageTimings {
	return SemanticStageTimings{
		FileOpenMs:              durationMillis(stages[SemanticStageFileOpen]),
		FileReadMs:              durationMillis(stages[SemanticStageFileRead]),
		DecodeMs:                durationMillis(stages[SemanticStageDecode]),
		PreprocessMs:            durationMillis(stages[SemanticStagePreprocess]),
		RuntimeLockWaitMs:       durationMillis(stages[SemanticStageRuntimeLockWait]),
		ORTRunLockWaitMs:        durationMillis(stages[SemanticStageORTRunLockWait]),
		TensorSetupMs:           durationMillis(stages[SemanticStageTensorSetup]),
		ORTRunMs:                durationMillis(stages[SemanticStageORTRun]),
		EmbeddingPersistenceMs:  durationMillis(stages[SemanticStageEmbeddingPersistence]),
		CompletionPersistenceMs: durationMillis(stages[SemanticStageCompletionPersistence]),
		IndexLockWaitMs:         durationMillis(stages[SemanticStageIndexLockWait]),
		IndexUpdateMs:           durationMillis(stages[SemanticStageIndexUpdate]),
	}
}

func durationMillis(value time.Duration) float64 {
	return float64(value) / float64(time.Millisecond)
}

func (s SemanticPipelineDiagnosticsSnapshot) String() string {
	return fmt.Sprintf(
		"ready/min=%d ort/min=%d retries=%d completion_failures=%d sqlite_busy=%d",
		s.ReadyLastMinute,
		s.OrtRunsLastMinute,
		s.RetryCount,
		s.FailedCompletionCount,
		s.SQLiteBusyCount,
	)
}
