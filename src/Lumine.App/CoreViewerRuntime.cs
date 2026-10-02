using Lumine.Core;
using Lumine.Image;
using Lumine.Library;
using Lumine.Viewer;

namespace Lumine.App;

internal enum CoreViewerOpenStage
{
    InitializingDatabase = 0,
    RegisteringLibrary = 1,
    SynchronizingLibrary = 2,
    CreatingViewer = 3,
    Ready = 4
}

internal sealed record CoreViewerOpenProgress(
    CoreViewerOpenStage Stage,
    string Message);

internal sealed class CoreViewerRuntime : IAsyncDisposable
{
    private readonly WindowsLibrarySyncSession _syncSession;
    private readonly ThumbnailPipeline _thumbnailPipeline;
    private readonly CursorPagedViewerAssetProvider _assetProvider;
    private readonly TaskCompletionSource _disposeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeStarted;

    private CoreViewerRuntime(
        string libraryRoot,
        LibraryInfo library,
        long assetCount,
        LibraryService libraryService,
        WindowsLibrarySyncSession syncSession,
        ThumbnailCache thumbnailCache,
        ThumbnailPipeline thumbnailPipeline,
        CursorPagedViewerAssetProvider assetProvider,
        ViewerSession viewerSession,
        ViewerDetailSession detailSession)
    {
        LibraryRoot = libraryRoot;
        Library = library;
        AssetCount = assetCount;
        LibraryService = libraryService;
        SyncSession = syncSession;
        ThumbnailCache = thumbnailCache;
        _syncSession = syncSession;
        _thumbnailPipeline = thumbnailPipeline;
        _assetProvider = assetProvider;
        ViewerSession = viewerSession;
        DetailSession = detailSession;
    }

    public string LibraryRoot { get; }

    public LibraryInfo Library { get; }

    public long AssetCount { get; }

    public LibraryService LibraryService { get; }

    public WindowsLibrarySyncSession SyncSession { get; }

    public ThumbnailCache ThumbnailCache { get; }

    internal ThumbnailDiagnosticsSnapshot ThumbnailPipelineDiagnostics =>
        _thumbnailPipeline.Diagnostics;

    internal ThumbnailStorageMode ThumbnailStorageMode =>
        _thumbnailPipeline.StorageMode;

    internal ThumbnailMemoryCacheStats ThumbnailMemoryCacheStats =>
        _thumbnailPipeline.MemoryCacheStats;

    internal ThumbnailCacheMaintenanceDiagnosticsSnapshot
        ThumbnailCacheMaintenanceDiagnostics =>
        _thumbnailPipeline.MaintenanceDiagnostics;

    public ViewerSession ViewerSession { get; }

    public ViewerDetailSession DetailSession { get; }

    public static async Task<CoreViewerRuntime> OpenAsync(
        string libraryRoot,
        AppDataPaths dataPaths,
        CoreResourcePolicy resourcePolicy,
        IProgress<CoreViewerOpenProgress>? progress = null,
        CancellationToken cancellationToken = default,
        ThumbnailStorageMode thumbnailStorageMode =
            ThumbnailStorageMode.MemoryOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        ArgumentNullException.ThrowIfNull(dataPaths);
        ArgumentNullException.ThrowIfNull(resourcePolicy);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Lumine v2 Core Viewer is currently Windows-only.");
        }

