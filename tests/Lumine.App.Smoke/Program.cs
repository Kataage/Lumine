using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumine.App;
using Lumine.Core;
using Lumine.Diagnostics;
using Lumine.Image;
using Lumine.Library;
using Lumine.Viewer;
using NetVips;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}


static double RelativeLuminance(Color color)
{
    static double Channel(byte value)
    {
        var normalized = value / 255d;
        return normalized <= 0.04045
            ? normalized / 12.92
            : Math.Pow(
                (normalized + 0.055) / 1.055,
                2.4);
    }

    return
        (0.2126 * Channel(color.R))
        + (0.7152 * Channel(color.G))
        + (0.0722 * Channel(color.B));
}

static double ContrastRatio(
    Color foreground,
    Color background)
{
    var first = RelativeLuminance(foreground);
    var second = RelativeLuminance(background);
    var lighter = Math.Max(first, second);
    var darker = Math.Min(first, second);
    return (lighter + 0.05) / (darker + 0.05);
}

static void WriteBmp24(
    string path,
    int width,
    int height)
{
    var rowStride =
        checked(((width * 3 + 3) / 4) * 4);
    const int pixelOffset = 54;
    var bytes = new byte[
        checked(pixelOffset + (rowStride * height))];

    bytes[0] = (byte)'B';
    bytes[1] = (byte)'M';
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(2, 4),
        checked((uint)bytes.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(10, 4),
        pixelOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(14, 4),
        40);
    BinaryPrimitives.WriteInt32LittleEndian(
        bytes.AsSpan(18, 4),
        width);
    BinaryPrimitives.WriteInt32LittleEndian(
        bytes.AsSpan(22, 4),
        height);
    BinaryPrimitives.WriteUInt16LittleEndian(
        bytes.AsSpan(26, 2),
        1);
    BinaryPrimitives.WriteUInt16LittleEndian(
        bytes.AsSpan(28, 2),
        24);

    for (var y = 0; y < height; y++)
    {
        var storedY = height - 1 - y;
        var row =
            pixelOffset + (storedY * rowStride);

        for (var x = 0; x < width; x++)
        {
            var offset = row + (x * 3);
            bytes[offset] = (byte)(56 + x + y);
            bytes[offset + 1] = (byte)(34 + y);
            bytes[offset + 2] = (byte)(12 + x);
        }
    }

    File.WriteAllBytes(path, bytes);
}

var root = Path.Combine(
    Path.GetTempPath(),
    $"lumine-app-smoke-{Guid.NewGuid():N}");
var libraryRoot = Path.Combine(root, "library");
var cacheRoot = Path.Combine(root, "cache");
var databasePath = Path.Combine(root, "library.db");
var sourcePath = Path.Combine(libraryRoot, "adapter-source.png");
var replacementPath = Path.Combine(root, "adapter-replacement.png");
var bmpPath = Path.Combine(libraryRoot, "adapter-source.bmp");

Directory.CreateDirectory(libraryRoot);

Require(
    !LibraryFileTypes.IsSupportedPath("contract.heic")
    && !LibraryFileTypes.IsSupportedPath("contract.heif")
    && LibraryFileTypes.IsSupportedPath("contract.avif"),
    "Library HEIC/HEIF/AVIF advertised-extension contract drifted from #312.");

