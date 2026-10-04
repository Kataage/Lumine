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
    string Message,
    LibraryScanProgress? ScanProgress = null);

internal sealed class InlineLibraryScanProgress(
    Action<LibraryScanProgress> report)
    : IProgress<LibraryScanProgress>
{
    private readonly Action<LibraryScanProgress> _report =
        report ?? throw new ArgumentNullException(nameof(report));

    public void Report(
        LibraryScanProgress value) =>
        _report(value);
}

internal sealed class CoreViewerRuntime : IAsyncDisposable
{
    private readonly WindowsLibrarySyncSession _syncSession;
    private readonly ThumbnailPipeline _thumbnailPipeline;
    private CursorPagedViewerAssetProvider _assetProvider;
    private readonly SemaphoreSlim _queryGate = new(1, 1);
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

    public long AssetCount { get; private set; }

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

    public ViewerSession ViewerSession { get; private set; }

    public ViewerDetailSession DetailSession { get; private set; }

    public AssetQuery? CurrentQuery { get; private set; }

    public static async Task<CoreViewerRuntime> OpenAsync(
        string libraryRoot,
        AppDataPaths dataPaths,
        CoreResourcePolicy resourcePolicy,
        IProgress<CoreViewerOpenProgress>? progress = null,
        CancellationToken cancellationToken = default,
        ThumbnailStorageMode thumbnailStorageMode =
            ThumbnailStorageMode.MemoryOnly,
        AssetQuery? initialQuery = null)
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

        var fullScanProgress =
            progress is null
                ? null
                : new InlineLibraryScanProgress(
                    scan =>
                        progress.Report(
                            new CoreViewerOpenProgress(
                                CoreViewerOpenStage.SynchronizingLibrary,
                                "Synchronizing library…",
                                scan)));

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
                    cancellationToken,
                    fullScanProgress).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            var assetCount =
                initialQuery is null
                    ? await libraryService.CountAssetsAsync(
                        library.Id,
                        cancellationToken).ConfigureAwait(false)
                    : await libraryService.CountAssetsAsync(
                        library.Id,
                        initialQuery,
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
            IViewerPageSource pageSource =
                initialQuery is null
                    ? new LibraryViewerPageSource(
                        libraryService,
                        library.Id,
                        assetCount)
                    : new LibraryViewerQueryPageSource(
                        libraryService,
                        library.Id,
                        initialQuery,
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

            var runtime =
                new CoreViewerRuntime(
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
            runtime.CurrentQuery = initialQuery;
            return runtime;
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

    public async Task ApplyQueryAsync(
        AssetQuery? query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _queryGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposeStarted) != 0,
                this);

            var nextCount =
                query is null
                    ? await LibraryService.CountAssetsAsync(
                        Library.Id,
                        cancellationToken).ConfigureAwait(false)
                    : await LibraryService.CountAssetsAsync(
                        Library.Id,
                        query,
                        cancellationToken).ConfigureAwait(false);

            // Keep the already-resolved Viewer options. Query changes must
            // replace only paging/session state, never product resource policy.
            var viewerOptions = ViewerSession.Options;

            IViewerPageSource pageSource =
                query is null
                    ? new LibraryViewerPageSource(
                        LibraryService,
                        Library.Id,
                        nextCount)
                    : new LibraryViewerQueryPageSource(
                        LibraryService,
                        Library.Id,
                        query,
                        nextCount);

            var nextProvider =
                new CursorPagedViewerAssetProvider(
                    pageSource,
                    viewerOptions);
            var thumbnailProvider =
                new ImageViewerThumbnailProvider(
                    _thumbnailPipeline,
                    LibraryRoot,
                    LibraryService,
                    Library.Id);
            var detailProvider =
                new ImageViewerDetailProvider(
                    _thumbnailPipeline,
                    LibraryRoot,
                    LibraryService,
                    Library.Id);
            var nextViewer =
                new ViewerSession(
                    nextProvider,
                    thumbnailProvider,
                    viewerOptions);
            var nextDetail =
                new ViewerDetailSession(
                    nextProvider,
                    detailProvider,
                    DetailSession.Options);

            var previousProvider = _assetProvider;
            var previousViewer = ViewerSession;
            var previousDetail = DetailSession;

            _assetProvider = nextProvider;
            ViewerSession = nextViewer;
            DetailSession = nextDetail;
            AssetCount = nextCount;
            CurrentQuery = query;

            await previousDetail.DisposeAsync()
                .ConfigureAwait(false);
            await previousViewer.DisposeAsync()
                .ConfigureAwait(false);
            previousProvider.Dispose();
        }
        finally
        {
            _queryGate.Release();
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

        await _queryGate.WaitAsync()
            .ConfigureAwait(false);

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
        finally
        {
            _queryGate.Release();
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