        var normalizedRoot =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(libraryRoot));

        if (!Directory.Exists(normalizedRoot))
        {
            throw new DirectoryNotFoundException(
                $"Library root does not exist: {normalizedRoot}");
        }

        GuardAgainstAppDataRecursion(
            normalizedRoot,
            dataPaths.RootPath);

        progress?.Report(
            new CoreViewerOpenProgress(
                CoreViewerOpenStage.InitializingDatabase,
                "Initializing Lumine v2 library database…"));

        var libraryService =
            new LibraryService(dataPaths.DatabasePath);
        await libraryService.InitializeAsync(
            cancellationToken).ConfigureAwait(false);

        progress?.Report(
            new CoreViewerOpenProgress(
                CoreViewerOpenStage.RegisteringLibrary,
                "Registering library…"));

        var name = new DirectoryInfo(normalizedRoot).Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = normalizedRoot;
        }

        var library = await libraryService.RegisterLibraryAsync(
            name,
            normalizedRoot,
            cancellationToken).ConfigureAwait(false);

        progress?.Report(
            new CoreViewerOpenProgress(
                CoreViewerOpenStage.SynchronizingLibrary,
                library.ScanState == LibraryScanState.Unknown
                    ? "Indexing library for first use…"
                    : "Synchronizing library changes…"));

        WindowsLibrarySyncSession? syncSession = null;
        ThumbnailPipeline? pipeline = null;
        CursorPagedViewerAssetProvider? assetProvider = null;
        ViewerSession? viewerSession = null;
        ViewerDetailSession? detailSession = null;

        try
        {
            syncSession =
                await libraryService.StartWindowsSyncAsync(
                    library.Id,
                    cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            var assetCount =
                await libraryService.CountAssetsAsync(
                    library.Id,
                    cancellationToken).ConfigureAwait(false);

            progress?.Report(
                new CoreViewerOpenProgress(
                    CoreViewerOpenStage.CreatingViewer,
                    $"Creating viewer for {assetCount:N0} assets…"));

            var thumbnailCache = new ThumbnailCache(
                dataPaths.ThumbnailCachePath,
                resourcePolicy);

            pipeline = new ThumbnailPipeline(
                thumbnailCache,
                ThumbnailPipelineOptions.FromResourcePolicy(
                    resourcePolicy,
                    thumbnailStorageMode));

            var viewerOptions =
                ViewerOptions.FromResourcePolicy(
                    resourcePolicy);
            var pageSource = new LibraryViewerPageSource(
                libraryService,
                library.Id,
                assetCount);
            assetProvider =
                new CursorPagedViewerAssetProvider(
                    pageSource,
                    viewerOptions);

            var thumbnailProvider =
                new ImageViewerThumbnailProvider(
                    pipeline,
                    normalizedRoot,
                    libraryService,
                    library.Id);
            var detailProvider =
                new ImageViewerDetailProvider(
                    pipeline,
                    normalizedRoot,
                    libraryService,
                    library.Id);

            viewerSession = new ViewerSession(
                assetProvider,
                thumbnailProvider,
                viewerOptions);
            detailSession = new ViewerDetailSession(
                assetProvider,
                detailProvider,
                ViewerDetailOptions.FromResourcePolicy(
                    resourcePolicy));

            progress?.Report(
                new CoreViewerOpenProgress(
                    CoreViewerOpenStage.Ready,
                    $"{assetCount:N0} assets ready"));

            return new CoreViewerRuntime(
                normalizedRoot,
                library,
                assetCount,
                libraryService,
                syncSession,
                thumbnailCache,
                pipeline,
                assetProvider,
                viewerSession,
                detailSession);
        }
        catch
        {
            if (detailSession is not null)
            {
                await detailSession.DisposeAsync()
                    .ConfigureAwait(false);
            }

            if (viewerSession is not null)
            {
                await viewerSession.DisposeAsync()
                    .ConfigureAwait(false);
            }

            assetProvider?.Dispose();

            if (pipeline is not null)
            {
                await pipeline.DisposeAsync()
                    .ConfigureAwait(false);
            }

            if (syncSession is not null)
            {
                await syncSession.DisposeAsync()
                    .ConfigureAwait(false);
            }

            LibraryDatabase.ClearPools();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(
                ref _disposeStarted,
                1,
                0) != 0)
        {
            await _disposeCompletion.Task
                .ConfigureAwait(false);
            return;
        }

        Exception? failure = null;

        try
        {
            try
            {
                await _syncSession.DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                await DetailSession.DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }

            try
            {
                await ViewerSession.DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }

            try
            {
                _assetProvider.Dispose();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }

            try
            {
                await _thumbnailPipeline.DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }

            try
            {
                await LibraryService.CheckpointAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
            finally
            {
                LibraryDatabase.ClearPools();
            }

            if (failure is not null)
            {
                throw failure;
            }

            _disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            _disposeCompletion.TrySetException(exception);
            throw;
        }
    }

    private static void GuardAgainstAppDataRecursion(
        string libraryRoot,
        string appDataRoot)
    {
        var normalizedAppData =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(appDataRoot));

        var rootWithSeparator =
            libraryRoot + Path.DirectorySeparatorChar;

        if (string.Equals(
                libraryRoot,
                normalizedAppData,
                StringComparison.OrdinalIgnoreCase)
            || normalizedAppData.StartsWith(
                rootWithSeparator,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected library contains Lumine's own v2 data/cache directory. Select a narrower image library folder so generated thumbnails cannot index themselves.");
        }
    }
}