try
{
    var acceptanceParse =
        RealLibraryAcceptanceOptions.Parse(
        [
            "--core-acceptance",
            $"--library-dir={libraryRoot}",
            $"--data-dir={Path.Combine(root, "acceptance-parse-data")}",
            $"--output={Path.Combine(root, "acceptance-parse.json")}",
            "--mode=smoke",
            "--min-assets=2",
            "--browse-seconds=1",
            "--idle-seconds=1"
        ]);

    Require(
        acceptanceParse.MinimumAssets == 2
        && acceptanceParse.BrowseDuration == TimeSpan.FromSeconds(1)
        && acceptanceParse.IdleDuration == TimeSpan.FromSeconds(1)
        && acceptanceParse.Mode == "smoke",
        "Real-library acceptance CLI options did not parse deterministically.");

    var rawWatcherNoise =
        new LibrarySyncDiagnostics(
            EventsObserved: 1,
            EventsApplied: 0,
            EventsCoalesced: 0,
            Overflows: 0,
            Reconciliations: 0,
            ReconcileFailures: 0,
            RenameOperations: 0,
            Deletes: 0,
            Upserts: 0,
            LastApplyLatencyMs: 0,
            MaxApplyLatencyMs: 0,
            QueueDepth: 0);

    Require(
        !RealLibraryAcceptanceSession.HasTrackedSourceMutation(
            rawWatcherNoise),
        "Raw watcher notification without tracked mutation incorrectly fails real-library acceptance.");
    Require(
        RealLibraryAcceptanceSession.HasTrackedSourceMutation(
            rawWatcherNoise with
            {
                EventsApplied = 1,
                Upserts = 1
            })
        && RealLibraryAcceptanceSession.HasTrackedSourceMutation(
            rawWatcherNoise with
            {
                EventsApplied = 1,
                Deletes = 1
            })
        && RealLibraryAcceptanceSession.HasTrackedSourceMutation(
            rawWatcherNoise with
            {
                EventsApplied = 1,
                RenameOperations = 1
            }),
        "Tracked upsert/delete/rename mutations must remain disqualifying for real-library acceptance.");

    var functionalAcceptanceMetadata =
        new Dictionary<string, string>(
            StringComparer.Ordinal);
    await CoreAcceptanceFunctionalScenario.RunAsync(
        new BenchmarkRecorder(),
        functionalAcceptanceMetadata);

    Require(
        functionalAcceptanceMetadata.TryGetValue(
            "functional.search_metadata",
            out var functionalSearch)
        && functionalSearch == "pass"
        && functionalAcceptanceMetadata.TryGetValue(
            "functional.live_changes",
            out var functionalChanges)
        && functionalChanges == "pass"
        && functionalAcceptanceMetadata.TryGetValue(
            "functional.offline_recovery",
            out var functionalRecovery)
        && functionalRecovery == "pass",
        "Core acceptance isolated filesystem/search scenario did not complete.");

    var originalDataDir =
        Environment.GetEnvironmentVariable(
            "LUMINE_DATA_DIR");
    var originalPortable =
        Environment.GetEnvironmentVariable(
            "LUMINE_PORTABLE");
    try
    {
        Environment.SetEnvironmentVariable(
            "LUMINE_DATA_DIR",
            null);
        Environment.SetEnvironmentVariable(
            "LUMINE_PORTABLE",
            null);

        var explicitPortable =
            AppDataPaths.Resolve(
                ["--portable"]);
        Require(
            explicitPortable.IsPortable
            && explicitPortable.LocationKind
                == AppDataLocationKind.Portable
            && Path.GetFileName(
                explicitPortable.RootPath)
                == "data"
            && Path.GetDirectoryName(
                explicitPortable.SettingsPath)
                == explicitPortable.RootPath
            && Path.GetDirectoryName(
                explicitPortable.DatabasePath)
                == explicitPortable.RootPath,
            "--portable did not resolve to a self-contained executable-local data root.");

        Environment.SetEnvironmentVariable(
            "LUMINE_PORTABLE",
            "1");
        var environmentPortable =
            AppDataPaths.Resolve(
                Array.Empty<string>());
        Require(
            environmentPortable.IsPortable
            && environmentPortable.RootPath
                == explicitPortable.RootPath,
            "LUMINE_PORTABLE=1 did not resolve the portable data root.");

        var customRoot =
            Path.Combine(
                root,
                "custom-data-location");
        Environment.SetEnvironmentVariable(
            "LUMINE_DATA_DIR",
            customRoot);
        var custom =
            AppDataPaths.Resolve(
                Array.Empty<string>());
        Require(
            custom.LocationKind
                == AppDataLocationKind.Custom
            && custom.RootPath
                == Path.GetFullPath(customRoot),
            "LUMINE_DATA_DIR did not retain explicit custom data-location precedence.");
    }
    finally
    {
        Environment.SetEnvironmentVariable(
            "LUMINE_DATA_DIR",
            originalDataDir);
        Environment.SetEnvironmentVariable(
            "LUMINE_PORTABLE",
            originalPortable);
    }

    var lifecyclePaths =
        AppDataPaths.FromRoot(
            Path.Combine(root, "lifecycle-host"));

    var settingsStore =
        new AppSettingsStore(
            lifecyclePaths.SettingsPath);
    await settingsStore.SaveAsync(
        new AppSettingsDocument
        {
            ResourcePolicy =
                new ResourcePolicySettings
                {
                    ThumbnailQueueCapacity = 17,
                    ThumbnailCacheByteLimit =
                        128L * 1024 * 1024
                }
        });

    await using (var lifecycleHost =
                 await AppHost.StartAsync(
                     lifecyclePaths))
    {
        Require(
            lifecycleHost.ResourcePolicy.ThumbnailQueueCapacity == 17
            && lifecycleHost.ResourcePolicy.ThumbnailCacheByteLimit
                == 128L * 1024 * 1024,
            "Persisted resource settings were not resolved before runtime startup.");
        Require(
            !lifecycleHost.PreviousShutdownWasUnclean,
            "First lifecycle host incorrectly reported an unclean previous shutdown.");

        var initialBrowse =
            BrowsePreferenceResolver.Resolve(
                lifecycleHost.Settings,
                out var initialBrowseWarning);
        Require(
            initialBrowseWarning is null
            && initialBrowse.ViewMode == BrowseViewMode.Grid
            && initialBrowse.Density == 1
            && initialBrowse.SortOrder
                == AssetSortOrder.ModifiedNewest,
            "Fresh browse preferences did not resolve to the product defaults.");

        await lifecycleHost.SaveBrowsePreferencesAsync(
            new BrowsePreferences(
                BrowseViewMode.List,
                2,
                AssetSortOrder.FileNameDescending));

        await lifecycleHost.SaveSettingsAsync(
            (lifecycleHost.Settings.ResourcePolicy
                ?? new ResourcePolicySettings()) with
            {
                EncodedThumbnailMemoryByteLimit =
                    512L * 1024 * 1024,
                ThumbnailCacheByteLimit =
                    4L * 1024 * 1024 * 1024
            });

        AppHost? unexpectedSecondHost = null;
        try
        {
            unexpectedSecondHost =
                await AppHost.StartAsync(
                    lifecyclePaths);
            throw new InvalidOperationException(
                "Second AppHost acquired the same data-root instance lock.");
        }
        catch (AppAlreadyRunningException)
        {
        }
        finally
        {
            if (unexpectedSecondHost is not null)
            {
                await unexpectedSecondHost.DisposeAsync();
            }
        }

        await lifecycleHost.CompleteCleanShutdownAsync();
    }

    await using (var cleanRestart =
                 await AppHost.StartAsync(
                     lifecyclePaths))
    {
        Require(
            !cleanRestart.PreviousShutdownWasUnclean,
            "Clean lifecycle restart was reported as unclean.");

        var restoredBrowse =
            BrowsePreferenceResolver.Resolve(
                cleanRestart.Settings,
                out var restoredBrowseWarning);
        Require(
            restoredBrowseWarning is null
            && restoredBrowse.ViewMode
                == BrowseViewMode.List
            && restoredBrowse.Density == 2
            && restoredBrowse.SortOrder
                == AssetSortOrder.FileNameDescending,
            "Browse view/density/sort preferences did not persist across restart.");

        Require(
            cleanRestart.ResourcePolicy
                .EncodedThumbnailMemoryByteLimit
                == 512L * 1024 * 1024,
            "User-facing encoded thumbnail memory budget did not persist across restart.");
        Require(
            cleanRestart.ResourcePolicy
                .ThumbnailCacheByteLimit
                == 4L * 1024 * 1024 * 1024,
            "User-facing persistent thumbnail disk budget did not persist across restart.");

        await cleanRestart.CompleteCleanShutdownAsync();
    }

    var simulatedCrash =
        await AppHost.StartAsync(
            lifecyclePaths);
    await simulatedCrash.DisposeAsync();

    await using (var recovered =
                 await AppHost.StartAsync(
                     lifecyclePaths))
    {
        Require(
            recovered.PreviousShutdownWasUnclean,
            "Unclean-shutdown marker was not detected on restart.");
        await recovered.CompleteCleanShutdownAsync();
    }

    Require(
        !File.Exists(
            lifecyclePaths.RuntimeMarkerPath),
        "Clean recovery left the unclean-shutdown marker behind.");
    Require(
        File.Exists(
            lifecyclePaths.RuntimeLogPath),
        "Bounded runtime event log was not created.");

    var corruptSettingsPaths =
        AppDataPaths.FromRoot(
            Path.Combine(root, "corrupt-settings-host"));
    Directory.CreateDirectory(
        corruptSettingsPaths.RootPath);
    await File.WriteAllTextAsync(
        corruptSettingsPaths.SettingsPath,
        "{ this is not valid json");

    await using (var degradedSettingsHost =
                 await AppHost.StartAsync(
                     corruptSettingsPaths))
    {
        Require(
            !string.IsNullOrWhiteSpace(
                degradedSettingsHost.SettingsWarning),
            "Corrupt settings did not degrade to defaults with a diagnostic warning.");
        await degradedSettingsHost.CompleteCleanShutdownAsync();
    }

    var storagePolicyPaths =
        AppDataPaths.FromRoot(
            Path.Combine(root, "thumbnail-storage-policy-host"));
    Directory.CreateDirectory(
        storagePolicyPaths.ThumbnailCachePath);
    var legacyThumbnail =
        Path.Combine(
            storagePolicyPaths.ThumbnailCachePath,
            "legacy.webp");
    await File.WriteAllBytesAsync(
        legacyThumbnail,
        [1, 2, 3, 4]);

    var originalStorageOverride =
        Environment.GetEnvironmentVariable(
            ThumbnailStoragePreference.EnvironmentVariable);

    try
    {
        Environment.SetEnvironmentVariable(
            ThumbnailStoragePreference.EnvironmentVariable,
            null);

        await using (var defaultStorageHost =
                     await AppHost.StartAsync(
                         storagePolicyPaths))
        {
            Require(
                defaultStorageHost.ThumbnailStorageMode
                    == ThumbnailStorageMode.MemoryOnly,
                "Fresh AppHost did not default to MemoryOnly thumbnail storage.");
            Require(
                !File.Exists(legacyThumbnail),
                "MemoryOnly migration left the active legacy persistent thumbnail file in place.");

            await defaultStorageHost.SaveThumbnailStorageModeAsync(
                ThumbnailStorageMode.PersistentDisk);
            await defaultStorageHost.CompleteCleanShutdownAsync();
        }

        Directory.CreateDirectory(
            storagePolicyPaths.ThumbnailCachePath);
        var preservedPersistentThumbnail =
            Path.Combine(
                storagePolicyPaths.ThumbnailCachePath,
                "preserved.webp");
        await File.WriteAllBytesAsync(
            preservedPersistentThumbnail,
            [5, 6, 7, 8]);

        await using (var persistentStorageHost =
                     await AppHost.StartAsync(
                         storagePolicyPaths))
        {
            Require(
                persistentStorageHost.ThumbnailStorageMode
                    == ThumbnailStorageMode.PersistentDisk
                && File.Exists(
                    preservedPersistentThumbnail),
                "Explicit PersistentDisk preference was not restored without deleting its cache.");

            await persistentStorageHost.SaveThumbnailStorageModeAsync(
                ThumbnailStorageMode.MemoryOnly);
            await persistentStorageHost.CompleteCleanShutdownAsync();
        }

        await using (var migratedBackToMemory =
                     await AppHost.StartAsync(
                         storagePolicyPaths))
        {
            Require(
                migratedBackToMemory.ThumbnailStorageMode
                    == ThumbnailStorageMode.MemoryOnly
                && !File.Exists(
                    preservedPersistentThumbnail),
                "Switching back to MemoryOnly did not retire the persistent display-thumbnail cache.");

            await migratedBackToMemory.CompleteCleanShutdownAsync();
        }

        Environment.SetEnvironmentVariable(
            ThumbnailStoragePreference.EnvironmentVariable,
            "PersistentDisk");

        await using (var overriddenStorageHost =
                     await AppHost.StartAsync(
                         storagePolicyPaths))
        {
            Require(
                overriddenStorageHost.ThumbnailStorageMode
                    == ThumbnailStorageMode.PersistentDisk
                && string.Equals(
                    overriddenStorageHost.Settings.ThumbnailStorageMode,
                    nameof(ThumbnailStorageMode.MemoryOnly),
                    StringComparison.Ordinal),
                "Environment thumbnail-storage override leaked into the persisted product preference.");

            await overriddenStorageHost.CompleteCleanShutdownAsync();
        }
    }
    finally
    {
        Environment.SetEnvironmentVariable(
            ThumbnailStoragePreference.EnvironmentVariable,
            originalStorageOverride);
    }

    VipsRuntimePolicy.EnsureConfigured();

    using (var blank = NetVips.Image.Black(320, 200, bands: 4))
    using (var values = blank.NewFromImage([20, 80, 160, 128]))
    using (var rgba = values.Copy(interpretation: Enums.Interpretation.Srgb))
    {
        rgba.Pngsave(sourcePath);
    }

    using (var blank = NetVips.Image.Black(320, 200, bands: 4))
    using (var values = blank.NewFromImage([180, 40, 70, 128]))
    using (var rgba = values.Copy(interpretation: Enums.Interpretation.Srgb))
    {
        rgba.Pngsave(replacementPath);
    }

    WriteBmp24(
        bmpPath,
        7,
        5);

    var sameStatFixtureLength = Math.Max(
        new FileInfo(sourcePath).Length,
        new FileInfo(replacementPath).Length);
    using (var source = new FileStream(
               sourcePath,
               FileMode.Open,
               FileAccess.Write,
               FileShare.None))
    {
        source.SetLength(sameStatFixtureLength);
    }

    using (var replacement = new FileStream(
               replacementPath,
               FileMode.Open,
               FileAccess.Write,
               FileShare.None))
    {
        replacement.SetLength(sameStatFixtureLength);
    }

    var libraryService = new LibraryService(databasePath);
    await libraryService.InitializeAsync();
    var library = await libraryService.RegisterLibraryAsync(
        "App smoke",
        libraryRoot);
    var scan = await libraryService.ScanAsync(library.Id);
    Require(scan.Completed, "App smoke library scan did not complete.");

    var indexed = await libraryService.GetAssetAsync(
        library.Id,
        "adapter-source.png")
        ?? throw new InvalidOperationException(
            "App smoke source was not indexed.");


    _ = await libraryService.SetUserMetadataAsync(
        library.Id,
        indexed.Id,
        new AssetUserMetadataUpdate(
            Rating: 5,
            Favorite: true,
            Notes: "猫耳 app viewer search",
            Tags: ["viewer-tag"]));

    var viewerQuery = new AssetQuery(
        SearchText: "猫耳",
        RequiredTags: ["viewer-tag"],
        Favorite: true,
        MinRating: 5);
    var viewerQueryCount = await libraryService.CountAssetsAsync(
        library.Id,
        viewerQuery);
    Require(
        viewerQueryCount == 1,
        $"App search query expected one Viewer asset, got {viewerQueryCount}.");

    var viewerQuerySource = new LibraryViewerQueryPageSource(
        libraryService,
        library.Id,
        viewerQuery,
        viewerQueryCount);
    using (var viewerQueryProvider =
           new CursorPagedViewerAssetProvider(
               viewerQuerySource,
               new ViewerOptions
               {
                   MetadataPageSize = 1,
                   MetadataPageCacheSize = 2,
                   CursorCheckpointStride = 1,
                   CursorCheckpointLimit = 4
               }))
    {
        var searchedViewerAsset =
            await viewerQueryProvider.GetAssetAsync(0);
        Require(
            searchedViewerAsset.Id == indexed.Id
            && string.Equals(
                searchedViewerAsset.RelativePath,
                indexed.RelativePath,
                StringComparison.Ordinal),
            "Search/filter page source did not preserve Library asset identity through the Viewer paging adapter.");
    }

    var asset = new ViewerAsset(
        indexed.Id,
        indexed.SourceRevision,
        indexed.RelativePath,
        indexed.FileName,
        indexed.FileSize,
        indexed.ModifiedAtUtc.UtcDateTime.Ticks,
        indexed.Width,
        indexed.Height,
        indexed.Format,
        indexed.SourceIdentity,
        indexed.RawWidth,
        indexed.RawHeight,
        indexed.HasAlpha);

    var cache = new ThumbnailCache(cacheRoot);
    await using var pipeline = new ThumbnailPipeline(
        cache,
        new ThumbnailPipelineOptions
        {
            WorkerCount = 1,
            QueueCapacity = 8,
            MaxForegroundBurst = 2
        });
    var provider = new ImageViewerDetailProvider(
        pipeline,
        libraryRoot,
        libraryService,
        library.Id);

    var preview = await provider.RequestPreviewAsync(asset);
    Require(
        File.Exists(preview.CachePath)
        && preview.Width == 320
        && preview.Height == 200,
        "Production Detail adapter failed to produce its persistent preview.");
    Require(
        preview.SourceMetadata is not null
        && preview.SourceMetadata.Width == 320
        && preview.SourceMetadata.Height == 200
        && preview.SourceMetadata.RawWidth == 320
        && preview.SourceMetadata.RawHeight == 200
        && preview.SourceMetadata.HasAlpha,
        "Production Detail adapter did not expose source technical metadata.");

    var persistenceWritesAfterFirstPreview =
        ViewerImageMetadataBridge.MetadataPersistenceWrites;
    Require(
        persistenceWritesAfterFirstPreview == 1,
        "First preview did not persist technical metadata exactly once.");

    var repeatDiagnostics = pipeline.Diagnostics;
    _ = await provider.RequestPreviewAsync(asset);
    Require(
        ViewerImageMetadataBridge.MetadataPersistenceWrites
            == persistenceWritesAfterFirstPreview,
        "Repeated stale ViewerAsset caused duplicate technical metadata DB writes.");
    Require(
        pipeline.Diagnostics.MetadataProbes
            == repeatDiagnostics.MetadataProbes,
        "Repeated stale ViewerAsset reprobed the original instead of using bounded metadata reuse.");

    var persisted = await libraryService.GetAssetAsync(
        library.Id,
        "adapter-source.png")
        ?? throw new InvalidOperationException(
            "Persisted app smoke asset disappeared.");
    Require(
        persisted.Width == 320
        && persisted.Height == 200
        && persisted.RawWidth == 320
        && persisted.RawHeight == 200
        && persisted.HasAlpha == true
        && FileSourceIdentityProbe.IsValid(persisted.SourceIdentity),
        "Image source technical metadata was not persisted through the App composition boundary.");

    LibraryDatabase.ClearPools();
    var restartedLibraryService = new LibraryService(databasePath);
    await restartedLibraryService.InitializeAsync();
    persisted = await restartedLibraryService.GetAssetAsync(
        library.Id,
        "adapter-source.png")
        ?? throw new InvalidOperationException(
            "Restarted LibraryService did not reload persisted source metadata.");
    Require(
        persisted.Width == 320
        && persisted.Height == 200
        && persisted.RawWidth == 320
        && persisted.RawHeight == 200
        && persisted.HasAlpha == true
        && FileSourceIdentityProbe.IsValid(persisted.SourceIdentity),
        "Restarted LibraryService lost persisted source technical metadata.");

    provider = new ImageViewerDetailProvider(
        pipeline,
        libraryRoot,
        restartedLibraryService,
        library.Id);

    var indexedBmp =
        await restartedLibraryService.GetAssetAsync(
            library.Id,
            "adapter-source.bmp")
        ?? throw new InvalidOperationException(
            "App smoke BMP source was not indexed.");

    var bmpAsset = new ViewerAsset(
        indexedBmp.Id,
        indexedBmp.SourceRevision,
        indexedBmp.RelativePath,
        indexedBmp.FileName,
        indexedBmp.FileSize,
        indexedBmp.ModifiedAtUtc.UtcDateTime.Ticks,
        indexedBmp.Width,
        indexedBmp.Height,
        indexedBmp.Format,
        indexedBmp.SourceIdentity,
        indexedBmp.RawWidth,
        indexedBmp.RawHeight,
        indexedBmp.HasAlpha);

    var bmpPreview =
        await provider.RequestPreviewAsync(
            bmpAsset);
    Require(
        File.Exists(bmpPreview.CachePath)
        && bmpPreview.Width == 7
        && bmpPreview.Height == 5
        && string.Equals(
            bmpPreview.SourceMetadata?.Format,
            "bmp",
            StringComparison.Ordinal),
        "Production Detail adapter failed to produce a BMP persistent preview.");

    asset = new ViewerAsset(
        persisted.Id,
        persisted.SourceRevision,
        persisted.RelativePath,
        persisted.FileName,
        persisted.FileSize,
        persisted.ModifiedAtUtc.UtcDateTime.Ticks,
        persisted.Width,
        persisted.Height,
        persisted.Format,
        persisted.SourceIdentity,
        persisted.RawWidth,
        persisted.RawHeight,
        persisted.HasAlpha);

    var hiddenSource = sourcePath + ".hidden";
    var warmDiagnostics = pipeline.Diagnostics;
    File.Move(sourcePath, hiddenSource);
    try
    {
        var warmPreview = await provider.RequestPreviewAsync(asset);
        Require(
            File.Exists(warmPreview.CachePath)
            && pipeline.Diagnostics.SourceOpens
                == warmDiagnostics.SourceOpens
            && pipeline.Diagnostics.MetadataProbes
                == warmDiagnostics.MetadataProbes,
            "Warm Detail preview cache hit reopened or reprobed the original source.");
    }
    finally
    {
        File.Move(hiddenSource, sourcePath);
    }

    await using var headless = HeadlessUnitTestSession.StartNew(
        typeof(AppAdapterSmokeApplication));

    await headless.Dispatch(
        async () =>
        {
            var fatalStartup =
                App.CreateStartupFailureWindow(
                    "bootstrap smoke failure");
            Require(
                fatalStartup.Title.Contains(
                    "起動エラー",
                    StringComparison.Ordinal)
                && fatalStartup.Content is not null,
                "Fatal/bootstrap failure did not render a Lumine product-state window.");
            fatalStartup.Close();

            // Strict navigation acceptance: large secondary collections must
            // stay virtualized, active/offline/disabled library rows must
            // match their interaction semantics, and async command failures
            // must remain inside the product error boundary.
            var activeNavRoot =
                Path.Combine(root, "nav-active");
            var openNavRoot =
                Path.Combine(root, "nav-open");
            Directory.CreateDirectory(activeNavRoot);
            Directory.CreateDirectory(openNavRoot);

            var navLibraries =
                new[]
                {
                    new LibraryCatalogItem(
                        1001,
                        "Active library",
                        activeNavRoot,
                        true,
                        LibraryScanState.Complete,
                        12,
                        DateTimeOffset.UtcNow,
                        null),
                    new LibraryCatalogItem(
                        1002,
                        "Openable library",
                        openNavRoot,
                        true,
                        LibraryScanState.Complete,
                        8,
                        DateTimeOffset.UtcNow,
                        null),
                    new LibraryCatalogItem(
                        1003,
                        "Offline library",
                        Path.Combine(root, "nav-missing"),
                        true,
                        LibraryScanState.Complete,
                        4,
                        DateTimeOffset.UtcNow,
                        null),
                    new LibraryCatalogItem(
                        1004,
                        "Disabled library",
                        openNavRoot,
                        false,
                        LibraryScanState.Complete,
                        3,
                        DateTimeOffset.UtcNow,
                        null)
                };

            string? navigationError = null;
            var openAttempts = 0;
            var rescanAttempts = 0;
            var navigationView =
                ProductNavigationViews.CreateLibraries(
                    navLibraries,
                    activeLibraryId: 1001,
                    addLibrary: static () => Task.CompletedTask,
                    openLibrary: item =>
                    {
                        openAttempts++;
                        return item.Id == 1002
                            ? Task.FromException(
                                new InvalidOperationException(
                                    "navigation-smoke-failure"))
                            : Task.CompletedTask;
                    },
                    rescanLibrary: _ =>
                    {
                        rescanAttempts++;
                        return Task.CompletedTask;
                    },
                    toggleEnabled: static _ => Task.CompletedTask,
                    removeLibrary: static _ => Task.CompletedTask,
                    reportError: message =>
                        navigationError = message);

            var navigationWindow =
                new Window
                {
                    Width = 420,
                    Height = 600,
                    Content = navigationView
                };
            navigationWindow.Show();
            Dispatcher.UIThread.RunJobs();

            TextBlock FindNavigationTitle(string text) =>
                navigationView.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .First(block =>
                        string.Equals(
                            block.Text,
                            text,
                            StringComparison.Ordinal));

            Require(
                FindNavigationTitle("Active library")
                    .FindAncestorOfType<Button>() is null,
                "Active library still presents as an enabled no-op command.");

            var rescanButton =
                navigationView.GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(
                        button =>
                            string.Equals(
                                button.Content as string,
                                "再スキャン",
                                StringComparison.Ordinal));
            Require(
                rescanButton is { IsEnabled: true },
                "Active library did not expose the direct rescan action.");
            rescanButton.RaiseEvent(
                new RoutedEventArgs(
                    Button.ClickEvent));
            for (var attempt = 0;
                 attempt < 50
                 && rescanAttempts == 0;
                 attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(1);
            }
            Require(
                rescanAttempts == 1,
                "Active library rescan action did not invoke its callback.");

            var openableButton =
                FindNavigationTitle("Openable library")
                    .FindAncestorOfType<Button>();
            var offlineButton =
                FindNavigationTitle("Offline library")
                    .FindAncestorOfType<Button>();
            var disabledButton =
                FindNavigationTitle("Disabled library")
                    .FindAncestorOfType<Button>();

            Require(
                openableButton is { IsEnabled: true }
                && offlineButton is { IsEnabled: false }
                && disabledButton is { IsEnabled: false },
                "Library navigation interaction state drifted for openable/offline/disabled rows.");

            openableButton.RaiseEvent(
                new RoutedEventArgs(
                    Button.ClickEvent));

            for (var attempt = 0;
                 attempt < 100
                 && navigationError is null;
                 attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(1);
            }

            Require(
                openAttempts == 1
                && navigationError?.Contains(
                    "navigation-smoke-failure",
                    StringComparison.Ordinal) == true
                && openableButton.IsEnabled,
                "Async library open failure escaped the navigation error boundary or left the command disabled.");

            navigationError = null;
            Require(
                openableButton.Focus(
                    NavigationMethod.Tab,
                    KeyModifiers.None),
                "Openable library row did not accept keyboard focus.");
            openableButton.RaiseEvent(
                new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Enter
                });
            openableButton.RaiseEvent(
                new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyUpEvent,
                    Key = Key.Enter
                });

            for (var attempt = 0;
                 attempt < 100
                 && navigationError is null;
                 attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(1);
            }

            Require(
                openAttempts == 2
                && navigationError?.Contains(
                    "navigation-smoke-failure",
                    StringComparison.Ordinal) == true
                && !offlineButton.Focus(
                    NavigationMethod.Tab,
                    KeyModifiers.None)
                && !disabledButton.Focus(
                    NavigationMethod.Tab,
                    KeyModifiers.None),
                "Library row keyboard behavior drifted for openable/offline/disabled states.");

            foreach (var commandPath in
                     new[]
                     {
                         "enable",
                         "remove"
                     })
            {
                var command =
                    new MenuItem
                    {
                        Header = commandPath
                    };
                string? commandError = null;

                await ProductNavigationViews.ExecuteAsyncForSmoke(
                    command,
                    () => Task.FromException(
                        new InvalidOperationException(
                            $"{commandPath}-navigation-smoke-failure")),
                    message =>
                        commandError = message);

                Require(
                    command.IsEnabled
                    && commandError?.Contains(
                        $"{commandPath}-navigation-smoke-failure",
                        StringComparison.Ordinal) == true,
                    $"Async library {commandPath} failure escaped the shared navigation error boundary or left the command disabled.");
            }

            navigationWindow.Close();
            Dispatcher.UIThread.RunJobs();

            var largeFolders =
                Enumerable.Range(0, 10_000)
                    .Select(index =>
                        new LibraryFolderInfo(
                            index + 1,
                            $"root/folder-{index:D5}",
                            2,
                            index % 17))
                    .ToArray();
            var largeTags =
                Enumerable.Range(0, 10_000)
                    .Select(index =>
                        new LibraryTagInfo(
                            index + 1,
                            $"tag-{index:D5}",
                            "#6366f1",
                            index % 31))
                    .ToArray();
            var now =
                DateTimeOffset.UtcNow;
            var largePublications =
                Enumerable.Range(0, 2_000)
                    .Select(index =>
                        new PublicationInfo(
                            index + 1,
                            1,
                            null,
                            $"Publication {index:D4}",
                            string.Empty,
                            string.Empty,
                            "Pixiv",
                            string.Empty,
                            now.AddMinutes(-index),
                            string.Empty,
                            string.Empty,
                            "{}",
                            Array.Empty<PublicationAssetSnapshot>(),
                            now,
                            now))
                    .ToArray();

            static int RealizedNavigationRows(Control view)
            {
                var list =
                    view as ListBox
                    ?? view.GetVisualDescendants()
                        .OfType<ListBox>()
                        .FirstOrDefault()
                    ?? throw new InvalidOperationException(
                        "Navigation view did not contain a ListBox.");
                return list.GetRealizedContainers().Count();
            }

            var navigationScaleWatch =
                Stopwatch.StartNew();
            var foldersView =
                ProductNavigationViews.CreateFolders(
                    largeFolders,
                    null,
                    new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase),
                    static _ => Task.CompletedTask);
            var scaleWindow =
                new Window
                {
                    Width = 420,
                    Height = 600,
                    Content = foldersView
                };
            scaleWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Require(
                RealizedNavigationRows(foldersView) is > 0 and < 128,
                "10k folder navigation materialized an unbounded visual tree.");
            scaleWindow.Close();

            var hierarchyExpansion =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            var hierarchyFolders =
                new[]
                {
                    new LibraryFolderInfo(
                        1,
                        "root",
                        1,
                        4),
                    new LibraryFolderInfo(
                        2,
                        "root/a",
                        2,
                        2),
                    new LibraryFolderInfo(
                        3,
                        "root/a/deep",
                        3,
                        1),
                    new LibraryFolderInfo(
                        4,
                        "root/b",
                        2,
                        1)
                };
            var hierarchyView =
                ProductNavigationViews.CreateFolders(
                    hierarchyFolders,
                    null,
                    hierarchyExpansion,
                    static _ => Task.CompletedTask);
            var hierarchyWindow =
                new Window
                {
                    Width = 420,
                    Height = 600,
                    Content = hierarchyView
                };
            hierarchyWindow.Show();
            Dispatcher.UIThread.RunJobs();

            var hierarchyList =
                hierarchyView
                    .GetVisualDescendants()
                    .OfType<ListBox>()
                    .First();
            static int VisibleFolderCount(
                ListBox list) =>
                (list.ItemsSource as IEnumerable<LibraryFolderInfo>)
                    ?.Count()
                ?? 0;

            Require(
                VisibleFolderCount(hierarchyList) == 1,
                "Collapsed folder tree exposed descendants before disclosure.");

            var rootDisclosure =
                hierarchyView
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .First(button =>
                        string.Equals(
                            button.Content as string,
                            "▶",
                            StringComparison.Ordinal));
            rootDisclosure.RaiseEvent(
                new RoutedEventArgs(
                    Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Require(
                VisibleFolderCount(hierarchyList) == 3
                && hierarchyExpansion.Contains("root"),
                "Expanding a root folder did not reveal only its direct children.");

            var nestedDisclosure =
                hierarchyView
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .First(button =>
                        string.Equals(
                            button.Content as string,
                            "▶",
                            StringComparison.Ordinal));
            nestedDisclosure.RaiseEvent(
                new RoutedEventArgs(
                    Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Require(
                VisibleFolderCount(hierarchyList) == 4
                && hierarchyExpansion.Contains("root/a"),
                "Expanding a nested folder did not reveal its descendants.");

            hierarchyWindow.Close();

            var tagsView =
                ProductNavigationViews.CreateTags(
                    largeTags,
                    Array.Empty<string>(),
                    static _ => Task.CompletedTask,
                    static (_, _) => Task.CompletedTask,
                    static _ => Task.CompletedTask);
            scaleWindow =
                new Window
                {
                    Width = 420,
                    Height = 600,
                    Content = tagsView
                };
            scaleWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Require(
                RealizedNavigationRows(tagsView) is > 0 and < 128,
                "10k tag navigation materialized an unbounded visual tree.");

            var tagSearch =
                tagsView.GetVisualDescendants()
                    .OfType<TextBox>()
                    .First(box =>
                        string.Equals(
                            box.PlaceholderText,
                            "タグを検索",
                            StringComparison.Ordinal));
            var tagList =
                tagsView.GetVisualDescendants()
                    .OfType<ListBox>()
                    .First();
            tagSearch.Text = "tag-09999";
            await Task.Delay(180);
            Dispatcher.UIThread.RunJobs();

            Require(
                tagList.ItemsSource?.Cast<LibraryTagInfo>()
                    .SingleOrDefault()?.Name
                    == "tag-09999"
                && tagList.GetRealizedContainers().Count() < 128,
                "Large tag filtering failed to debounce/filter while retaining bounded realization.");
            scaleWindow.Close();

            var publicationsView =
                ProductNavigationViews.CreatePublicationEntry(
                    largePublications);
            scaleWindow =
                new Window
                {
                    Width = 420,
                    Height = 600,
                    Content = publicationsView
                };
            scaleWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Require(
                RealizedNavigationRows(publicationsView) is > 0 and < 128,
                "2k publication navigation materialized an unbounded visual tree.");
            scaleWindow.Close();
            Dispatcher.UIThread.RunJobs();
            navigationScaleWatch.Stop();

            Require(
                navigationScaleWatch.Elapsed
                    < TimeSpan.FromSeconds(2),
                $"High-count navigation acceptance exceeded the responsiveness budget: {navigationScaleWatch.Elapsed.TotalMilliseconds:N0} ms.");

            var original = await provider.LoadOriginalAsync(
                asset,
                8L * 1024 * 1024);

            try
            {
                Require(
                    original.Metadata.Width == 320
                    && original.Metadata.Height == 200
                    && original.Bitmap.PixelSize.Width == 320
                    && original.Bitmap.PixelSize.Height == 200,
                    "Production Detail adapter returned incorrect full-resolution dimensions.");

                var writable = original.Bitmap as WriteableBitmap
                    ?? throw new InvalidOperationException(
                        "Production Detail adapter did not return a WriteableBitmap.");

                using var framebuffer = writable.Lock();
                Require(
                    framebuffer.RowBytes >= 320 * 4,
                    "Production Detail framebuffer stride is smaller than one RGBA row.");

                var pixel = new byte[4];
                Marshal.Copy(
                    framebuffer.Address,
                    pixel,
                    0,
                    pixel.Length);

                Require(
                    pixel[0] == 20
                    && pixel[1] == 80
                    && pixel[2] == 160
                    && pixel[3] is >= 127 and <= 129,
                    $"Production Detail framebuffer copy corrupted RGBA bytes: {string.Join(",", pixel)}.");
            }
            finally
            {
                var disposal = original.BeginDispose();
                Dispatcher.UIThread.RunJobs();
                await disposal;
            }

            var bmpOriginal =
                await provider.LoadOriginalAsync(
                    bmpAsset,
                    1024 * 1024);

            try
            {
                Require(
                    bmpOriginal.Metadata.Width == 7
                    && bmpOriginal.Metadata.Height == 5
                    && string.Equals(
                        bmpOriginal.Metadata.Format,
                        "bmp",
                        StringComparison.Ordinal),
                    "Production Detail adapter returned incorrect BMP metadata.");

                var bmpWritable =
                    bmpOriginal.Bitmap
                        as WriteableBitmap
                    ?? throw new InvalidOperationException(
                        "Production Detail BMP did not return a WriteableBitmap.");

                using var bmpFramebuffer =
                    bmpWritable.Lock();
                var bmpPixel = new byte[4];
                Marshal.Copy(
                    bmpFramebuffer.Address,
                    bmpPixel,
                    0,
                    bmpPixel.Length);

                Require(
                    bmpPixel[0] == 12
                    && bmpPixel[1] == 34
                    && bmpPixel[2] == 56
                    && bmpPixel[3] == 255,
                    $"Production Detail BMP framebuffer copy corrupted RGBA bytes: {string.Join(",", bmpPixel)}.");
            }
            finally
            {
                var bmpDisposal =
                    bmpOriginal.BeginDispose();
                Dispatcher.UIThread.RunJobs();
                await bmpDisposal;
            }

            try
            {
                _ = await provider.LoadOriginalAsync(
                    asset,
                    1024);
                throw new InvalidOperationException(
                    "Production Detail adapter ignored its decoded-byte budget.");
            }
            catch (FullResolutionBudgetExceededException)
            {
            }

            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();

                try
                {
                    using var unexpected =
                        ImageViewerDetailProvider.CreateOriginalBitmap(
                            new FullResolutionInfo(
                                6000,
                                4000,
                                true,
                                6000L * 4000 * 4),
                            cancelled.Token);
                    throw new InvalidOperationException(
                        "Production Detail bitmap allocation ignored cancellation.");
                }
                catch (OperationCanceledException)
                {
                }
            }

            return 0;
        },
        CancellationToken.None);

    var currentBeforeReplacement = await restartedLibraryService.GetAssetAsync(
        library.Id,
        "adapter-source.png")
        ?? throw new InvalidOperationException(
            "App smoke asset disappeared after original-load identity repair.");

    asset = new ViewerAsset(
        currentBeforeReplacement.Id,
        currentBeforeReplacement.SourceRevision,
        currentBeforeReplacement.RelativePath,
        currentBeforeReplacement.FileName,
        currentBeforeReplacement.FileSize,
        currentBeforeReplacement.ModifiedAtUtc.UtcDateTime.Ticks,
        currentBeforeReplacement.Width,
        currentBeforeReplacement.Height,
        currentBeforeReplacement.Format,
        currentBeforeReplacement.SourceIdentity,
        currentBeforeReplacement.RawWidth,
        currentBeforeReplacement.RawHeight,
        currentBeforeReplacement.HasAlpha);

    var staleRevision = asset.SourceRevision;
    var staleIdentity = asset.SourceIdentity
        ?? throw new InvalidOperationException(
            "App smoke asset lost source identity before repair test.");
    var staleLength = new FileInfo(sourcePath).Length;
    var staleTimestamp = File.GetLastWriteTimeUtc(sourcePath);
    await File.WriteAllBytesAsync(
        sourcePath,
        await File.ReadAllBytesAsync(replacementPath));
    File.SetLastWriteTimeUtc(sourcePath, staleTimestamp);

    var sameStatReplacement = new FileInfo(sourcePath);
    Require(
        sameStatReplacement.Length == staleLength
        && sameStatReplacement.LastWriteTimeUtc.Ticks
            == staleTimestamp.Ticks,
        "App same-stat replacement fixture did not preserve size/mtime.");

    await headless.Dispatch(
        async () =>
        {
            var repaired = await provider.LoadOriginalAsync(
                asset,
                8L * 1024 * 1024);

            try
            {
                var writable = repaired.Bitmap as WriteableBitmap
                    ?? throw new InvalidOperationException(
                        "Repaired original did not return a WriteableBitmap.");
                using var framebuffer = writable.Lock();
                var pixel = new byte[4];
                Marshal.Copy(
                    framebuffer.Address,
                    pixel,
                    0,
                    pixel.Length);

                Require(
                    pixel[0] == 180
                    && pixel[1] == 40
                    && pixel[2] == 70
                    && pixel[3] is >= 127 and <= 129,
                    $"Identity repair decoded stale pixels: {string.Join(",", pixel)}.");
            }
            finally
            {
                var disposal = repaired.BeginDispose();
                Dispatcher.UIThread.RunJobs();
                await disposal;
            }

            return 0;
        },
        CancellationToken.None);

    var repairedAsset = await restartedLibraryService.GetAssetAsync(
        library.Id,
        "adapter-source.png")
        ?? throw new InvalidOperationException(
            "Identity-repaired asset disappeared.");
    Require(
        repairedAsset.SourceRevision == staleRevision + 1,
        "Identity mismatch did not self-heal by advancing source_revision.");
    Require(
        FileSourceIdentityProbe.IsValid(repairedAsset.SourceIdentity)
        && !string.Equals(
            repairedAsset.SourceIdentity,
            staleIdentity,
            StringComparison.Ordinal),
        "Identity repair did not persist the replacement source identity.");

    var shellLibraryRoot =
        Path.Combine(root, "shell-library");
    var shellDataRoot =
        Path.Combine(root, "shell-data");
    Directory.CreateDirectory(shellLibraryRoot);

    var shellImagePath =
        Path.Combine(shellLibraryRoot, "viewer-shell.bmp");
    WriteBmp24(
        shellImagePath,
        width: 1200,
        height: 900);
    WriteBmp24(
        Path.Combine(
            shellLibraryRoot,
            "viewer-shell-second.bmp"),
        width: 640,
        height: 480);

    var shellRuntime =
        await CoreViewerRuntime.OpenAsync(
            shellLibraryRoot,
            AppDataPaths.FromRoot(shellDataRoot),
            Lumine.App.Program.ResourcePolicy);

    Require(
        shellRuntime.AssetCount == 2,
        $"Production Core Viewer runtime indexed {shellRuntime.AssetCount} assets; expected 2.");

    await headless.Dispatch(
        async () =>
        {
            var metadataRefreshCount = 0;
            var shell =
                new CoreViewerShell(
                    shellRuntime,
                    afterBulkMutation:
                        () =>
                        {
                            metadataRefreshCount++;
                            return Task.CompletedTask;
                        });
            var window =
                new Avalonia.Controls.Window
                {
                    Width = 1100,
                    Height = 720,
                    Content = shell
                };

            window.Show();
            Dispatcher.UIThread.RunJobs();

            Require(
                shell.GridViewer.AssetCount == 2,
                "Real App shell did not expose the runtime asset count.");

            Require(
                !shell.IsContextDetailVisible
                && !shell.IsFocusedViewVisible,
                "Context/focused surfaces should not consume the initial browse workspace.");

            shell.GridViewer.SelectAsset(0);
            Dispatcher.UIThread.RunJobs();
            Require(
                !shell.IsBulkSelectionBarVisible,
                "Single selection unexpectedly displaced the browse grid with bulk chrome.");

            var firstContextAsset =
                await shellRuntime.ViewerSession.GetAssetAsync(0);
            await shellRuntime.LibraryService.SetUserMetadataAsync(
                shellRuntime.Library.Id,
                firstContextAsset.Id,
                new AssetUserMetadataUpdate(
                    Rating: 5,
                    Favorite: true,
                    Notes: "context-detail-note",
                    StatusLabel: "candidate",
                    ColorLabel: "purple",
                    Tags: ["context-tag"]));

            await shell.ShowContextDetailAsync();
            Dispatcher.UIThread.RunJobs();

            Require(
                shell.IsContextDetailVisible
                && shell.ContextDetail.AssetId
                    == firstContextAsset.Id,
                "Contextual detail panel did not open for the primary selection.");
            Require(
                shell.ContextDetail.TitleText
                    == firstContextAsset.DisplayName
                && shell.ContextDetail.PathText.Contains(
                    firstContextAsset.RelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase),
                "Contextual detail panel did not expose image/path context.");
            Require(
                shell.ContextDetail.RatingText == "★5"
                && shell.ContextDetail.TagsText.Contains(
                    "context-tag",
                    StringComparison.Ordinal)
                && shell.ContextDetail.NotesText
                    == "context-detail-note",
                "Contextual detail panel did not expose user-owned metadata.");

            Require(
                shell.ContextDetail.UsesDirectRatingControlsForSmoke
                && shell.ContextDetail.UsesDirectColorControlsForSmoke
                && !shell.ContextDetail.RetryVisibleForSmoke,
                "Inspector did not expose direct rating/color controls in the successful loaded state.");

            shell.ContextDetail.InvokeRatingForSmoke(4);
            shell.ContextDetail.InvokeColorForSmoke(4);
            Dispatcher.UIThread.RunJobs();
            Require(
                shell.ContextDetail.RatingText == "★4"
                && shell.ContextDetail.ColorLabelForSmoke == "green"
                && shell.ContextDetail.IsDirty,
                "One-click Inspector rating/color controls did not update visible editor state.");

            shell.ContextDetail.InvokeRatingForSmoke(4);
            Dispatcher.UIThread.RunJobs();
            Require(
                shell.ContextDetail.RatingText == "未設定",
                "Clicking the active Inspector rating did not clear the rating.");

            var runtimeBeforeInspectorRetry =
                shellRuntime;
            shell.ContextDetail.PresentLoadFailureForSmoke();
            Dispatcher.UIThread.RunJobs();
            Require(
                shell.ContextDetail.RetryVisibleForSmoke,
                "Inspector load failure did not expose a direct retry action.");

            await shell.ContextDetail.InvokeRetryForSmokeAsync();
            Dispatcher.UIThread.RunJobs();
            Require(
                ReferenceEquals(
                    shellRuntime,
                    runtimeBeforeInspectorRetry)
                && !shell.ContextDetail.RetryVisibleForSmoke
                && shell.ContextDetail.RatingText == "★5"
                && shell.ContextDetail.ColorLabelForSmoke == "purple"
                && !shell.ContextDetail.IsDirty,
                "Inspector retry did not reload metadata in place without recreating Viewer runtime.");

            Require(
                shell.ContextDetail.HasPreview
                && shell.ContextDetail.TabHeaders.SequenceEqual(
                    new[] { "整理", "制作", "公開", "情報" })
                && shell.ContextDetail.TabStripUsesLumineStatesForSmoke
                && shell.ContextDetail.TabPagesHaveIndependentScrollStateForSmoke,
                "Inspector did not expose the selected image preview, four-destination Lumine tab strip, and independent scroll ownership.");

            shell.ContextDetail.SelectTabForSmoke(3);
            Dispatcher.UIThread.RunJobs();
            Require(
                shell.ContextDetail.SelectedTabIndex == 3
                && shell.ContextDetail.SelectedTabHeader == "情報"
                && shell.ContextDetail.PathText.Contains(
                    firstContextAsset.RelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase),
                "Inspector information tab did not remain reachable after the tabbed redesign.");

            shell.ContextDetail.SelectTabForSmoke(0);
            Dispatcher.UIThread.RunJobs();

            shell.ContextDetail.SetEditorValuesForSmoke(
                rating: 3,
                favorite: false,
                statusLabel: "reviewed",
                colorLabel: "green",
                tags: "context-tag, edited-tag",
                notes: "metadata-editor-search-token");
            Require(
                shell.ContextDetail.IsDirty,
                "Contextual metadata editor did not expose its unsaved state.");

            var savedContextMetadata =
                await shell.ContextDetail.SaveEditorAsync();
            Require(
                savedContextMetadata is not null
                && savedContextMetadata.Rating == 3
                && !savedContextMetadata.Favorite
                && savedContextMetadata.StatusLabel == "reviewed"
                && savedContextMetadata.ColorLabel == "green"
                && savedContextMetadata.Tags.Contains("context-tag")
                && savedContextMetadata.Tags.Contains("edited-tag")
                && savedContextMetadata.Notes
                    == "metadata-editor-search-token",
                "Contextual metadata editor did not persist the complete user metadata record.");
            Require(
                metadataRefreshCount == 1
                && !shell.ContextDetail.IsDirty,
                "Contextual metadata save did not notify the browse/navigation refresh path exactly once.");

            var secondCreativeAsset =
                await shellRuntime.ViewerSession.GetAssetAsync(1);
            shell.GridViewer.SelectAsset(
                1,
                scrollIntoView: false,
                mode: ViewerSelectionMode.Toggle);
            Dispatcher.UIThread.RunJobs();

            Require(
                shell.GridViewer.SelectedAssetCount == 2,
                "Creative archive smoke did not establish a two-asset selection.");

            Require(
                shell.IsBulkSelectionBarVisible,
                "Multi-selection did not expose the contextual bulk action bar.");

            var smokeWork =
                await shell.CreateWorkFromSelectionAsync(
                    new CreativeWorkDialogResult(
                        "App Smoke Work",
                        "creative archive"));
            Require(
                smokeWork is not null
                && smokeWork.Assets.Select(
                        static asset => asset.Id)
                    .SequenceEqual(
                        new[]
                        {
                            firstContextAsset.Id,
                            secondCreativeAsset.Id
                        }),
                "App selection organizer did not create an ordered Work.");

            var smokeGroup =
                await shell.CreateGenerationGroupFromSelectionAsync(
                    new CreativeGroupDialogResult(
                        "App Smoke Group",
                        smokeWork!.Id,
                        "app-smoke-prompt",
                        "app-smoke-negative",
                        "app-smoke-model",
                        "euler",
                        "normal",
                        24,
                        4.5,
                        "{\"workflow\":true}",
                        "manual group"));
            Require(
                smokeGroup is not null
                && smokeGroup.WorkId == smokeWork.Id
                && smokeGroup.Assets.Count == 2
                && smokeGroup.Prompt == "app-smoke-prompt",
                "App selection organizer did not create Generation Group context.");

            var smokeRelation =
                await shell.CreateRelationFromSelectionAsync(
                    new CreativeRelationDialogResult(
                        ReverseDirection: false,
                        RelationType: "img2img",
                        Note: "app-smoke-lineage"));
            Require(
                smokeRelation is not null
                && smokeRelation.Parent.Id
                    == firstContextAsset.Id
                && smokeRelation.Child.Id
                    == secondCreativeAsset.Id,
                "App selection organizer did not preserve explicit lineage direction.");

            var smokePublication =
                await shell.CreatePublicationFromSelectionAsync(
                    new CreativePublicationDialogResult(
                        smokeWork.Id,
                        "Pixiv",
                        "@app-smoke",
                        "App Smoke Publication",
                        "snapshot body",
                        "app-smoke #publication",
                        new DateTimeOffset(
                            2026,
                            10,
                            2,
                            6,
                            30,
                            0,
                            TimeSpan.Zero),
                        "external-smoke",
                        "https://example.invalid/app-smoke",
                        "{\"ageRestriction\":\"all\",\"aiGenerated\":true}"));
            Require(
                smokePublication is not null
                && smokePublication.Assets.Count == 2
                && smokePublication.Assets[0].AssetId
                    == firstContextAsset.Id
                && smokePublication.Assets[1].AssetId
                    == secondCreativeAsset.Id,
                "App selection organizer did not create an ordered Publication snapshot.");

            var creativeContext =
                await shellRuntime.LibraryService
                    .GetAssetCreativeContextAsync(
                        shellRuntime.Library.Id,
                        secondCreativeAsset.Id);
            Require(
                creativeContext.Works.Any(
                    work => work.Id == smokeWork.Id)
                && creativeContext.GenerationGroups.Any(
                    group => group.Id == smokeGroup!.Id)
                && creativeContext.Relations.Any(
                    relation => relation.Id == smokeRelation!.Id)
                && creativeContext.Publications.Any(
                    publication =>
                        publication.Id == smokePublication!.Id),
                "Creative context query did not return all archive concepts for the selected asset.");

            shell.ContextDetail.SelectTabForSmoke(1);
            Dispatcher.UIThread.RunJobs();

            Require(
                shell.ContextDetail.WorksText.Contains(
                    "App Smoke Work",
                    StringComparison.Ordinal)
                && shell.ContextDetail.GroupsText.Contains(
                    "App Smoke Group",
                    StringComparison.Ordinal)
                && shell.ContextDetail.RelationsText.Contains(
                    "img2img",
                    StringComparison.Ordinal),
                "Contextual detail did not render human-readable creative archive context.");

            Require(
                shell.ContextDetail.SelectedTabHeader == "制作",
                "Inspector creative context was not reachable through the dedicated 制作 tab.");

            shell.ContextDetail.SelectTabForSmoke(2);
            Dispatcher.UIThread.RunJobs();
            Require(
                shell.ContextDetail.SelectedTabHeader == "公開"
                && shell.ContextDetail.PublicationsText.Contains(
                    "App Smoke Publication",
                    StringComparison.Ordinal)
                && shell.ContextDetail
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Any(button =>
                        string.Equals(
                            button.Content as string,
                            "公開記録を作成",
                            StringComparison.Ordinal)),
                "Inspector publication context/actions were not isolated under the dedicated 公開 tab.");

            var publicationHistory =
                await shellRuntime.LibraryService.ListPublicationsAsync(
                    shellRuntime.Library.Id,
                    limit: 10);
            var publicationView =
                ProductNavigationViews.CreatePublicationEntry(
                    publicationHistory);
            Require(
                publicationHistory.Count == 1
                && publicationHistory[0].Id
                    == smokePublication.Id
                && publicationView is not null,
                "Publication navigation did not render persisted publication history.");

            // Single-image creative workflows must be reachable without
            // entering bulk-selection mode.
            shell.GridViewer.SelectAsset(0);
            await shell.ShowContextDetailAsync();
            shell.ContextDetail.SelectTabForSmoke(1);
            Dispatcher.UIThread.RunJobs();

            Require(
                !shell.IsBulkSelectionBarVisible
                && shell.ContextDetail
                    .GetVisualDescendants()
                    .OfType<DropDownButton>()
                    .Any(button =>
                        string.Equals(
                            button.Content as string,
                            "新規作成",
                            StringComparison.Ordinal)),
                "Single-selection Inspector did not expose its creative creation menu.");

            var singleWork =
                await shell.CreateWorkFromSelectionAsync(
                    new CreativeWorkDialogResult(
                        "Single Asset Work",
                        "single-image acceptance"));
            Require(
                singleWork is not null
                && singleWork.Assets.Count == 1
                && singleWork.Assets[0].Id
                    == firstContextAsset.Id,
                "Single-image Work creation is not semantically reachable.");

            var singleGroup =
                await shell.CreateGenerationGroupFromSelectionAsync(
                    new CreativeGroupDialogResult(
                        "Single Asset Group",
                        singleWork!.Id,
                        "single prompt",
                        string.Empty,
                        "single-model",
                        "euler",
                        "normal",
                        12,
                        3.5,
                        "{}",
                        "single-image acceptance"));
            Require(
                singleGroup is not null
                && singleGroup.Assets.Count == 1
                && singleGroup.Assets[0].Id
                    == firstContextAsset.Id,
                "Single-image Generation Group creation is not semantically reachable.");

            var singlePublication =
                await shell.CreatePublicationFromSelectionAsync(
                    new CreativePublicationDialogResult(
                        singleWork.Id,
                        "Pixiv",
                        "@single",
                        "Single Asset Publication",
                        string.Empty,
                        string.Empty,
                        new DateTimeOffset(
                            2026,
                            10,
                            3,
                            0,
                            0,
                            0,
                            TimeSpan.Zero),
                        "single-acceptance",
                        "https://example.invalid/single",
                        "{}"));
            Require(
                singlePublication is not null
                && singlePublication.Assets.Count == 1
                && singlePublication.Assets[0].AssetId
                    == firstContextAsset.Id,
                "Single-image Publication creation is not semantically reachable.");

            var editedQueryPage =
                await shellRuntime.LibraryService.GetAssetPageAsync(
                    shellRuntime.Library.Id,
                    new AssetQuery(
                        SearchText: "metadata-editor-search-token",
                        RequiredTags: ["edited-tag"],
                        MinRating: 3,
                        MaxRating: 3,
                        Favorite: false,
                        StatusLabel: "reviewed",
                        ColorLabel: "green"),
                    limit: 10);
            Require(
                editedQueryPage.Items.Count == 1
                && editedQueryPage.Items[0].Id
                    == firstContextAsset.Id,
                "Metadata editor save was not immediately visible to composed search/filter queries.");

            shell.HideContextDetail();
            Require(
                !shell.IsContextDetailVisible,
                "Contextual detail panel did not return the workspace to full browse width.");

            await shell.OpenFocusedViewAsync(0);
            Dispatcher.UIThread.RunJobs();
            Require(
                shell.IsFocusedViewVisible
                && shell.DetailViewer.SelectedAssetIndex == 0
                && shell.DetailViewer.LoadState
                    == ViewerDetailLoadState.PreviewReady,
                "Focused viewer did not reuse the Detail engine for the selected asset.");

            shell.CloseFocusedView();
            Require(
                !shell.IsFocusedViewVisible,
                "Focused viewer did not return to the browse workspace.");

            await shell.DetailViewer.SelectAsync(0);
            Dispatcher.UIThread.RunJobs();

            Require(
                shell.DetailViewer.LoadState
                    == ViewerDetailLoadState.PreviewReady,
                $"Real App shell Detail preview did not become ready: {shell.DetailViewer.LoadState}.");

            await shell.DetailViewer.ActualSizeAsync();
            Dispatcher.UIThread.RunJobs();

            Require(
                shell.DetailViewer.IsOriginal,
                "Real App shell 1:1 path did not promote to the full-resolution original.");

            await shell.DetailViewer.ZoomByAsync(1.25);
            Require(
                shell.DetailViewer.Zoom > 1,
                "Real App shell zoom command did not update the production Detail control.");

            shell.DetailViewer.PanBy(24, 16);
            Require(
                shell.DetailViewer.PanOffset.X >= 0
                && shell.DetailViewer.PanOffset.Y >= 0,
                "Real App shell pan produced an invalid scroll offset.");

            shell.DetailViewer.Fit();
            Require(
                shell.DetailViewer.SelectedAssetIndex == 0,
                "Real App shell lost Detail selection during Fit.");

            await shell.DetailViewer.SelectAsync(1);
            Require(
                shell.DetailViewer.SelectedAssetIndex == 1,
                "Real App shell did not move to the next asset.");

            await shell.DetailViewer.ActualSizeAsync();
            Dispatcher.UIThread.RunJobs();
            Require(
                shell.DetailViewer.IsOriginal,
                "Real App shell did not admit the next original after compositor-safe release.");

            await shell.DetailViewer.SelectAsync(0);
            await shell.DetailViewer.ActualSizeAsync();
            Dispatcher.UIThread.RunJobs();
            Require(
                shell.DetailViewer.SelectedAssetIndex == 0
                && shell.DetailViewer.IsOriginal,
                "Real App shell did not move back and re-admit an original through the composition release contract.");

            shell.SetBrowseLayout(
                new BrowsePreferences(
                    BrowseViewMode.List,
                    2,
                    AssetSortOrder.ModifiedNewest));
            Dispatcher.UIThread.RunJobs();

            Require(
                shell.GridViewer.LayoutMode
                    == ViewerLayoutMode.List
                && shell.GridViewer.DensityLevel == 2
                && shell.GridViewer.Columns == 1,
                "List browse mode did not preserve single-column virtualization.");

            shell.SetBrowseLayout(
                new BrowsePreferences(
                    BrowseViewMode.Grid,
                    0,
                    AssetSortOrder.ModifiedNewest));
            Dispatcher.UIThread.RunJobs();

            Require(
                shell.GridViewer.LayoutMode
                    == ViewerLayoutMode.Grid
                && shell.GridViewer.DensityLevel == 0
                && shell.GridViewer.Columns >= 1,
                "Density/view-mode switch did not reuse the active Viewer session.");

            var navigationAsset =
                await shellRuntime.ViewerSession.GetAssetAsync(0);
            await shellRuntime.LibraryService.SetUserMetadataAsync(
                shellRuntime.Library.Id,
                navigationAsset.Id,
                new AssetUserMetadataUpdate(
                    Rating: 4,
                    Favorite: true,
                    Notes: "browse-search-token",
                    StatusLabel: "reviewed",
                    ColorLabel: "blue",
                    Tags: ["navigation-smoke"]));

            window.Close();
            Dispatcher.UIThread.RunJobs();
            await shell.DetachAsync();

            await shellRuntime.ApplyQueryAsync(
                new AssetQuery(
                    RequiredTags: ["navigation-smoke"]));
            Require(
                shellRuntime.AssetCount == 1
                && shellRuntime.CurrentQuery?.RequiredTags is { Count: 1 },
                "CoreViewerRuntime did not apply the navigation tag query in place.");

            var filteredAsset =
                await shellRuntime.ViewerSession.GetAssetAsync(0);
            Require(
                filteredAsset.Id == navigationAsset.Id,
                "Filtered Viewer session did not expose the tagged asset.");

            await shellRuntime.ApplyQueryAsync(
                new AssetQuery(
                    SearchText: "browse",
                    RequiredTags: ["navigation-smoke"],
                    MinRating: 4,
                    Favorite: true,
                    StatusLabel: "reviewed",
                    ColorLabel: "blue"));
            Require(
                shellRuntime.AssetCount == 1,
                "Composed browse search/filter query did not flow through the Viewer runtime.");

            foreach (var sortOrder in new[]
                     {
                         AssetSortOrder.ModifiedNewest,
                         AssetSortOrder.ModifiedOldest,
                         AssetSortOrder.FileNameAscending,
                         AssetSortOrder.FileNameDescending,
                         AssetSortOrder.CreatedNewest,
                         AssetSortOrder.CreatedOldest,
                         AssetSortOrder.FileSizeLargest,
                         AssetSortOrder.FileSizeSmallest,
                         AssetSortOrder.RatingHighest,
                         AssetSortOrder.RatingLowest,
                         AssetSortOrder.StatusAscending,
                         AssetSortOrder.StatusDescending
                     })
            {
                await shellRuntime.ApplyQueryAsync(
                    new AssetQuery(
                        SortOrder: sortOrder));

                var firstSorted =
                    await shellRuntime.ViewerSession
                        .GetAssetAsync(0);
                var secondSorted =
                    await shellRuntime.ViewerSession
                        .GetAssetAsync(1);

                var sortedCorrectly =
                    sortOrder switch
                    {
                        AssetSortOrder.ModifiedNewest =>
                            firstSorted.ModifiedAtUtcTicks
                                >= secondSorted.ModifiedAtUtcTicks,
                        AssetSortOrder.ModifiedOldest =>
                            firstSorted.ModifiedAtUtcTicks
                                <= secondSorted.ModifiedAtUtcTicks,
                        AssetSortOrder.FileNameAscending =>
                            StringComparer.OrdinalIgnoreCase.Compare(
                                firstSorted.DisplayName,
                                secondSorted.DisplayName) <= 0,
                        AssetSortOrder.FileNameDescending =>
                            StringComparer.OrdinalIgnoreCase.Compare(
                                firstSorted.DisplayName,
                                secondSorted.DisplayName) >= 0,
                        AssetSortOrder.CreatedNewest =>
                            (firstSorted.CreatedAtUtcTicks ?? long.MinValue)
                                >= (secondSorted.CreatedAtUtcTicks ?? long.MinValue),
                        AssetSortOrder.CreatedOldest =>
                            (firstSorted.CreatedAtUtcTicks ?? long.MinValue)
                                <= (secondSorted.CreatedAtUtcTicks ?? long.MinValue),
                        AssetSortOrder.FileSizeLargest =>
                            firstSorted.FileSize
                                >= secondSorted.FileSize,
                        AssetSortOrder.FileSizeSmallest =>
                            firstSorted.FileSize
                                <= secondSorted.FileSize,
                        AssetSortOrder.RatingHighest =>
                            (firstSorted.Rating ?? 0)
                                >= (secondSorted.Rating ?? 0),
                        AssetSortOrder.RatingLowest =>
                            (firstSorted.Rating ?? 0)
                                <= (secondSorted.Rating ?? 0),
                        AssetSortOrder.StatusAscending =>
                            StringComparer.OrdinalIgnoreCase.Compare(
                                firstSorted.StatusLabel ?? string.Empty,
                                secondSorted.StatusLabel ?? string.Empty) <= 0,
                        AssetSortOrder.StatusDescending =>
                            StringComparer.OrdinalIgnoreCase.Compare(
                                firstSorted.StatusLabel ?? string.Empty,
                                secondSorted.StatusLabel ?? string.Empty) >= 0,
                        _ => false
                    };

                Require(
                    sortedCorrectly,
                    $"Viewer keyset paging did not honor browse sort {sortOrder}.");
            }

            await shellRuntime.ApplyQueryAsync(null);
            Require(
                shellRuntime.AssetCount == 2
                && shellRuntime.CurrentQuery is null,
                "CoreViewerRuntime did not clear the browse query.");

            await shellRuntime.DisposeAsync();
            Dispatcher.UIThread.RunJobs();

            return 0;
        },
        CancellationToken.None);

    Console.WriteLine(
        "App shell smoke: browse / contextual detail / focused viewer / multi-selection / Detail / shutdown OK");

    Require(
        Math.Abs(
            WindowsTextScale.NormalizeRegistryValue(100)
            - 1.0) < 0.001
        && Math.Abs(
            WindowsTextScale.NormalizeRegistryValue(125)
            - 1.25) < 0.001
        && Math.Abs(
            WindowsTextScale.NormalizeRegistryValue(225)
            - 2.25) < 0.001,
        "Windows registry text-scale percentages were not normalized to 1.0-2.25 factors.");

    var previousTextScaleEnvironment =
        Environment.GetEnvironmentVariable(
            "LUMINE_TEXT_SCALE");
    try
    {
        Environment.SetEnvironmentVariable(
            "LUMINE_TEXT_SCALE",
            "2.25");
        Require(
            Math.Abs(
                WindowsTextScale.Resolve()
                - 2.25) < 0.001,
            "Windows text-scale bridge did not accept the 225% accessibility override.");
    }
    finally
    {
        Environment.SetEnvironmentVariable(
            "LUMINE_TEXT_SCALE",
            previousTextScaleEnvironment);
    }

    var baselineTextScale =
        LumineVisualMetrics.TextScaleFactor;

    for (var iteration = 0;
         iteration < 3;
         iteration++)
    {
        var repeatedLibraryRoot =
            Path.Combine(
                root,
                $"repeated-library-{iteration}");
        var repeatedDataRoot =
            Path.Combine(
                root,
                $"repeated-data-{iteration}");
        var repeatedPaths =
            AppDataPaths.FromRoot(
                repeatedDataRoot);
        var repeatedEmptyLibraryRoot =
            Path.Combine(
                root,
                $"repeated-empty-library-{iteration}");
        Directory.CreateDirectory(
            repeatedEmptyLibraryRoot);

        if (iteration == 0)
        {
            var repeatedSettings =
                new AppSettingsStore(
                    repeatedPaths.SettingsPath);
            await repeatedSettings.SaveAsync(
                new AppSettingsDocument
                {
                    ResourcePolicy =
                        new ResourcePolicySettings
                        {
                            ThumbnailQueueCapacity = 19,
                            ThumbnailCacheByteLimit =
                                128L * 1024 * 1024,
                            EncodedThumbnailMemoryByteLimit =
                                512L * 1024 * 1024
                        }
                });
        }

        Directory.CreateDirectory(
            repeatedLibraryRoot);
        WriteBmp24(
            Path.Combine(
                repeatedLibraryRoot,
                "first.bmp"),
            width: 2000,
            height: 1400);
        WriteBmp24(
            Path.Combine(
                repeatedLibraryRoot,
                "second.bmp"),
            width: 1280,
            height: 900);

        LumineVisualMetrics.ConfigureTextScaleFactor(
            iteration == 2
                ? 2.25
                : baselineTextScale);

        await using (var appHost =
                     await AppHost.StartAsync(
                         repeatedPaths))
        {
            await headless.Dispatch(
                async () =>
                {
                    var window =
                        new MainWindow(
                            repeatedPaths,
                            appHost.ResourcePolicy,
                            appHost);
                    window.Show();
                    Dispatcher.UIThread.RunJobs();

                    Require(
                        string.Equals(
                            window.Title,
                            "Lumine",
                            StringComparison.Ordinal)
                        && window.Icon is not null,
                        "MainWindow did not expose Lumine product branding in native Window chrome.");
                    Require(
                        MainWindow.ProductNavigationLabels.SequenceEqual(
                        [
                            "ライブラリ",
                            "フォルダー",
                            "タグ",
                            "公開履歴",
                            "設定"
                        ])
                        && LumineDesign.NavigationWidth < 100,
                        "Branded shell navigation contract drifted from the compact v1 product hierarchy.");
                    Require(
                        LumineDesign.InteractionNeutralColor
                            != LumineDesign.InteractionHoverColor
                        && LumineDesign.InteractionHoverColor
                            != LumineDesign.InteractionPressedColor
                        && LumineDesign.InteractionSelectedColor
                            != LumineDesign.InteractionSelectedHoverColor
                        && LumineDesign.InteractionFocusColor
                            != LumineDesign.InteractionSelectedColor
                        && LumineDesign.InteractionDangerHoverColor
                            != LumineDesign.InteractionDangerPressedColor
                        && ContrastRatio(
                            LumineDesign.InteractionFocusColor,
                            LumineDesign.BackgroundColor) >= 3.0,
                        "Lumine interaction-state tokens collapsed distinct hover/pressed/selected/focus semantics.");

                    var neutralStateButton =
                        LumineDesign.ConfigureSecondaryButton(
                            new Button());
                    var primaryStateButton =
                        LumineDesign.ConfigurePrimaryButton(
                            new Button());
                    var textStateControl =
                        LumineDesign.ConfigureTextBox(
                            new TextBox());
                    var comboStateControl =
                        LumineDesign.ConfigureComboBox(
                            new ComboBox());

                    Require(
                        ReferenceEquals(
                            neutralStateButton.Resources[
                                "ButtonBackgroundPointerOver"],
                            LumineDesign.InteractionHover)
                        && ReferenceEquals(
                            neutralStateButton.Resources[
                                "ButtonBackgroundPressed"],
                            LumineDesign.InteractionPressed)
                        && ReferenceEquals(
                            primaryStateButton.Resources[
                                "ButtonForegroundPointerOver"],
                            LumineDesign.Background)
                        && ReferenceEquals(
                            textStateControl.Resources[
                                "TextControlBorderBrushFocused"],
                            LumineDesign.InteractionFocus)
                        && ReferenceEquals(
                            comboStateControl.Resources[
                                "ComboBoxBackgroundPointerOver"],
                            LumineDesign.InteractionHover),
                        "Representative Fluent controls are not bound to the canonical Lumine interaction-state resources.");

                    Require(
                        LumineDesign.Space2 < LumineDesign.Space4
                        && LumineDesign.Space4 < LumineDesign.Space6
                        && LumineDesign.Space6 < LumineDesign.Space8
                        && LumineDesign.Space8 < LumineDesign.Space12
                        && LumineDesign.Space12 < LumineDesign.Space16
                        && LumineDesign.Space16 < LumineDesign.Space24
                        && LumineDesign.PageGutter
                            == LumineDesign.Space24
                        && neutralStateButton.CornerRadius
                            == new CornerRadius(
                                LumineDesign.ControlRadius)
                        && Math.Abs(
                            neutralStateButton.Padding.Left
                            - LumineDesign.Space12) < 0.001
                        && Math.Abs(
                            textStateControl.Padding.Left
                            - LumineDesign.Space12) < 0.001
                        && Math.Abs(
                            comboStateControl.Padding.Left
                            - LumineDesign.Space8) < 0.001,
                        "Canonical Lumine spacing/control metrics drifted or representative controls stopped using them.");

                    Require(
                        ContrastRatio(
                            LumineDesign.DangerColor,
                            LumineDesign.BackgroundColor) >= 4.5
                        && ContrastRatio(
                            LumineDesign.DangerColor,
                            LumineDesign.SurfaceRaisedColor) >= 4.5
                        && ContrastRatio(
                            LumineDesign.WarningColor,
                            LumineDesign.BackgroundColor) >= 4.5
                        && ContrastRatio(
                            LumineDesign.WarningColor,
                            LumineDesign.SurfaceRaisedColor) >= 4.5
                        && ContrastRatio(
                            LumineDesign.BorderStrongColor,
                            LumineDesign.BackgroundColor) >= 3.0,
                        "Lumine semantic color tokens regressed below readable text/UI contrast.");
                    Require(
                        string.Equals(
                            window.ProductShellState,
                            "Welcome",
                            StringComparison.Ordinal),
                        $"Fresh MainWindow did not start in the branded Welcome state: {window.ProductShellState}.");

                    await window.OpenLibraryAsync(
                        repeatedLibraryRoot);

                    Require(
                        string.Equals(
                            window.ProductShellState,
                            "Workspace",
                            StringComparison.Ordinal),
                        $"MainWindow did not transition from Welcome to Workspace after opening a populated library: {window.ProductShellState}.");

                    Require(
                        window.CurrentRuntime is not null
                        && window.CurrentShell is not null,
                        "MainWindow did not compose the production Core Viewer runtime/shell.");

                    var shellBeforeSettings =
                        window.CurrentShell;

                    window.NavigateForSmoke(
                        "設定");
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        window.WorkspacePageForSmoke
                            is Border
                        && window.IsWorkspacePageVisibleForSmoke
                        && !window.IsNavigationPaneVisibleForSmoke
                        && window.WorkspacePageBoundsForSmoke.Width >= 500
                        && ReferenceEquals(
                            window.CurrentShell,
                            shellBeforeSettings)
                        && window.SettingsSnapshot.DataPaths.RootPath
                            == repeatedPaths.RootPath
                        && window.SettingsSnapshot.PersistedThumbnailStorageMode
                            == ThumbnailStorageMode.MemoryOnly
                        && window.SettingsSnapshot.ThumbnailCacheByteLimit
                            == (appHost.Settings.ResourcePolicy
                                    ?.ThumbnailCacheByteLimit
                                ?? appHost.ResourcePolicy
                                    .ThumbnailCacheByteLimit)
                        && window.SettingsSnapshot.EncodedThumbnailMemoryByteLimit
                            == (iteration == 0
                                ? 512L * 1024 * 1024
                                : appHost.ResourcePolicy
                                    .EncodedThumbnailMemoryByteLimit),
                        "Product Settings did not open as a main-workspace page while preserving the active viewer runtime.");

                    Require(
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<TextBlock>()
                            .Any(block =>
                                string.Equals(
                                    block.Text,
                                    "設定",
                                    StringComparison.Ordinal))
                        && window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<TextBlock>()
                            .Any(block =>
                                string.Equals(
                                    block.Text,
                                    "ディスク保持上限",
                                    StringComparison.Ordinal))
                        && window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<TextBlock>()
                            .Any(block =>
                                string.Equals(
                                    block.Text,
                                    "高速再表示用メモリ上限",
                                    StringComparison.Ordinal)),
                        "Product Settings did not expose a readable full-page hierarchy for viewer/cache/storage controls.");

                    window.NavigateForSmoke(
                        "ライブラリ");
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        !window.IsWorkspacePageVisibleForSmoke
                        && window.IsNavigationPaneVisibleForSmoke
                        && ReferenceEquals(
                            window.CurrentShell,
                            shellBeforeSettings),
                        "Returning from Settings did not restore browse navigation without rebuilding the viewer shell.");

                    var shellBeforeQueryChange =
                        window.CurrentShell!;
                    var gridBeforeQueryChange =
                        shellBeforeQueryChange.GridViewer;
                    gridBeforeQueryChange.SelectAsset(0);
                    await shellBeforeQueryChange
                        .ShowContextDetailAsync();
                    var selectedAssetBeforeQuery =
                        await window.CurrentRuntime!
                            .ViewerSession
                            .GetAssetAsync(0);

                    await window.ApplyBrowseFilterForSmokeAsync(
                        new BrowseFilterState(
                            SortOrder:
                                AssetSortOrder.ModifiedOldest));
                    Dispatcher.UIThread.RunJobs();

                    var selectedIndexAfterSort =
                        window.CurrentShell!
                            .GridViewer
                            .SelectedAssetIndex;
                    var selectedAssetAfterSort =
                        await window.CurrentRuntime!
                            .ViewerSession
                            .GetAssetAsync(
                                selectedIndexAfterSort);
                    Require(
                        ReferenceEquals(
                            window.CurrentShell,
                            shellBeforeQueryChange)
                        && ReferenceEquals(
                            window.CurrentShell.GridViewer,
                            gridBeforeQueryChange)
                        && selectedAssetAfterSort.Id
                            == selectedAssetBeforeQuery.Id
                        && window.CurrentShell
                            .IsContextDetailVisible,
                        "Browse sort rebuilt the Viewer shell/control or lost a still-matching selected Inspector asset.");

                    await window.ApplyBrowseFilterForSmokeAsync(
                        new BrowseFilterState(
                            SearchText:
                                "__lumine_no_match_smoke__"));
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        string.Equals(
                            window.ProductShellState,
                            "Workspace",
                            StringComparison.Ordinal)
                        && window.CurrentRuntime is not null
                        && window.CurrentRuntime.AssetCount == 0
                        && ReferenceEquals(
                            window.CurrentShell,
                            shellBeforeQueryChange)
                        && ReferenceEquals(
                            window.CurrentShell.GridViewer,
                            gridBeforeQueryChange)
                        && window.CurrentShell
                            .GridViewer
                            .SelectedAssetIndex == -1
                        && !window.CurrentShell
                            .IsContextDetailVisible,
                        "Filtered zero-result query replaced the Viewer shell or kept stale selection/Inspector state.");

                    await window.ApplyBrowseFilterForSmokeAsync(
                        new BrowseFilterState(
                            SortOrder:
                                window.SettingsSnapshot
                                    .ViewerDefaults
                                    .SortOrder));
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        string.Equals(
                            window.ProductShellState,
                            "Workspace",
                            StringComparison.Ordinal)
                        && window.CurrentShell is not null
                        && ReferenceEquals(
                            window.CurrentShell,
                            shellBeforeQueryChange)
                        && ReferenceEquals(
                            window.CurrentShell.GridViewer,
                            gridBeforeQueryChange)
                        && window.CurrentRuntime!.AssetCount > 0,
                        "Clearing the no-match filter rebuilt the Viewer shell/control instead of restoring data in place.");

                    await window.OpenLibraryAsync(
                        repeatedEmptyLibraryRoot);
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        string.Equals(
                            window.ProductShellState,
                            "EmptyLibrary",
                            StringComparison.Ordinal)
                        && window.CurrentRuntime is not null
                        && window.CurrentShell is null,
                        "Truly empty library did not use the EmptyLibrary product state.");

                    await window.OpenLibraryAsync(
                        repeatedLibraryRoot);
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        string.Equals(
                            window.ProductShellState,
                            "Workspace",
                            StringComparison.Ordinal)
                        && window.CurrentShell is not null,
                        "Reopening a populated library after EmptyLibrary did not restore Workspace.");

                    var runtimeBeforeManualRescan =
                        window.CurrentRuntime;
                    var shellBeforeManualRescan =
                        window.CurrentShell;
                    await window.RescanActiveLibraryForSmokeAsync();
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        ReferenceEquals(
                            window.CurrentRuntime,
                            runtimeBeforeManualRescan)
                        && ReferenceEquals(
                            window.CurrentShell,
                            shellBeforeManualRescan)
                        && string.Equals(
                            window.ProductShellState,
                            "Workspace",
                            StringComparison.Ordinal),
                        "Manual library rescan recreated the active runtime/shell or failed to restore Workspace.");

                    // App integration must produce a real virtualized thumbnail
                    // surface before downstream interaction/DPI checks. The
                    // standalone Viewer smoke has the same first-frame gate.
                    for (var attempt = 0;
                         attempt < 250
                         && (window.CurrentShell.GridViewer.RealizedRowCount == 0
                             || window.CurrentShell.GridViewer.Diagnostics.ReadyTiles == 0);
                         attempt++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        await Task.Delay(1);
                    }

                    Require(
                        window.CurrentShell.GridViewer.RealizedRowCount > 0
                        && window.CurrentShell.GridViewer.Diagnostics.ReadyTiles > 0
                        && window.CurrentShell.GridViewer
                            .GetAssetFocusTarget(0) is not null,
                        "MainWindow workspace did not realize its initial thumbnail surface.");

                    var viewportMatrix =
                        new[]
                        {
                            (Width: 900d, Height: 600d),
                            (Width: 1024d, Height: 768d),
                            (Width: 1440d, Height: 900d),
                            (Width: 1920d, Height: 1080d)
                        };

                    foreach (var viewport in viewportMatrix)
                    {
                        window.Width = viewport.Width;
                        window.Height = viewport.Height;
                        window.SetRenderScaling(1.0);
                        Dispatcher.UIThread.RunJobs();

                        foreach (var mode in
                                 new[]
                                 {
                                     BrowseViewMode.Grid,
                                     BrowseViewMode.List
                                 })
                        {
                            window.CurrentShell!.SetBrowseLayout(
                                new BrowsePreferences(
                                    mode,
                                    1,
                                    AssetSortOrder.ModifiedNewest));
                            Dispatcher.UIThread.RunJobs();

                            var runtimeBeforeNavigation =
                                window.CurrentRuntime;
                            window.CurrentShell!.HideContextDetail();
                            window.SetNavigationPinnedForSmoke(false);
                            window.SetNavigationPaneVisibleForSmoke(false);
                            Dispatcher.UIThread.RunJobs();
                            var unpinnedClosedCanvasWidth =
                                window.CurrentShell.GridViewerBounds.Width;

                            foreach (var navigationVisible in
                                     new[] { true, false })
                            {
                                window.SetNavigationPaneVisibleForSmoke(
                                    navigationVisible);
                                Dispatcher.UIThread.RunJobs();

                                Require(
                                    window.IsNavigationPaneVisibleForSmoke
                                        == navigationVisible
                                    && ReferenceEquals(
                                        window.CurrentRuntime,
                                        runtimeBeforeNavigation)
                                    && window.IsCompactNavigationLayout
                                        == (viewport.Width <= 1040)
                                    && window.BrowseControlsForSmoke is not null
                                    && window.BrowseControlsForSmoke
                                        .PrimaryToolbarIsContainedForSmoke
                                    && window.BrowseControlsForSmoke
                                        .DirectFiltersAreVisibleForSmoke
                                    && (viewport.Width < 1440
                                        || window.BrowseControlsForSmoke
                                            .DirectFiltersFitWithoutScrollForSmoke)
                                    && window.BrowseControlsForSmoke
                                        .SearchPaddingForSmoke.Top <= 4
                                    && (iteration == 2
                                        || window.BrowseControlsForSmoke
                                            .SearchHeightForSmoke <= 33.5),
                                    $"Responsive shell/navigation or primary toolbar containment regressed at {viewport.Width:N0}x{viewport.Height:N0}, {mode}, nav={(navigationVisible ? "open" : "closed")}.");

                                window.CurrentShell.HideContextDetail();
                                Dispatcher.UIThread.RunJobs();
                                Require(
                                    !window.CurrentShell.IsContextDetailVisible
                                    && window.CurrentShell.GridViewerBounds.Width
                                        >= 500
                                    && (viewport.Width < 1440
                                        || Math.Abs(
                                            window.CurrentShell.GridViewerBounds.Width
                                            - unpinnedClosedCanvasWidth) < 1),
                                    $"Browse canvas changed width for unpinned secondary navigation at {viewport.Width:N0}x{viewport.Height:N0}, {mode}, nav={(navigationVisible ? "open" : "closed")}.");

                                window.CurrentShell.GridViewer.SelectAsset(0);
                                await window.CurrentShell
                                    .ShowContextDetailAsync();
                                Dispatcher.UIThread.RunJobs();

                                Require(
                                    window.CurrentShell.IsContextDetailVisible
                                    && window.CurrentShell.IsCompactInspectorLayout
                                        == (viewport.Width <= 1080)
                                    && window.CurrentShell.ContextSurfaceBounds.Width
                                        is >= 300 and <= 400
                                    && window.CurrentShell.GridViewerBounds.Width
                                        >= 500
                                    && !window.CurrentShell.IsInspectorPinnedForSmoke
                                    && Math.Abs(
                                        window.CurrentShell.GridViewerBounds.Width
                                        - unpinnedClosedCanvasWidth) < 1
                                    && ReferenceEquals(
                                        window.CurrentRuntime,
                                        runtimeBeforeNavigation)
                                    && window.CurrentShell.ContextDetail.HasPreview
                                    && window.CurrentShell.ContextDetail
                                        .TabHeaders.SequenceEqual(
                                            new[] { "整理", "制作", "公開", "情報" })
                                    && window.CurrentShell.ContextDetail
                                        .TabStripUsesLumineStatesForSmoke
                                    && window.CurrentShell.ContextDetail
                                        .TabPagesHaveIndependentScrollStateForSmoke,
                                    $"Inspector geometry/IA regressed at {viewport.Width:N0}x{viewport.Height:N0}, {mode}, nav={(navigationVisible ? "open" : "closed")}.");

                                for (var tabIndex = 0;
                                     tabIndex
                                         < window.CurrentShell.ContextDetail
                                             .TabHeaders.Count;
                                     tabIndex++)
                                {
                                    window.CurrentShell.ContextDetail
                                        .SelectTabForSmoke(tabIndex);
                                    Dispatcher.UIThread.RunJobs();
                                    Require(
                                        window.CurrentShell.ContextDetail
                                            .SelectedTabIndex == tabIndex
                                        && window.CurrentShell.ContextDetail
                                            .TabStripUsesLumineStatesForSmoke,
                                        $"Inspector tab {tabIndex} was not reachable or lost Lumine state styling at {viewport.Width:N0}x{viewport.Height:N0}, {mode}.");
                                }

                                window.CurrentShell.HideContextDetail();
                                window.CurrentShell.GridViewer.ClearSelection();
                                window.CurrentShell.GridViewer.SelectAsset(0);
                                Dispatcher.UIThread.RunJobs();

                                var gridBeforeBulk =
                                    window.CurrentShell.GridViewerBounds;

                                window.CurrentShell.GridViewer.SelectAsset(
                                    1,
                                    scrollIntoView: false,
                                    mode: ViewerSelectionMode.Toggle);
                                Dispatcher.UIThread.RunJobs();

                                var gridDuringBulk =
                                    window.CurrentShell.GridViewerBounds;
                                Require(
                                    window.CurrentShell.IsBulkSelectionBarVisible
                                    && window.CurrentShell
                                        .SelectionToolbarIsContainedForSmoke
                                    && window.CurrentShell
                                        .BulkSelectionUsesDirectActionsForSmoke
                                    && window.CurrentShell.GridViewer
                                        .BottomOverlayInset >= 70
                                    && Math.Abs(
                                        gridDuringBulk.Y
                                        - gridBeforeBulk.Y) < 0.5
                                    && Math.Abs(
                                        gridDuringBulk.Height
                                        - gridBeforeBulk.Height) < 0.5,
                                    $"Bulk selection direct-action/safe-area contract regressed at {viewport.Width:N0}x{viewport.Height:N0}, {mode}, nav={(navigationVisible ? "open" : "closed")}.");

                                window.CurrentShell.GridViewer.ClearSelection();
                                Dispatcher.UIThread.RunJobs();
                                var gridAfterBulk =
                                    window.CurrentShell.GridViewerBounds;
                                Require(
                                    Math.Abs(
                                        gridAfterBulk.Y
                                        - gridBeforeBulk.Y) < 0.5
                                    && Math.Abs(
                                        gridAfterBulk.Height
                                        - gridBeforeBulk.Height) < 0.5
                                    && window.CurrentShell.GridViewer
                                        .BottomOverlayInset < 0.5,
                                    $"Leaving bulk selection shifted the image canvas or retained its bottom safe area at {viewport.Width:N0}x{viewport.Height:N0}, {mode}.");
                            }

                            if (viewport.Width >= 1440)
                            {
                                window.CurrentShell.GridViewer.SelectAsset(0);
                                await window.CurrentShell.ShowContextDetailAsync();
                                window.CurrentShell.SetInspectorPinnedForSmoke(true);
                                Dispatcher.UIThread.RunJobs();

                                var inspectorPinnedCanvasWidth =
                                    window.CurrentShell.GridViewerBounds.Width;
                                Require(
                                    window.CurrentShell.IsInspectorPinnedForSmoke
                                    && ReferenceEquals(
                                        window.CurrentRuntime,
                                        runtimeBeforeNavigation)
                                    && inspectorPinnedCanvasWidth
                                        < unpinnedClosedCanvasWidth - 250,
                                    $"Pinned Inspector did not dock beside the canvas at {viewport.Width:N0}x{viewport.Height:N0}, {mode}.");

                                window.CurrentShell.SetInspectorPinnedForSmoke(false);
                                Dispatcher.UIThread.RunJobs();
                                Require(
                                    !window.CurrentShell.IsInspectorPinnedForSmoke
                                    && Math.Abs(
                                        window.CurrentShell.GridViewerBounds.Width
                                        - unpinnedClosedCanvasWidth) < 1,
                                    $"Unpinning Inspector did not restore overlay canvas width at {viewport.Width:N0}x{viewport.Height:N0}, {mode}.");
                                window.CurrentShell.HideContextDetail();

                                window.SetNavigationPaneVisibleForSmoke(true);
                                window.SetNavigationPinnedForSmoke(true);
                                Dispatcher.UIThread.RunJobs();

                                var pinnedCanvasWidth =
                                    window.CurrentShell.GridViewerBounds.Width;
                                Require(
                                    window.IsNavigationPinnedForSmoke
                                    && ReferenceEquals(
                                        window.CurrentRuntime,
                                        runtimeBeforeNavigation)
                                    && pinnedCanvasWidth
                                        < unpinnedClosedCanvasWidth - 200,
                                    $"Pinned navigation did not dock beside the canvas at {viewport.Width:N0}x{viewport.Height:N0}, {mode}.");

                                window.SetNavigationPinnedForSmoke(false);
                                Dispatcher.UIThread.RunJobs();
                                Require(
                                    !window.IsNavigationPinnedForSmoke
                                    && Math.Abs(
                                        window.CurrentShell.GridViewerBounds.Width
                                        - unpinnedClosedCanvasWidth) < 1,
                                    $"Unpinning navigation did not restore overlay canvas width at {viewport.Width:N0}x{viewport.Height:N0}, {mode}.");

                                window.SetNavigationPaneVisibleForSmoke(false);
                                Dispatcher.UIThread.RunJobs();
                            }
                        }
                    }

                    window.SetNavigationPaneVisibleForSmoke(true);
                    window.CurrentShell!.SetBrowseLayout(
                        new BrowsePreferences(
                            BrowseViewMode.Grid,
                            1,
                            AssetSortOrder.ModifiedNewest));

                    window.Width = 900;
                    window.Height = 600;
                    Dispatcher.UIThread.RunJobs();

                    if (iteration == 2)
                    {
                        var clippedText =
                            window.GetVisualDescendants()
                                .OfType<TextBlock>()
                                .FirstOrDefault(
                                    block =>
                                        block.IsEffectivelyVisible
                                        && !string.IsNullOrWhiteSpace(
                                            block.Text)
                                        && block.Bounds.Height > 0
                                        && block.Bounds.Height + 0.5
                                            < block.FontSize);

                        Require(
                            clippedText is null,
                            $"225% Windows text scaling clipped visible product text: '{clippedText?.Text}' ({clippedText?.Bounds.Height:N1} DIP high for {clippedText?.FontSize:N1} DIP font).");
                    }

                    Require(
                        window.IsCompactNavigationLayout,
                        "Minimum-width MainWindow did not switch navigation to compact overlay layout.");

                    Require(
                        window.BrowseControlsForSmoke is not null
                        && window.BrowseControlsForSmoke
                            .PrimaryToolbarIsContainedForSmoke
                        && window.BrowseControlsForSmoke
                            .SearchPaddingForSmoke.Top <= 4
                        && (iteration == 2
                            || window.BrowseControlsForSmoke
                                .SearchHeightForSmoke <= 33.5),
                        "Minimum-width browse command bar clipped or escaped the workspace bounds.");

                    window.CurrentShell!.GridViewer.SelectAsset(0);
                    await window.CurrentShell.ShowContextDetailAsync();
                    Dispatcher.UIThread.RunJobs();

                    Require(
                        window.CurrentShell.IsCompactInspectorLayout
                        && window.CurrentShell.ContextSurfaceBounds.Width <= 400
                        && window.CurrentShell.GridViewerBounds.Width >= 500,
                        "Minimum-width workspace did not preserve an image-dominant canvas with overlay inspector.");

                    var renderScalings =
                        iteration == 2
                            ? new[] { 1.0 }
                            : new[]
                            {
                                1.25,
                                1.5,
                                2.0,
                                2.25
                            };

                    foreach (var scaling in
                             renderScalings)
                    {
                        window.SetRenderScaling(scaling);
                        Dispatcher.UIThread.RunJobs();

                        for (var attempt = 0;
                             attempt < 100
                             && window.CurrentShell.GridViewer.RealizedRowCount == 0;
                             attempt++)
                        {
                            Dispatcher.UIThread.RunJobs();
                            await Task.Delay(1);
                        }

                        Require(
                            Math.Abs(
                                window.RenderScaling
                                - scaling) < 0.001
                            && window.IsCompactNavigationLayout
                            && window.CurrentShell.IsCompactInspectorLayout
                            && window.CurrentShell.GridViewerBounds.Width >= 500
                            && window.CurrentShell.GridViewer.RealizedRowCount > 0
                            && window.BrowseControlsForSmoke is not null
                            && window.BrowseControlsForSmoke
                                .PrimaryToolbarIsContainedForSmoke
                            && window.BrowseControlsForSmoke
                                .SearchPaddingForSmoke.Top <= 4,
                            $"MainWindow responsive/layout virtualization or browse command containment regressed at {scaling:P0} render scaling.");
                    }

                    Require(
                        window.CurrentShell.GridViewer.FocusAsset(0)
                        && window.CurrentShell.IsAssetFocusedForSmoke(0),
                        "Focused-view acceptance could not focus the realized invoking thumbnail.");

                    await window.CurrentShell
                        .OpenFocusedViewAsync(0);
                    Dispatcher.UIThread.RunJobs();

                    Require(
                        Math.Abs(
                            window.LightboxBounds.Width
                            - window.ClientSize.Width) < 1
                        && Math.Abs(
                            window.LightboxBounds.Height
                            - window.ClientSize.Height) < 1,
                        "Focused lightbox did not cover the complete MainWindow client area.");

                    Require(
                        window.IsLightboxVisible
                        && !window.IsWorkspaceInteractionEnabled
                        && window.CurrentShell.IsFocusedViewVisible
                        && window.CurrentShell.DetailViewer.SelectedAssetIndex == 0,
                        "Focused image viewer did not mount as a modal MainWindow-level lightbox.");

                    var unnamedIconButton =
                        window.GetVisualDescendants()
                            .OfType<Button>()
                            .FirstOrDefault(
                                button =>
                                    button.Content
                                        is Avalonia.Controls.Shapes.Path
                                    && string.IsNullOrWhiteSpace(
                                        AutomationProperties.GetName(
                                            button)));
                    Require(
                        unnamedIconButton is null,
                        "An icon-only product control is missing an accessibility name.");

                    var fullScreenAutomation =
                        window.GetVisualDescendants()
                            .OfType<Button>()
                            .FirstOrDefault(
                                button =>
                                    string.Equals(
                                        AutomationProperties
                                            .GetAutomationId(button),
                                        "viewer.fullscreen",
                                        StringComparison.Ordinal));
                    Require(
                        fullScreenAutomation is not null
                        && string.Equals(
                            AutomationProperties.GetAcceleratorKey(
                                fullScreenAutomation),
                            "F11",
                            StringComparison.Ordinal),
                        "Focused viewer full-screen automation metadata/accelerator regressed.");

                    Require(
                        window.IsFocusInsideLightboxForSmoke,
                        "Opening the lightbox did not move keyboard focus into the modal layer.");

                    for (var tabIndex = 0;
                         tabIndex < 12;
                         tabIndex++)
                    {
                        var focusedElement =
                            window.FocusManager
                                .GetFocusedElement()
                            as InputElement
                            ?? throw new InvalidOperationException(
                                "Lightbox lost its focused input element during Tab-cycle acceptance.");

                        focusedElement.RaiseEvent(
                            new KeyEventArgs
                            {
                                RoutedEvent =
                                    InputElement.KeyDownEvent,
                                Key = Key.Tab,
                                KeyModifiers =
                                    tabIndex >= 6
                                        ? KeyModifiers.Shift
                                        : KeyModifiers.None
                            });
                        Dispatcher.UIThread.RunJobs();

                        Require(
                            window.IsFocusInsideLightboxForSmoke,
                            "Tab/Shift+Tab escaped the modal lightbox into the background workspace.");
                    }

                    var windowStateBeforeFullscreen =
                        window.WindowState;
                    window.ToggleLightboxFullScreen();
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        window.IsLightboxFullScreen
                        && window.WindowState
                            == WindowState.FullScreen,
                        "Focused viewer did not enter full-screen state.");

                    window.ToggleLightboxFullScreen();
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        !window.IsLightboxFullScreen
                        && window.WindowState
                            == windowStateBeforeFullscreen,
                        "Focused viewer did not restore the prior window state after full-screen exit.");

                    window.ToggleLightboxFullScreen();
                    Dispatcher.UIThread.RunJobs();

                    var focusedGeometry =
                        window.CurrentShell
                            .FocusedViewerGeometryForSmoke;
                    var detailImageBounds =
                        focusedGeometry.ImageBounds;
                    var detailViewportBounds =
                        focusedGeometry.ViewportBounds;
                    var detailImageCenterX =
                        detailImageBounds.X
                        + (detailImageBounds.Width / 2);
                    var detailImageCenterY =
                        detailImageBounds.Y
                        + (detailImageBounds.Height / 2);
                    var detailViewportCenterX =
                        detailViewportBounds.X
                        + (detailViewportBounds.Width / 2);
                    var detailViewportCenterY =
                        detailViewportBounds.Y
                        + (detailViewportBounds.Height / 2);

                    Require(
                        detailImageBounds.Width > 0
                        && detailImageBounds.Height > 0
                        && Math.Abs(
                            detailImageCenterX
                            - detailViewportCenterX) <= 1.5
                        && Math.Abs(
                            detailImageCenterY
                            - detailViewportCenterY) <= 1.5,
                        "Focused image did not start visually centered in the viewer viewport.");

                    // The focus contract is tied to the element that opened
                    // the modal, not whichever asset happens to be selected
                    // inside the viewer when it closes.
                    await window.CurrentShell.DetailViewer
                        .SelectAsync(1);
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        window.CurrentShell.DetailViewer.SelectedAssetIndex == 1,
                        "Focused viewer did not move away from the invoking asset for focus-return coverage.");

                    window.CurrentShell.CloseFocusedView();
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        window.CurrentShell
                            .IsAssetFocusedForSmoke(0),
                        "Closing the lightbox did not restore keyboard focus to the invoking thumbnail.");

                    window.SetRenderScaling(1.0);
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        !window.IsLightboxVisible
                        && !window.IsLightboxFullScreen
                        && window.WindowState
                            == windowStateBeforeFullscreen
                        && window.IsWorkspaceInteractionEnabled
                        && !window.CurrentShell.IsFocusedViewVisible,
                        "Closing the focused image viewer did not release modality/full-screen state and restore workspace interaction.");

                    var cacheSafetyAsset =
                        await window.CurrentRuntime!
                            .ViewerSession
                            .GetAssetAsync(0);
                    await window.CurrentRuntime
                        .LibraryService
                        .SetUserMetadataAsync(
                            window.CurrentRuntime.Library.Id,
                            cacheSafetyAsset.Id,
                            new AssetUserMetadataUpdate(
                                Notes:
                                    "cache-prune-safety",
                                Tags:
                                    ["cache-prune-safety"]));
                    var disposableCacheDirectory =
                        Path.Combine(
                            repeatedPaths.ThumbnailCachePath,
                            "aa",
                            "bb");
                    Directory.CreateDirectory(
                        disposableCacheDirectory);
                    var disposableCacheFile =
                        Path.Combine(
                            disposableCacheDirectory,
                            "settings-smoke.webp");
                    await File.WriteAllBytesAsync(
                        disposableCacheFile,
                        [1, 2, 3, 4]);

                    var cachePrune =
                        await window.PruneThumbnailCacheForSmokeAsync();
                    var metadataAfterCachePrune =
                        await window.CurrentRuntime
                            .LibraryService
                            .GetUserMetadataAsync(
                                window.CurrentRuntime.Library.Id,
                                cacheSafetyAsset.Id);
                    Require(
                        cachePrune.FilesDeleted >= 1
                        && !File.Exists(
                            disposableCacheFile)
                        && File.Exists(
                            Path.Combine(
                                repeatedLibraryRoot,
                                "first.bmp"))
                        && File.Exists(
                            repeatedPaths.DatabasePath)
                        && metadataAfterCachePrune is not null
                        && metadataAfterCachePrune.Notes
                            == "cache-prune-safety"
                        && metadataAfterCachePrune.Tags.Contains(
                            "cache-prune-safety"),
                        "Display-cache deletion touched an original image or user-owned metadata.");

                    if (iteration == 0)
                    {
                        Require(
                            window.CurrentRuntime!.ThumbnailCache.ConfiguredByteLimit
                                == 128L * 1024 * 1024,
                            "Persisted non-default thumbnail cache budget did not reach the real CoreViewerRuntime.");
                    }

                    var diagnostics =
                        await window.BuildRuntimeDiagnosticsTextAsync();
                    Require(
                        diagnostics.Contains(
                            "Effective bounded resource policy:",
                            StringComparison.Ordinal)
                        && diagnostics.Contains(
                            "Thumbnail cache files:",
                            StringComparison.Ordinal)
                        && diagnostics.Contains(
                            "Configured thumbnail storage: MemoryOnly",
                            StringComparison.Ordinal)
                        && diagnostics.Contains(
                            "Thumbnail storage mode: MemoryOnly",
                            StringComparison.Ordinal),
                        "User-visible runtime diagnostics omitted the product-default memory-only policy/cache state.");

                    var detail =
                        window.CurrentShell!.DetailViewer;
                    await detail.SelectAsync(0);

                    var originalTask =
                        detail.ActualSizeAsync();
                    var nextTask =
                        detail.SelectAsync(1);

                    window.Close();

                    for (var attempt = 0;
                         attempt < 10000
                         && window.IsVisible;
                         attempt++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        await Task.Delay(1);
                    }

                    Dispatcher.UIThread.RunJobs();

                    try
                    {
                        await originalTask;
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    try
                    {
                        await nextTask;
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    Require(
                        !window.IsVisible,
                        "MainWindow did not complete its coordinated close.");
                    Require(
                        window.CurrentRuntime is null
                        && window.CurrentShell is null,
                        "MainWindow returned from close with owned Viewer runtime state still attached.");

                    return 0;
                },
                CancellationToken.None);

            await appHost.CompleteCleanShutdownAsync();
        }

        LibraryDatabase.ClearPools();

        var walPath =
            repeatedPaths.DatabasePath + "-wal";
        Require(
            !File.Exists(walPath)
            || new FileInfo(walPath).Length == 0,
            "Repeated MainWindow close left a non-empty SQLite WAL.");

        try
        {
            Directory.Delete(
                repeatedLibraryRoot,
                recursive: true);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                $"MainWindow lifecycle iteration {iteration} returned before all source handles were released.",
                exception);
        }
        Directory.Delete(
            repeatedDataRoot,
            recursive: true);
        Directory.Delete(
            repeatedEmptyLibraryRoot,
            recursive: true);

        LumineVisualMetrics.ConfigureTextScaleFactor(
            baselineTextScale);
    }

    Console.WriteLine(
        "MainWindow lifecycle smoke: branded shell / Welcome-to-Workspace / repeated launch-close / rapid original-navigation close / diagnostics / handle release OK");

    Console.WriteLine(
        "App Detail adapter smoke: persistent preview / production full-resolution framebuffer copy / budget guard OK");
}
finally
{
    LibraryDatabase.ClearPools();

    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

internal sealed class AppAdapterSmokeApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AppAdapterSmokeApplication>()
            .UseSkia()
            .UseHeadless(
                new AvaloniaHeadlessPlatformOptions
                {
                    UseHeadlessDrawing = false,
                    OverlayPopups = false
                });

    public override void Initialize() =>
        App.ApplyProductTheme(this);
}
