namespace Lumine.Viewer;

public sealed class ViewerDetailSession : IAsyncDisposable
{
    private readonly IViewerAssetProvider _assets;
    private readonly IViewerDetailProvider _provider;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _selectionCancellation;
    private DecodedBitmapLease? _previewLease;
    private ViewerOriginalBitmap? _original;
    private Task? _originalLoadTask;
    private long _originalLoadVersion;
    private ViewerDetailSnapshot _snapshot =
        new(
            -1,
            null,
            null,
            ViewerDetailLoadState.Empty,
            null,
            false,
            null,
            0);
    private long _version;
    private bool _disposed;

    public ViewerDetailSession(
        IViewerAssetProvider assets,
        IViewerDetailProvider provider,
        ViewerDetailOptions? options = null)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Options = options ?? new ViewerDetailOptions();

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            Options.PreviewDecodedByteLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            Options.PreviewDecodedEntryLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            Options.OriginalDecodedByteLimit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            Options.MinZoom,
            0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            Options.MaxZoom,
            Options.MinZoom);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            Options.ZoomStep,
            1);

        PreviewBitmapCache = new DecodedBitmapCache(
            Options.PreviewDecodedEntryLimit,
            Options.PreviewDecodedByteLimit);
    }

    public ViewerDetailOptions Options { get; }

    public long Count => _assets.Count;

    public DecodedBitmapCache PreviewBitmapCache { get; }

    public ViewerDetailSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public event EventHandler<ViewerDetailSnapshot>? StateChanged;

    public event EventHandler<long>? SelectedIndexChanged;

    public async Task SelectAsync(
        long index,
        CancellationToken cancellationToken = default)
    {
        if ((ulong)index >= (ulong)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        CancellationTokenSource selection;
        long version;

        lock (_gate)
        {
            ThrowIfDisposedLocked();

            if (_snapshot.SelectedIndex == index
                && _snapshot.State is not ViewerDetailLoadState.Empty
                and not ViewerDetailLoadState.Error)
            {
                return;
            }

            CancelSelectionLocked();
            _originalLoadTask = null;
            ReleaseImagesLocked();

            selection = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _shutdown.Token);
            _selectionCancellation = selection;
            version = ++_version;

            _snapshot = new ViewerDetailSnapshot(
                index,
                null,
                null,
                ViewerDetailLoadState.LoadingPreview,
                null,
                false,
                null,
                version);
        }

        PublishState();
        SelectedIndexChanged?.Invoke(this, index);

        try
        {
            var asset = await _assets.GetAssetAsync(
                index,
                selection.Token).ConfigureAwait(false);
            var metadataTask = _provider.ProbeOriginalAsync(
                asset,
                selection.Token).AsTask();
            var previewTask = _provider.RequestPreviewAsync(
                asset,
                selection.Token).AsTask();

            await Task.WhenAll(metadataTask, previewTask).ConfigureAwait(false);

            var metadata = metadataTask.Result;
            var preview = previewTask.Result;
            var lease = await PreviewBitmapCache.AcquireAsync(
                preview.CachePath,
                selection.Token).ConfigureAwait(false);

            lock (_gate)
            {
                if (!IsCurrentLocked(version, selection))
                {
                    lease.Dispose();
                    return;
                }

                _previewLease = lease;
                _snapshot = new ViewerDetailSnapshot(
                    index,
                    asset,
                    metadata,
                    ViewerDetailLoadState.PreviewReady,
                    lease.Bitmap,
                    false,
                    null,
                    version);
            }

            PublishState();
        }
        catch (OperationCanceledException)
            when (selection.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (!IsCurrentLocked(version, selection))
                {
                    return;
                }

                _snapshot = _snapshot with
                {
                    State = ViewerDetailLoadState.Error,
                    ErrorMessage = exception.Message
                };
            }

            PublishState();
        }
    }

    public async Task EnsureOriginalAsync(
        CancellationToken cancellationToken = default)
    {
        Task loadTask;
        var publishLoading = false;

        lock (_gate)
        {
            ThrowIfDisposedLocked();

            if (_snapshot.State == ViewerDetailLoadState.OriginalReady)
            {
                return;
            }

            var asset = _snapshot.Asset
                ?? throw new InvalidOperationException(
                    "Select an asset before requesting full resolution.");
            var selection = _selectionCancellation
                ?? throw new InvalidOperationException(
                    "Detail selection has no active lifetime.");
            var version = _snapshot.SelectionVersion;

            if (_originalLoadTask is { IsCompleted: false }
                && _originalLoadVersion == version)
            {
                loadTask = _originalLoadTask;
            }
            else
            {
                _snapshot = _snapshot with
                {
                    State = ViewerDetailLoadState.LoadingOriginal,
                    ErrorMessage = null
                };

                loadTask = LoadOriginalCoreAsync(
                    asset,
                    selection,
                    version);
                _originalLoadTask = loadTask;
                _originalLoadVersion = version;
                publishLoading = true;
            }
        }

        if (publishLoading)
        {
            PublishState();
        }

        await loadTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadOriginalCoreAsync(
        ViewerAsset asset,
        CancellationTokenSource selection,
        long version)
    {
        try
        {
            var original = await _provider.LoadOriginalAsync(
                asset,
                Options.OriginalDecodedByteLimit,
                selection.Token).ConfigureAwait(false);

            lock (_gate)
            {
                if (!IsCurrentLocked(version, selection))
                {
                    original.Dispose();
                    return;
                }

                _original?.Dispose();
                _original = original;
                _previewLease?.Dispose();
                _previewLease = null;

                _snapshot = _snapshot with
                {
                    Metadata = original.Metadata,
                    State = ViewerDetailLoadState.OriginalReady,
                    Bitmap = original.Bitmap,
                    IsOriginal = true,
                    ErrorMessage = null
                };
            }

            PublishState();
        }
        catch (OperationCanceledException)
            when (selection.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (!IsCurrentLocked(version, selection))
                {
                    return;
                }

                _snapshot = _snapshot with
                {
                    State = _snapshot.Bitmap is null
                        ? ViewerDetailLoadState.Error
                        : ViewerDetailLoadState.PreviewReady,
                    ErrorMessage = exception.Message
                };
            }

            PublishState();
        }
        finally
        {
            lock (_gate)
            {
                if (_originalLoadVersion == version)
                {
                    _originalLoadTask = null;
                }
            }
        }
    }

    public Task MoveAsync(
        long delta,
        CancellationToken cancellationToken = default)
    {
        var current = Snapshot.SelectedIndex;
        if (Count == 0)
        {
            return Task.CompletedTask;
        }

        var target = Math.Clamp(
            current < 0 ? 0 : current + delta,
            0,
            Count - 1);

        return SelectAsync(target, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? selection;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            selection = _selectionCancellation;
            _selectionCancellation = null;
            ReleaseImagesLocked();
        }

        _shutdown.Cancel();
        selection?.Cancel();
        selection?.Dispose();
        var previewCache = PreviewBitmapCache;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            previewCache.Dispose,
            Avalonia.Threading.DispatcherPriority.Background);

        _shutdown.Dispose();

        await Task.CompletedTask;
    }

    private void CancelSelectionLocked()
    {
        var previous = _selectionCancellation;
        _selectionCancellation = null;

        if (previous is null)
        {
            return;
        }

        previous.Cancel();
        previous.Dispose();
    }

    private void ReleaseImagesLocked()
    {
        _previewLease?.Dispose();
        _previewLease = null;

        _original?.Dispose();
        _original = null;
    }

    private bool IsCurrentLocked(
        long version,
        CancellationTokenSource selection) =>
        !_disposed
        && _version == version
        && ReferenceEquals(_selectionCancellation, selection)
        && !selection.IsCancellationRequested;

    private void PublishState()
    {
        ViewerDetailSnapshot snapshot;
        lock (_gate)
        {
            snapshot = _snapshot;
        }

        StateChanged?.Invoke(this, snapshot);
    }

    private void ThrowIfDisposedLocked() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
