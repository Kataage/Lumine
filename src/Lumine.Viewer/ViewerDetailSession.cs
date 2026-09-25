namespace Lumine.Viewer;

public sealed class ViewerDetailSession : IAsyncDisposable
{
    private readonly IViewerAssetProvider _assets;
    private readonly IViewerDetailProvider _provider;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _originalAdmission = new(1, 1);
    private TaskCompletionSource _selectionOperationsDrained =
        NewCompletedSignal();
    private readonly TaskCompletionSource _disposeCompletion =
        NewSignal();
    private CancellationTokenSource? _selectionLifetimeCancellation;
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
    private int _activeSelectionOperations;
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

        cancellationToken.ThrowIfCancellationRequested();

        CancellationTokenSource selectionLifetime;
        CancellationTokenSource operation;
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

            CancelSelectionLifetimeLocked();
            _originalLoadTask = null;
            ReleaseImagesLocked();

            // The selected asset outlives this SelectAsync call. The caller
            // token owns only this operation; later cancellation must not
            // poison EnsureOriginalAsync for the active selection.
            selectionLifetime =
                CancellationTokenSource.CreateLinkedTokenSource(
                    _shutdown.Token);
            operation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    selectionLifetime.Token,
                    cancellationToken);
            RegisterSelectionOperationLocked();
            _selectionLifetimeCancellation = selectionLifetime;
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

        try
        {
            PublishState();
            PublishSelectedIndexChanged(index);

            using (operation)
            {
            var callerCancelledAtCommit = false;

            try
            {
                var asset = await _assets.GetAssetAsync(
                    index,
                    operation.Token).ConfigureAwait(false);
                var preview = await _provider.RequestPreviewAsync(
                    asset,
                    operation.Token).ConfigureAwait(false);
                var enrichedAsset = EnrichAsset(
                    asset,
                    preview.SourceMetadata);
                var lease = await PreviewBitmapCache.AcquireAsync(
                    preview.CachePath,
                    operation.Token).ConfigureAwait(false);

                var publishPreview = false;

                lock (_gate)
                {
                    if (!IsSelectionIdentityCurrentLocked(
                            version,
                            selectionLifetime))
                    {
                        lease.Dispose();
                        return;
                    }

                    if (operation.IsCancellationRequested)
                    {
                        lease.Dispose();

                        if (cancellationToken.IsCancellationRequested)
                        {
                            _snapshot = _snapshot with
                            {
                                State = ViewerDetailLoadState.Error,
                                ErrorMessage = "Selection cancelled."
                            };
                            callerCancelledAtCommit = true;
                        }
                    }
                    else
                    {
                        _previewLease = lease;
                        _snapshot = new ViewerDetailSnapshot(
                            index,
                            enrichedAsset,
                            CreateDetailMetadata(enrichedAsset),
                            ViewerDetailLoadState.PreviewReady,
                            lease.Bitmap,
                            false,
                            null,
                            version);
                        publishPreview = true;
                    }
                }

                if (callerCancelledAtCommit || publishPreview)
                {
                    PublishState();
                }
            }
            catch (OperationCanceledException)
                when (operation.IsCancellationRequested)
            {
                var callerCancelled =
                    cancellationToken.IsCancellationRequested;
                var publishCancelled = false;

                lock (_gate)
                {
                    if (callerCancelled
                        && IsSelectionIdentityCurrentLocked(
                            version,
                            selectionLifetime))
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
                    throw new OperationCanceledException(
                        cancellationToken);
                }
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    if (!IsCurrentLocked(version, selectionLifetime))
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

                if (callerCancelledAtCommit)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }
        finally
        {
            CompleteSelectionOperation();
        }
    }

    public async Task EnsureOriginalAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
            var selectionLifetime = _selectionLifetimeCancellation
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
                    selectionLifetime,
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
        CancellationTokenSource selectionLifetime,
        long version)
    {
        var selectionToken = selectionLifetime.Token;
        var admitted = false;

        try
        {
            await _originalAdmission.WaitAsync(
                selectionToken).ConfigureAwait(false);
            admitted = true;

            await _previousOriginalDisposal.WaitAsync(
                selectionToken).ConfigureAwait(false);

            var original = await _provider.LoadOriginalAsync(
                asset,
                Options.OriginalDecodedByteLimit,
                selectionToken).ConfigureAwait(false);

            Task? staleDisposal = null;
            var publish = false;

            lock (_gate)
            {
                if (!IsCurrentLocked(version, selectionLifetime))
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
            when (selectionLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (!IsCurrentLocked(version, selectionLifetime))
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

            CancelSelectionLifetimeLocked();
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
        CancellationTokenSource? selectionLifetime = null;
        Task? originalLoad = null;
        Task originalDisposal = Task.CompletedTask;
        Task selectionOperationDrain = Task.CompletedTask;
        var ownsShutdown = false;

        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                ownsShutdown = true;
                selectionLifetime = _selectionLifetimeCancellation;
                _selectionLifetimeCancellation = null;
                originalLoad = _originalLoadTask;
                selectionOperationDrain =
                    _selectionOperationsDrained.Task;
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
        }

        if (!ownsShutdown)
        {
            await _disposeCompletion.Task
                .ConfigureAwait(false);
            return;
        }

        try
        {
            CancelSourceNoThrow(_shutdown);
            if (selectionLifetime is not null)
            {
                CancelSourceNoThrow(selectionLifetime);
            }

            // Begin cache shutdown on the UI thread so resident Avalonia
            // bitmaps are released on the same thread as the visual owner.
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => PreviewBitmapCache.Dispose());

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
                    // State/error propagation is irrelevant after disposal;
                    // the important invariant is that native decode stopped.
                }
            }

            await selectionOperationDrain
                .ConfigureAwait(false);

            await PreviewBitmapCache.DisposeAsync()
                .ConfigureAwait(false);

            await _originalAdmission.WaitAsync()
                .ConfigureAwait(false);
            _originalAdmission.Release();

            try
            {
                await originalDisposal.ConfigureAwait(false);
            }
            catch
            {
                // Platform bitmap disposal is best-effort during teardown.
            }

            selectionLifetime?.Dispose();
            _originalAdmission.Dispose();
            _shutdown.Dispose();
            _disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            _disposeCompletion.TrySetException(exception);
            throw;
        }
    }

    private void RegisterSelectionOperationLocked()
    {
        if (_activeSelectionOperations == 0)
        {
            _selectionOperationsDrained = NewSignal();
        }

        _activeSelectionOperations++;
    }

    private void CompleteSelectionOperation()
    {
        TaskCompletionSource? drained = null;

        lock (_gate)
        {
            _activeSelectionOperations--;
            if (_activeSelectionOperations == 0)
            {
                drained = _selectionOperationsDrained;
            }
        }

        drained?.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource NewCompletedSignal()
    {
        var signal = NewSignal();
        signal.TrySetResult();
        return signal;
    }

    private void CancelSelectionLifetimeLocked()
    {
        var previous = _selectionLifetimeCancellation;
        _selectionLifetimeCancellation = null;

        if (previous is null)
        {
            return;
        }

        CancelSourceNoThrow(previous);
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
        CancellationTokenSource selectionLifetime) =>
        IsSelectionIdentityCurrentLocked(version, selectionLifetime)
        && !selectionLifetime.IsCancellationRequested;

    private bool IsSelectionIdentityCurrentLocked(
        long version,
        CancellationTokenSource selectionLifetime) =>
        !_disposed
        && _version == version
        && ReferenceEquals(_selectionLifetimeCancellation, selectionLifetime);

    private void PublishState()
    {
        ViewerDetailSnapshot snapshot;
        lock (_gate)
        {
            snapshot = _snapshot;
        }

        InvokeObserversSafely(
            StateChanged,
            snapshot);
    }

    private void PublishSelectedIndexChanged(long index) =>
        InvokeObserversSafely(
            SelectedIndexChanged,
            index);

    private void InvokeObserversSafely<T>(
        EventHandler<T>? handlers,
        T args)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<T> handler
                 in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // UI/app lifecycle must not be corrupted by an observer.
                // #293 owns the later diagnostic logging policy.
            }
        }
    }

    private static ViewerAsset EnrichAsset(
        ViewerAsset asset,
        ViewerSourceTechnicalMetadata? metadata)
    {
        if (metadata is null)
        {
            return asset;
        }

        return asset with
        {
            Width = metadata.Width,
            Height = metadata.Height,
            RawWidth = metadata.RawWidth,
            RawHeight = metadata.RawHeight,
            HasAlpha = metadata.HasAlpha,
            Format = metadata.Format,
            SourceContentSha256 = metadata.ContentSha256
        };
    }

    private static ViewerDetailMetadata? CreateDetailMetadata(
        ViewerAsset asset)
    {
        var metadata = asset.PersistedSourceMetadata;
        return metadata is null
            ? null
            : new ViewerDetailMetadata(
                metadata.Width,
                metadata.Height,
                metadata.HasAlpha,
                metadata.Format,
                asset.FileSize,
                metadata.EstimatedRgbaBytes);
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
            // Lifecycle cancellation must complete even if an external
            // cancellation callback fails. #293 owns diagnostics.
        }
    }

    private void ThrowIfDisposedLocked() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
