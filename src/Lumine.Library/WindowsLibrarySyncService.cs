namespace Lumine.Library;

public sealed class WindowsLibrarySyncSession : IAsyncDisposable
{
    private readonly long _libraryId;
    private readonly LibraryRepository _repository;
    private readonly LibraryChangeProcessor _processor;
    private readonly WindowsDirectoryChangeWatcher _watcher;
    private bool _disposed;

    internal WindowsLibrarySyncSession(
        long libraryId,
        LibraryRepository repository,
        LibraryChangeProcessor processor,
        WindowsDirectoryChangeWatcher watcher)
    {
        _libraryId = libraryId;
        _repository = repository;
        _processor = processor;
        _watcher = watcher;
    }

    public LibrarySyncDiagnostics Diagnostics => _processor.Diagnostics;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await _watcher.DisposeAsync().ConfigureAwait(false);
        await _processor.DisposeAsync().ConfigureAwait(false);

        var state = await _repository.GetOrCreateSyncStateAsync(
            _libraryId).ConfigureAwait(false);

        await _repository.UpdateUsnCheckpointAsync(
            _libraryId,
            state.UsnJournalId,
            state.NextUsn,
            state.ReconcileRequired,
            DateTimeOffset.UtcNow,
            state.LastError).ConfigureAwait(false);
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

        var state = await _repository.GetOrCreateSyncStateAsync(
            libraryId,
            cancellationToken).ConfigureAwait(false);

        if (state.ReconcileRequired)
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

        var processor = new LibraryChangeProcessor(
            libraryId,
            library,
            _repository,
            _reconciler);
        var watcher = new WindowsDirectoryChangeWatcher(library.RootPath);

        try
        {
            await watcher.StartAsync(
                processor.Publish,
                cancellationToken).ConfigureAwait(false);

            return new WindowsLibrarySyncSession(
                libraryId,
                _repository,
                processor,
                watcher);
        }
        catch
        {
            await watcher.DisposeAsync().ConfigureAwait(false);
            await processor.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
