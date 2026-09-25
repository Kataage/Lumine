namespace Lumine.Viewer;

public sealed class ViewerDetailSession : IAsyncDisposable
{
    private readonly IViewerAssetProvider _assets;
    private readonly IViewerDetailProvider _provider;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _originalAdmission = new(1, 1);
    private CancellationTokenSource? _selectionCancellation;
    private DecodedBitmapLease? _previewLease;
    private ViewerOriginalBitmap? _original;
    private Task? _originalLoadTask;
    private long _originalLoadVersion;
    private Task _previousOriginalDisposal = Task.CompletedTask;
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
            var preview = await _provider.RequestPreviewAsync(
                asset,
                selection.Token).ConfigureAwait(false);
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
                    null,
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
            var callerCancelled = cancellationToken.IsCancellationRequested;
            var publishCancelled = false;

            lock (_gate)
            {
                if (callerCancelled
                    && IsSelectionIdentityCurrentLocked(version, selection))
                {
                    _snapshot = _snapshot with
                    {
                        State = ViewerDetailLoadState.Error,
                        ErrorMessage = "Selection cancelled."
                    };
                    publishCancelled = true;
                }
            }

            if (publishCancelled)
            {
                PublishState();
            }

            if (callerCancelled)
            {
                throw new OperationCanceledException(cancellationToken);
            }
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
        var admitted = false;

        try
        {
            await _originalAdmission.WaitAsync(
                selection.Token).ConfigureAwait(false);
            admitted = true;

            await _previousOriginalDisposal.WaitAsync(
                selection.Token).ConfigureAwait(false);

            var original = await _provider.LoadOriginalAsync(
                asset,
                Options.OriginalDecodedByteLimit,
                selection.Token).ConfigureAwait(false);

            Task? staleDisposal = null;
            var publish = false;

            lock (_gate)
            {
                if (!IsCurrentLocked(version, selection))
                {
                    staleDisposal = original.BeginDispose();
                }
                else
                {
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
                    publish = true;
                }
            }

            if (staleDisposal is not null)
            {
                await staleDisposal.ConfigureAwait(false);
                return;
            }

            if (publish)
            {
                PublishState();
            }
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

            if (admitted)
            {
                _originalAdmission.Release();
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

    public void Clear()
    {
        lock (_gate)
        {
            ThrowIfDisposedLocked();

            CancelSelectionLocked();
            _originalLoadTask = null;
            ReleaseImagesLocked();

            var version = ++_version;
            _snapshot = new ViewerDetailSnapshot(
                -1,
                null,
                null,
                ViewerDetailLoadState.Empty,
                null,
                false,
                null,
                version);
        }

        PublishState();
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? selection;
        Task? originalLoad;
        Task originalDisposal;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            selection = _selectionCancellation;
            _selectionCancellation = null;
            originalLoad = _originalLoadTask;
            ReleaseImagesLocked();
            originalDisposal = _previousOriginalDisposal;

            var version = ++_version;
            _snapshot = new ViewerDetailSnapshot(
                -1,
                null,
                null,
                ViewerDetailLoadState.Empty,
                null,
                false,
                null,
                version);
        }

        _shutdown.Cancel();
        selection?.Cancel();

        if (originalLoad is not null)
        {
            try
            {
                await originalLoad.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // State/error propagation is irrelevant after disposal; the
                // important invariant is that the native decode has stopped.
            }
        }

        await _originalAdmission.WaitAsync().ConfigureAwait(false);
        _originalAdmission.Release();

        try
        {
            await originalDisposal.ConfigureAwait(false);
        }
        catch
        {
            // Platform bitmap disposal is best-effort during teardown.
        }

        selection?.Dispose();

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () => PreviewBitmapCache.Dispose());

        _originalAdmission.Dispose();
        _shutdown.Dispose();
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

        if (_original is not null)
        {
            _previousOriginalDisposal = _original.BeginDispose();
            _original = null;
        }
    }

    private bool IsCurrentLocked(
        long version,
        CancellationTokenSource selection) =>
        IsSelectionIdentityCurrentLocked(version, selection)
        && !selection.IsCancellationRequested;

    private bool IsSelectionIdentityCurrentLocked(
        long version,
        CancellationTokenSource selection) =>
        !_disposed
        && _version == version
        && ReferenceEquals(_selectionCancellation, selection);

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
