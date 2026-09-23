namespace Lumine.Viewer;

public sealed class ViewerSession : IAsyncDisposable
{
    private readonly IViewerAssetProvider _assets;
    private readonly IViewerThumbnailProvider _thumbnails;
    private readonly object _gate = new();
    private readonly Dictionary<long, InFlightRequest> _inFlight = [];
    private readonly CancellationTokenSource _shutdown = new();
    private long _thumbnailRequests;
    private long _thumbnailRequestsCoalesced;
    private long _thumbnailRequestsCancelled;
    private int _attachedTiles;
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
                    _inFlight.Count,
                    Volatile.Read(ref _attachedTiles),
                    bitmap.EntryCount,
                    bitmap.EstimatedBytes);
            }
        }
    }

    public void NotifyTileAttached() => Interlocked.Increment(ref _attachedTiles);

    public void NotifyTileDetached() => Interlocked.Decrement(ref _attachedTiles);

    public ValueTask<ViewerAsset> GetAssetAsync(
        long index,
        CancellationToken cancellationToken = default) =>
        _assets.GetAssetAsync(index, cancellationToken);

    public ValueTask<ViewerThumbnail> GetThumbnailAsync(
        ViewerAsset asset,
        ViewerThumbnailPriority priority,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        InFlightRequest request;
        lock (_gate)
        {
            if (_inFlight.TryGetValue(asset.Id, out var existing))
            {
                if (existing.Priority == ViewerThumbnailPriority.Background
                    && priority == ViewerThumbnailPriority.Foreground)
                {
                    existing.Cancellation.Cancel();
                    _inFlight.Remove(asset.Id);
                    Interlocked.Increment(ref _thumbnailRequestsCancelled);
                }
                else
                {
                    existing.Waiters++;
                    Interlocked.Increment(ref _thumbnailRequestsCoalesced);
                    request = existing;
                    return AwaitSharedAsync(asset.Id, request, cancellationToken);
                }
            }

            var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            var task = RequestCoreAsync(asset, priority, requestCancellation.Token);
            request = new InFlightRequest(priority, requestCancellation, task)
            {
                Waiters = 1
            };
            _inFlight[asset.Id] = request;
            Interlocked.Increment(ref _thumbnailRequests);
        }

        return AwaitSharedAsync(asset.Id, request, cancellationToken);
    }

    public async Task PrefetchAsync(
        long startIndex,
        int count,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0 || Count == 0)
        {
            return;
        }

        var start = Math.Clamp(startIndex, 0, Count - 1);
        var endExclusive = Math.Min(Count, start + count);
        var tasks = new List<Task>(checked((int)Math.Min(endExclusive - start, 256)));

        for (var index = start; index < endExclusive; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tasks.Add(PrefetchOneAsync(index, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        List<InFlightRequest> requests;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            requests = [.. _inFlight.Values];
            _inFlight.Clear();
        }

        _shutdown.Cancel();
        foreach (var request in requests)
        {
            request.Cancellation.Cancel();
        }

        try
        {
            await Task.WhenAll(requests.Select(static request => request.Task)).ConfigureAwait(false);
        }
        catch
        {
            // Individual waiters already observe their own failures/cancellation.
        }

        foreach (var request in requests)
        {
            request.Cancellation.Dispose();
        }

        BitmapCache.Dispose();
        _shutdown.Dispose();
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
    }

    private async Task<ViewerThumbnail> RequestCoreAsync(
        ViewerAsset asset,
        ViewerThumbnailPriority priority,
        CancellationToken cancellationToken) =>
        await _thumbnails.RequestAsync(asset, priority, cancellationToken).ConfigureAwait(false);

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
                        request.Cancellation.Cancel();
                        Interlocked.Increment(ref _thumbnailRequestsCancelled);
                    }

                    if (_inFlight.TryGetValue(assetId, out var current)
                        && ReferenceEquals(current, request))
                    {
                        _inFlight.Remove(assetId);
                    }

                    request.Cancellation.Dispose();
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

    private sealed class InFlightRequest(
        ViewerThumbnailPriority priority,
        CancellationTokenSource cancellation,
        Task<ViewerThumbnail> task)
    {
        public ViewerThumbnailPriority Priority { get; } = priority;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task<ViewerThumbnail> Task { get; } = task;

        public int Waiters { get; set; }
    }
}
