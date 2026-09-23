namespace Lumine.Viewer;

public sealed class CursorPagedViewerAssetProvider : IViewerAssetProvider, IDisposable
{
    private readonly IViewerPageSource _source;
    private readonly int _pageSize;
    private readonly int _pageCacheLimit;
    private readonly int _checkpointStride;
    private readonly int _checkpointLimit;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<long, CachedPage> _pages = [];
    private readonly Dictionary<long, CursorCheckpoint> _checkpoints = [];
    private long _accessSequence;
    private long _pagesFetched;

    public CursorPagedViewerAssetProvider(
        IViewerPageSource source,
        ViewerOptions? options = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        options ??= new ViewerOptions();

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MetadataPageSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MetadataPageCacheSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.CursorCheckpointStride);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.CursorCheckpointLimit);

        _pageSize = options.MetadataPageSize;
        _pageCacheLimit = options.MetadataPageCacheSize;
        _checkpointStride = options.CursorCheckpointStride;
        _checkpointLimit = options.CursorCheckpointLimit;

        _checkpoints[0] = new CursorCheckpoint(null, NextSequence());
    }

    public long Count => _source.Count;

    public void Dispose() => _gate.Dispose();

    public ViewerPagingDiagnostics Diagnostics =>
        new(
            Interlocked.Read(ref _pagesFetched),
            _pages.Count,
            _checkpoints.Count);

    public async ValueTask<ViewerAsset> GetAssetAsync(
        long index,
        CancellationToken cancellationToken = default)
    {
        if ((ulong)index >= (ulong)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var pageIndex = index / _pageSize;
        var itemOffset = checked((int)(index % _pageSize));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var page = await GetPageLockedAsync(pageIndex, cancellationToken).ConfigureAwait(false);
            if ((uint)itemOffset >= (uint)page.Items.Count)
            {
                throw new InvalidOperationException(
                    $"Page {pageIndex} did not contain item offset {itemOffset}.");
            }

            page.LastAccess = NextSequence();
            return page.Items[itemOffset];
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CachedPage> GetPageLockedAsync(
        long pageIndex,
        CancellationToken cancellationToken)
    {
        if (_pages.TryGetValue(pageIndex, out var cached))
        {
            return cached;
        }

        var checkpointPage = 0L;
        ViewerPageCursor? cursor = null;
        foreach (var pair in _checkpoints)
        {
            if (pair.Key <= pageIndex && pair.Key >= checkpointPage)
            {
                checkpointPage = pair.Key;
                cursor = pair.Value.Cursor;
            }
        }

        _checkpoints[checkpointPage] = new CursorCheckpoint(cursor, NextSequence());

        CachedPage? result = null;
        for (var current = checkpointPage; current <= pageIndex; current++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_pages.TryGetValue(current, out var pageHit))
            {
                result = pageHit;
                cursor = pageHit.NextCursor;
                continue;
            }

            var fetched = await _source.GetPageAsync(
                _pageSize,
                cursor,
                cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _pagesFetched);

            if (fetched.Items.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Cursor page source ended before logical page {current}.");
            }

            result = new CachedPage(
                fetched.Items,
                fetched.NextCursor,
                NextSequence());
            _pages[current] = result;
            cursor = fetched.NextCursor;

            var nextPage = current + 1;
            if (cursor is not null
                && (nextPage % _checkpointStride == 0 || nextPage == pageIndex))
            {
                _checkpoints[nextPage] = new CursorCheckpoint(cursor, NextSequence());
                TrimCheckpointsLocked();
            }

            TrimPagesLocked();
        }

        return result
            ?? throw new InvalidOperationException($"Unable to resolve page {pageIndex}.");
    }

    private void TrimPagesLocked()
    {
        while (_pages.Count > _pageCacheLimit)
        {
            var victim = _pages.MinBy(static pair => pair.Value.LastAccess);
            _pages.Remove(victim.Key);
        }
    }

    private void TrimCheckpointsLocked()
    {
        while (_checkpoints.Count > _checkpointLimit)
        {
            var victim = _checkpoints
                .Where(static pair => pair.Key != 0)
                .MinBy(static pair => pair.Value.LastAccess);

            _checkpoints.Remove(victim.Key);
        }
    }

    private long NextSequence() => ++_accessSequence;

    private sealed class CachedPage(
        IReadOnlyList<ViewerAsset> items,
        ViewerPageCursor? nextCursor,
        long lastAccess)
    {
        public IReadOnlyList<ViewerAsset> Items { get; } = items;

        public ViewerPageCursor? NextCursor { get; } = nextCursor;

        public long LastAccess { get; set; } = lastAccess;
    }

    private sealed record CursorCheckpoint(
        ViewerPageCursor? Cursor,
        long LastAccess);
}
