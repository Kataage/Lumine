namespace Lumine.Viewer;

public sealed class ViewerSession : IAsyncDisposable
{
    private readonly IViewerAssetProvider _assets;
    private readonly IViewerThumbnailProvider _thumbnails;
    private readonly object _gate = new();
    private readonly Dictionary<long, InFlightRequest> _inFlight = [];
    private readonly HashSet<InFlightRequest> _activeRequests = [];
    private readonly CancellationTokenSource _shutdown = new();
    private TaskCompletionSource _operationsDrained =
        NewCompletedSignal();
    private readonly TaskCompletionSource _disposeCompletion =
        NewSignal();
    private int _activeOperations;
    private long _thumbnailRequests;
    private long _thumbnailRequestsCoalesced;
    private long _thumbnailRequestsCancelled;
    private long _thumbnailRequestsFailed;
    private long _tileLoadFailures;
    private string? _lastTileLoadError;
    private int _attachedTiles;
    private int _readyTiles;
    private bool _disposed;

    public ViewerSession(
        IViewerAssetProvider assets,
        IViewerThumbnailProvider thumbnails,
        ViewerOptions? options = null)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        Options = options ?? new ViewerOptions();

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Options.TileWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Options.TileHeight, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(Options.TileSpacing);
        ArgumentOutOfRangeException.ThrowIfNegative(Options.PrefetchRows);
        ArgumentOutOfRangeException.ThrowIfLessThan(Options.PrefetchDelay, TimeSpan.Zero);

