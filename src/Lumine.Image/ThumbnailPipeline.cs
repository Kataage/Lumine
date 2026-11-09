using System.Diagnostics;

namespace Lumine.Image;

// Sampled on demand to distinguish queued thumbnail work from active generation.
public readonly record struct ThumbnailPipelineQueueSnapshot(
    int InteractiveQueued,
    int ForegroundQueued,
    int BackgroundQueued,
    int ActiveWorkers);

// No source paths or names; visible only in owner acceptance diagnostics.
public readonly record struct ThumbnailActiveWorkSnapshot(
    long AssetId,
    ThumbnailPriority Priority,
    ThumbnailWorkPhase Phase,
    bool BorrowedInteractiveWorker,
    bool CancellationRequested,
    double AgeMilliseconds,
    double SourceMiB);

public sealed class ThumbnailPipeline : IAsyncDisposable
{
    private readonly ThumbnailGenerator _generator;
    private readonly ThumbnailCacheMaintenance? _maintenance;
    private readonly object _queueGate = new();
    private readonly Queue<WorkItem> _interactive = new();
    private readonly Queue<WorkItem> _foreground = new();
    private readonly Queue<WorkItem> _background = new();
    private readonly SemaphoreSlim _queuedItems = new(0);
    private readonly SemaphoreSlim _interactiveQueuedItems = new(0);
    private readonly Dictionary<int, ActiveWorkState> _activeByWorker = [];
    private CancellationTokenSource? _interactiveLoanCancellation;
    private static readonly TimeSpan InteractiveBorrowProbe =
        TimeSpan.FromMilliseconds(100);
    private readonly SemaphoreSlim _queueSlots;
    private readonly SemaphoreSlim _interactiveQueueSlots;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _disposeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task[] _workers;
    private int _activeWorkItems;
    private bool _disposed;

    public ThumbnailPipeline(
        ThumbnailCache cache,
        ThumbnailPipelineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cache);

