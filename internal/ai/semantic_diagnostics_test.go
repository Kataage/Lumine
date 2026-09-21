package ai

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/kataage/lumine/internal/domain"
)

type diagnosticSQLiteError struct {
	code int
}

func (e diagnosticSQLiteError) Error() string { return "sqlite diagnostic error" }
func (e diagnosticSQLiteError) Code() int     { return e.code }

func TestSemanticDiagnosticsAreOptIn(t *testing.T) {
	diagnostics := NewSemanticDiagnostics()
	job := domain.AIJob{
		ID:         1,
		AssetID:    2,
		Capability: domain.AICapabilitySemanticSearch,
		CreatedAt:  time.Now(),
	}
	if trace := diagnostics.StartJob(job, 0); trace != nil {
		t.Fatal("disabled diagnostics must not allocate a trace")
	}

	diagnostics.SetEnabled(true)
	if trace := diagnostics.StartJob(job, 0); trace == nil {
		t.Fatal("enabled diagnostics should create a trace")
	}
}

func TestSemanticDiagnosticsRecordAttemptStagesAndThroughput(t *testing.T) {
	diagnostics := NewSemanticDiagnostics()
	diagnostics.SetEnabled(true)
	job := domain.AIJob{
		ID:           41,
		AssetID:      99,
		Capability:   domain.AICapabilitySemanticSearch,
		AttemptCount: 2,
		CreatedAt:    time.Now().Add(-2 * time.Second),
	}
	trace := diagnostics.StartJob(job, 3)
	if trace == nil {
		t.Fatal("expected trace")
	}
	ctx := WithSemanticJobTrace(context.Background(), trace)
	SemanticJobTraceFromContext(ctx).AddStage(SemanticStageFileOpen, 2*time.Millisecond)
	SemanticJobTraceFromContext(ctx).AddStage(SemanticStageDecode, 3*time.Millisecond)
	SemanticJobTraceFromContext(ctx).AddStage(SemanticStageRuntimeLockWait, 4*time.Millisecond)
	SemanticJobTraceFromContext(ctx).AddStage(SemanticStageORTRun, 5*time.Millisecond)
	SemanticJobTraceFromContext(ctx).MarkOrtRun()
	diagnostics.RecordRetry()
	diagnostics.FinishJob(trace, "success", nil)

	snapshot := diagnostics.Snapshot()
	if !snapshot.Enabled {
		t.Fatal("diagnostics should be enabled")
	}
	if snapshot.ReadyLastMinute != 1 || snapshot.OrtRunsLastMinute != 1 {
		t.Fatalf("unexpected throughput counters: %+v", snapshot)
	}
	if snapshot.RetryCount != 1 || snapshot.ReInferenceCount != 1 {
		t.Fatalf("unexpected retry counters: %+v", snapshot)
	}
	if len(snapshot.RecentJobs) != 1 {
		t.Fatalf("recent jobs = %d, want 1", len(snapshot.RecentJobs))
	}
	recent := snapshot.RecentJobs[0]
	if recent.JobID != 41 || recent.AssetID != 99 || recent.WorkerID != 3 || recent.Attempt != 2 {
		t.Fatalf("unexpected trace identity: %+v", recent)
	}
	if recent.Outcome != "success" || recent.QueueWaitMs < 1000 {
		t.Fatalf("unexpected trace outcome/timing: %+v", recent)
	}
	if recent.Stages.FileOpenMs != 2 || recent.Stages.DecodeMs != 3 ||
		recent.Stages.RuntimeLockWaitMs != 4 || recent.Stages.ORTRunMs != 5 {
		t.Fatalf("unexpected stage timings: %+v", recent.Stages)
	}
}

func TestSemanticDiagnosticsRecordSQLiteAndSnapshotFailures(t *testing.T) {
	diagnostics := NewSemanticDiagnostics()
	diagnostics.SetEnabled(true)

	err := diagnosticSQLiteError{code: 517}
	diagnostics.RecordCompletionFailure(err)

	job := domain.AIJob{
		ID:         7,
		AssetID:    8,
		Capability: domain.AICapabilitySemanticSearch,
		CreatedAt:  time.Now(),
	}
	trace := diagnostics.StartJob(job, 0)
	trace.RecordError(SemanticStageCompletionPersistence, err)
	diagnostics.FinishJob(trace, "completion_failed", err)

	finishSnapshot := diagnostics.BeginSnapshot()
	finishSnapshot("generation_changed", 1024)

	snapshot := diagnostics.Snapshot()
	if snapshot.FailedCompletionCount != 1 {
		t.Fatalf("failed completion count = %d, want 1", snapshot.FailedCompletionCount)
	}
	if snapshot.SQLiteBusyCount == 0 || snapshot.SQLiteBusySnapshotCount == 0 {
		t.Fatalf("expected SQLITE_BUSY_SNAPSHOT counters: %+v", snapshot)
	}
	if snapshot.SQLiteErrorCodes["517"] == 0 {
		t.Fatalf("expected sqlite code 517: %+v", snapshot.SQLiteErrorCodes)
	}
	if snapshot.SnapshotAttempts != 1 || snapshot.SnapshotDiscarded != 1 || snapshot.SnapshotBytes != 1024 {
		t.Fatalf("unexpected snapshot counters: %+v", snapshot)
	}
}

func TestSemanticDiagnosticsCancelledAfterOrtRunCountsDiscard(t *testing.T) {
	diagnostics := NewSemanticDiagnostics()
	diagnostics.SetEnabled(true)
	trace := diagnostics.StartJob(domain.AIJob{
		ID:         11,
		AssetID:    12,
		Capability: domain.AICapabilitySemanticSearch,
		CreatedAt:  time.Now(),
	}, 1)
	trace.MarkOrtRun()
	diagnostics.FinishJob(trace, "cancelled", errors.New("cancelled"))

	if got := diagnostics.Snapshot().DiscardedAfterCancel; got != 1 {
		t.Fatalf("discarded after cancel = %d, want 1", got)
	}
}
