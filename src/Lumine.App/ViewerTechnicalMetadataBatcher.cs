using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lumine.Library;

namespace Lumine.App;

/// <summary>
/// Bounded, per-LibraryService coordination for background thumbnail metadata.
/// Interactive and foreground thumbnail writes deliberately bypass this queue:
/// they retain the existing direct, synchronous-on-request persistence path.
/// </summary>
internal readonly record struct ViewerTechnicalMetadataBatchDiagnostics(
    long CommittedBatches,
    long BatchedRows,
    long QueuedWrites,
    long SingleWriteCommits,
    long RejectedWrites,
    long CanceledBeforeWrite,
    long FailedGroups,
    long PeakPendingQueueDepth,
    long CompletedWriteLatencyTicks,
    long MaxCompletedWriteLatencyTicks);

internal sealed class ViewerTechnicalMetadataBatcher
{
    private const int MaxQueuedWrites = 128;
    private static readonly ConditionalWeakTable<
        LibraryService, ViewerTechnicalMetadataBatcher> Writers = new();

    private readonly LibraryService _library;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _slots = new(MaxQueuedWrites, MaxQueuedWrites);
    private readonly Queue<PendingWrite> _pending = new();
    private bool _draining;
    private long _committedBatches;
    private long _batchedRows;
    private long _queuedWrites;
    private long _singleWriteCommits;
    private long _rejectedWrites;
    private long _canceledBeforeWrite;
    private long _failedGroups;
    private int _peakPendingDepth;
    private long _completedWriteLatencyTicks;
    private long _maxCompletedWriteLatencyTicks;

    private ViewerTechnicalMetadataBatcher(LibraryService library) =>
        _library = library;

    internal static ViewerTechnicalMetadataBatcher For(LibraryService library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return Writers.GetValue(
            library,
            static key => new ViewerTechnicalMetadataBatcher(key));
    }

    internal ViewerTechnicalMetadataBatchDiagnostics Diagnostics =>
        new(
            Interlocked.Read(ref _committedBatches),
            Interlocked.Read(ref _batchedRows),
            Interlocked.Read(ref _queuedWrites),
            Interlocked.Read(ref _singleWriteCommits),
            Interlocked.Read(ref _rejectedWrites),
            Interlocked.Read(ref _canceledBeforeWrite),
            Interlocked.Read(ref _failedGroups),
            Volatile.Read(ref _peakPendingDepth),
            Interlocked.Read(ref _completedWriteLatencyTicks),
            Interlocked.Read(ref _maxCompletedWriteLatencyTicks));

    /// <summary>
    /// Completing the task means SQLite COMMIT has finished, not that the work
    /// was merely enqueued. Canceled entries are discarded before beginning
    /// the next transaction; individual cancel tokens cannot abort a group
    /// containing independent background thumbnail requests.
    /// </summary>
    internal async Task<bool> WriteAsync(
        long libraryId,
        TechnicalMetadataBatchItem item,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        // Unlike an unbounded channel, this gates even callers waiting for
        // scheduling capacity and never launches one Task.Run per image.
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        var pending = new PendingWrite(libraryId, item, cancellationToken);
        try
        {
            lock (_gate)
            {
                _pending.Enqueue(pending);
                Interlocked.Increment(ref _queuedWrites);
                _peakPendingDepth = Math.Max(
                    _peakPendingDepth, _pending.Count);
                if (!_draining)
                {
                    _draining = true;
                    _ = Task.Run(DrainAsync);
                }
            }
        }
        catch
        {
            _slots.Release();
            throw;
        }

        return await pending.Completion.Task.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DrainAsync()
    {
        // Give concurrent background thumbnail completions a short coalescing
        // window without adding any delay to foreground/interactive rendering.
        await Task.Delay(2).ConfigureAwait(false);

        while (true)
        {
            List<PendingWrite> selected = [];
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    _draining = false;
                    return;
                }

                var libraryId = _pending.Peek().LibraryId;
                while (selected.Count < LibraryRepository.MaxTechnicalMetadataBatchSize
                       && _pending.Count > 0
                       && _pending.Peek().LibraryId == libraryId)
                {
                    selected.Add(_pending.Dequeue());
                }
            }

            try
            {
                var live = new List<PendingWrite>(selected.Count);
                foreach (var pending in selected)
                {
                    if (pending.CancellationToken.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref _canceledBeforeWrite);
                        pending.Completion.TrySetCanceled(pending.CancellationToken);
                    }
                    else
                    {
                        live.Add(pending);
                    }
                }

                if (live.Count == 1)
                {
                    var one = live[0];
                    var item = one.Item;
                    var updated = await _library.UpdateTechnicalMetadataAsync(
                        one.LibraryId,
                        item.AssetId,
                        item.ExpectedSourceRevision,
                        item.ExpectedFileSize,
                        item.ExpectedModifiedAtUtcTicks,
                        item.Metadata).ConfigureAwait(false);
                    if (updated)
                    {
                        Interlocked.Increment(ref _singleWriteCommits);
                    }
                    RecordCompletedWrite(one, updated);
                    one.Completion.TrySetResult(updated);
                }
                else if (live.Count > 1)
                {
                    var items = live.Select(static write => write.Item).ToArray();
                    var results = await _library.UpdateTechnicalMetadataBatchAsync(
                        live[0].LibraryId,
                        items).ConfigureAwait(false);

                    if (results.Count != live.Count)
                    {
                        throw new InvalidOperationException(
                            "SQLite technical metadata batch returned a mismatched result count.");
                    }

                    Interlocked.Increment(ref _committedBatches);
                    Interlocked.Add(ref _batchedRows, results.Count);
                    for (var index = 0; index < live.Count; index++)
                    {
                        RecordCompletedWrite(live[index], results[index]);
                        live[index].Completion.TrySetResult(results[index]);
                    }
                }
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref _failedGroups);
                foreach (var pending in selected)
                {
                    pending.Completion.TrySetException(exception);
                }
            }
            finally
            {
                // Every accepted queue position is returned even after
                // SQLite exception, cancellation, or a conflicting revision.
                foreach (var _ in selected)
                {
                    _slots.Release();
                }
            }

            // Background metadata commits must not monopolize the scheduler
            // while a foreground image request is ready.
            await Task.Yield();
        }
    }

    private void RecordCompletedWrite(PendingWrite pending, bool accepted)
    {
        // The clock starts when the request is admitted to the bounded
        // background queue and ends only after SQLite COMMIT has returned.
        // This is production-path wall time (including scheduling), not a
        // pure SQLite COMMIT or a first-raster measurement.
        var elapsedTicks = Stopwatch.GetElapsedTime(
            pending.AdmittedAtTimestamp).Ticks;
        Interlocked.Add(ref _completedWriteLatencyTicks, elapsedTicks);
        if (!accepted)
        {
            Interlocked.Increment(ref _rejectedWrites);
        }

        long observed;
        while ((observed = Interlocked.Read(
                   ref _maxCompletedWriteLatencyTicks)) < elapsedTicks)
        {
            if (Interlocked.CompareExchange(
                    ref _maxCompletedWriteLatencyTicks,
                    elapsedTicks,
                    observed) == observed)
            {
                break;
            }
        }
    }

    private sealed class PendingWrite(
        long libraryId,
        TechnicalMetadataBatchItem item,
        CancellationToken cancellationToken)
    {
        public long LibraryId { get; } = libraryId;
        public TechnicalMetadataBatchItem Item { get; } = item;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public long AdmittedAtTimestamp { get; } = Stopwatch.GetTimestamp();
        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
