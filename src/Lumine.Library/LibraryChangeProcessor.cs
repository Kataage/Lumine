using System.Threading.Channels;

namespace Lumine.Library;

public sealed class LibraryChangeProcessor : IAsyncDisposable
{
    private const int QueueCapacity = 4096;
    private const int CoalesceCapacity = 4096;
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);

    private readonly long _libraryId;
    private readonly LibraryInfo _library;
    private readonly LibraryRepository _repository;
    private readonly LibraryReconciler _reconciler;
    private readonly Channel<DirectoryChange> _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _processorTask;

    private long _eventsObserved;
    private long _eventsApplied;
    private long _eventsCoalesced;
    private long _overflows;
    private long _reconciliations;
    private long _reconcileFailures;
    private long _renameOperations;
    private long _deletes;
    private long _upserts;
    private long _lastLatencyBits;
    private long _maxLatencyBits;
    private int _queueDepth;
    private int _overflowRequested;

    public LibraryChangeProcessor(
        long libraryId,
        LibraryInfo library,
        LibraryRepository repository,
        LibraryReconciler reconciler)
    {
        _libraryId = libraryId;
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));

        _queue = Channel.CreateBounded<DirectoryChange>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        _processorTask = ProcessLoopAsync(_shutdown.Token);
    }

    public Task Completion => _processorTask;

    public LibrarySyncDiagnostics Diagnostics =>
        new(
            Interlocked.Read(ref _eventsObserved),
            Interlocked.Read(ref _eventsApplied),
            Interlocked.Read(ref _eventsCoalesced),
            Interlocked.Read(ref _overflows),
            Interlocked.Read(ref _reconciliations),
            Interlocked.Read(ref _reconcileFailures),
            Interlocked.Read(ref _renameOperations),
            Interlocked.Read(ref _deletes),
            Interlocked.Read(ref _upserts),
            BitConverter.Int64BitsToDouble(
                Interlocked.Read(ref _lastLatencyBits)),
            BitConverter.Int64BitsToDouble(
                Interlocked.Read(ref _maxLatencyBits)),
            Volatile.Read(ref _queueDepth));

    public void Publish(IReadOnlyList<DirectoryChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        foreach (var change in changes)
        {
            Interlocked.Increment(ref _eventsObserved);

            if (change.Kind == DirectoryChangeKind.Overflow)
            {
                Interlocked.Exchange(ref _overflowRequested, 1);
                Interlocked.Increment(ref _overflows);

                if (_queue.Writer.TryWrite(change))
                {
                    Interlocked.Increment(ref _queueDepth);
                }

                continue;
            }

            if (_queue.Writer.TryWrite(change))
            {
                Interlocked.Increment(ref _queueDepth);
            }
            else
            {
                Interlocked.Exchange(ref _overflowRequested, 1);
                Interlocked.Increment(ref _overflows);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        var forced = false;

        try
        {
            try
            {
                await _processorTask
                    .WaitAsync(ShutdownDrainTimeout)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                forced = true;
                _shutdown.Cancel();

                try
                {
                    await _processorTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (forced)
            {
                await _repository.MarkReconcileRequiredAsync(
                    _libraryId,
                    "Filesystem event queue did not drain within the shutdown budget; startup reconciliation is required.",
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
        }
    }

    internal async Task ApplyBootstrapChangesAsync(
        IReadOnlyList<DirectoryChange> changes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var coalesced = new Dictionary<string, DirectoryChange>(
            Math.Min(changes.Count, CoalesceCapacity),
            StringComparer.OrdinalIgnoreCase);

        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (change.Kind == DirectoryChangeKind.Overflow)
            {
                await ReconcileAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            Coalesce(coalesced, change);

            if (coalesced.Count >= CoalesceCapacity)
            {
                await ReconcileAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        foreach (var change in coalesced.Values)
        {
            if (await ApplyWithRecoveryAsync(change, cancellationToken).ConfigureAwait(false))
            {
                await ReconcileAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
        }
    }

    private async Task ProcessLoopAsync(CancellationToken cancellationToken)
    {
        var coalesced = new Dictionary<string, DirectoryChange>(
            CoalesceCapacity,
            StringComparer.OrdinalIgnoreCase);

        while (await _queue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Interlocked.Exchange(ref _overflowRequested, 0) != 0)
            {
                // Discard events known to precede the overflow, then reconcile.
                // Events arriving while reconciliation runs remain queued and
                // are processed afterward.
                DrainQueue();
                await ReconcileAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            coalesced.Clear();

            while (_queue.Reader.TryRead(out var change))
            {
                Interlocked.Decrement(ref _queueDepth);
                Coalesce(coalesced, change);

                if (coalesced.Count >= CoalesceCapacity)
                {
                    Interlocked.Increment(ref _overflows);
                    coalesced.Clear();
                    DrainQueue();
                    await ReconcileAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }
            }

            if (coalesced.Count == 0)
            {
                continue;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(75),
                cancellationToken).ConfigureAwait(false);

            while (_queue.Reader.TryRead(out var change))
            {
                Interlocked.Decrement(ref _queueDepth);
                Coalesce(coalesced, change);

                if (coalesced.Count >= CoalesceCapacity)
                {
                    Interlocked.Increment(ref _overflows);
                    coalesced.Clear();
                    DrainQueue();
                    await ReconcileAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }
            }

            var requiresReconcile = await ApplyBatchAsync(
                coalesced.Values,
                cancellationToken).ConfigureAwait(false);

            if (requiresReconcile)
            {
                DrainQueue();
                await ReconcileAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> ApplyBatchAsync(
        IEnumerable<DirectoryChange> changes,
        CancellationToken cancellationToken)
    {
        var upserts = new List<(AssetUpsert Asset, DirectoryChange Change)>();
        var deletes = new List<(string Path, DirectoryChange Change)>();

        async Task FlushPendingAsync()
        {
            if (upserts.Count > 0)
            {
                try
                {
                    await _repository.UpsertAssetsAsync(
                        _libraryId,
                        upserts.Select(static item => item.Asset).ToArray(),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is IOException
                    or UnauthorizedAccessException
                    or System.Security.SecurityException)
                {
                    await _repository.MarkReconcileRequiredAsync(
                        _libraryId,
                        $"Batched filesystem upsert failed: {exception.GetType().Name}.",
                        cancellationToken).ConfigureAwait(false);
                    throw;
                }

                Interlocked.Add(ref _upserts, upserts.Count);
                foreach (var item in upserts)
                {
                    RecordApplied(item.Change);
                }

                upserts.Clear();
            }

            if (deletes.Count > 0)
            {
                var removed = await _repository.RemoveAssetsAsync(
                    _libraryId,
                    deletes.Select(static item => item.Path).ToArray(),
                    cancellationToken).ConfigureAwait(false);

                Interlocked.Add(ref _deletes, removed);
                foreach (var item in deletes)
                {
                    RecordApplied(item.Change);
                }

                deletes.Clear();
            }
        }

        foreach (var change in changes.OrderBy(static item => item.ObservedAtUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (change.Kind == DirectoryChangeKind.Overflow)
            {
                await FlushPendingAsync().ConfigureAwait(false);
                return true;
            }

            var fullPath = Path.Combine(
                _library.RootPath,
                change.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            switch (change.Kind)
            {
                case DirectoryChangeKind.Added:
                case DirectoryChangeKind.Modified:
                    if (Directory.Exists(fullPath))
                    {
                        if (change.Kind == DirectoryChangeKind.Added)
                        {
                            await FlushPendingAsync().ConfigureAwait(false);
                            return true;
                        }

                        continue;
                    }

                    if (LibraryFileTypes.IsSupportedPath(change.RelativePath)
                        && File.Exists(fullPath))
                    {
                        try
                        {
                            upserts.Add((
                                CreateUpsert(
                                    change.RelativePath,
                                    fullPath,
                                    forceSourceRevision:
                                        change.Kind is DirectoryChangeKind.Added
                                        or DirectoryChangeKind.Modified),
                                change));
                        }
                        catch (Exception exception) when (
                            exception is IOException
                            or UnauthorizedAccessException
                            or System.Security.SecurityException)
                        {
                            await FlushPendingAsync().ConfigureAwait(false);

                            if (await ApplyWithRecoveryAsync(
                                    change,
                                    cancellationToken).ConfigureAwait(false))
                            {
                                return true;
                            }
                        }
                    }

                    break;

                case DirectoryChangeKind.Removed:
                    if (await _repository.HasTrackedFolderAtOrBelowAsync(
                            _libraryId,
                            change.RelativePath,
                            cancellationToken).ConfigureAwait(false))
                    {
                        await FlushPendingAsync().ConfigureAwait(false);
                        return true;
                    }

                    if (LibraryFileTypes.IsSupportedPath(change.RelativePath))
                    {
                        deletes.Add((change.RelativePath, change));
                    }

                    break;

                case DirectoryChangeKind.Renamed:
                    // Rename is an ordering barrier. Flushing file operations
                    // on each side preserves final-state semantics such as
                    // "delete destination, then rename source into it".
                    await FlushPendingAsync().ConfigureAwait(false);

                    if (await ApplyWithRecoveryAsync(
                            change,
                            cancellationToken).ConfigureAwait(false))
                    {
                        return true;
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(changes),
                        change.Kind,
                        "Unknown filesystem change kind.");
            }
        }

        await FlushPendingAsync().ConfigureAwait(false);
        return false;
    }

    private async Task<bool> ApplyWithRecoveryAsync(
        DirectoryChange change,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await ApplyAsync(change, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
            {
                if (attempt == 2)
                {
                    await _repository.MarkReconcileRequiredAsync(
                        _libraryId,
                        $"Incremental filesystem apply failed for '{change.RelativePath}': {exception.GetType().Name}.",
                        cancellationToken).ConfigureAwait(false);
                    return true;
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(50 * (attempt + 1)),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return true;
    }

    private async Task<bool> ApplyAsync(
        DirectoryChange change,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(
            _library.RootPath,
            change.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        switch (change.Kind)
        {
            case DirectoryChangeKind.Added:
                if (Directory.Exists(fullPath))
                {
                    return true;
                }

                if (LibraryFileTypes.IsSupportedPath(change.RelativePath)
                    && File.Exists(fullPath))
                {
                    await UpsertPathAsync(
                        change.RelativePath,
                        fullPath,
                        forceSourceRevision: true,
                        cancellationToken).ConfigureAwait(false);
                }

                break;

            case DirectoryChangeKind.Modified:
                if (Directory.Exists(fullPath))
                {
                    break;
                }

                if (LibraryFileTypes.IsSupportedPath(change.RelativePath)
                    && File.Exists(fullPath))
                {
                    await UpsertPathAsync(
                        change.RelativePath,
                        fullPath,
                        forceSourceRevision: true,
                        cancellationToken).ConfigureAwait(false);
                }

                break;

            case DirectoryChangeKind.Removed:
                // A directory may legally end in an image-looking extension.
                // Check structural state before interpreting a vanished path
                // as an individual image file.
                if (await _repository.HasTrackedFolderAtOrBelowAsync(
                        _libraryId,
                        change.RelativePath,
                        cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }

                if (LibraryFileTypes.IsSupportedPath(change.RelativePath))
                {
                    if (await _repository.RemoveAssetAsync(
                            _libraryId,
                            change.RelativePath,
                            cancellationToken).ConfigureAwait(false))
                    {
                        Interlocked.Increment(ref _deletes);
                    }
                }

                break;

            case DirectoryChangeKind.Renamed:
                return await ApplyRenameAsync(
                    change,
                    fullPath,
                    cancellationToken).ConfigureAwait(false);

            case DirectoryChangeKind.Overflow:
                return true;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(change),
                    change.Kind,
                    "Unknown filesystem change kind.");
        }

        RecordApplied(change);
        return false;
    }

    private async Task<bool> ApplyRenameAsync(
        DirectoryChange change,
        string newFullPath,
        CancellationToken cancellationToken)
    {
        var oldPath = change.OldRelativePath;
        if (string.IsNullOrWhiteSpace(oldPath))
        {
            return true;
        }

        if (Directory.Exists(newFullPath))
        {
            return true;
        }

        var oldSupported = LibraryFileTypes.IsSupportedPath(oldPath);
        var newSupported = LibraryFileTypes.IsSupportedPath(change.RelativePath);

        if (newSupported && File.Exists(newFullPath))
        {
            var replacement = CreateUpsert(
                change.RelativePath,
                newFullPath);

            if (oldSupported)
            {
                var renamed = await _repository.RenameAssetAsync(
                    _libraryId,
                    oldPath,
                    replacement,
                    cancellationToken).ConfigureAwait(false);

                if (!renamed)
                {
                    await _repository.UpsertAssetsAsync(
                        _libraryId,
                        [replacement],
                        cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref _upserts);
                }
                else
                {
                    Interlocked.Increment(ref _renameOperations);
                }
            }
            else
            {
                await _repository.UpsertAssetsAsync(
                    _libraryId,
                    [replacement],
                    cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _upserts);
            }

            RecordApplied(change);
            return false;
        }

        if (oldSupported)
        {
            if (await _repository.RemoveAssetAsync(
                    _libraryId,
                    oldPath,
                    cancellationToken).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _deletes);
            }

            RecordApplied(change);
            return false;
        }

        if (await _repository.HasTrackedFolderAtOrBelowAsync(
                _libraryId,
                oldPath,
                cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        RecordApplied(change);
        return false;
    }

    private async Task UpsertPathAsync(
        string relativePath,
        string fullPath,
        bool forceSourceRevision,
        CancellationToken cancellationToken)
    {
        var upsert = CreateUpsert(
            relativePath,
            fullPath,
            forceSourceRevision);
        await _repository.UpsertAssetsAsync(
            _libraryId,
            [upsert],
            cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _upserts);
    }

    private static AssetUpsert CreateUpsert(
        string relativePath,
        string fullPath,
        bool forceSourceRevision = false)
    {
        var info = new FileInfo(fullPath);
        return new AssetUpsert(
            relativePath,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc),
            Format: LibraryFileTypes.GetFormat(relativePath),
            ForceSourceRevision: forceSourceRevision);
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _reconciliations);

        try
        {
            var result = await _reconciler.ReconcileAsync(
                _libraryId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!result.Completed)
            {
                Interlocked.Increment(ref _reconcileFailures);
                throw new InvalidOperationException(
                    "Filesystem reconciliation was incomplete; incremental synchronization cannot safely continue.");
            }
        }
        catch
        {
            Interlocked.Increment(ref _reconcileFailures);
            await _repository.MarkReconcileRequiredAsync(
                _libraryId,
                "Reconciliation failed after watcher overflow or ambiguous directory change.",
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private void DrainQueue()
    {
        while (_queue.Reader.TryRead(out _))
        {
            Interlocked.Decrement(ref _queueDepth);
            Interlocked.Increment(ref _eventsCoalesced);
        }
    }

    private void RecordApplied(DirectoryChange change)
    {
        Interlocked.Increment(ref _eventsApplied);

        var latency = Math.Max(
            0,
            (DateTimeOffset.UtcNow - change.ObservedAtUtc).TotalMilliseconds);

        Interlocked.Exchange(
            ref _lastLatencyBits,
            BitConverter.DoubleToInt64Bits(latency));

        while (true)
        {
            var currentBits = Interlocked.Read(ref _maxLatencyBits);
            var current = BitConverter.Int64BitsToDouble(currentBits);
            if (latency <= current)
            {
                break;
            }

            if (Interlocked.CompareExchange(
                    ref _maxLatencyBits,
                    BitConverter.DoubleToInt64Bits(latency),
                    currentBits) == currentBits)
            {
                break;
            }
        }
    }

    private void Coalesce(
        Dictionary<string, DirectoryChange> target,
        DirectoryChange change)
    {
        var key = change.Kind == DirectoryChangeKind.Renamed
            ? $"{change.OldRelativePath}\0{change.RelativePath}"
            : change.RelativePath;

        if (target.ContainsKey(key))
        {
            Interlocked.Increment(ref _eventsCoalesced);
        }

        target[key] = change;
    }
}