        options ??= new ThumbnailPipelineOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.WorkerCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.QueueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.InteractiveQueueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxForegroundBurst);
        if (options.CacheMaintenanceQuietPeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.CacheMaintenanceQuietPeriod,
                "Cache maintenance quiet period cannot be negative.");
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.EncodedMemoryByteLimit);

        WorkerCount = options.WorkerCount;
        QueueCapacity = options.QueueCapacity;
        InteractiveQueueCapacity =
            options.InteractiveQueueCapacity;
        MaxForegroundBurst = options.MaxForegroundBurst;
        StorageMode = options.StorageMode;
        EncodedMemoryByteLimit = options.EncodedMemoryByteLimit;
        _queueSlots =
            new SemaphoreSlim(
                options.QueueCapacity,
                options.QueueCapacity);
        _interactiveQueueSlots =
            new SemaphoreSlim(
                options.InteractiveQueueCapacity,
                options.InteractiveQueueCapacity);
        _generator = new ThumbnailGenerator(
            cache,
            options.StorageMode,
            options.EncodedMemoryByteLimit);
        _maintenance = options.StorageMode == ThumbnailStorageMode.PersistentDisk
            ? new ThumbnailCacheMaintenance(
                cache,
                options.CacheMaintenanceQuietPeriod)
            : null;

        // #634: do not let an ordinary visible PNG decode occupy the
        // last worker while an Interactive target waits for 874ms.
        // Reserve capacity INSIDE the existing native worker budget.
        ReservedInteractiveWorkers = options.WorkerCount >= 4 ? 1 : 0;
        _workers = Enumerable.Range(0, options.WorkerCount)
            .Select(index => Task.Run(() => WorkerLoopAsync(
                index,
                interactiveOnly: index < ReservedInteractiveWorkers)))
            .ToArray();
    }

    public int WorkerCount { get; }

    // Zero for constrained 1-3-worker configurations; never an extra worker.
    public int ReservedInteractiveWorkers { get; }

    public int QueueCapacity { get; }

    public int InteractiveQueueCapacity { get; }

    public int MaxForegroundBurst { get; }

    public ThumbnailStorageMode StorageMode { get; }

    public long EncodedMemoryByteLimit { get; }

    public ThumbnailMemoryCacheStats MemoryCacheStats =>
        _generator.MemoryCacheStats;

    public ThumbnailDiagnosticsSnapshot Diagnostics =>
        _generator.SnapshotDiagnostics();

    public ThumbnailPipelineQueueSnapshot QueueSnapshot
    {
        get
        {
            lock (_queueGate)
            {
                return new ThumbnailPipelineQueueSnapshot(
                    _interactive.Count,
                    _foreground.Count,
                    _background.Count,
                    _activeWorkItems);
            }
        }
    }

    public ThumbnailCacheMaintenanceDiagnosticsSnapshot MaintenanceDiagnostics =>
        _maintenance?.Diagnostics ?? default;

    // Safe snapshot of exact running native jobs, sampled only at idle.
    public IReadOnlyList<ThumbnailActiveWorkSnapshot> SnapshotActiveWork()
    {
        lock (_queueGate)
        {
            return _activeByWorker.OrderBy(static pair => pair.Key)
                .Select(static pair => new ThumbnailActiveWorkSnapshot(
                    pair.Value.Item.Source.AssetId,
                    pair.Value.Item.Priority,
                    pair.Value.Probe.Phase,
                    pair.Value.Borrowed,
                    pair.Value.Item.CancellationToken.IsCancellationRequested,
                    Stopwatch.GetElapsedTime(
                        pair.Value.StartedAtTimestamp).TotalMilliseconds,
                    pair.Value.Item.Source.FileSize / 1_048_576.0))
                .ToArray();
        }
    }

    public async Task<ThumbnailResult> RequestAsync(
        ThumbnailSource source,
        ThumbnailProfile profile,
        ThumbnailPriority priority = ThumbnailPriority.Foreground,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var immediate =
            _generator.TryGetMemoryCached(
                source,
                profile,
                cancellationToken);
        if (immediate is not null)
        {
            return immediate;
        }

        if (_maintenance is not null)
        {
            await _maintenance.PauseForRequestAsync(
                priority is ThumbnailPriority.Interactive
                    or ThumbnailPriority.Foreground)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        using var enqueueCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);

        var admissionSlots =
            priority == ThumbnailPriority.Interactive
                ? _interactiveQueueSlots
                : _queueSlots;

        var admissionStarted = Stopwatch.GetTimestamp();
        try
        {
            await admissionSlots
                .WaitAsync(enqueueCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            _shutdown.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(ThumbnailPipeline));
        }

        var admissionMilliseconds =
            Stopwatch.GetElapsedTime(admissionStarted).TotalMilliseconds;
        var completion = new TaskCompletionSource<ThumbnailResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var item =
            new WorkItem(
                source,
                profile,
                priority,
                completion,
                cancellationToken,
                admissionMilliseconds,
                Stopwatch.GetTimestamp());

        CancellationTokenSource? loanToPreempt = null;
        lock (_queueGate)
        {
            if (_disposed)
            {
                admissionSlots.Release();
                throw new ObjectDisposedException(nameof(ThumbnailPipeline));
            }

            switch (priority)
            {
                case ThumbnailPriority.Interactive:
                    _interactive.Enqueue(item);
                    break;
                case ThumbnailPriority.Foreground:
                    _foreground.Enqueue(item);
                    break;
                case ThumbnailPriority.Background:
                    _background.Enqueue(item);
                    break;
                default:
                    admissionSlots.Release();
                    throw new ArgumentOutOfRangeException(
                        nameof(priority),
                        priority,
                        "Unknown thumbnail priority.");
            }

            if (priority == ThumbnailPriority.Interactive)
            {
                loanToPreempt = _interactiveLoanCancellation;
            }
        }

        // Native libvips kill callbacks must run outside the queue lock.
        if (loanToPreempt is not null)
        {
            try
            {
                loanToPreempt.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The borrowed operation finished concurrently.
            }
            catch (AggregateException)
            {
                // An external native SetKill callback can fail; do not
                // strand the queued Interactive request or its permit.
                // The borrowed worker still owns its completion/cleanup.
            }
        }

        if (priority == ThumbnailPriority.Interactive
            && ReservedInteractiveWorkers > 0)
        {
            _interactiveQueuedItems.Release();
        }
        else
        {
            _queuedItems.Release();
        }

        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        List<WorkItem>? abandoned = null;
        var ownsShutdown = false;

        lock (_queueGate)
        {
            if (!_disposed)
            {
                _disposed = true;
                ownsShutdown = true;
                abandoned = new List<WorkItem>(
                    _interactive.Count
                    + _foreground.Count
                    + _background.Count);

                while (_interactive.TryDequeue(out var interactive))
                {
                    abandoned.Add(interactive);
                }

                while (_foreground.TryDequeue(out var foreground))
                {
                    abandoned.Add(foreground);
                }

                while (_background.TryDequeue(out var background))
                {
                    abandoned.Add(background);
                }
            }
        }

        if (!ownsShutdown)
        {
            await _disposeCompletion.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            if (_maintenance is not null)
            {
                await _maintenance.StopBackgroundAsync()
                    .ConfigureAwait(false);
            }

            _shutdown.Cancel();

            foreach (var item in abandoned!)
            {
                ReleaseAdmissionSlot(
                    item.Priority);
                item.Completion.TrySetException(
                    new ObjectDisposedException(nameof(ThumbnailPipeline)));
            }

            try
            {
                await Task.WhenAll(_workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
            }

            if (_maintenance is not null)
            {
                await _maintenance.FinalizeAsync()
                    .ConfigureAwait(false);
            }

            _shutdown.Dispose();
            _queuedItems.Dispose();
            _interactiveQueuedItems.Dispose();
            _queueSlots.Dispose();
            _interactiveQueueSlots.Dispose();
            _disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            _shutdown.Dispose();
            _queuedItems.Dispose();
            _interactiveQueuedItems.Dispose();
            _queueSlots.Dispose();
            _interactiveQueueSlots.Dispose();
            _disposeCompletion.TrySetException(exception);
            throw;
        }
    }

    private async Task WorkerLoopAsync(
        int workerIndex,
        bool interactiveOnly)
    {
        var foregroundBurst = 0;

        while (true)
        {
            var borrowed = false;
            if (interactiveOnly)
            {
                // #634: the reserved lane must not stay idle for >10s
                // while three Foreground workers are saturated. It may
                // borrow a queued generic request during Interactive
                // quiet time; a new target preempts that native attempt.
                var gotInteractive = await _interactiveQueuedItems.WaitAsync(
                    InteractiveBorrowProbe,
                    _shutdown.Token).ConfigureAwait(false);
                if (!gotInteractive)
                {
                    if (!_queuedItems.Wait(0))
                    {
                        continue;
                    }

                    borrowed = true;
                }
            }
            else
            {
                await _queuedItems.WaitAsync(
                    _shutdown.Token).ConfigureAwait(false);
            }

            WorkItem item;
            CancellationTokenSource? loanCancellation = null;
            var phaseProbe = new ThumbnailGenerationPhaseProbe();

            lock (_queueGate)
            {
                if (borrowed && _interactive.Count > 0)
                {
                    // An Interactive request raced with borrowing.
                    // Restore the general queue's semaphore permit.
                    _queuedItems.Release();
                    continue;
                }

                var nextPriority = SelectNextPriorityForWorker(
                    interactiveOnly && !borrowed,
                    ReservedInteractiveWorkers > 0,
                    _interactive.Count,
                    _foreground.Count,
                    _background.Count,
                    foregroundBurst,
                    MaxForegroundBurst);

                switch (nextPriority)
                {
                    case ThumbnailPriority.Interactive:
                        item = _interactive.Dequeue();
                        break;
                    case ThumbnailPriority.Foreground:
                        item = _foreground.Dequeue();
                        foregroundBurst++;
                        break;
                    case ThumbnailPriority.Background:
                        item = _background.Dequeue();
                        foregroundBurst = 0;
                        break;
                    case null:
                        continue;
                    default:
                        throw new InvalidOperationException(
                            $"Unknown thumbnail queue priority: {nextPriority}.");
                }

                if (borrowed)
                {
                    loanCancellation = new CancellationTokenSource();
                    _interactiveLoanCancellation = loanCancellation;
                }

                _activeWorkItems++;
                _activeByWorker.Add(
                    workerIndex,
                    new ActiveWorkState(
                        item,
                        Stopwatch.GetTimestamp(),
                        phaseProbe,
                        borrowed));
            }

            // A borrowed work item retains its existing queue capacity
            // permit while active, so a preemption can requeue that same
            // item without growing the configured bounded queue.
            if (!borrowed)
            {
                ReleaseAdmissionSlot(item.Priority);
            }

            var workerQueueMilliseconds =
                Stopwatch.GetElapsedTime(
                    item.QueuedAtTimestamp).TotalMilliseconds;
            ThumbnailResult? result = null;
            var requeued = false;

            try
            {
                if (item.CancellationToken.IsCancellationRequested)
                {
                    item.Completion.TrySetCanceled(item.CancellationToken);
                    continue;
                }

                using var activeCancellation = loanCancellation is null
                    ? CancellationTokenSource.CreateLinkedTokenSource(
                        item.CancellationToken,
                        _shutdown.Token)
                    : CancellationTokenSource.CreateLinkedTokenSource(
                        item.CancellationToken,
                        _shutdown.Token,
                        loanCancellation.Token);

                result = _generator.GetOrCreate(
                    item.Source,
                    item.Profile,
                    activeCancellation.Token,
                    phaseProbe);
                if (!result.CacheHit)
                {
                    result = result with
                    {
                        WorkTimings = new ThumbnailWorkTimings(
                            item.QueueAdmissionMilliseconds,
                            workerQueueMilliseconds,
                            phaseProbe.SourceMetadataMilliseconds,
                            phaseProbe.SourceKeyWaitMilliseconds,
                            phaseProbe.NativeSetupMilliseconds,
                            phaseProbe.RenderEncodeMilliseconds)
                    };
                }

                phaseProbe.SetPhase(ThumbnailWorkPhase.Completed);
                item.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException)
                when (borrowed
                    && loanCancellation?.IsCancellationRequested == true
                    && !item.CancellationToken.IsCancellationRequested
                    && !_shutdown.IsCancellationRequested)
            {
                // This is only an INTERNAL loan preemption, not caller
                // cancellation. Preserve the existing completion task,
                // source identity, priority and bounded queue permit.
                lock (_queueGate)
                {
                    if (!_disposed)
                    {
                        var pending = item with
                        {
                            QueuedAtTimestamp = Stopwatch.GetTimestamp()
                        };
                        if (item.Priority == ThumbnailPriority.Foreground)
                        {
                            _foreground.Enqueue(pending);
                        }
                        else
                        {
                            _background.Enqueue(pending);
                        }

                        requeued = true;
                    }
                }

                if (requeued)
                {
                    _queuedItems.Release();
                }
                else
                {
                    item.Completion.TrySetException(
                        new ObjectDisposedException(nameof(ThumbnailPipeline)));
                }
            }
            catch (OperationCanceledException)
                when (item.CancellationToken.IsCancellationRequested)
            {
                item.Completion.TrySetCanceled(item.CancellationToken);
            }
            catch (OperationCanceledException)
                when (_shutdown.IsCancellationRequested)
            {
                item.Completion.TrySetException(
                    new ObjectDisposedException(nameof(ThumbnailPipeline)));
            }
            catch (Exception exception)
            {
                item.Completion.TrySetException(exception);
            }
            finally
            {
                lock (_queueGate)
                {
                    _activeWorkItems--;
                    _activeByWorker.Remove(workerIndex);
                    if (loanCancellation is not null
                        && ReferenceEquals(
                            _interactiveLoanCancellation,
                            loanCancellation))
                    {
                        _interactiveLoanCancellation = null;
                    }
                }

                if (borrowed && !requeued)
                {
                    ReleaseAdmissionSlot(item.Priority);
                }

                loanCancellation?.Dispose();
                _maintenance?.NotifyRequestCompleted(
                    result,
                    IsIdleForMaintenance);
            }
        }
    }

    // Dedicated lane sees only Interactive; generic workers see
    // Foreground/Background. The fallback shared queue (<=3 workers)
    // retains the original Interactive -> Foreground -> Background order.
    private static ThumbnailPriority? SelectNextPriorityForWorker(
        bool interactiveOnly,
        bool reservedInteractiveWorker,
        int interactiveCount,
        int foregroundCount,
        int backgroundCount,
        int foregroundBurst,
        int maxForegroundBurst) =>
        SelectNextPriority(
            interactiveOnly || !reservedInteractiveWorker
                ? interactiveCount
                : 0,
            interactiveOnly ? 0 : foregroundCount,
            interactiveOnly ? 0 : backgroundCount,
            foregroundBurst,
            maxForegroundBurst);

    private static ThumbnailPriority? SelectNextPriority(
        int interactiveCount,
        int foregroundCount,
        int backgroundCount,
        int foregroundBurst,
        int maxForegroundBurst)
    {
        if (interactiveCount > 0)
        {
            return ThumbnailPriority.Interactive;
        }

        if (foregroundCount > 0
            && (backgroundCount == 0
                || foregroundBurst < maxForegroundBurst))
        {
            return ThumbnailPriority.Foreground;
        }

        if (backgroundCount > 0)
        {
            return ThumbnailPriority.Background;
        }

        if (foregroundCount > 0)
        {
            return ThumbnailPriority.Foreground;
        }

        return null;
    }

    private void ReleaseAdmissionSlot(
        ThumbnailPriority priority)
    {
        if (priority == ThumbnailPriority.Interactive)
        {
            _interactiveQueueSlots.Release();
            return;
        }

        _queueSlots.Release();
    }

    private bool IsIdleForMaintenance()
    {
        lock (_queueGate)
        {
            return !_disposed
                && _activeWorkItems == 0
                && _interactive.Count == 0
                && _foreground.Count == 0
                && _background.Count == 0;
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_queueGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

    private sealed record ActiveWorkState(
        WorkItem Item,
        long StartedAtTimestamp,
        ThumbnailGenerationPhaseProbe Probe,
        bool Borrowed);

    private sealed record WorkItem(
        ThumbnailSource Source,
        ThumbnailProfile Profile,
        ThumbnailPriority Priority,
        TaskCompletionSource<ThumbnailResult> Completion,
        CancellationToken CancellationToken,
        double QueueAdmissionMilliseconds,
        long QueuedAtTimestamp);
}
