namespace Lumine.Library;

public enum LibrarySyncBootstrapMode
{
    Reconcile = 1,
    UsnDelta = 2,
    ReconcileFallback = 3
}

public sealed class WindowsLibrarySyncSession : IAsyncDisposable
{
    private readonly long _libraryId;
    private readonly LibraryRepository _repository;
    private readonly LibraryChangeProcessor _processor;
    private readonly WindowsDirectoryChangeWatcher _watcher;
    private readonly string _rootPath;
    private readonly Task _watcherMonitor;
    private readonly TaskCompletionSource _disposeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeStarted;

    internal WindowsLibrarySyncSession(
        long libraryId,
        LibraryRepository repository,
        LibraryChangeProcessor processor,
        WindowsDirectoryChangeWatcher watcher,
        string rootPath,
        LibrarySyncBootstrapMode bootstrapMode,
        UsnCatchUpResult? catchUp)
    {
        _libraryId = libraryId;
        _repository = repository;
        _processor = processor;
        _watcher = watcher;
        _rootPath = rootPath;
        BootstrapMode = bootstrapMode;
        CatchUp = catchUp;
        _watcherMonitor = MonitorWatcherAsync();
    }

    public LibrarySyncBootstrapMode BootstrapMode { get; }

    public UsnCatchUpResult? CatchUp { get; }

    public LibrarySyncDiagnostics Diagnostics => _processor.Diagnostics;

    public Task Completion => _watcherMonitor;

    public Task<LibraryReconcileResult> ReconcileNowAsync(
        CancellationToken cancellationToken = default) =>
        _processor.ReconcileNowAsync(
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(
                ref _disposeStarted,
                1,
                0) != 0)
        {
            await _disposeCompletion.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            // Capture a conservative journal boundary while the watcher is still
            // active. Events after this USN may also be applied before shutdown;
            // replaying them next start is idempotent and safer than skipping a gap.
            var checkpoint = WindowsUsnJournal.Query(_rootPath);
            Exception? shutdownFailure = null;

            try
            {
                await _watcher.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                shutdownFailure = exception;
            }

            try
            {
                await _processor.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                shutdownFailure ??= exception;
            }

            try
            {
                await _watcherMonitor.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // A monitor failure is itself a shutdown failure. Continue
                // through checkpoint persistence before surfacing it so the
                // next startup cannot mistake this session for clean.
                shutdownFailure ??= exception;
            }

            var state = await _repository.GetOrCreateSyncStateAsync(
                _libraryId).ConfigureAwait(false);

            await _repository.UpdateUsnCheckpointAsync(
                _libraryId,
                checkpoint.Available ? checkpoint.JournalId : null,
                checkpoint.Available ? checkpoint.NextUsn : null,
                state.ReconcileRequired || shutdownFailure is not null,
                DateTimeOffset.UtcNow,
                shutdownFailure is not null
                    ? $"Filesystem synchronization shutdown failed: {shutdownFailure.GetType().Name}."
                    : checkpoint.Available
                        ? state.LastError
                        : checkpoint.UnavailableReason).ConfigureAwait(false);

            if (shutdownFailure is not null)
            {
                throw shutdownFailure;
            }

            _disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            _disposeCompletion.TrySetException(exception);
            throw;
        }
    }

    private async Task MonitorWatcherAsync()
    {
        try
        {
            var completed = await Task.WhenAny(
                _watcher.Completion,
                _processor.Completion).ConfigureAwait(false);

            await completed.ConfigureAwait(false);

            if (Volatile.Read(ref _disposeStarted) == 0)
            {
                throw new InvalidOperationException(
                    "Filesystem synchronization worker stopped unexpectedly.");
            }
        }
        catch (OperationCanceledException) when (Volatile.Read(ref _disposeStarted) != 0)
        {
        }
        catch (Exception exception)
        {
            await _repository.MarkReconcileRequiredAsync(
                _libraryId,
                $"Filesystem synchronization failed: {exception.GetType().Name}: {exception.Message}")
                .ConfigureAwait(false);
            throw;
        }
    }
}

