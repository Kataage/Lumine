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

    public string DatabasePath => _database.DatabasePath;

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

    public Task CheckpointAsync(
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _database.CheckpointAsync(token),
            cancellationToken);

    public Task<LibraryInfo> RegisterLibraryAsync(
        string name,
        string rootPath,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.RegisterLibraryAsync(name, rootPath, token),
            cancellationToken);

    public Task<LibraryInfo?> GetLibraryAsync(
        long libraryId,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.GetLibraryAsync(libraryId, token),
            cancellationToken);

    public Task<IReadOnlyList<LibraryCatalogItem>> ListLibrariesAsync(
        bool includeDisabled = true,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.ListLibrariesAsync(
                includeDisabled,
                token),
            cancellationToken);

    public Task<bool> SetLibraryEnabledAsync(
        long libraryId,
        bool isEnabled,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.SetLibraryEnabledAsync(
                libraryId,
                isEnabled,
                token),
            cancellationToken);

    public Task<bool> RemoveLibraryRegistrationAsync(
        long libraryId,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.RemoveLibraryRegistrationAsync(
                libraryId,
                token),
            cancellationToken);

    public Task<IReadOnlyList<LibraryFolderInfo>> ListFoldersAsync(
        long libraryId,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.ListFoldersAsync(
                libraryId,
                token),
            cancellationToken);

    public Task<IReadOnlyList<LibraryTagInfo>> ListTagsAsync(
        long libraryId,
        string? searchText = null,
        int limit = LibraryRepository.MaxTagListLimit,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.ListTagsAsync(
                libraryId,
                searchText,
                limit,
                token),
            cancellationToken);

    public Task<LibraryTagInfo> CreateTagAsync(
        long libraryId,
        string name,
        string color,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.CreateTagAsync(
                libraryId,
                name,
                color,
                token),
            cancellationToken);

    public Task<bool> DeleteTagAsync(
        long libraryId,
        long tagId,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.DeleteTagAsync(
                libraryId,
                tagId,
                token),
            cancellationToken);

    public Task<LibraryBrowseFacets> GetBrowseFacetsAsync(
        long libraryId,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.GetBrowseFacetsAsync(
                libraryId,
                token),
            cancellationToken);

    public Task<AssetInfo?> GetAssetAsync(
        long libraryId,
        string relativePath,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.GetAssetAsync(libraryId, relativePath, token),
            cancellationToken);

    public Task<bool> UpdateTechnicalMetadataAsync(
        long libraryId,
        long assetId,
        long expectedSourceRevision,
        long expectedFileSize,
        long expectedModifiedAtUtcTicks,
        AssetTechnicalMetadata metadata,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.UpdateTechnicalMetadataAsync(
                libraryId,
                assetId,
                expectedSourceRevision,
                expectedFileSize,
                expectedModifiedAtUtcTicks,
                metadata,
                token),
            cancellationToken);

    public Task<int> RefreshAssetSourceAsync(
        long libraryId,
        string relativePath,
        long fileSize,
        long modifiedAtUtcTicks,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.UpsertAssetsAsync(
                libraryId,
                [
                    new AssetUpsert(
                        relativePath,
                        fileSize,
                        new DateTimeOffset(
                            new DateTime(
                                modifiedAtUtcTicks,
                                DateTimeKind.Utc)),
                        Format: LibraryFileTypes.GetFormat(relativePath),
                        ForceSourceRevision: true)
                ],
                token),
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


    public Task<AssetUserMetadata?> GetUserMetadataAsync(
        long libraryId,
        long assetId,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.GetUserMetadataAsync(
                libraryId,
                assetId,
                token),
            cancellationToken);

    public Task<AssetUserMetadata> SetUserMetadataAsync(
        long libraryId,
        long assetId,
        AssetUserMetadataUpdate update,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.SetUserMetadataAsync(
                libraryId,
                assetId,
                update,
                token),
            cancellationToken);

    public Task<IReadOnlyList<string>> SetAssetTagsAsync(
        long libraryId,
        long assetId,
        IReadOnlyList<string> tagNames,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.SetAssetTagsAsync(
                libraryId,
                assetId,
                tagNames,
                token),
            cancellationToken);

    public Task<int> PatchUserMetadataAsync(
        long libraryId,
        IReadOnlyList<long> assetIds,
        AssetUserMetadataPatch patch,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.PatchUserMetadataAsync(
                libraryId,
                assetIds,
                patch,
                token),
            cancellationToken);

    public Task<AssetUserMetadataSelectionSummary>
        GetUserMetadataSelectionSummaryAsync(
            long libraryId,
            IReadOnlyList<long> assetIds,
            CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token =>
                _repository.GetUserMetadataSelectionSummaryAsync(
                    libraryId,
                    assetIds,
                    token),
            cancellationToken);

    public Task<int> RemoveAssetsAsync(
        long libraryId,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.RemoveAssetsAsync(
                libraryId,
                relativePaths,
                token),
            cancellationToken);

    public Task<long> CountAssetsAsync(
        long libraryId,
        AssetQuery query,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.CountAssetsAsync(
                libraryId,
                query,
                token),
            cancellationToken);


    public Task<int> RebuildSearchIndexAsync(
        long libraryId,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.RebuildSearchIndexAsync(
                libraryId,
                token),
            cancellationToken);

    public Task<AssetQueryPage> GetAssetPageAsync(
        long libraryId,
        AssetQuery query,
        int limit,
        AssetQueryCursor? cursor = null,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.GetAssetPageAsync(
                libraryId,
                query,
                limit,
                cursor,
                token),
            cancellationToken);

    public Task<IReadOnlyList<long>> GetOrderedAssetIdsAsync(
        long libraryId,
        AssetQuery query,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.GetOrderedAssetIdsAsync(
                libraryId,
                query,
                token),
            cancellationToken);

    public Task<IReadOnlyDictionary<long, long>> GetAssetIndicesAsync(
        long libraryId,
        AssetQuery query,
        IReadOnlyList<long> assetIds,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.GetAssetIndicesAsync(
                libraryId,
                query,
                assetIds,
                token),
            cancellationToken);

    public Task<IReadOnlyList<AssetInfo>> GetAssetsByIdsAsync(
        long libraryId,
        IReadOnlyList<long> assetIds,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.GetAssetsByIdsAsync(
                libraryId,
                assetIds,
                token),
            cancellationToken);

    public Task<WorkInfo> CreateWorkAsync(
        long libraryId,
        WorkCreate create,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.CreateWorkAsync(
                libraryId,
                create,
                token),
            cancellationToken);

    public Task<GenerationGroupInfo> CreateGenerationGroupAsync(
        long libraryId,
        GenerationGroupCreate create,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.CreateGenerationGroupAsync(
                libraryId,
                create,
                token),
            cancellationToken);

    public Task<AssetRelationInfo> CreateAssetRelationAsync(
        long libraryId,
        AssetRelationCreate create,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.CreateAssetRelationAsync(
                libraryId,
                create,
                token),
            cancellationToken);

    public Task<PublicationInfo> CreatePublicationAsync(
        long libraryId,
        PublicationCreate create,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.CreatePublicationAsync(
                libraryId,
                create,
                token),
            cancellationToken);

    public Task<IReadOnlyList<WorkInfo>> ListWorksAsync(
        long libraryId,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.ListWorksAsync(
                libraryId,
                limit,
                token),
            cancellationToken);

    public Task<IReadOnlyList<PublicationInfo>> ListPublicationsAsync(
        long libraryId,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.ListPublicationsAsync(
                libraryId,
                limit,
                token),
            cancellationToken);

    public Task<AssetCreativeContext> GetAssetCreativeContextAsync(
        long libraryId,
        long assetId,
        CancellationToken cancellationToken = default) =>
        LibraryBackgroundExecution.RunAsync(
            token => _repository.GetAssetCreativeContextAsync(
                libraryId,
                assetId,
                token),
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
        CancellationToken cancellationToken = default,
        IProgress<LibraryScanProgress>? progress = null) =>
        LibraryBackgroundExecution.RunAsync(
            token => _syncService.StartAsync(
                libraryId,
                token,
                progress),
            cancellationToken);
}
