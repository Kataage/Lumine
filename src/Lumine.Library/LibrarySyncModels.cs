namespace Lumine.Library;

public enum DirectoryChangeKind
{
    Added = 1,
    Removed = 2,
    Modified = 3,
    Renamed = 4,
    Overflow = 5
}

public sealed record DirectoryChange(
    DirectoryChangeKind Kind,
    string RelativePath,
    string? OldRelativePath,
    DateTimeOffset ObservedAtUtc);

public sealed record LibraryReconcileResult(
    long Generation,
    int Discovered,
    int Persisted,
    int Skipped,
    long Deleted,
    bool Completed,
    DateTimeOffset FinishedAtUtc,
    IReadOnlyList<LibraryScanFailure> FailureSamples);

public readonly record struct LibrarySyncDiagnostics(
    long EventsObserved,
    long EventsApplied,
    long EventsCoalesced,
    long Overflows,
    long Reconciliations,
    long ReconcileFailures,
    long RenameOperations,
    long Deletes,
    long Upserts,
    double LastApplyLatencyMs,
    double MaxApplyLatencyMs,
    int QueueDepth);