public sealed class WindowsLibrarySyncService
{
    private readonly LibraryRepository _repository;
    private readonly LibraryReconciler _reconciler;

    public WindowsLibrarySyncService(LibraryDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _repository = new LibraryRepository(database);
        _reconciler = new LibraryReconciler(_repository);
    }

    public async Task<WindowsLibrarySyncSession> StartAsync(
        long libraryId,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Incremental Windows filesystem synchronization is Windows-only.");
        }

        var library = await _repository.GetLibraryAsync(
            libraryId,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Library {libraryId} does not exist.");

        WindowsFilesystemSemantics.RequireCaseInsensitiveDirectory(library.RootPath);

        var state = await _repository.GetOrCreateSyncStateAsync(
            libraryId,
            cancellationToken).ConfigureAwait(false);

        var processor = new LibraryChangeProcessor(
            libraryId,
            library,
            _repository,
            _reconciler);
        var watcher = new WindowsDirectoryChangeWatcher(library.RootPath);
        var router = new BootstrapChangeRouter();

        try
        {
            // Start watching first. Bootstrap work can take hundreds of
            // milliseconds; changes that occur during it are buffered and
            // replayed after the baseline is established.
            await watcher.StartAsync(
                router.Publish,
                cancellationToken).ConfigureAwait(false);

            var journalAtStart = WindowsUsnJournal.Query(library.RootPath);
            UsnCatchUpResult? catchUp = null;
            LibrarySyncBootstrapMode bootstrapMode;

            var canUseCheckpoint =
                !state.ReconcileRequired
                && journalAtStart.Available
                && state.UsnJournalId is not null
                && state.NextUsn is not null;

            if (canUseCheckpoint)
            {
                catchUp = WindowsUsnJournal.ReadChanges(
                    library.RootPath,
                    state.UsnJournalId!,
                    state.NextUsn!.Value,
                    journalAtStart.NextUsn,
                    cancellationToken);

                if (catchUp.RequiresReconcile)
                {
                    await RequireCompleteReconcileAsync(
                        libraryId,
                        cancellationToken).ConfigureAwait(false);
                    bootstrapMode = LibrarySyncBootstrapMode.ReconcileFallback;
                }
                else
                {
                    await processor.ApplyBootstrapChangesAsync(
                        catchUp.Changes,
                        cancellationToken).ConfigureAwait(false);
                    bootstrapMode = LibrarySyncBootstrapMode.UsnDelta;
                }
            }
            else
            {
                await RequireCompleteReconcileAsync(
                    libraryId,
                    cancellationToken).ConfigureAwait(false);

                bootstrapMode = journalAtStart.Available
                    ? LibrarySyncBootstrapMode.Reconcile
                    : LibrarySyncBootstrapMode.ReconcileFallback;
            }

            await _repository.UpdateUsnCheckpointAsync(
                libraryId,
                journalAtStart.Available ? journalAtStart.JournalId : null,
                journalAtStart.Available ? journalAtStart.NextUsn : null,
                false,
                null,
                journalAtStart.Available
                    ? null
                    : journalAtStart.UnavailableReason,
                cancellationToken).ConfigureAwait(false);

            // Preserve watcher ordering: buffered events are queued before the
            // router begins forwarding newly arriving events directly.
            _ = router.Activate(processor);

            return new WindowsLibrarySyncSession(
                libraryId,
                _repository,
                processor,
                watcher,
                library.RootPath,
                bootstrapMode,
                catchUp);
        }
        catch
        {
            await watcher.DisposeAsync().ConfigureAwait(false);
            await processor.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task RequireCompleteReconcileAsync(
        long libraryId,
        CancellationToken cancellationToken)
    {
        var reconcile = await _reconciler.ReconcileAsync(
            libraryId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!reconcile.Completed)
        {
            throw new InvalidOperationException(
                "Library reconciliation was incomplete; incremental watcher was not started.");
        }
    }
}
