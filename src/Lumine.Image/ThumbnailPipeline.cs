namespace Lumine.Image;

public sealed class ThumbnailPipeline : IAsyncDisposable
{
    private readonly ThumbnailGenerator _generator;
    private readonly object _queueGate = new();
    private readonly Queue<WorkItem> _foreground = new();
    private readonly Queue<WorkItem> _background = new();
    private readonly SemaphoreSlim _queuedItems = new(0);
    private readonly SemaphoreSlim _queueSlots;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    private bool _disposed;

    public ThumbnailPipeline(
        ThumbnailCache cache,
        ThumbnailPipelineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cache);

        options ??= new ThumbnailPipelineOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.WorkerCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.QueueCapacity);

        WorkerCount = options.WorkerCount;
        QueueCapacity = options.QueueCapacity;
        _queueSlots = new SemaphoreSlim(options.QueueCapacity, options.QueueCapacity);
        _generator = new ThumbnailGenerator(cache);

        _workers = Enumerable.Range(0, options.WorkerCount)
            .Select(_ => Task.Run(WorkerLoopAsync))
            .ToArray();
    }

    public int WorkerCount { get; }

    public int QueueCapacity { get; }

    public ThumbnailDiagnosticsSnapshot Diagnostics =>
        _generator.SnapshotDiagnostics();

    public async Task<ThumbnailResult> RequestAsync(
        ThumbnailSource source,
        ThumbnailProfile profile,
        ThumbnailPriority priority = ThumbnailPriority.Foreground,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _queueSlots.WaitAsync(cancellationToken).ConfigureAwait(false);

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

            if (priority == ThumbnailPriority.Foreground)
            {
                _foreground.Enqueue(item);
            }
            else
            {
                _background.Enqueue(item);
            }
        }

        _queuedItems.Release();

        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        List<WorkItem> abandoned;

        lock (_queueGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            abandoned = new List<WorkItem>(_foreground.Count + _background.Count);

            while (_foreground.TryDequeue(out var foreground))
            {
                abandoned.Add(foreground);
            }

            while (_background.TryDequeue(out var background))
            {
                abandoned.Add(background);
            }
        }

        foreach (var item in abandoned)
        {
            item.Completion.TrySetCanceled();
        }

        _shutdown.Cancel();

        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            _shutdown.Dispose();
            _queuedItems.Dispose();
            _queueSlots.Dispose();
        }
    }

    private async Task WorkerLoopAsync()
    {
        while (true)
        {
            await _queuedItems.WaitAsync(_shutdown.Token).ConfigureAwait(false);

            WorkItem? item;
            lock (_queueGate)
            {
                if (_foreground.TryDequeue(out var foreground))
                {
                    item = foreground;
                }
                else if (_background.TryDequeue(out var background))
                {
                    item = background;
                }
                else
                {
                    continue;
                }
            }

            _queueSlots.Release();

            if (item.CancellationToken.IsCancellationRequested)
            {
                item.Completion.TrySetCanceled(item.CancellationToken);
                continue;
            }

            try
            {
                var result = _generator.GetOrCreate(
                    item.Source,
                    item.Profile,
                    item.CancellationToken);

                item.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException)
                when (item.CancellationToken.IsCancellationRequested)
            {
                item.Completion.TrySetCanceled(item.CancellationToken);
            }
            catch (Exception exception)
            {
                item.Completion.TrySetException(exception);
            }
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
