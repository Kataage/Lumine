using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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
                    512L * 1024 * 1024
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

            Require(
                shell.ContextDetail.WorksText.Contains(
                    "App Smoke Work",
                    StringComparison.Ordinal)
                && shell.ContextDetail.GroupsText.Contains(
                    "App Smoke Group",
                    StringComparison.Ordinal)
                && shell.ContextDetail.RelationsText.Contains(
                    "img2img",
                    StringComparison.Ordinal)
                && shell.ContextDetail.PublicationsText.Contains(
                    "App Smoke Publication",
                    StringComparison.Ordinal),
                "Contextual detail did not render human-readable creative archive context.");

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
                         AssetSortOrder.FileNameDescending
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

                    window.NavigateForSmoke(
                        "設定");
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        window.NavigationContentForSmoke
                            is Avalonia.Controls.ScrollViewer
                        && window.SettingsSnapshot.DataPaths.RootPath
                            == repeatedPaths.RootPath
                        && window.SettingsSnapshot.PersistedThumbnailStorageMode
                            == ThumbnailStorageMode.MemoryOnly
                        && window.SettingsSnapshot.EncodedThumbnailMemoryByteLimit
                            == (iteration == 0
                                ? 512L * 1024 * 1024
                                : appHost.ResourcePolicy
                                    .EncodedThumbnailMemoryByteLimit),
                        "Product Settings did not expose viewer/cache/storage state without diagnostics.");

                    await window.ApplyBrowseFilterForSmokeAsync(
                        new BrowseFilterState(
                            SearchText:
                                "__lumine_no_match_smoke__"));
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        string.Equals(
                            window.ProductShellState,
                            "NoMatch",
                            StringComparison.Ordinal)
                        && window.CurrentRuntime is not null
                        && window.CurrentShell is null,
                        "Filtered zero-result workspace did not transition to the distinct NoMatch product state.");

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
                        && window.CurrentShell is not null,
                        "Clearing the no-match filter did not restore the Workspace state.");

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

                    window.Width = 900;
                    window.Height = 600;
                    Dispatcher.UIThread.RunJobs();

                    Require(
                        window.IsCompactNavigationLayout,
                        "Minimum-width MainWindow did not switch navigation to compact overlay layout.");

                    window.CurrentShell!.GridViewer.SelectAsset(0);
                    await window.CurrentShell.ShowContextDetailAsync();
                    Dispatcher.UIThread.RunJobs();

                    Require(
                        window.CurrentShell.IsCompactInspectorLayout
                        && window.CurrentShell.ContextSurfaceBounds.Width <= 400
                        && window.CurrentShell.GridViewerBounds.Width >= 500,
                        "Minimum-width workspace did not preserve an image-dominant canvas with overlay inspector.");

                    foreach (var scaling in
                             new[]
                             {
                                 1.25,
                                 1.5,
                                 2.0
                             })
                    {
                        window.SetRenderScaling(scaling);
                        Dispatcher.UIThread.RunJobs();

                        Require(
                            Math.Abs(
                                window.RenderScaling
                                - scaling) < 0.001
                            && window.IsCompactNavigationLayout
                            && window.CurrentShell.IsCompactInspectorLayout
                            && window.CurrentShell.GridViewerBounds.Width >= 500,
                            $"MainWindow responsive layout regressed at {scaling:P0} render scaling.");
                    }

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

                    window.CurrentShell.CloseFocusedView();
                    Dispatcher.UIThread.RunJobs();
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
                    Require(
                        window.CurrentShell
                            .IsAssetFocusedForSmoke(0),
                        "Closing the lightbox did not restore keyboard focus to the invoking thumbnail.");

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
}
