namespace Lumine.Image;

public sealed class ThumbnailPipeline : IAsyncDisposable
{
    private readonly ThumbnailGenerator _generator;
    private readonly ThumbnailCacheMaintenance? _maintenance;
    private readonly object _queueGate = new();
    private readonly Queue<WorkItem> _interactive = new();
    private readonly Queue<WorkItem> _foreground = new();
    private readonly Queue<WorkItem> _background = new();
    private readonly SemaphoreSlim _queuedItems = new(0);
    private readonly SemaphoreSlim _queueSlots;
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
        MaxForegroundBurst = options.MaxForegroundBurst;
        StorageMode = options.StorageMode;
        EncodedMemoryByteLimit = options.EncodedMemoryByteLimit;
        _queueSlots = new SemaphoreSlim(options.QueueCapacity, options.QueueCapacity);
        _generator = new ThumbnailGenerator(
            cache,
            options.StorageMode,
            options.EncodedMemoryByteLimit);
        _maintenance = options.StorageMode == ThumbnailStorageMode.PersistentDisk
            ? new ThumbnailCacheMaintenance(
                cache,
                options.CacheMaintenanceQuietPeriod)
            : null;

        _workers = Enumerable.Range(0, options.WorkerCount)
            .Select(_ => Task.Run(WorkerLoopAsync))
            .ToArray();
    }

    public int WorkerCount { get; }

    public int QueueCapacity { get; }

    public int MaxForegroundBurst { get; }

    public ThumbnailStorageMode StorageMode { get; }

    public long EncodedMemoryByteLimit { get; }

    public ThumbnailMemoryCacheStats MemoryCacheStats =>
        _generator.MemoryCacheStats;

    public ThumbnailDiagnosticsSnapshot Diagnostics =>
        _generator.SnapshotDiagnostics();

    public ThumbnailCacheMaintenanceDiagnosticsSnapshot MaintenanceDiagnostics =>
        _maintenance?.Diagnostics ?? default;

    public async Task<ThumbnailResult> RequestAsync(
        ThumbnailSource source,
        ThumbnailProfile profile,
        ThumbnailPriority priority = ThumbnailPriority.Foreground,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

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

        try
        {
            await _queueSlots.WaitAsync(enqueueCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            _shutdown.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(ThumbnailPipeline));
        }

        var completion = new TaskCompletionSource<ThumbnailResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new WorkItem(source, profile, completion, cancellationToken);

        lock (_queueGate)
        {
            if (_disposed)
            {
                _queueSlots.Release();
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
                    _queueSlots.Release();
                    throw new ArgumentOutOfRangeException(
                        nameof(priority),
                        priority,
                        "Unknown thumbnail priority.");
            }
        }

        _queuedItems.Release();

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
                _queueSlots.Release();
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
            _queueSlots.Dispose();
            _disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            _shutdown.Dispose();
            _queuedItems.Dispose();
            _queueSlots.Dispose();
            _disposeCompletion.TrySetException(exception);
            throw;
        }
    }

    private async Task WorkerLoopAsync()
    {
        var foregroundBurst = 0;

        while (true)
        {
            await _queuedItems.WaitAsync(_shutdown.Token).ConfigureAwait(false);

            WorkItem? item;
            lock (_queueGate)
            {
                if (_interactive.TryDequeue(out var interactive))
                {
                    item = interactive;
                }
                else if (_foreground.Count > 0
                    && (_background.Count == 0 || foregroundBurst < MaxForegroundBurst))
                {
                    item = _foreground.Dequeue();
                    foregroundBurst++;
                }
                else if (_background.TryDequeue(out var background))
                {
                    item = background;
                    foregroundBurst = 0;
                }
                else if (_foreground.TryDequeue(out var foreground))
                {
                    item = foreground;
                    foregroundBurst = 1;
                }
                else
                {
                    continue;
                }

                _activeWorkItems++;
            }

            _queueSlots.Release();

            ThumbnailResult? result = null;

            try
            {
                if (item.CancellationToken.IsCancellationRequested)
                {
                    item.Completion.TrySetCanceled(
                        item.CancellationToken);
                    continue;
                }

                using var activeCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        item.CancellationToken,
                        _shutdown.Token);

                result = _generator.GetOrCreate(
                    item.Source,
                    item.Profile,
                    activeCancellation.Token);

                item.Completion.TrySetResult(result);
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
                }

                _maintenance?.NotifyRequestCompleted(
                    result,
                    IsIdleForMaintenance);
            }
        }
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

    private sealed record WorkItem(
        ThumbnailSource Source,
        ThumbnailProfile Profile,
        TaskCompletionSource<ThumbnailResult> Completion,
        CancellationToken CancellationToken);
}
