namespace Lumine.Library;

/// <summary>
/// Explicit boundary for work that may block in SQLite or synchronous filesystem APIs.
/// Product UI code should enter Library Core through this boundary rather than executing
/// low-level repository work on the UI thread.
/// </summary>
public static class LibraryBackgroundExecution
{
    public static Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        return Task.Run(
            async () => await work(cancellationToken).ConfigureAwait(false),
            cancellationToken);
    }

    public static Task RunAsync(
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        return Task.Run(
            async () => await work(cancellationToken).ConfigureAwait(false),
            cancellationToken);
    }
}

public sealed class LibraryService
{
    private readonly LibraryDatabase _database;
    private readonly LibraryRepository _repository;
    private readonly LibraryScanner _scanner;
    private readonly LibraryReconciler _reconciler;
    private readonly WindowsLibrarySyncService _syncService;

    public LibraryService(string databasePath)
    {
        _database = new LibraryDatabase(databasePath);
        _repository = new LibraryRepository(_database);
        _scanner = new LibraryScanner(_repository);
        _reconciler = new LibraryReconciler(_repository);
        _syncService = new WindowsLibrarySyncService(_database);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _database.InitializeAsync(token),
            cancellationToken);

    public Task<LibraryInfo> RegisterLibraryAsync(
        string name,
        string rootPath,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.RegisterLibraryAsync(name, rootPath, token),
            cancellationToken);

    public Task<AssetInfo?> GetAssetAsync(
        long libraryId,
        string relativePath,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.GetAssetAsync(libraryId, relativePath, token),
            cancellationToken);

    public Task<AssetPage> GetAssetPageAsync(
        long libraryId,
        int limit,
        AssetCursor? cursor = null,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.GetAssetPageAsync(libraryId, limit, cursor, token),
            cancellationToken);

    public Task<long> CountAssetsAsync(
        long libraryId,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.CountAssetsAsync(libraryId, token),
            cancellationToken);

    public Task<LibraryScanResult> ScanAsync(
        long libraryId,
        IProgress<LibraryScanProgress>? progress = null,
        int batchSize = 2048,
        CancellationToken cancellationToken = default) =>
        _scanner.ScanAsync(libraryId, progress, batchSize, cancellationToken);

    public Task<LibraryReconcileResult> ReconcileAsync(
        long libraryId,
        IProgress<LibraryScanProgress>? progress = null,
        int batchSize = 2048,
        CancellationToken cancellationToken = default) =>
        _reconciler.ReconcileAsync(
            libraryId,
            progress,
            batchSize,
            cancellationToken);

    public Task<WindowsLibrarySyncSession> StartWindowsSyncAsync(
        long libraryId,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _syncService.StartAsync(libraryId, token),
            cancellationToken);
}
