using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
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
            var shell =
                new CoreViewerShell(shellRuntime);
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

            var navigationAsset =
                await shellRuntime.ViewerSession.GetAssetAsync(0);
            await shellRuntime.LibraryService.SetUserMetadataAsync(
                shellRuntime.Library.Id,
                navigationAsset.Id,
                new AssetUserMetadataUpdate(
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

            var filteredShell =
                new CoreViewerShell(shellRuntime);
            var filteredWindow =
                new Avalonia.Controls.Window
                {
                    Width = 900,
                    Height = 620,
                    Content = filteredShell
                };
            filteredWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Require(
                filteredShell.GridViewer.AssetCount == 1,
                "Filtered App shell did not bind the replacement Viewer sessions.");
            filteredWindow.Close();
            Dispatcher.UIThread.RunJobs();
            await filteredShell.DetachAsync();

            await shellRuntime.ApplyQueryAsync(null);
            Require(
                shellRuntime.AssetCount == 2
                && shellRuntime.CurrentQuery is null,
                "CoreViewerRuntime did not clear the navigation query.");

            await shellRuntime.DisposeAsync();
            Dispatcher.UIThread.RunJobs();

            return 0;
        },
        CancellationToken.None);

    Console.WriteLine(
        "App shell smoke: runtime composition / grid / selection / 1:1 / zoom / pan / Fit / navigation requery / shutdown OK");

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
                                128L * 1024 * 1024
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

        Directory.Delete(
            repeatedLibraryRoot,
            recursive: true);
        Directory.Delete(
            repeatedDataRoot,
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