        BitmapCache = new DecodedBitmapCache(
            Options.DecodedBitmapEntryLimit,
            Options.DecodedBitmapByteLimit);
    }

    public ViewerOptions Options { get; }

    public long Count => _assets.Count;

    public DecodedBitmapCache BitmapCache { get; }

    public ViewerRuntimeDiagnostics Diagnostics
    {
        get
        {
            lock (_gate)
            {
                var bitmap = BitmapCache.Diagnostics;
                return new ViewerRuntimeDiagnostics(
                    Interlocked.Read(ref _thumbnailRequests),
                    Interlocked.Read(ref _thumbnailRequestsCoalesced),
                    Interlocked.Read(ref _thumbnailRequestsCancelled),
                    Interlocked.Read(ref _thumbnailRequestsFailed),
                    Interlocked.Read(ref _tileLoadFailures),
                    _inFlight.Count,
                    Volatile.Read(ref _attachedTiles),
                    Volatile.Read(ref _readyTiles),
                    bitmap.EntryCount,
                    bitmap.EstimatedBytes,
                    bitmap.ActiveDecodes,
                    bitmap.PeakConcurrentDecodes,
                    _lastTileLoadError);
            }
        }
    }

    public void NotifyTileAttached() => Interlocked.Increment(ref _attachedTiles);

    public void NotifyTileDetached() => Interlocked.Decrement(ref _attachedTiles);

    public void NotifyTileReady() => Interlocked.Increment(ref _readyTiles);

    public void NotifyTileNotReady() => Interlocked.Decrement(ref _readyTiles);

    public void NotifyTileLoadFailed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Interlocked.Increment(ref _tileLoadFailures);

        lock (_gate)
        {
            _lastTileLoadError =
                $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    public async ValueTask<ViewerAsset> GetAssetAsync(
        long index,
        CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation(
            cancellationToken);

        try
        {
            return await _assets.GetAssetAsync(
                index,
                operation.Token).ConfigureAwait(false);
        }
        finally
        {
            CompleteOperation();
        }
    }

    public ValueTask<ViewerThumbnail> GetThumbnailAsync(
        ViewerAsset asset,
        ViewerThumbnailPriority priority,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        InFlightRequest request;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_inFlight.TryGetValue(asset.Id, out var existing))
            {
                if (existing.Priority == ViewerThumbnailPriority.Background
                    && priority == ViewerThumbnailPriority.Foreground)
                {
                    existing.Cancel();
                    _inFlight.Remove(asset.Id);
                    Interlocked.Increment(
                        ref _thumbnailRequestsCancelled);
                }
                else
                {
                    existing.Waiters++;
                    Interlocked.Increment(
                        ref _thumbnailRequestsCoalesced);
                    request = existing;
                    return AwaitSharedAsync(
                        asset.Id,
                        request,
                        cancellationToken);
                }
            }

            var requestCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    _shutdown.Token);
            var task = RequestCoreAsync(
                asset,
                priority,
                requestCancellation.Token);
            request = new InFlightRequest(
                priority,
                requestCancellation,
                task)
            {
                Waiters = 1
            };
            _inFlight[asset.Id] = request;
            _activeRequests.Add(request);
            request.CleanupTask =
                ObserveRequestCompletionAsync(
                    asset.Id,
                    request);
            Interlocked.Increment(ref _thumbnailRequests);
        }

        return AwaitSharedAsync(
            asset.Id,
            request,
            cancellationToken);
    }

    public async Task PrefetchAsync(
        long startIndex,
        int count,
        CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation(
            cancellationToken);

        try
        {
            var operationToken = operation.Token;

            if (count <= 0 || Count == 0)
            {
                return;
            }

            var start = Math.Clamp(
                startIndex,
                0,
                Count - 1);
            var endExclusive = Math.Min(
                Count,
                start + count);
            var tasks = new List<Task>(
                checked((int)Math.Min(
                    endExclusive - start,
                    256)));

            for (var index = start;
                 index < endExclusive;
                 index++)
            {
                operationToken.ThrowIfCancellationRequested();
                tasks.Add(PrefetchOneAsync(
                    index,
                    operationToken));
            }

            await Task.WhenAll(tasks)
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteOperation();
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<InFlightRequest>? requests = null;
        Task? operationDrain = null;
        var ownsShutdown = false;

        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                requests = [.. _activeRequests];
                _inFlight.Clear();
                operationDrain = _operationsDrained.Task;
                ownsShutdown = true;
            }
        }

        if (!ownsShutdown)
        {
            await _disposeCompletion.Task
                .ConfigureAwait(false);
            return;
        }

        var ownedRequests = requests!;
        var ownedOperationDrain = operationDrain!;

        try
        {
            CancelSourceNoThrow(_shutdown);

            foreach (var request in ownedRequests)
            {
                request.Cancel();
            }

            try
            {
                await Task.WhenAll(
                    ownedRequests.Select(
                        static request => request.Task))
                    .ConfigureAwait(false);
            }
            catch
            {
                // Shutdown owns draining, not propagation of individual
                // provider cancellation/failure.
            }

            await ownedOperationDrain
                .ConfigureAwait(false);

            await Task.WhenAll(
                ownedRequests.Select(
                    static request => request.CleanupTask))
                .ConfigureAwait(false);

            foreach (var request in ownedRequests)
            {
                request.DisposeCancellation();
            }

            await BitmapCache.DisposeAsync()
                .ConfigureAwait(false);

            _shutdown.Dispose();
            _disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            _disposeCompletion.TrySetException(exception);
            throw;
        }
    }

    private async Task PrefetchOneAsync(long index, CancellationToken cancellationToken)
    {
        try
        {
            var asset = await _assets.GetAssetAsync(index, cancellationToken).ConfigureAwait(false);
            _ = await GetThumbnailAsync(
                asset,
                ViewerThumbnailPriority.Background,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A background request may be intentionally cancelled when the
            // same asset becomes visible and is re-issued at foreground priority.
        }
        catch
        {
            // Background prefetch is opportunistic. A visible tile will retry
            // and surface the failure if/when the asset enters the viewport.
        }
    }

    private CancellationTokenSource BeginOperation(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_activeOperations == 0)
            {
                _operationsDrained = NewSignal();
            }

            _activeOperations++;

            try
            {
                return CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken,
                        _shutdown.Token);
            }
            catch
            {
                var drained = CompleteOperationLocked();
                drained?.TrySetResult();
                throw;
            }
        }
    }

    private void CompleteOperation()
    {
        TaskCompletionSource? drained;

        lock (_gate)
        {
            drained = CompleteOperationLocked();
        }

        drained?.TrySetResult();
    }

    private TaskCompletionSource? CompleteOperationLocked()
    {
        _activeOperations--;
        return _activeOperations == 0
            ? _operationsDrained
            : null;
    }

    private static void CancelSourceNoThrow(
        CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch
        {
            // Cancellation callbacks are external to lifecycle ownership.
            // Cleanup continues and #293 will own diagnostic logging.
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource NewCompletedSignal()
    {
        var signal = NewSignal();
        signal.TrySetResult();
        return signal;
    }

    private async Task<ViewerThumbnail> RequestCoreAsync(
        ViewerAsset asset,
        ViewerThumbnailPriority priority,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _thumbnails.RequestAsync(
                asset,
                priority,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            Interlocked.Increment(ref _thumbnailRequestsFailed);
            throw;
        }
    }

    private async Task ObserveRequestCompletionAsync(
        long assetId,
        InFlightRequest request)
    {
        try
        {
            await request.Task.ConfigureAwait(false);
        }
        catch
        {
            // Individual waiters observe the actual result. This task owns
            // request lifetime cleanup only.
        }
        finally
        {
            lock (_gate)
            {
                _activeRequests.Remove(request);

                if (request.Waiters == 0
                    && _inFlight.TryGetValue(
                        assetId,
                        out var current)
                    && ReferenceEquals(current, request))
                {
                    _inFlight.Remove(assetId);
                }
            }

            request.DisposeCancellation();
        }
    }

    private async ValueTask<ViewerThumbnail> AwaitSharedAsync(
        long assetId,
        InFlightRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await request.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                request.Waiters--;

                if (request.Waiters == 0)
                {
                    if (!request.Task.IsCompleted)
                    {
                        request.Cancel();
                        Interlocked.Increment(
                            ref _thumbnailRequestsCancelled);
                    }

                    if (_inFlight.TryGetValue(assetId, out var current)
                        && ReferenceEquals(current, request))
                    {
                        _inFlight.Remove(assetId);
                    }
                }
            }
        }
    }

    private sealed class InFlightRequest(
        ViewerThumbnailPriority priority,
        CancellationTokenSource cancellation,
        Task<ViewerThumbnail> task)
    {
        private int _cancellationDisposed;

        public ViewerThumbnailPriority Priority { get; } = priority;

        public Task<ViewerThumbnail> Task { get; } = task;

        public Task CleanupTask { get; set; } =
            System.Threading.Tasks.Task.CompletedTask;

        public int Waiters { get; set; }

        public void Cancel()
        {
            if (Volatile.Read(ref _cancellationDisposed) != 0)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch
            {
                // Cancellation callbacks are outside ViewerSession ownership.
                // Shutdown must still continue; #293 owns later diagnostics.
            }
        }

        public void DisposeCancellation()
        {
            if (Interlocked.Exchange(
                    ref _cancellationDisposed,
                    1) != 0)
            {
                return;
            }

            cancellation.Dispose();
        }
    }
}
