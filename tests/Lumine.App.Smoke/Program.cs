using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Layout;
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

static bool DetailImageHasSource(
    DetailViewerControl detail)
{
    var field =
        typeof(DetailViewerControl).GetField(
            "_image",
            BindingFlags.Instance
            | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "DetailViewerControl._image was not found.");

    var image =
        field.GetValue(detail)
            as Avalonia.Controls.Image
        ?? throw new InvalidOperationException(
            "DetailViewerControl._image was not an Avalonia Image.");

    return image.Source is not null;
}

static IEnumerable<Control> EnumeratePanelTree(
    Control root)
{
    yield return root;

    if (root is not Panel panel)
    {
        yield break;
    }

    foreach (var child in panel.Children
                 .OfType<Control>())
    {
        foreach (var descendant in
                 EnumeratePanelTree(child))
        {
            yield return descendant;
        }
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

const string VisualOutputPrefix =
    "--visual-output=";
var visualOutputArgument =
    args.FirstOrDefault(
        static argument =>
            argument.StartsWith(
                VisualOutputPrefix,
                StringComparison.Ordinal));
var visualOutputValue =
    visualOutputArgument is null
        ? null
        : visualOutputArgument[
            VisualOutputPrefix.Length..];
if (visualOutputArgument is not null
    && string.IsNullOrWhiteSpace(
        visualOutputValue))
{
    throw new ArgumentException(
        "--visual-output requires a directory.");
}

var visualOutputDirectory =
    visualOutputValue is null
        ? null
        : Path.GetFullPath(
            visualOutputValue);

var visualEvidenceNames =
    new HashSet<string>(
        StringComparer.Ordinal);
if (visualOutputDirectory is not null)
{
    Directory.CreateDirectory(
        visualOutputDirectory);
    var manifestPath =
        Path.Combine(
            visualOutputDirectory,
            "manifest.tsv");
    File.WriteAllText(
        manifestPath,
        "surface\tpixels\tbytes"
        + Environment.NewLine);
}

void CaptureVisualEvidence(
    Window window,
    string name)
{
    if (visualOutputDirectory is null)
    {
        return;
    }

    // Nested overlay popups can need multiple headless render passes
    // before Skia paints their full content (not just popup geometry).
    for (var renderPass = 0;
         renderPass < 3;
         renderPass++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform
            .ForceRenderTimerTick();
    }
    Dispatcher.UIThread.RunJobs();

    using var frame =
        window.CaptureRenderedFrame();
    Require(
        frame.PixelSize.Width > 0
        && frame.PixelSize.Height > 0,
        $"Visual evidence '{name}' produced an empty frame.");

    var outputPath =
        Path.Combine(
            visualOutputDirectory,
            name + ".png");
    frame.Save(
        outputPath);

    var outputInfo =
        new FileInfo(
            outputPath);
    Require(
        outputInfo.Exists
        && outputInfo.Length > 1024,
        $"Visual evidence '{name}' was not persisted as a non-empty PNG.");

    visualEvidenceNames.Add(
        name);
    File.AppendAllText(
        Path.Combine(
            visualOutputDirectory,
            "manifest.tsv"),
        $"{name}\t{frame.PixelSize.Width}x{frame.PixelSize.Height}\t{outputInfo.Length}"
        + Environment.NewLine);
}

async Task WaitForVisualPickerSpectrumAsync(
    TagColorEditor editor,
    string context)
{
    const int maxAttempts = 200;

    for (var attempt = 0;
         attempt < maxAttempts;
         attempt++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform
            .ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        if (editor.VisualPickerFlyoutOpenForSmoke
            && editor.VisualPickerFlyoutHasSpectrumForSmoke
            && editor.VisualPickerSpectrumRenderedForSmoke)
        {
            return;
        }

        await Task.Delay(10);
    }

    throw new InvalidOperationException(
        $"{context} did not finish rendering the real ColorPicker spectrum within {maxAttempts * 10} ms.");
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

var defaultFileTypes =
    new LibraryFileTypePolicy();
Require(
    !defaultFileTypes.IsSupportedPath("contract.heic")
    && !defaultFileTypes.IsSupportedPath("contract.heif")
    && defaultFileTypes.IsSupportedPath("contract.avif"),
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

        var productSmokePortable =
            ProductRuntimeSmoke.ResolveDataPaths(
                [
                    ProductRuntimeSmoke.Switch,
                    $"--library-dir={libraryRoot}"
                ]);
        Require(
            productSmokePortable.IsPortable
            && productSmokePortable.RootPath
                == explicitPortable.RootPath,
            "Product runtime smoke bypassed normal portable data resolution when --data-dir was omitted.");

        var productSmokeCustomRoot =
            Path.Combine(
                root,
                "product-smoke-custom-data");
        var productSmokeCustom =
            ProductRuntimeSmoke.ResolveDataPaths(
                [
                    ProductRuntimeSmoke.Switch,
                    $"--data-dir={productSmokeCustomRoot}",
                    $"--library-dir={libraryRoot}"
                ]);
        Require(
            productSmokeCustom.LocationKind
                == AppDataLocationKind.Custom
            && productSmokeCustom.RootPath
                == Path.GetFullPath(
                    productSmokeCustomRoot),
            "Product runtime smoke did not preserve an explicit --data-dir override.");

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

        var initialScanExtensions =
            ScanExtensionPreference.Resolve(
                lifecycleHost.Settings,
                out var initialScanExtensionWarning);
        Require(
            initialScanExtensionWarning is null
            && initialScanExtensions.SequenceEqual(
                LibraryFileTypes.DefaultExtensions,
                StringComparer.Ordinal),
            "Fresh scan-extension preference did not resolve to the product defaults.");

        await lifecycleHost.SaveScanExtensionsAsync(
            [".JPG", "png", "JFIF"]);
        Require(
            ScanExtensionPreference.Resolve(
                    lifecycleHost.Settings,
                    out var savedScanExtensionWarning)
                .SequenceEqual(
                    new[] { ".jpg", ".png", ".jfif" },
                    StringComparer.Ordinal)
            && savedScanExtensionWarning is null,
            "Built-in/custom scan-extension preference was not normalized before persistence.");

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

        var restoredScanExtensions =
            ScanExtensionPreference.Resolve(
                cleanRestart.Settings,
                out var restoredScanExtensionWarning);
        Require(
            restoredScanExtensionWarning is null
            && restoredScanExtensions.SequenceEqual(
                new[] { ".jpg", ".png", ".jfif" },
                StringComparer.Ordinal),
            "Built-in/custom scan-extension preference did not persist across restart.");

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

    var scanExtensionSettingsPaths =
        AppDataPaths.FromRoot(
            Path.Combine(
                root,
                "scan-extension-settings-host"));
    var scanExtensionSettingsStore =
        new AppSettingsStore(
            scanExtensionSettingsPaths.SettingsPath);
    await scanExtensionSettingsStore.SaveAsync(
        new AppSettingsDocument
        {
            ScanExtensions =
            [
                ".JPG",
                "png",
                ".PNG",
                "JFIF",
                ".bad-ext",
                ""
            ]
        });
    await using (var normalizedScanSettingsHost =
                 await AppHost.StartAsync(
                     scanExtensionSettingsPaths))
    {
        var normalizedScanExtensions =
            ScanExtensionPreference.Resolve(
                normalizedScanSettingsHost.Settings,
                out var normalizedScanWarning);
        Require(
            normalizedScanWarning is null
            && normalizedScanExtensions.SequenceEqual(
                new[] { ".jpg", ".png", ".jfif" },
                StringComparer.Ordinal)
            && !string.IsNullOrWhiteSpace(
                normalizedScanSettingsHost.SettingsWarning),
            "Valid custom scan extensions or invalid-entry startup normalization drifted.");
        await normalizedScanSettingsHost.CompleteCleanShutdownAsync();
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

            var librarySurfaces =
                navigationView
                    .GetVisualDescendants()
                    .OfType<Border>()
                    .Where(
                        border =>
                            (AutomationProperties.GetAutomationId(
                                border)
                            ?? string.Empty)
                            .StartsWith(
                                "library-card-",
                                StringComparison.Ordinal))
                    .ToArray();
            var activeLibrarySurface =
                librarySurfaces.Single(
                    border =>
                        string.Equals(
                            AutomationProperties.GetAutomationId(
                                border),
                            "library-card-1001",
                            StringComparison.Ordinal));
            var addLibraryButton =
                navigationView
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(
                        button =>
                            string.Equals(
                                AutomationProperties.GetName(
                                    button),
                                "画像フォルダーを追加",
                                StringComparison.Ordinal));
            Require(
                librarySurfaces.Length == navLibraries.Length
                && librarySurfaces.All(
                    static surface =>
                        surface.Classes.Contains(
                            "lumine-library-row")
                        && surface.BorderThickness
                            == new Thickness(0))
                && activeLibrarySurface.Classes.Contains(
                    "selected")
                && (activeLibrarySurface.Background is null
                    || (activeLibrarySurface.Background
                            is ISolidColorBrush activeLibraryBackground
                        && activeLibraryBackground.Color.A == 0))
                && librarySurfaces
                    .Where(
                        surface =>
                            !ReferenceEquals(
                                surface,
                                activeLibrarySurface))
                    .All(
                        static surface =>
                            !surface.Classes.Contains(
                                "selected"))
                && addLibraryButton is not null
                && addLibraryButton.Classes.Contains(
                    "lumine-tertiary"),
                "Library sidebar regressed from flat semantic rows to card-heavy or primary-action chrome.");

            var manageLibraryButton =
                navigationView.GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(
                        button =>
                            string.Equals(
                                AutomationProperties.GetName(
                                    button),
                                "ライブラリを管理",
                                StringComparison.Ordinal));
            Require(
                manageLibraryButton is { IsEffectivelyVisible: true }
                && manageLibraryButton.Content
                    is Avalonia.Controls.Shapes.Path manageLibraryIcon
                && manageLibraryIcon.Data is not null
                && manageLibraryIcon.Data.Bounds.Height > 0.5
                && manageLibraryIcon.Width >= 15.5
                && manageLibraryIcon.Height >= 15.5
                && manageLibraryButton.Classes.Contains(
                    "lumine-icon")
                && manageLibraryButton.Classes.Contains(
                    "lumine-tertiary"),
                "Library Manage overflow lost its visible non-zero-height themed vector affordance.");

            var rescanButton =
                navigationView.GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(
                        button =>
                            string.Equals(
                                AutomationProperties.GetName(
                                    button),
                                "現在のライブラリを再スキャン",
                                StringComparison.Ordinal));
            Require(
                rescanButton is { IsEnabled: true }
                && rescanButton.Classes.Contains(
                    "lumine-icon")
                && rescanButton.Classes.Contains(
                    "lumine-tertiary"),
                "Active library did not expose the themed direct rescan utility action.");
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
                && string.Equals(
                    navigationError,
                    "操作を完了できませんでした。もう一度お試しください。",
                    StringComparison.Ordinal)
                && !navigationError.Contains(
                    "navigation-smoke-failure",
                    StringComparison.Ordinal)
                && openableButton.IsEnabled,
                "Async library open failure escaped the navigation error boundary, leaked technical detail, or left the command disabled.");

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
                && string.Equals(
                    navigationError,
                    "操作を完了できませんでした。もう一度お試しください。",
                    StringComparison.Ordinal)
                && !navigationError.Contains(
                    "navigation-smoke-failure",
                    StringComparison.Ordinal)
                && !offlineButton.Focus(
                    NavigationMethod.Tab,
                    KeyModifiers.None)
                && !disabledButton.Focus(
                    NavigationMethod.Tab,
                    KeyModifiers.None),
                "Library row keyboard/error behavior drifted or leaked technical detail for openable/offline/disabled states.");

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
                    && string.Equals(
                        commandError,
                        "操作を完了できませんでした。もう一度お試しください。",
                        StringComparison.Ordinal)
                    && !commandError.Contains(
                        $"{commandPath}-navigation-smoke-failure",
                        StringComparison.Ordinal),
                    $"Async library {commandPath} failure escaped the shared navigation error boundary, leaked technical detail, or left the command disabled.");
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
                            AutomationProperties.GetName(button),
                            "root を開く",
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
                            AutomationProperties.GetName(button),
                            "a を開く",
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
                    static (_, _, _) => Task.CompletedTask,
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

            var publicationLoadMoreCalls = 0;
            var pagedPublicationView =
                ProductNavigationViews.CreatePublicationEntry(
                    largePublications.Take(100).ToArray(),
                    totalCount: 150,
                    hasMore: true,
                    loadMore:
                        () =>
                        {
                            publicationLoadMoreCalls++;
                            return Task.FromResult(
                                new PublicationPage(
                                    largePublications
                                        .Skip(100)
                                        .Take(50)
                                        .ToArray(),
                                    NextCursor: null,
                                    TotalCount: 150));
                        });
            scaleWindow =
                new Window
                {
                    Width = 420,
                    Height = 600,
                    Content = pagedPublicationView
                };
            scaleWindow.Show();
            Dispatcher.UIThread.RunJobs();

            var publicationLoadMore =
                pagedPublicationView
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Single(
                        button =>
                            string.Equals(
                                button.Content as string,
                                "さらに読み込む",
                                StringComparison.Ordinal));
            publicationLoadMore.RaiseEvent(
                new RoutedEventArgs(
                    Button.ClickEvent));
            for (var attempt = 0;
                 attempt < 50
                 && publicationLoadMoreCalls == 0;
                 attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(1);
            }
            Dispatcher.UIThread.RunJobs();

            var pagedPublicationList =
                pagedPublicationView
                    .GetVisualDescendants()
                    .OfType<ListBox>()
                    .Single();
            Require(
                publicationLoadMoreCalls == 1
                && pagedPublicationList.ItemsSource
                    ?.Cast<PublicationInfo>()
                    .Count() == 150
                && !publicationLoadMore.IsVisible
                && pagedPublicationList
                    .GetRealizedContainers()
                    .Count() < 128,
                "Publication history load-more did not append older rows while retaining bounded realization.");
            scaleWindow.Close();
            Dispatcher.UIThread.RunJobs();

            navigationScaleWatch.Stop();
            Require(
                navigationScaleWatch.Elapsed
                    < TimeSpan.FromSeconds(2),
                $"High-count navigation acceptance exceeded the responsiveness budget: {navigationScaleWatch.Elapsed.TotalMilliseconds:N0} ms.");

            var pixivMetadataEditor =
                new CreativePublicationPixivMetadataEditor();
            Require(
                pixivMetadataEditor
                    .AgeRestrictionOptionsForSmoke
                    .SequenceEqual(
                        new[]
                        {
                            "全年齢",
                            "R-18",
                            "R-18G"
                        },
                        StringComparer.Ordinal)
                && string.Equals(
                    pixivMetadataEditor
                        .SelectedAgeRestrictionForSmoke,
                    "全年齢",
                    StringComparison.Ordinal)
                && pixivMetadataEditor
                    .AgeRestrictionAccessibleForSmoke
                && string.Equals(
                    pixivMetadataEditor
                        .PlatformMetadataJson,
                    "{\"ageRestriction\":\"all\",\"aiGenerated\":true}",
                    StringComparison.Ordinal),
                "Pixiv Publication metadata editor did not restore the v1 all-age / AI-generated defaults.");

            pixivMetadataEditor
                .SelectAgeRestrictionForSmoke(
                    "R-18G");
            pixivMetadataEditor
                .AiGeneratedForSmoke = false;
            Require(
                string.Equals(
                    pixivMetadataEditor
                        .PlatformMetadataJson,
                    "{\"ageRestriction\":\"r18g\",\"aiGenerated\":false}",
                    StringComparison.Ordinal),
                "Pixiv R-18G authoring did not persist the canonical r18g value independently from AI-generated state.");

            var allAgeFlags =
                ProductNavigationViews
                    .ExtractPublicationFlagsForSmoke(
                        "{\"ageRestriction\":\"all\"}");
            var r18Flags =
                ProductNavigationViews
                    .ExtractPublicationFlagsForSmoke(
                        "{\"ageRestriction\":\"r18\"}");
            var r18gFlags =
                ProductNavigationViews
                    .ExtractPublicationFlagsForSmoke(
                        "{\"ageRestriction\":\"r18g\"}");
            var legacyR18gFlags =
                ProductNavigationViews
                    .ExtractPublicationFlagsForSmoke(
                        "{\"ageRestriction\":\"R-18G\"}");
            Require(
                allAgeFlags.Contains(
                    "年齢制限: 全年齢",
                    StringComparer.Ordinal)
                && r18Flags.Contains(
                    "年齢制限: R-18",
                    StringComparer.Ordinal)
                && r18gFlags.Contains(
                    "年齢制限: R-18G",
                    StringComparer.Ordinal)
                && legacyR18gFlags.Contains(
                    "年齢制限: R-18G",
                    StringComparer.Ordinal),
                "Publication history did not humanize canonical and legacy Pixiv age restrictions.");

            var publicationDetailSummaryAssets =
                Enumerable.Range(
                        0,
                        LibraryRepository.PublicationSummaryAssetLimit)
                    .Select(
                        index =>
                            new PublicationAssetSnapshot(
                                index + 1,
                                $"publish-{index:D3}.png",
                                $"publish-{index:D3}.png",
                                index))
                    .ToArray();
            var publicationDetailFullAssets =
                Enumerable.Range(0, 20)
                    .Select(
                        index =>
                            new PublicationAssetSnapshot(
                                index + 1,
                                $"publish-{index:D3}.png",
                                $"publish-{index:D3}.png",
                                index))
                    .ToArray();
            var publicationDetailSummary =
                new PublicationInfo(
                    90_001,
                    1,
                    null,
                    "Expandable Publication",
                    new string('B', 420),
                    "tag-one tag-two",
                    "Pixiv",
                    "Smoke Creator",
                    now,
                    "external-only",
                    string.Empty,
                    "{\"aiGenerated\":true,\"ageRestriction\":\"r18\"}",
                    publicationDetailSummaryAssets,
                    now,
                    now,
                    AssetCount: 20);
            var publicationDetailFull =
                publicationDetailSummary with
                {
                    Assets =
                        publicationDetailFullAssets
                };
            var publicationDetailLoadCalls =
                0;
            var expandablePublicationView =
                ProductNavigationViews.CreatePublicationEntry(
                    new[]
                    {
                        publicationDetailSummary
                    },
                    totalCount: 1,
                    hasMore: false,
                    loadMore:
                        static () =>
                            Task.FromResult(
                                new PublicationPage(
                                    Array.Empty<PublicationInfo>(),
                                    null,
                                    1)),
                    loadPublicationDetail:
                        publication =>
                        {
                            publicationDetailLoadCalls++;
                            return Task.FromResult<PublicationInfo?>(
                                publication.Id
                                    == publicationDetailFull.Id
                                        ? publicationDetailFull
                                        : null);
                        });
            scaleWindow =
                new Window
                {
                    Width = 420,
                    Height = 600,
                    Content =
                        expandablePublicationView
                };
            scaleWindow.Show();
            Dispatcher.UIThread.RunJobs();

            var collapsedPublicationText =
                string.Join(
                    "\n",
                    expandablePublicationView
                        .GetVisualDescendants()
                        .OfType<TextBlock>()
                        .Select(
                            static text =>
                                text.Text));
            Require(
                collapsedPublicationText.Contains(
                    "外部ID: external-only",
                    StringComparison.Ordinal),
                "Publication summary hid External ID when External URL was empty.");

            var publicationExpand =
                expandablePublicationView
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Single(
                        button =>
                            string.Equals(
                                button.Content as string,
                                "投稿内容をすべて表示",
                                StringComparison.Ordinal));
            publicationExpand.RaiseEvent(
                new RoutedEventArgs(
                    Button.ClickEvent));
            for (var attempt = 0;
                 attempt < 100
                 && !string.Equals(
                     publicationExpand.Content as string,
                     "詳細を閉じる",
                     StringComparison.Ordinal);
                 attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(1);
            }
            Dispatcher.UIThread.RunJobs();

            var expandedPublicationText =
                string.Join(
                    "\n",
                    expandablePublicationView
                        .GetVisualDescendants()
                        .OfType<TextBlock>()
                        .Select(
                            static text =>
                                text.Text));
            Require(
                publicationDetailLoadCalls == 1
                && string.Equals(
                    publicationExpand.Content as string,
                    "詳細を閉じる",
                    StringComparison.Ordinal)
                && expandedPublicationText.Contains(
                    "20. publish-019.png",
                    StringComparison.Ordinal)
                && expandedPublicationText.Contains(
                    "AI生成: はい",
                    StringComparison.Ordinal)
                && expandedPublicationText.Contains(
                    "年齢制限: R-18",
                    StringComparison.Ordinal),
                "Publication history did not lazily expose the complete ordered snapshot and human-readable platform metadata.");

            publicationExpand.RaiseEvent(
                new RoutedEventArgs(
                    Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Require(
                string.Equals(
                    publicationExpand.Content as string,
                    "投稿内容をすべて表示",
                    StringComparison.Ordinal),
                "Publication detail did not collapse back to the bounded summary.");

            publicationExpand.RaiseEvent(
                new RoutedEventArgs(
                    Button.ClickEvent));
            for (var attempt = 0;
                 attempt < 100
                 && !string.Equals(
                     publicationExpand.Content as string,
                     "詳細を閉じる",
                     StringComparison.Ordinal);
                 attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(1);
            }
            Require(
                publicationDetailLoadCalls == 1,
                "Publication detail reloaded the full snapshot after collapse instead of reusing the card-local detail.");
            scaleWindow.Close();
            Dispatcher.UIThread.RunJobs();

            var colorEditorSmoke =
                new TagColorEditor();
            Require(
                colorEditorSmoke.IsColorValid
                && colorEditorSmoke.SelectedColor
                    == TagColor.Default
                && colorEditorSmoke.PresetCountForSmoke
                    == TagColor.Presets.Count,
                "Reusable tag color editor did not initialize with a valid shared preset.");

            foreach (var customColor in
                     new[]
                     {
                         "#abc",
                         "#123456",
                         "#12345678"
                     })
            {
                colorEditorSmoke.SetCustomTextForSmoke(
                    customColor);
                Require(
                    colorEditorSmoke.IsColorValid
                    && string.Equals(
                        colorEditorSmoke.SelectedColor,
                        customColor,
                        StringComparison.Ordinal)
                    && !colorEditorSmoke.ValidationVisibleForSmoke,
                    $"Tag color editor rejected valid custom color {customColor}.");
            }

            var alphaSmoke =
                TagColor.ToColor(
                    "#12345678");
            Require(
                alphaSmoke.R == 0x12
                && alphaSmoke.G == 0x34
                && alphaSmoke.B == 0x56
                && alphaSmoke.A == 0x78,
                "#RRGGBBAA tag color parsing drifted from repository storage semantics.");

            colorEditorSmoke.SetVisualColorForSmoke(
                Color.FromArgb(
                    0x80,
                    0x12,
                    0x34,
                    0x56));
            Require(
                colorEditorSmoke.IsColorValid
                && string.Equals(
                    colorEditorSmoke.SelectedColor,
                    "#12345680",
                    StringComparison.Ordinal)
                && string.Equals(
                    colorEditorSmoke.CustomTextForSmoke,
                    "#12345680",
                    StringComparison.Ordinal),
                "Visual tag ColorPicker did not synchronize alpha-aware color selection into the canonical HEX field.");

            colorEditorSmoke.SetCustomTextForSmoke(
                "#abcdef40");
            var visualFromHex =
                colorEditorSmoke.VisualColorForSmoke;
            Require(
                visualFromHex.R == 0xab
                && visualFromHex.G == 0xcd
                && visualFromHex.B == 0xef
                && visualFromHex.A == 0x40,
                "HEX tag color input did not synchronize back into the visual ColorPicker.");

            colorEditorSmoke.SetCustomTextForSmoke(
                "not-a-color");
            Require(
                !colorEditorSmoke.IsColorValid
                && colorEditorSmoke.SelectedColor is null
                && colorEditorSmoke.ValidationVisibleForSmoke,
                "Invalid custom tag color did not enter an explicit validation state.");

            colorEditorSmoke.SetCustomTextForSmoke(
                "  #ABCDEF80  ");
            Require(
                colorEditorSmoke.IsColorValid
                && string.Equals(
                    colorEditorSmoke.CustomTextForSmoke,
                    "#abcdef80",
                    StringComparison.Ordinal)
                && string.Equals(
                    colorEditorSmoke.SelectedColor,
                    "#abcdef80",
                    StringComparison.Ordinal),
                "Valid custom tag color was not normalized consistently for display/save.");

            colorEditorSmoke.SelectPresetForSmoke(1);
            Require(
                colorEditorSmoke.IsColorValid
                && string.Equals(
                    colorEditorSmoke.CustomTextForSmoke,
                    TagColor.Presets[1],
                    StringComparison.Ordinal)
                && string.Equals(
                    colorEditorSmoke.SelectedColor,
                    TagColor.Presets[1],
                    StringComparison.Ordinal),
                "Preset selection did not synchronize the free-entry color field.");

            var previousNavigationTextScale =
                LumineVisualMetrics.TextScaleFactor;
            try
            {
                LumineVisualMetrics.ConfigureTextScaleFactor(
                    2.25);
                App.RefreshScaledProductResources(
                    Application.Current
                    ?? throw new InvalidOperationException(
                        "Tag smoke has no current Avalonia application."));

                string? createdTagColor = null;
                long? editedTagId = null;
                string? editedTagName = null;
                string? editedTagColor = null;
                var compactTags =
                    new[]
                    {
                        new LibraryTagInfo(
                            1,
                            "a-very-long-tag-name-that-must-trim",
                            "#12345678",
                            123),
                        new LibraryTagInfo(
                            2,
                            "short",
                            "#abc",
                            4)
                    };
                var compactTagsView =
                    ProductNavigationViews.CreateTags(
                        compactTags,
                        Array.Empty<string>(),
                        static _ => Task.CompletedTask,
                        (_, color) =>
                        {
                            createdTagColor = color;
                            return Task.CompletedTask;
                        },
                        (tag, name, color) =>
                        {
                            if (string.Equals(
                                    name,
                                    "collision",
                                    StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException(
                                    "Tag 'collision' already exists.");
                            }

                            editedTagId = tag.Id;
                            editedTagName = name;
                            editedTagColor = color;
                            return Task.CompletedTask;
                        },
                        static _ => Task.CompletedTask);
                compactTagsView.Width = 300;
                compactTagsView.HorizontalAlignment =
                    Avalonia.Layout.HorizontalAlignment.Left;

                var compactTagsWindow =
                    new Window
                    {
                        Width = 900,
                        Height = 600,
                        Content = compactTagsView
                    };
                compactTagsWindow.Show();
                Dispatcher.UIThread.RunJobs();

                var compactActionRow =
                    compactTagsView
                        .GetVisualDescendants()
                        .OfType<Panel>()
                        .First(
                            panel =>
                                panel.Children
                                    .OfType<Button>()
                                    .Any(
                                        button =>
                                            string.Equals(
                                                button.Content
                                                    as string,
                                                "＋ 新規",
                                                StringComparison.Ordinal))
                                && panel.Children
                                    .OfType<Button>()
                                    .Any(
                                        button =>
                                            string.Equals(
                                                button.Content
                                                    as string,
                                                "管理",
                                                StringComparison.Ordinal)));
                var newTagButton =
                    compactActionRow.Children
                        .OfType<Button>()
                        .First(
                            button =>
                                string.Equals(
                                    button.Content as string,
                                    "＋ 新規",
                                    StringComparison.Ordinal));
                var compactTagList =
                    compactTagsView
                        .GetVisualDescendants()
                        .OfType<ListBox>()
                        .Single();
                var compactCreateFlyout =
                    newTagButton.Flyout as Flyout
                    ?? throw new InvalidOperationException(
                        "Top-level tag create command did not own a Flyout.");

                Require(
                    ReferenceEquals(
                        newTagButton.Flyout,
                        compactCreateFlyout),
                    "Top-level tag create Button did not own the expected Flyout.");

                compactCreateFlyout.ShowAt(
                    newTagButton);
                Dispatcher.UIThread.RunJobs();

                Require(
                    compactCreateFlyout.IsOpen,
                    "Top-level tag create Flyout could not be opened at its command anchor.");

                var compactCreateSurface =
                    compactCreateFlyout.Content as Border
                    ?? throw new InvalidOperationException(
                        "Tag create Flyout did not expose the expected product surface.");
                var compactCreateName =
                    compactCreateSurface
                        .GetVisualDescendants()
                        .OfType<TextBox>()
                        .First(
                            box =>
                                string.Equals(
                                    box.PlaceholderText,
                                    "新しいタグ名",
                                    StringComparison.Ordinal));
                var compactColorEditor =
                    compactCreateSurface
                        .GetVisualDescendants()
                        .OfType<TagColorEditor>()
                        .Single();
                Require(
                    compactColorEditor
                        .GetVisualDescendants()
                        .OfType<ColorPicker>()
                        .Count() == 1
                    && compactColorEditor
                        .UsesSharedThemeForSmoke
                    && compactColorEditor
                        .PresetGridIsFiveByTwoForSmoke
                    && compactColorEditor
                        .FirstPresetWidthForSmoke > 30
                    && compactColorEditor
                        .FirstPresetWidthForSmoke <= 38.5,
                    "Top-level tag create Flyout did not expose one themed, scale-aware color editor.");
                var compactCreateButton =
                    compactCreateSurface
                        .GetVisualDescendants()
                        .OfType<Button>()
                        .First(
                            button =>
                                string.Equals(
                                    button.Content as string,
                                    "作成",
                                    StringComparison.Ordinal));

                compactCreateName.Text =
                    "custom-color-smoke";
                compactColorEditor.SetCustomTextForSmoke(
                    "#12345678");
                Require(
                    !compactColorEditor.AdvancedVisibleForSmoke,
                    "Tag create expanded custom color controls by default.");

                if (visualOutputDirectory is not null)
                {
                    CaptureVisualEvidence(
                        compactTagsWindow,
                        "tags-create-900x600-text225");

                    compactColorEditor.OpenVisualPickerForSmoke();
                    await WaitForVisualPickerSpectrumAsync(
                        compactColorEditor,
                        "Tag create custom-color visual evidence");
                    CaptureVisualEvidence(
                        compactTagsWindow,
                        "tags-create-custom-color-900x600-text225");
                    compactColorEditor.CloseVisualPickerForSmoke();
                    compactColorEditor.SetAdvancedVisibleForSmoke(false);
                    Dispatcher.UIThread.RunJobs();
                }

                foreach (var navigationWidth in
                         new[]
                         {
                             250d,
                             280d,
                             300d
                         })
                {
                    compactCreateFlyout.Hide();
                    compactTagsView.Width =
                        navigationWidth;
                    Dispatcher.UIThread.RunJobs();

                    var closedListHeight =
                        compactTagList.Bounds.Height;
                    Require(
                        compactActionRow.Bounds.Width
                            <= compactTagsView.Bounds.Width + 0.5
                        && compactActionRow.Children
                            .OfType<Control>()
                            .Where(
                                child =>
                                    child.IsVisible)
                            .All(
                                child =>
                                    child.Bounds.X >= -0.5
                                    && child.Bounds.Right
                                        <= compactActionRow.Bounds.Width
                                            + 0.5),
                        $"Tags toolbar escaped its available width at {navigationWidth:N0} DIP / 225% text scale.");

                    compactCreateFlyout.ShowAt(
                        newTagButton);
                    Dispatcher.UIThread.RunJobs();

                    Require(
                        compactCreateFlyout.IsOpen
                        && compactCreateButton.IsEnabled
                        && compactCreateSurface.Classes.Contains(
                            "lumine-popover")
                        && compactCreateSurface.Bounds.Width
                            is > 360 and <= 400.5
                        && !compactCreateSurface
                            .GetVisualAncestors()
                            .OfType<ScrollViewer>()
                            .Any(
                                static scroll =>
                                    scroll.Viewport.Width > 0
                                    && scroll.Extent.Width
                                        > scroll.Viewport.Width + 0.5)
                        && compactColorEditor.Bounds.Width
                            <= compactCreateSurface.Bounds.Width + 0.5
                        && compactTagList.Bounds.Height > 24
                        && Math.Abs(
                            compactTagList.Bounds.Height
                            - closedListHeight) < 1,
                        $"Tag create Flyout changed pane layout or list viewport at {navigationWidth:N0} DIP / 225% text scale. "
                        + $"flyoutOpen={compactCreateFlyout.IsOpen}, "
                        + $"surface={compactCreateSurface.Bounds.Width:N1}x{compactCreateSurface.Bounds.Height:N1}, "
                        + $"pane={compactTagsView.Bounds.Width:N1}x{compactTagsView.Bounds.Height:N1}, "
                        + $"listClosed={closedListHeight:N1}, listOpen={compactTagList.Bounds.Height:N1}.");
                }

                compactTagsView.Width = 250;
                compactColorEditor.SetCustomTextForSmoke(
                    "broken");
                Dispatcher.UIThread.RunJobs();
                Require(
                    !compactCreateButton.IsEnabled,
                    "Tag creation remained enabled with an invalid custom color at the minimum navigation width.");

                compactColorEditor.SetCustomTextForSmoke(
                    "#12345678");
                Dispatcher.UIThread.RunJobs();
                Require(
                    compactCreateButton.IsEnabled,
                    "Tag creation did not recover after restoring a valid custom color.");

                compactCreateButton.RaiseEvent(
                    new RoutedEventArgs(
                        Button.ClickEvent));
                for (var attempt = 0;
                     attempt < 50
                     && createdTagColor is null;
                     attempt++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(1);
                }
                Require(
                    string.Equals(
                        createdTagColor,
                        "#12345678",
                        StringComparison.Ordinal)
                    && !compactCreateFlyout.IsOpen,
                    "Top-level tag creation did not persist the exact validated custom color and dismiss its Flyout.");

                var manageButton =
                    compactActionRow.Children
                        .OfType<Button>()
                        .First(
                            button =>
                                string.Equals(
                                    button.Content as string,
                                    "管理",
                                    StringComparison.Ordinal));
                manageButton.RaiseEvent(
                    new RoutedEventArgs(
                        Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Require(
                    manageButton.Classes.Contains(
                        "active"),
                    "Tag management mode did not use the shared semantic active state.");

                var editButton =
                    compactTagsView
                        .GetVisualDescendants()
                        .OfType<Button>()
                        .First(
                            button =>
                                string.Equals(
                                    button.Content as string,
                                    "編集",
                                    StringComparison.Ordinal));
                var deleteButton =
                    compactTagsView
                        .GetVisualDescendants()
                        .OfType<Button>()
                        .First(
                            button =>
                                string.Equals(
                                    button.Content as string,
                                    "削除",
                                    StringComparison.Ordinal));
                Require(
                    !editButton
                        .GetVisualAncestors()
                        .OfType<Button>()
                        .Any(),
                    "Tag manage mode nested the edit command inside another Button.");

                var editFlyout =
                    editButton.Flyout as Flyout
                    ?? throw new InvalidOperationException(
                        "Tag edit command did not own a Flyout.");
                editFlyout.ShowAt(
                    editButton);
                Dispatcher.UIThread.RunJobs();

                var editSurface =
                    editFlyout.Content as Border
                    ?? throw new InvalidOperationException(
                        "Tag edit Flyout did not expose the expected product surface.");
                var editName =
                    editSurface
                        .GetVisualDescendants()
                        .OfType<TextBox>()
                        .First(
                            box =>
                                string.Equals(
                                    box.PlaceholderText,
                                    "タグ名",
                                    StringComparison.Ordinal));
                var editColor =
                    editSurface
                        .GetVisualDescendants()
                        .OfType<TagColorEditor>()
                        .Single();
                var saveEdit =
                    editSurface
                        .GetVisualDescendants()
                        .OfType<Button>()
                        .First(
                            button =>
                                string.Equals(
                                    button.Content as string,
                                    "保存",
                                    StringComparison.Ordinal));
                var cancelEdit =
                    editSurface
                        .GetVisualDescendants()
                        .OfType<Button>()
                        .First(
                            button =>
                                string.Equals(
                                    button.Content as string,
                                    "キャンセル",
                                    StringComparison.Ordinal));

                Require(
                    editFlyout.IsOpen
                    && editSurface.Classes.Contains(
                        "lumine-popover")
                    && editSurface.Bounds.Width
                        is > 360 and <= 400.5
                    && editName.Text
                        == compactTags[0].Name
                    && editColor.SelectedColor
                        == compactTags[0].Color
                    && editColor.UsesSharedThemeForSmoke
                    && editColor.PresetGridIsFiveByTwoForSmoke
                    && !editSurface
                        .GetVisualAncestors()
                        .OfType<ScrollViewer>()
                        .Any(
                            static scroll =>
                                scroll.Viewport.Width > 0
                                && scroll.Extent.Width
                                    > scroll.Viewport.Width + 0.5)
                    && editButton.Classes.Contains(
                        "lumine-compact")
                    && deleteButton.Classes.Contains(
                        "lumine-compact")
                    && editSurface
                        .GetVisualDescendants()
                        .OfType<TextBlock>()
                        .Any(
                            block =>
                                string.Equals(
                                    block.Text,
                                    "123件の画像で使用",
                                    StringComparison.Ordinal)),
                    "Tag edit Flyout did not use the scale-aware shared editor surface and compact management actions.");

                Require(
                    !editColor.AdvancedVisibleForSmoke,
                    "Tag edit expanded custom color controls by default.");

                if (visualOutputDirectory is not null)
                {
                    CaptureVisualEvidence(
                        compactTagsWindow,
                        "tags-edit-900x600-text225");

                    // ColorSpectrum builds its bitmap asynchronously after layout.
                    // Wait on the actual ImageBrush rather than sleeping blindly.
                    editColor.OpenVisualPickerForSmoke();
                    await WaitForVisualPickerSpectrumAsync(
                        editColor,
                        "Tag edit custom-color visual evidence");
                    CaptureVisualEvidence(
                        compactTagsWindow,
                        "tags-edit-custom-color-900x600-text225");
                    editColor.CloseVisualPickerForSmoke();
                    editColor.SetAdvancedVisibleForSmoke(false);
                    Dispatcher.UIThread.RunJobs();
                }

                editFlyout.Hide();
                Dispatcher.UIThread.RunJobs();
                editFlyout.ShowAt(
                    editButton);
                Dispatcher.UIThread.RunJobs();

                editName.Text =
                    "renamed-long-tag";
                editColor.SetCustomTextForSmoke(
                    "#abcdef80");
                Dispatcher.UIThread.RunJobs();
                Require(
                    saveEdit.IsEnabled,
                    "Tag edit save did not enable for a valid renamed tag/color.");

                saveEdit.RaiseEvent(
                    new RoutedEventArgs(
                        Button.ClickEvent));
                for (var attempt = 0;
                     attempt < 50
                     && editedTagName is null;
                     attempt++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(1);
                }
                Require(
                    editedTagId == compactTags[0].Id
                    && editedTagName
                        == "renamed-long-tag"
                    && editedTagColor
                        == "#abcdef80"
                    && !editFlyout.IsOpen,
                    "Tag edit did not submit the current tag identity/name/color and dismiss on success.");

                editFlyout.ShowAt(
                    editButton);
                Dispatcher.UIThread.RunJobs();
                editName.Text =
                    "collision";
                Dispatcher.UIThread.RunJobs();
                saveEdit.RaiseEvent(
                    new RoutedEventArgs(
                        Button.ClickEvent));
                for (var attempt = 0;
                     attempt < 50
                     && !editSurface
                         .GetVisualDescendants()
                         .OfType<TextBlock>()
                         .Any(
                             block =>
                                 block.Text?.Contains(
                                     "保存できませんでした。",
                                     StringComparison.Ordinal)
                                 == true);
                     attempt++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(1);
                }
                var tagEditFailureText =
                    editSurface
                        .GetVisualDescendants()
                        .OfType<TextBlock>()
                        .Select(
                            static block =>
                                block.Text
                                ?? string.Empty)
                        .ToArray();
                Require(
                    editFlyout.IsOpen
                    && tagEditFailureText.Any(
                        static text =>
                            text.Contains(
                                "保存できませんでした。",
                                StringComparison.Ordinal))
                    && !tagEditFailureText.Any(
                        static text =>
                            text.Contains(
                                "already exists",
                                StringComparison.OrdinalIgnoreCase)),
                    "Tag edit failure did not remain local/retryable or leaked raw technical detail.");
                cancelEdit.RaiseEvent(
                    new RoutedEventArgs(
                        Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                var removeButton =
                    compactTagsView
                        .GetVisualDescendants()
                        .OfType<Button>()
                        .First(
                            button =>
                                string.Equals(
                                    button.Content as string,
                                    "削除",
                                    StringComparison.Ordinal));
                Require(
                    !removeButton
                        .GetVisualAncestors()
                        .OfType<Button>()
                        .Any(),
                    "Tag manage mode still nests the destructive delete button inside another Button.");

                var manageRow =
                    removeButton
                        .GetVisualAncestors()
                        .OfType<Grid>()
                        .First(
                            grid =>
                                grid.Children.Contains(
                                    removeButton));

                foreach (var navigationWidth in
                         new[]
                         {
                             250d,
                             280d,
                             300d
                         })
                {
                    compactTagsView.Width =
                        navigationWidth;
                    Dispatcher.UIThread.RunJobs();

                    Require(
                        removeButton.Bounds.Right
                            <= manageRow.Bounds.Width + 0.5
                        && manageRow.Bounds.Width
                            <= compactTagsView.Bounds.Width + 0.5,
                        $"Tag manage row overflowed at {navigationWidth:N0} DIP / 225% text scale.");
                }

                BrowseFilterState? renamedScopeState = null;
                var scopeControls =
                    new BrowseWorkspaceControls(
                        new BrowseFilterState(
                            RequiredTags:
                                ["short"]),
                        new BrowsePreferences(
                            BrowseViewMode.Grid,
                            1,
                            AssetSortOrder.ModifiedNewest),
                        compactTags,
                        new LibraryBrowseFacets(
                            Array.Empty<string>(),
                            Array.Empty<string>()),
                        state =>
                        {
                            renamedScopeState = state;
                            return Task.CompletedTask;
                        },
                        static _ => Task.CompletedTask);
                await scopeControls.ReplaceTagScopeAsync(
                    "SHORT",
                    "renamed-short");
                Require(
                    scopeControls.State.TagNames.SequenceEqual(
                        ["renamed-short"],
                        StringComparer.Ordinal)
                    && renamedScopeState is not null
                    && renamedScopeState.TagNames.SequenceEqual(
                        ["renamed-short"],
                        StringComparer.Ordinal),
                    "Active Browse tag scope was not replaced case-insensitively after rename.");

                compactTagsWindow.Close();
                Dispatcher.UIThread.RunJobs();
            }
            finally
            {
                LumineVisualMetrics.ConfigureTextScaleFactor(
                    previousNavigationTextScale);
                App.RefreshScaledProductResources(
                    Application.Current
                    ?? throw new InvalidOperationException(
                        "Tag smoke has no current Avalonia application."));
            }

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

    var shellOpenProgress =
        new List<CoreViewerOpenProgress>();
    var shellRuntime =
        await CoreViewerRuntime.OpenAsync(
            shellLibraryRoot,
            AppDataPaths.FromRoot(shellDataRoot),
            Lumine.App.Program.ResourcePolicy,
            progress:
                new InlineProgress<CoreViewerOpenProgress>(
                    shellOpenProgress.Add));

    Require(
        shellRuntime.AssetCount == 2
        && shellOpenProgress.Any(
            static update =>
                update.Stage
                    == CoreViewerOpenStage.SynchronizingLibrary
                && update.ScanProgress
                    is { Discovered: >= 2, Persisted: >= 2 })
        && shellOpenProgress.Any(
            static update =>
                update.Stage
                    == CoreViewerOpenStage.Ready),
        $"Production Core Viewer runtime/progress bridge did not index and report the expected 2 assets; actual={shellRuntime.AssetCount}.");

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
            Require(
                !shell.ContextDetail.PathCopyEnabledForSmoke
                && string.IsNullOrEmpty(
                    shell.ContextDetail.PathCopyValueForSmoke),
                "Inspector exposed a stale source-path copy action before an asset was loaded.");

            Require(
                shell.ContextDetail.PublicationCardCountForSmoke == 1
                && string.Equals(
                    shell.ContextDetail.PublicationCountTextForSmoke,
                    "0件",
                    StringComparison.Ordinal)
                && string.Join(
                        "\n",
                        shell.ContextDetail.PublicationCardTextForSmoke)
                    .Contains(
                        "公開履歴はありません",
                        StringComparison.Ordinal),
                "Inspector Publication empty state was not explicit and compact.");

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
                    == firstContextAsset.Id
                && shell.ContextDetail.HasPreview
                && !shell.ContextDetail.PreviewStatusVisibleForSmoke,
                "Contextual detail panel did not render the selected image preview.");
            var expectedContextPath =
                Path.GetFullPath(
                    Path.Combine(
                        shellLibraryRoot,
                        firstContextAsset.RelativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));
            Require(
                shell.ContextDetail.TitleText
                    == firstContextAsset.DisplayName
                && string.Equals(
                    shell.ContextDetail.PathText,
                    expectedContextPath,
                    StringComparison.OrdinalIgnoreCase)
                && shell.ContextDetail.PathCopyEnabledForSmoke
                && string.Equals(
                    shell.ContextDetail.PathCopyValueForSmoke,
                    shell.ContextDetail.PathText,
                    StringComparison.Ordinal)
                && string.Equals(
                    shell.ContextDetail.DisplayedPathTextForSmoke,
                    firstContextAsset.RelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar),
                    StringComparison.Ordinal),
                "Contextual detail panel did not preserve full-path copy semantics while presenting a concise library-relative path.");

            var inspectorFieldNames =
                shell.ContextDetail
                    .GetVisualDescendants()
                    .OfType<Control>()
                    .Select(
                        AutomationProperties.GetName)
                    .Where(
                        static name =>
                            !string.IsNullOrWhiteSpace(name))
                    .ToHashSet(
                        StringComparer.Ordinal);
            Require(
                new[]
                {
                    "評価",
                    "お気に入り",
                    "状態",
                    "タグ",
                    "カラー",
                    "ノート"
                }.All(
                    inspectorFieldNames.Contains),
                "Inspector visual field labels are not exposed as accessible editor names.");

            Require(
                shell.ContextDetail.RatingText == "★5"
                && shell.ContextDetail.TagsText.Contains(
                    "context-tag",
                    StringComparison.Ordinal)
                && shell.ContextDetail.NotesText
                    == "context-detail-note",
                "Contextual detail panel did not expose user-owned metadata.");

            Grid InspectorEditorRow(string label) =>
                shell.ContextDetail
                    .GetVisualDescendants()
                    .OfType<Grid>()
                    .Single(
                        row =>
                            string.Equals(
                                AutomationProperties.GetAutomationId(
                                    row),
                                $"inspector-editor-row-{label}",
                                StringComparison.Ordinal));

            var ratingRow =
                InspectorEditorRow("評価");
            var directRatingButtons =
                ratingRow
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Where(
                        button =>
                            (AutomationProperties.GetName(button)
                                ?? string.Empty)
                            .StartsWith(
                                "評価 ",
                                StringComparison.Ordinal))
                    .ToArray();
            Require(
                directRatingButtons.Length == 5
                && ratingRow.Bounds.Height is >= 35 and <= 37
                && directRatingButtons.All(
                    static button =>
                    {
                        if (button.Content is not TextBlock glyph)
                        {
                            return false;
                        }

                        var glyphOrigin =
                            glyph.TranslatePoint(
                                new Point(0, 0),
                                button);
                        if (glyphOrigin is null)
                        {
                            return false;
                        }

                        var buttonCenterX =
                            button.Bounds.Width / 2;
                        var buttonCenterY =
                            button.Bounds.Height / 2;
                        var glyphCenterX =
                            glyphOrigin.Value.X
                            + (glyph.Bounds.Width / 2);
                        var glyphCenterY =
                            glyphOrigin.Value.Y
                            + (glyph.Bounds.Height / 2);

                        return
                            Math.Abs(button.Width - 34) < 0.01
                            && Math.Abs(button.Height - 34) < 0.01
                            && button.Padding.Left < 0.01
                            && button.Padding.Top < 0.01
                            && button.HorizontalContentAlignment
                                == HorizontalAlignment.Center
                            && button.VerticalContentAlignment
                                == VerticalAlignment.Center
                            && glyph.HorizontalAlignment
                                == HorizontalAlignment.Center
                            && glyph.VerticalAlignment
                                == VerticalAlignment.Center
                            && ReferenceEquals(
                                button.Background,
                                Brushes.Transparent)
                            && ReferenceEquals(
                                button.BorderBrush,
                                Brushes.Transparent)
                            && Math.Abs(
                                glyphCenterX
                                - buttonCenterX) <= 1.5
                            && Math.Abs(
                                glyphCenterY
                                - buttonCenterY) <= 1.5;
                    }),
                "Inspector rating row lost fixed geometry or the star glyph was not actually centered after layout.");

            var colorRow =
                InspectorEditorRow("カラー");
            var directColorButtons =
                colorRow
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Where(
                        button =>
                            (AutomationProperties.GetName(button)
                                ?? string.Empty)
                            .StartsWith(
                                "カラー ",
                                StringComparison.Ordinal))
                    .ToArray();
            var colorCenters =
                directColorButtons
                    .Select(
                        button =>
                        {
                            var origin =
                                button.TranslatePoint(
                                    new Point(0, 0),
                                    colorRow)
                                ?? new Point(
                                    double.NaN,
                                    double.NaN);
                            return new Point(
                                origin.X
                                    + (button.Bounds.Width / 2),
                                origin.Y
                                    + (button.Bounds.Height / 2));
                        })
                    .OrderBy(
                        static center =>
                            center.X)
                    .ToArray();
            var colorCenterGaps =
                colorCenters
                    .Zip(
                        colorCenters.Skip(1),
                        static (left, right) =>
                            right.X - left.X)
                    .ToArray();

            if (visualOutputDirectory is not null)
            {
                CaptureVisualEvidence(
                    window,
                    "inspector-organize-1100x720");
            }

            Require(
                directColorButtons.Length == 8
                && colorRow.Bounds.Height is >= 35 and <= 37
                && directColorButtons.All(
                    static button =>
                        Math.Abs(button.Width - 28) < 0.01
                        && Math.Abs(button.Height - 28) < 0.01
                        && Math.Abs(
                            button.BorderThickness.Left - 2) < 0.01
                        && button.HorizontalContentAlignment
                            == HorizontalAlignment.Center
                        && button.VerticalContentAlignment
                            == VerticalAlignment.Center
                        && button.HorizontalAlignment
                            == HorizontalAlignment.Center
                        && button.VerticalAlignment
                            == VerticalAlignment.Center)
                && colorCenters.Max(
                    static center => center.Y)
                    - colorCenters.Min(
                        static center => center.Y) <= 1
                && colorCenterGaps.Length == 7
                && colorCenterGaps.Max()
                    - colorCenterGaps.Min() <= 1.5,
                $"Inspector color row lost fixed chip geometry, a common centerline, or even spacing. count={directColorButtons.Length}, rowHeight={colorRow.Bounds.Height:F2}, centers={string.Join(" | ", colorCenters.Select(static center => $"({center.X:F2},{center.Y:F2})"))}, gaps={string.Join(",", colorCenterGaps.Select(static gap => gap.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))}");

            foreach (var simpleRowLabel in
                     new[]
                     {
                         "評価",
                         "お気に入り",
                         "状態",
                         "カラー"
                     })
            {
                var simpleRow =
                    InspectorEditorRow(simpleRowLabel);
                Require(
                    simpleRow.Bounds.Height is >= 35 and <= 37
                    && simpleRow.ColumnDefinitions.Count == 2,
                    $"Inspector {simpleRowLabel} row drifted from the shared normal-scale form grid.");
            }

            Require(
                shell.ContextDetail.UsesDirectRatingControlsForSmoke
                && shell.ContextDetail.UsesDirectColorControlsForSmoke
                && !shell.ContextDetail.RetryVisibleForSmoke,
                "Inspector did not expose direct rating/color controls in the successful loaded state.");

            var inspectorTagPicker =
                shell.ContextDetail
                    .GetVisualDescendants()
                    .OfType<ManagedTagPicker>()
                    .Single();
            var inspectorTagColorEditor =
                inspectorTagPicker
                    .GetVisualDescendants()
                    .OfType<TagColorEditor>()
                    .Single();
            Require(
                !inspectorTagColorEditor.AdvancedVisibleForSmoke,
                "Frequent Inspector tagging unexpectedly expanded custom color controls.");

            Require(
                inspectorTagPicker.CandidateCountForSmoke == 0
                && !inspectorTagPicker.CandidateSurfaceVisibleForSmoke
                && string.Equals(
                    inspectorTagPicker.SummaryTextForSmoke,
                    "追加できるタグはありません。",
                    StringComparison.Ordinal),
                "Inspector empty tag candidates still rendered a redundant boxed empty state.");


            if (visualOutputDirectory is not null)
            {
                CaptureVisualEvidence(
                    window,
                    "tags-assignment-1100x720");
            }

            inspectorTagPicker.SetSearchForSmoke(
                "__inspector_custom_tag__");
            Dispatcher.UIThread.RunJobs();

            Require(
                inspectorTagPicker.CreateSurfaceVisibleForSmoke
                && !inspectorTagColorEditor.AdvancedVisibleForSmoke
                && inspectorTagColorEditor
                    .GetVisualDescendants()
                    .OfType<ColorPicker>()
                    .Count() == 1,
                "Inspector tag creation did not expose the shared visual ColorPicker.");
            inspectorTagColorEditor.SetCustomTextForSmoke(
                "invalid");
            Dispatcher.UIThread.RunJobs();
            Require(
                inspectorTagPicker.CreateSurfaceVisibleForSmoke
                && !inspectorTagPicker.CreateButtonEnabledForSmoke
                && !inspectorTagColorEditor.IsColorValid,
                "Inspector tag creation did not block an invalid free color.");

            inspectorTagColorEditor.SetCustomTextForSmoke(
                "  #ABCDEF80  ");
            Dispatcher.UIThread.RunJobs();
            Require(
                inspectorTagPicker.CreateButtonEnabledForSmoke
                && inspectorTagColorEditor.IsColorValid
                && string.Equals(
                    inspectorTagColorEditor.SelectedColor,
                    "#abcdef80",
                    StringComparison.Ordinal)
                && string.Equals(
                    inspectorTagColorEditor.CustomTextForSmoke,
                    "#abcdef80",
                    StringComparison.Ordinal),
                "Inspector tag picker did not use the shared free-color editor or normalize custom input.");
            inspectorTagPicker.SetSearchForSmoke(
                string.Empty);
            Dispatcher.UIThread.RunJobs();

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

            if (visualOutputDirectory is not null)
            {
                shell.ContextDetail.SelectTabForSmoke(1);
                Dispatcher.UIThread.RunJobs();
                CaptureVisualEvidence(
                    window,
                    "inspector-creative-1100x720");

                shell.ContextDetail.SelectTabForSmoke(2);
                Dispatcher.UIThread.RunJobs();
                CaptureVisualEvidence(
                    window,
                    "inspector-publication-1100x720");

                shell.ContextDetail.SelectTabForSmoke(3);
                Dispatcher.UIThread.RunJobs();
                for (var attempt = 0;
                     attempt < 10;
                     attempt++)
                {
                    await Task.Delay(10);
                    Dispatcher.UIThread.RunJobs();
                }
                CaptureVisualEvidence(
                    window,
                    "inspector-information-1100x720");
            }

            shell.ContextDetail.SelectTabForSmoke(3);
            Dispatcher.UIThread.RunJobs();
            Require(
                shell.ContextDetail.SelectedTabIndex == 3
                && shell.ContextDetail.SelectedTabHeader == "情報"
                && string.Equals(
                    shell.ContextDetail.PathText,
                    expectedContextPath,
                    StringComparison.OrdinalIgnoreCase)
                && shell.ContextDetail
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Any(
                        button =>
                            string.Equals(
                                AutomationProperties.GetName(
                                    button),
                                "画像のフルパスをコピー",
                                StringComparison.Ordinal)
                            && button.IsEnabled),
                "Inspector information tab did not expose the keyboard-reachable source-path copy action.");

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

            var publicationOrderEditor =
                new CreativePublicationOrderEditor(
                    new[]
                    {
                        new CreativePublicationAssetOption(
                            firstContextAsset.Id,
                            firstContextAsset.DisplayName),
                        new CreativePublicationAssetOption(
                            secondCreativeAsset.Id,
                            secondCreativeAsset.DisplayName)
                    });
            Require(
                publicationOrderEditor
                    .OrderedAssetIdsForSmoke
                    .SequenceEqual(
                        new[]
                        {
                            firstContextAsset.Id,
                            secondCreativeAsset.Id
                        })
                && !publicationOrderEditor
                    .MoveUpEnabledForSmoke
                && publicationOrderEditor
                    .MoveDownEnabledForSmoke,
                "Publication order editor did not preserve the default selected order and endpoint controls.");

            publicationOrderEditor.SelectForSmoke(1);
            Require(
                publicationOrderEditor
                    .MoveSelectedUpForSmoke()
                && publicationOrderEditor
                    .OrderedAssetIdsForSmoke
                    .SequenceEqual(
                        new[]
                        {
                            secondCreativeAsset.Id,
                            firstContextAsset.Id
                        })
                && !publicationOrderEditor
                    .MoveUpEnabledForSmoke,
                "Publication order editor did not move the selected image upward.");

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
                        "{\"ageRestriction\":\"all\",\"aiGenerated\":true}",
                        publicationOrderEditor
                            .OrderedAssetIdsForSmoke));
            Require(
                smokePublication is not null
                && smokePublication.Assets.Count == 2
                && smokePublication.Assets[0].AssetId
                    == secondCreativeAsset.Id
                && smokePublication.Assets[0].SortOrder == 0
                && smokePublication.Assets[1].AssetId
                    == firstContextAsset.Id
                && smokePublication.Assets[1].SortOrder == 1,
                "App selection organizer did not persist the user-edited Publication image order.");

            var mismatchedPublication =
                await shell.CreatePublicationFromSelectionAsync(
                    new CreativePublicationDialogResult(
                        smokeWork.Id,
                        "Pixiv",
                        "@app-smoke",
                        "Invalid Publication",
                        string.Empty,
                        string.Empty,
                        new DateTimeOffset(
                            2026,
                            10,
                            2,
                            6,
                            31,
                            0,
                            TimeSpan.Zero),
                        string.Empty,
                        string.Empty,
                        "{}",
                        new[]
                        {
                            firstContextAsset.Id
                        }));
            Require(
                mismatchedPublication is null,
                "Publication save did not reject an ordered-ID list that no longer matched the current selection.");

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
                    StringComparison.Ordinal)
                && shell.ContextDetail
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Any(
                        button =>
                            (AutomationProperties.GetName(
                                 button)
                             ?? string.Empty)
                            .StartsWith(
                                "Lineageを削除:",
                                StringComparison.Ordinal)),
                "Contextual detail did not render human-readable creative archive context with lineage maintenance.");

            Require(
                shell.ContextDetail.SelectedTabHeader == "制作",
                "Inspector creative context was not reachable through the dedicated 制作 tab.");

            shell.ContextDetail.SelectTabForSmoke(2);
            Dispatcher.UIThread.RunJobs();
            var publicationCardText =
                string.Join(
                    "\n",
                    shell.ContextDetail
                        .PublicationCardTextForSmoke);
            Require(
                shell.ContextDetail.SelectedTabHeader == "公開"
                && shell.ContextDetail.PublicationCardCountForSmoke >= 1
                && !string.Equals(
                    shell.ContextDetail.PublicationCountTextForSmoke,
                    "0件",
                    StringComparison.Ordinal)
                && publicationCardText.Contains(
                    "App Smoke Publication",
                    StringComparison.Ordinal)
                && publicationCardText.Contains(
                    "snapshot body",
                    StringComparison.Ordinal)
                && publicationCardText.Contains(
                    "app-smoke #publication",
                    StringComparison.Ordinal)
                && publicationCardText.Contains(
                    "external-smoke",
                    StringComparison.Ordinal)
                && publicationCardText.Contains(
                    "https://example.invalid/app-smoke",
                    StringComparison.Ordinal)
                && publicationCardText.Contains(
                    "aiGenerated: true",
                    StringComparison.OrdinalIgnoreCase)
                && publicationCardText.Contains(
                    "ageRestriction: all",
                    StringComparison.OrdinalIgnoreCase)
                && shell.ContextDetail
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Any(button =>
                        string.Equals(
                            button.Content as string,
                            "公開記録を作成",
                            StringComparison.Ordinal)),
                "Inspector Publication tab did not render rich publication cards while keeping its direct create action.");

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

            var appPublicationDestinations =
                await shellRuntime.LibraryService
                    .ListPublicationDestinationsAsync(
                        shellRuntime.Library.Id);
            var appPixivDestination =
                appPublicationDestinations.Single(
                    destination =>
                        string.Equals(
                            destination.Name,
                            "Pixiv",
                            StringComparison.Ordinal));
            var appPublicationAccount =
                await shellRuntime.LibraryService
                    .CreatePublicationAccountAsync(
                        shellRuntime.Library.Id,
                        new PublicationAccountCreate(
                            appPixivDestination.Id,
                            "App Smoke Account",
                            "@app-smoke-profile"));
            var managedPublicationView =
                ProductNavigationViews.CreatePublicationEntry(
                    publicationHistory,
                    publicationHistory.Count,
                    hasMore: false,
                    static () =>
                        Task.FromResult(
                            new PublicationPage(
                                Array.Empty<PublicationInfo>(),
                                null,
                                0)),
                    reportError: null,
                    destinations:
                        appPublicationDestinations,
                    accounts:
                        new[]
                        {
                            appPublicationAccount
                        },
                    createDestination:
                        static (_, _) =>
                            Task.FromResult<PublicationDestinationInfo?>(
                                null),
                    updateDestination:
                        static (destination, _, _) =>
                            Task.FromResult<PublicationDestinationInfo?>(
                                destination),
                    deleteDestination:
                        static _ =>
                            Task.FromResult(false),
                    createAccount:
                        static (_, _, _) =>
                            Task.FromResult<PublicationAccountInfo?>(
                                null),
                    updateAccount:
                        static (account, _, _, _) =>
                            Task.FromResult<PublicationAccountInfo?>(
                                account),
                    deleteAccount:
                        static _ =>
                            Task.FromResult(false),
                    deletePublication:
                        static _ =>
                            Task.FromResult(false));
            var managedPublicationWindow =
                new Window
                {
                    Width = 360,
                    Height = 620,
                    Content =
                        managedPublicationView
                };
            managedPublicationWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Require(
                managedPublicationView
                    .GetVisualDescendants()
                    .OfType<DropDownButton>()
                    .Any(
                        button =>
                            string.Equals(
                                button.Content as string,
                                "投稿先設定",
                                StringComparison.Ordinal))
                && managedPublicationView
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Any(
                        button =>
                            string.Equals(
                                button.Content as string,
                                "履歴を削除…",
                                StringComparison.Ordinal)),
                "Publication navigation did not expose reusable profile management and history maintenance.");
            managedPublicationWindow.Close();
            Dispatcher.UIThread.RunJobs();

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
                        "{}",
                        new[]
                        {
                            firstContextAsset.Id
                        }));
            Require(
                singlePublication is not null
                && singlePublication.Assets.Count == 1
                && singlePublication.Assets[0].AssetId
                    == firstContextAsset.Id,
                "Single-image Publication creation is not semantically reachable.");

            shell.GridViewer.SelectAsset(1);
            Dispatcher.UIThread.RunJobs();
            var expandedSingleWork =
                await shell.AddSelectionToExistingWorkAsync(
                    singleWork.Id);
            var expandedSingleGroup =
                await shell
                    .AddSelectionToExistingGenerationGroupAsync(
                        singleGroup!.Id);
            Require(
                expandedSingleWork is not null
                && expandedSingleWork.Assets
                    .Select(
                        static asset =>
                            asset.Id)
                    .SequenceEqual(
                        new[]
                        {
                            firstContextAsset.Id,
                            secondCreativeAsset.Id
                        })
                && expandedSingleGroup is not null
                && expandedSingleGroup.Assets
                    .Select(
                        static asset =>
                            asset.Id)
                    .SequenceEqual(
                        new[]
                        {
                            firstContextAsset.Id,
                            secondCreativeAsset.Id
                        }),
                "Existing Work/Generation Group maintenance did not append the selected asset while preserving membership order.");

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
                    == ViewerDetailLoadState.PreviewReady
                && DetailImageHasSource(
                    shell.DetailViewer),
                "Focused viewer reached PreviewReady while its actual Image.Source was null.");

            shell.CloseFocusedView();
            Require(
                !shell.IsFocusedViewVisible,
                "Focused viewer did not return to the browse workspace.");

            await shell.DetailViewer.SelectAsync(0);
            Dispatcher.UIThread.RunJobs();

            Require(
                shell.DetailViewer.LoadState
                    == ViewerDetailLoadState.PreviewReady
                && DetailImageHasSource(
                    shell.DetailViewer),
                $"Real App shell Detail preview did not populate Image.Source: state={shell.DetailViewer.LoadState}.");

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
            WindowsTextScale.NormalizeRegistryValue(150)
            - 1.5) < 0.001
        && Math.Abs(
            WindowsTextScale.NormalizeRegistryValue(200)
            - 2.0) < 0.001
        && Math.Abs(
            WindowsTextScale.NormalizeRegistryValue(225)
            - 2.25) < 0.001,
        "Windows registry text-scale percentages were not normalized across the 100/125/150/200/225% product matrix.");

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

        // Product evidence requires deterministic 100% and 225%
        // text-scale runs regardless of the runner's inherited setting.
        // Use the same override WindowsTextScale.Resolve() consumes so any
        // theme re-application inside the headless UI session preserves the
        // requested scale instead of silently resetting it to the host value.
        var iterationTextScale =
            iteration == 2
                ? 2.25
                : 1.0;
        Environment.SetEnvironmentVariable(
            "LUMINE_TEXT_SCALE",
            iterationTextScale.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        LumineVisualMetrics.ConfigureTextScaleFactor(
            iterationTextScale);

        await using (var appHost =
                     await AppHost.StartAsync(
                         repeatedPaths))
        {
            await headless.Dispatch(
                async () =>
                {
                    if (iteration == 0)
                    {
                        var restoreTextScale =
                            LumineVisualMetrics.TextScaleFactor;
                        foreach (var scale in
                                 new[]
                                 {
                                     1.0,
                                     1.25,
                                     1.5,
                                     2.0,
                                     2.25
                                 })
                        {
                            LumineVisualMetrics.ConfigureTextScaleFactor(
                                scale);
                            App.RefreshScaledProductResources(
                                Application.Current
                                ?? throw new InvalidOperationException(
                                    "Design-system smoke has no current Avalonia application."));

                            var primary =
                                LumineDesign.ConfigurePrimaryButton(
                                    new Button
                                    {
                                        Content = "主操作"
                                    });
                            var secondary =
                                LumineDesign.ConfigureSecondaryButton(
                                    new Button
                                    {
                                        Content = "副操作"
                                    });
                            var danger =
                                LumineDesign.ConfigureDangerButton(
                                    new Button
                                    {
                                        Content = "削除"
                                    });
                            var icon =
                                LumineDesign.ConfigureIconButton(
                                    new Button
                                    {
                                        Content =
                                            LumineDesign.CreateStrokeIcon(
                                                LumineDesign.SettingsIconPath,
                                                18)
                                    },
                                    "設定");
                            var input =
                                LumineDesign.ConfigureTextBox(
                                    new TextBox
                                    {
                                        Text = "検索キーワード"
                                    });
                            var combo =
                                LumineDesign.ConfigureComboBox(
                                    new ComboBox
                                    {
                                        ItemsSource =
                                            new[]
                                            {
                                                "更新日時: 新しい順",
                                                "ファイル名: A → Z"
                                            },
                                        SelectedIndex = 0
                                    });
                            var check =
                                LumineDesign.ConfigureCheckBox(
                                    new CheckBox
                                    {
                                        Content =
                                            "表示用サムネイルを再利用する",
                                        IsChecked = true
                                    });

                            Require(
                                primary.Classes.Contains(
                                    "lumine-primary")
                                && secondary.Classes.Contains(
                                    "lumine-secondary")
                                && danger.Classes.Contains(
                                    "lumine-danger")
                                && icon.Classes.Contains(
                                    "lumine-icon")
                                && input.Classes.Contains(
                                    "lumine-input")
                                && combo.Classes.Contains(
                                    "lumine-combo")
                                && check.Classes.Contains(
                                    "lumine-check")
                                && secondary.Resources.Count == 0
                                && danger.Resources.Count == 0,
                                $"Design-system semantic classes regressed or common appearance leaked back into per-control resources at {scale:P0}.");

                            var actions =
                                new WrapPanel();
                            foreach (var control in
                                     new Control[]
                                     {
                                         primary,
                                         secondary,
                                         danger,
                                         icon
                                     })
                            {
                                control.Margin =
                                    new Thickness(
                                        0,
                                        0,
                                        LumineDesign.Space8,
                                        LumineDesign.Space8);
                                actions.Children.Add(control);
                            }

                            var specimenContent =
                                new StackPanel
                                {
                                    Spacing =
                                        LumineDesign.Space12,
                                    MaxWidth = 760
                                };
                            specimenContent.Children.Add(
                                new TextBlock
                                {
                                    Text =
                                        $"Lumine UI {scale:P0}",
                                    FontSize =
                                        LumineDesign.EmphasisFontSize,
                                    FontWeight =
                                        FontWeight.Bold,
                                    Foreground =
                                        LumineDesign.Foreground,
                                    TextWrapping =
                                        TextWrapping.Wrap
                                });
                            specimenContent.Children.Add(
                                new TextBlock
                                {
                                    Text =
                                        "主要操作・補助操作・破壊的操作・フォーム・長い日本語説明が同じ階層と余白規則で読めることを確認します。",
                                    FontSize =
                                        LumineDesign.BodyFontSize,
                                    Foreground =
                                        LumineDesign.Foreground,
                                    TextWrapping =
                                        TextWrapping.Wrap,
                                    LineHeight =
                                        LumineDesign.BodyLineHeight
                                });
                            specimenContent.Children.Add(actions);
                            specimenContent.Children.Add(input);
                            specimenContent.Children.Add(combo);
                            specimenContent.Children.Add(check);
                            specimenContent.Children.Add(
                                new TextBlock
                                {
                                    Text =
                                        "補足情報は本文より弱く、ただし読み取れるコントラストを維持します。",
                                    FontSize =
                                        LumineDesign.CaptionFontSize,
                                    Foreground =
                                        LumineDesign.MutedForeground,
                                    TextWrapping =
                                        TextWrapping.Wrap
                                });

                            var specimen =
                                new Window
                                {
                                    Width = 900,
                                    Height = 600,
                                    Background =
                                        LumineDesign.Background,
                                    Content =
                                        new ScrollViewer
                                        {
                                            Padding =
                                                new Thickness(
                                                    LumineDesign.PageGutter),
                                            Content =
                                                specimenContent
                                        }
                                };
                            specimen.Show();
                            Dispatcher.UIThread.RunJobs();

                            Require(
                                secondary.BorderThickness
                                    == new Thickness(0)
                                && secondary.Background
                                    is ISolidColorBrush secondaryBrush
                                && secondaryBrush.Color
                                    == LumineDesign.ControlSurfaceColor
                                && input.BorderThickness
                                    == new Thickness(1)
                                && input.BorderBrush
                                    is ISolidColorBrush inputBorder
                                && inputBorder.Color.A == 0
                                && combo.BorderThickness
                                    == new Thickness(1)
                                && combo.BorderBrush
                                    is ISolidColorBrush comboBorder
                                && comboBorder.Color.A == 0
                                && Math.Abs(
                                    primary.MinHeight
                                    - LumineDesign.CompactControlHeight) < 0.001
                                && Math.Abs(
                                    primary.FontSize
                                    - LumineDesign.CaptionFontSize) < 0.001
                                && Math.Abs(
                                    input.FontSize
                                    - LumineDesign.BodyFontSize) < 0.001
                                && Math.Abs(
                                    combo.FontSize
                                    - LumineDesign.CaptionFontSize) < 0.001
                                && Math.Abs(
                                    input.Padding.Left
                                    - LumineDesign.Space12) < 0.001,
                                $"Shared Lumine XAML styles did not resolve to the expected quiet, scale-aware resting geometry at {scale:P0}.");

                            var clipped =
                                specimen
                                    .GetVisualDescendants()
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
                                clipped is null,
                                $"Design-system specimen clipped visible text at {scale:P0}: '{clipped?.Text}'.");

                            CaptureVisualEvidence(
                                specimen,
                                $"design-system-scale-{scale * 100:N0}");
                            specimen.Close();
                            Dispatcher.UIThread.RunJobs();
                        }

                        LumineVisualMetrics.ConfigureTextScaleFactor(
                            restoreTextScale);
                        App.RefreshScaledProductResources(
                            Application.Current
                            ?? throw new InvalidOperationException(
                                "Design-system smoke has no current Avalonia application."));
                    }

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

                    var navigationPinnedBeforeRailSmoke =
                        window.IsNavigationPinnedForSmoke;
                    window.SetNavigationPinnedForSmoke(
                        false);
                    Dispatcher.UIThread.RunJobs();

                    var libraryDestination =
                        window.GetVisualDescendants()
                            .OfType<Button>()
                            .First(
                                button =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            button),
                                        "ライブラリ",
                                        StringComparison.Ordinal)
                                    && button.Classes.Contains(
                                        "lumine-nav-item")
                                    && button.Classes.Contains(
                                        "rail"));
                    Require(
                        libraryDestination.Classes.Contains(
                            "lumine-nav-item")
                        && libraryDestination.Classes.Contains(
                            "rail")
                        && libraryDestination.Classes.Contains(
                            "selected")
                        && libraryDestination.Resources.Count == 0
                        && (libraryDestination.IsFocused
                            ? libraryDestination.BorderThickness
                                == new Thickness(1)
                                && libraryDestination.BorderBrush
                                    is ISolidColorBrush focusBorder
                                && focusBorder.Color
                                    == LumineDesign.FocusColor
                            : libraryDestination.BorderThickness
                                == new Thickness(0))
                        && libraryDestination.MinHeight <= 52.5
                        && !libraryDestination
                            .GetVisualDescendants()
                            .OfType<Border>()
                            .Any(
                                indicator =>
                                    Math.Abs(
                                        indicator.Width - 3) < 0.01
                                    && ReferenceEquals(
                                        indicator.Background,
                                        LumineDesign.Accent)),
                        $"Selected global navigation regressed from the shared quiet sidebar selection treatment, focus ring contract, or restored the old accent stripe. focused={libraryDestination.IsFocused}, border={libraryDestination.BorderThickness}, resources={libraryDestination.Resources.Count}");

                    libraryDestination.Focus();
                    libraryDestination.RaiseEvent(
                        new KeyEventArgs
                        {
                            RoutedEvent =
                                InputElement.KeyDownEvent,
                            Key = Key.Down
                        });
                    Dispatcher.UIThread.RunJobs();
                    var folderDestination =
                        window.FocusManager.GetFocusedElement()
                            as Button;
                    Require(
                        folderDestination is not null
                        && string.Equals(
                            AutomationProperties.GetName(
                                folderDestination),
                            "フォルダー",
                            StringComparison.Ordinal),
                        "Global navigation Down Arrow did not follow visual destination order.");

                    folderDestination!.RaiseEvent(
                        new KeyEventArgs
                        {
                            RoutedEvent =
                                InputElement.KeyDownEvent,
                            Key = Key.End
                        });
                    Dispatcher.UIThread.RunJobs();
                    var settingsDestination =
                        window.FocusManager.GetFocusedElement()
                            as Button;
                    Require(
                        settingsDestination is not null
                        && string.Equals(
                            AutomationProperties.GetName(
                                settingsDestination),
                            "設定",
                            StringComparison.Ordinal),
                        "Global navigation End key did not reach the final Settings destination.");

                    settingsDestination!.RaiseEvent(
                        new KeyEventArgs
                        {
                            RoutedEvent =
                                InputElement.KeyDownEvent,
                            Key = Key.Home
                        });
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        ReferenceEquals(
                            window.FocusManager.GetFocusedElement(),
                            libraryDestination),
                        "Global navigation Home key did not return to the first destination.");

                    window.SetNavigationPinnedForSmoke(
                        navigationPinnedBeforeRailSmoke);
                    Dispatcher.UIThread.RunJobs();

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
                    var dangerStateButton =
                        LumineDesign.ConfigureDangerButton(
                            new Button());
                    var textStateControl =
                        LumineDesign.ConfigureTextBox(
                            new TextBox());
                    var comboStateControl =
                        LumineDesign.ConfigureComboBox(
                            new ComboBox());

                    Require(
                        Application.Current is { } currentApplication
                        && currentApplication.Styles
                            .OfType<LumineProductStyles>()
                            .Any()
                        && neutralStateButton.Classes.Contains(
                            "lumine-secondary")
                        && primaryStateButton.Classes.Contains(
                            "lumine-primary")
                        && dangerStateButton.Classes.Contains(
                            "lumine-danger")
                        && textStateControl.Classes.Contains(
                            "lumine-input")
                        && comboStateControl.Classes.Contains(
                            "lumine-combo")
                        && neutralStateButton.Resources.Count == 0
                        && primaryStateButton.Resources.Count == 0
                        && dangerStateButton.Resources.Count == 0,
                        "Representative controls are not driven by the shared Lumine product style layer.");

                    Require(
                        LumineDesign.Space2 < LumineDesign.Space4
                        && LumineDesign.Space4 < LumineDesign.Space6
                        && LumineDesign.Space6 < LumineDesign.Space8
                        && LumineDesign.Space8 < LumineDesign.Space12
                        && LumineDesign.Space12 < LumineDesign.Space16
                        && LumineDesign.Space16 < LumineDesign.Space24
                        && LumineDesign.PageGutter
                            == LumineDesign.Space24
                        && Application.Current?.Resources[
                            "Lumine.ControlHeight"]
                            is double controlHeight
                        && Math.Abs(
                            controlHeight
                            - LumineDesign.CompactControlHeight) < 0.001
                        && Application.Current?.Resources[
                            "Lumine.ControlRadius"]
                            is CornerRadius controlRadius
                        && controlRadius
                            == new CornerRadius(
                                LumineDesign.ControlRadius)
                        && Application.Current?.Resources[
                            "Lumine.AccentMuted"]
                            is IBrush
                        && Application.Current?.Resources[
                            "Lumine.InteractionSelected"]
                            is IBrush
                        && Application.Current?.Resources[
                            "Lumine.InteractionSelectedHover"]
                            is IBrush
                        && Application.Current?.Resources[
                            "Lumine.TagSwatchSize"]
                            is double tagSwatchSize
                        && tagSwatchSize >= 30
                        && tagSwatchSize <= 38
                        && Application.Current?.Resources[
                            "Lumine.TagSwatchDotSize"]
                            is double tagSwatchDotSize
                        && tagSwatchDotSize >= 18
                        && tagSwatchDotSize <= 24
                        && Application.Current?.Resources[
                            "Lumine.DialogTitleFontSize"]
                            is double dialogTitleFontSize
                        && dialogTitleFontSize
                            == LumineDesign.DialogTitleFontSize
                        && Application.Current?.Resources[
                            "Lumine.DialogSymbolSize"]
                            is double dialogSymbolSize
                        && dialogSymbolSize
                            is >= 36 and <= 48
                        && Application.Current?.Resources[
                            "Lumine.BodyLineHeight"]
                            is double bodyLineHeight
                        && bodyLineHeight
                            == LumineDesign.BodyLineHeight
                        && Application.Current?.Resources[
                            "Lumine.UiFont"]
                            is FontFamily,
                        "Canonical Lumine spacing/control metrics drifted or stopped flowing through the shared theme resources.");

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

                    if (iteration == 2
                        && visualOutputDirectory is not null)
                    {
                        window.Width = 900;
                        window.Height = 600;
                        window.PresentRecoverableErrorForSmoke(
                            "選択した画像フォルダーを読み込めませんでした。フォルダーが移動・削除されていないか、アクセス権限が変更されていないかを確認してください。必要であれば別の画像フォルダーを選択できます。");
                        Dispatcher.UIThread.RunJobs();

                        var scaledErrorDetail =
                            window.GetVisualDescendants()
                                .OfType<Expander>()
                                .FirstOrDefault(
                                    expander =>
                                        string.Equals(
                                            expander.Header as string,
                                            "エラー詳細",
                                            StringComparison.Ordinal));
                        var scaledRetry =
                            window.GetVisualDescendants()
                                .OfType<Button>()
                                .FirstOrDefault(
                                    button =>
                                        string.Equals(
                                            button.Content as string,
                                            "もう一度開く",
                                            StringComparison.Ordinal));
                        var scaledChoose =
                            window.GetVisualDescendants()
                                .OfType<Button>()
                                .FirstOrDefault(
                                    button =>
                                        string.Equals(
                                            button.Content as string,
                                            "別の画像フォルダーを選ぶ",
                                            StringComparison.Ordinal));
                        static bool IsVisibleInsideWindow(
                            Control? control,
                            MainWindow owner)
                        {
                            if (control is null
                                || !control.IsEffectivelyVisible
                                || control.Bounds.Width <= 0
                                || control.Bounds.Height <= 0)
                            {
                                return false;
                            }

                            var origin =
                                control.TranslatePoint(
                                    new Point(0, 0),
                                    owner);
                            var minimumVisibleX =
                                owner.IsNavigationPaneOverlayForSmoke
                                    ? owner.NavigationPaneBounds.Right
                                    : 0;
                            return origin is { } point
                                && point.X >= minimumVisibleX - 0.5
                                && point.Y >= -0.5
                                && point.X + control.Bounds.Width
                                    <= owner.ClientSize.Width + 0.5
                                && point.Y + control.Bounds.Height
                                    <= owner.ClientSize.Height + 0.5;
                        }

                        CaptureVisualEvidence(
                            window,
                            "error-900x600-text225");

                        var retryOrigin =
                            scaledRetry?.TranslatePoint(
                                new Point(0, 0),
                                window);
                        var chooseOrigin =
                            scaledChoose?.TranslatePoint(
                                new Point(0, 0),
                                window);
                        Require(
                            scaledErrorDetail is
                                { IsExpanded: false }
                            && IsVisibleInsideWindow(
                                scaledRetry,
                                window)
                            && IsVisibleInsideWindow(
                                scaledChoose,
                                window)
                            && string.IsNullOrWhiteSpace(
                                window.StatusTextForSmoke),
                            $"225% / 900x600 recoverable Error visibility failed. "
                            + $"navOverlay={window.IsNavigationPaneOverlayForSmoke}, nav={window.NavigationPaneBounds}, "
                            + $"retryVisible={scaledRetry?.IsEffectivelyVisible}, retryOrigin={retryOrigin}, retryBounds={scaledRetry?.Bounds}, "
                            + $"chooseVisible={scaledChoose?.IsEffectivelyVisible}, chooseOrigin={chooseOrigin}, chooseBounds={scaledChoose?.Bounds}, "
                            + $"detailCollapsed={scaledErrorDetail is { IsExpanded: false }}, status='{window.StatusTextForSmoke}'.");

                        window.PresentWelcomeStateForSmoke();
                        window.Width = 1440;
                        window.Height = 900;
                        Dispatcher.UIThread.RunJobs();
                    }

                    var dialogSize =
                        ProductDialogs.ResolveDialogSizeForSmoke(
                            340);
                    Require(
                        dialogSize.Width
                            is >= 500 and <= 640
                        && dialogSize.Height
                            is >= 340 and <= 520,
                        $"Product dialog adaptive sizing escaped the 900x600-safe contract: {dialogSize.Width:N0}x{dialogSize.Height:N0}.");

                    if ((iteration == 0 || iteration == 2)
                        && visualOutputDirectory is not null)
                    {
                        var confirmPreview =
                            ProductDialogs.CreatePreviewForSmoke(
                                ProductDialogTone.Danger,
                                notification: false);
                        confirmPreview.Show();
                        Dispatcher.UIThread.RunJobs();

                        var confirmContent =
                            confirmPreview.Content as StackPanel
                            ?? throw new InvalidOperationException(
                                "Confirmation preview did not expose the expected content panel.");
                        var confirmSymbol =
                            confirmContent
                                .GetVisualDescendants()
                                .OfType<Border>()
                                .First(
                                    border =>
                                        border.Classes.Contains(
                                            "lumine-dialog-symbol"));
                        var confirmDetail =
                            confirmContent
                                .GetVisualDescendants()
                                .OfType<Border>()
                                .First(
                                    border =>
                                        border.Classes.Contains(
                                            "lumine-dialog-detail"));
                        var confirmActions =
                            confirmContent
                                .GetVisualDescendants()
                                .OfType<StackPanel>()
                                .First(
                                    panel =>
                                        panel.Classes.Contains(
                                            "lumine-dialog-actions"));
                        Require(
                            confirmPreview.Classes.Contains(
                                "lumine-dialog-window")
                            && confirmContent.Classes.Contains(
                                "lumine-dialog-content")
                            && confirmSymbol.Classes.Contains(
                                "danger")
                            && confirmSymbol.Bounds.Width
                                is >= 35.5 and <= 48.5
                            && confirmDetail.BorderThickness
                                == new Thickness(0)
                            && confirmActions.Children
                                .OfType<Button>()
                                .Count() == 2
                            && confirmActions.Children
                                .OfType<Button>()
                                .Any(
                                    button =>
                                        button.Classes.Contains(
                                            "lumine-danger"))
                            && confirmActions.Children
                                .OfType<Button>()
                                .Any(
                                    button =>
                                        button.Classes.Contains(
                                            "lumine-secondary"))
                            && confirmPreview.Height <= 420.5,
                            "Danger confirmation dialog escaped the shared quiet product roles or became vertically oversized.");
                        CaptureVisualEvidence(
                            confirmPreview,
                            iteration == 2
                                ? "dialog-confirm-danger-text225"
                                : "dialog-confirm-danger");
                        confirmPreview.Close();

                        var notifyPreview =
                            ProductDialogs.CreatePreviewForSmoke(
                                ProductDialogTone.Default,
                                notification: true);
                        notifyPreview.Show();
                        Dispatcher.UIThread.RunJobs();

                        var notifyContent =
                            notifyPreview.Content as StackPanel
                            ?? throw new InvalidOperationException(
                                "Notification preview did not expose the expected content panel.");
                        Require(
                            notifyPreview.Classes.Contains(
                                "lumine-dialog-window")
                            && notifyContent.Classes.Contains(
                                "lumine-dialog-content")
                            && !notifyContent
                                .GetVisualDescendants()
                                .OfType<Border>()
                                .Any(
                                    border =>
                                        border.Classes.Contains(
                                            "lumine-dialog-detail"))
                            && notifyContent
                                .GetVisualDescendants()
                                .OfType<Button>()
                                .Single()
                                .Classes.Contains(
                                    "lumine-secondary")
                            && notifyPreview.Height <= 320.5,
                            "Notification dialog escaped the quiet acknowledgement hierarchy or became vertically oversized.");
                        CaptureVisualEvidence(
                            notifyPreview,
                            iteration == 2
                                ? "dialog-notify-text225"
                                : "dialog-notify");
                        notifyPreview.Close();
                    }

                    if (iteration == 0)
                    {
                        CaptureVisualEvidence(
                            window,
                            "welcome-1440x900");
                        window.PresentLoadingStateForSmoke(
                            "ライブラリを走査中… 検出 1,248 · 登録 1,104");
                        Dispatcher.UIThread.RunJobs();
                        Require(
                            string.Equals(
                                window.ProductShellState,
                                "Loading",
                                StringComparison.Ordinal)
                            && string.IsNullOrWhiteSpace(
                                window.StatusTextForSmoke),
                            "Loading state duplicated its central progress through the transient status banner.");
                        CaptureVisualEvidence(
                            window,
                            "loading-1440x900");
                        window.PresentWelcomeStateForSmoke();
                        Dispatcher.UIThread.RunJobs();
                    }

                    if (iteration == 0)
                    {
                        var retryableMissingRoot =
                            Path.Combine(
                                repeatedLibraryRoot,
                                "retryable-missing");

                        if (Directory.Exists(
                                retryableMissingRoot))
                        {
                            Directory.Delete(
                                retryableMissingRoot,
                                recursive: true);
                        }

                        await window.OpenLibraryAsync(
                            retryableMissingRoot);
                        Dispatcher.UIThread.RunJobs();

                        var errorDetail =
                            window.GetVisualDescendants()
                                .OfType<Expander>()
                                .FirstOrDefault(
                                    expander =>
                                        string.Equals(
                                            expander.Header
                                                as string,
                                            "エラー詳細",
                                            StringComparison.Ordinal));

                        Require(
                            string.Equals(
                                window.ProductShellState,
                                "Error",
                                StringComparison.Ordinal)
                            && window.HasRetryableOpenFailureForSmoke
                            && window.OpenFolderCommandEnabledForSmoke
                            && window.GetVisualDescendants()
                                .OfType<Button>()
                                .Any(button =>
                                    string.Equals(
                                        button.Content
                                            as string,
                                        "もう一度開く",
                                        StringComparison.Ordinal))
                            && window.GetVisualDescendants()
                                .OfType<Button>()
                                .Any(button =>
                                    string.Equals(
                                        button.Content
                                            as string,
                                        "別の画像フォルダーを選ぶ",
                                        StringComparison.Ordinal))
                            && errorDetail is
                                { IsExpanded: false }
                            && string.IsNullOrWhiteSpace(
                                window.StatusTextForSmoke),
                            "Library-open failure did not expose local retry/reselect recovery actions with secondary technical detail or duplicated its central error in the status banner.");

                        CaptureVisualEvidence(
                            window,
                            "error-1440x900");

                        Directory.CreateDirectory(
                            retryableMissingRoot);
                        await window
                            .RetryFailedLibraryForSmokeAsync();
                        Dispatcher.UIThread.RunJobs();
                        Require(
                            string.Equals(
                                window.ProductShellState,
                                "EmptyLibrary",
                                StringComparison.Ordinal)
                            && !window.HasRetryableOpenFailureForSmoke
                            && window.OpenFolderCommandEnabledForSmoke,
                            "Retrying the same recovered library path did not transition out of Error cleanly.");

                        var alternateMissingRoot =
                            Path.Combine(
                                repeatedLibraryRoot,
                                "reselect-missing");
                        await window.OpenLibraryAsync(
                            alternateMissingRoot);
                        Dispatcher.UIThread.RunJobs();
                        Require(
                            string.Equals(
                                window.ProductShellState,
                                "Error",
                                StringComparison.Ordinal)
                            && window.HasRetryableOpenFailureForSmoke,
                            "Second library-open failure did not retain retry context for reselect coverage.");
                    }

                    await window.OpenLibraryAsync(
                        repeatedLibraryRoot);

                    Require(
                        string.Equals(
                            window.ProductShellState,
                            "Workspace",
                            StringComparison.Ordinal)
                        && !window.HasRetryableOpenFailureForSmoke
                        && window.OpenFolderCommandEnabledForSmoke,
                        $"MainWindow did not transition from Welcome/Error recovery to Workspace after opening a populated library: {window.ProductShellState}.");

                    Require(
                        window.CurrentRuntime is not null
                        && window.CurrentShell is not null,
                        "MainWindow did not compose the production Core Viewer runtime/shell.");

                    if (iteration == 0)
                    {
                        await window.NavigationRefreshForSmokeAsync();
                        window.Width = 1440;
                        window.Height = 900;
                        Dispatcher.UIThread.RunJobs();
                        Require(
                            window.IsNavigationPinnedForSmoke
                            && window.IsNavigationPaneVisibleForSmoke
                            && window.PinnedNavigationUsesUnifiedSidebarForSmoke,
                            "Wide default workspace did not present the unified desktop sidebar.");
                        CaptureVisualEvidence(
                            window,
                            "browse-1440x900-sidebar");
                    }

                    var shellBeforeSettings =
                        window.CurrentShell;

                    window.NavigateForSmoke(
                        "設定");
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        window.WorkspacePageForSmoke
                            is Border
                        && window.IsWorkspacePageVisibleForSmoke
                        && window.IsNavigationPaneVisibleForSmoke
                        && window.PinnedNavigationUsesUnifiedSidebarForSmoke
                        && !window.StatusSurfaceVisibleForSmoke
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
                                    .EncodedThumbnailMemoryByteLimit)
                        && window.SettingsSnapshot.ScanExtensions.Count > 0
                        && window.SettingsSnapshot.ScanExtensions.All(
                            extension =>
                                LibraryFileTypes.TryNormalizeExtension(
                                    extension,
                                    out _))
                        && window.SettingsSnapshot.HasActiveLibrary,
                        "Product Settings did not keep the unified desktop sidebar while opening as a clean main-workspace page without browse status chrome.");

                    var settingsText =
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<TextBlock>()
                            .ToArray();
                    var advancedSettings =
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<Expander>()
                            .FirstOrDefault(
                                expander =>
                                    string.Equals(
                                        expander.Header as string,
                                        "詳細設定",
                                        StringComparison.Ordinal))
                        ?? throw new InvalidOperationException(
                            "Product Settings did not expose the advanced performance/cache disclosure.");

                    var settingsFieldNames =
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<ComboBox>()
                            .Select(
                                AutomationProperties.GetName)
                            .Where(
                                static name =>
                                    !string.IsNullOrWhiteSpace(name))
                            .ToHashSet(
                                StringComparer.Ordinal);
                    Require(
                        settingsFieldNames.Contains("表示")
                        && settingsFieldNames.Contains("密度")
                        && settingsFieldNames.Contains("並び順"),
                        "Visible Settings fields lost accessible names tied to their visual labels.");

                    var settingsScroll =
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<ScrollViewer>()
                            .FirstOrDefault(
                                scroll =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            scroll),
                                        "設定スクロール",
                                        StringComparison.Ordinal));
                    var settingsContent =
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<StackPanel>()
                            .FirstOrDefault(
                                panel =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            panel),
                                        "設定コンテンツ",
                                        StringComparison.Ordinal));
                    var viewerDefaultsGroup =
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<WrapPanel>()
                            .FirstOrDefault(
                                panel =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            panel),
                                        "表示設定グループ",
                                        StringComparison.Ordinal));
                    Require(
                        settingsScroll is not null
                        && settingsScroll.Offset.Y <= 0.5
                        && settingsContent is not null
                        && settingsContent.Bounds.Width <= 720.5
                        && viewerDefaultsGroup is not null
                        && viewerDefaultsGroup.Children.Count == 3,
                        "Settings did not open at the top with capped content width and one grouped display-preference form.");

                    var settingsSections =
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<StackPanel>()
                            .Where(
                                panel =>
                                    (AutomationProperties.GetName(
                                        panel)
                                    ?? string.Empty)
                                    .StartsWith(
                                        "設定セクション ",
                                        StringComparison.Ordinal))
                            .ToArray();
                    var settingsGroups =
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<Border>()
                            .Where(
                                border =>
                                    (AutomationProperties.GetName(
                                        border)
                                    ?? string.Empty)
                                    .StartsWith(
                                        "設定グループ ",
                                        StringComparison.Ordinal))
                            .ToArray();
                    Require(
                        settingsSections.Length == 4
                        && settingsGroups.Length == 4
                        && settingsSections.All(
                            static section =>
                                section.Children.Count == 2
                                && section.Spacing
                                    >= LumineDesign.Space8)
                        && settingsGroups.All(
                            static group =>
                                group.Classes.Contains(
                                    "lumine-settings-group")
                                && group.BorderThickness
                                    == new Thickness(0)),
                        "Settings regressed from four quiet grouped sections back to card-heavy or ad-hoc surfaces.");


                    var scanExtensionControls =
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<CheckBox>()
                            .Where(
                                check =>
                                    (AutomationProperties.GetName(
                                        check)
                                    ?? string.Empty)
                                    .StartsWith(
                                        "読み込み対象 .",
                                        StringComparison.Ordinal))
                            .ToArray();
                    var customExtensionInput =
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<TextBox>()
                            .FirstOrDefault(
                                input =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            input),
                                        "独自読み込み対象を入力",
                                        StringComparison.Ordinal));
                    var addCustomExtension =
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<Button>()
                            .FirstOrDefault(
                                button =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            button),
                                        "独自読み込み対象を追加",
                                        StringComparison.Ordinal));
                    var customExtensionList =
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<WrapPanel>()
                            .FirstOrDefault(
                                panel =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            panel),
                                        "独自読み込み対象一覧",
                                        StringComparison.Ordinal));
                    Require(
                        scanExtensionControls.Length
                            == LibraryFileTypes.DefaultExtensions.Count
                        && scanExtensionControls.Any(
                            check =>
                                string.Equals(
                                    check.Content as string,
                                    ".bmp",
                                    StringComparison.Ordinal))
                        && customExtensionInput is not null
                        && addCustomExtension is not null
                        && customExtensionList is not null
                        && window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<Button>()
                            .Any(
                                button =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            button),
                                        "現在のライブラリを再スキャン",
                                        StringComparison.Ordinal)
                                    && button.IsEnabled),
                        "Product Settings did not expose built-in/custom scan-extension controls and the active-library rescan action.");

                    var scanActionGroup =
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<StackPanel>()
                            .FirstOrDefault(
                                panel =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            panel),
                                        "読み込み対象アクション",
                                        StringComparison.Ordinal));
                    Require(
                        scanActionGroup is not null
                        && scanActionGroup
                            .GetVisualDescendants()
                            .OfType<Button>()
                            .Any(
                                button =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            button),
                                        "読み込み対象を保存",
                                        StringComparison.Ordinal))
                        && scanActionGroup
                            .GetVisualDescendants()
                            .OfType<Button>()
                            .Any(
                                button =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            button),
                                        "現在のライブラリを再スキャン",
                                        StringComparison.Ordinal)),
                        "Settings scan-target save/rescan feedback/actions were not kept in one form footer.");

                    var cacheBasicGroup =
                        window.WorkspacePageForSmoke
                            .GetVisualDescendants()
                            .OfType<StackPanel>()
                            .FirstOrDefault(
                                panel =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            panel),
                                        "キャッシュ基本設定",
                                        StringComparison.Ordinal));
                    Require(
                        cacheBasicGroup is not null
                        && cacheBasicGroup.Children
                            .OfType<CheckBox>()
                            .Any(),
                        "Settings cache basics were not separated from the advanced disclosure.");


                    customExtensionInput!.Text =
                        "JFIF";
                    addCustomExtension!.RaiseEvent(
                        new RoutedEventArgs(
                            Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        string.IsNullOrEmpty(
                            customExtensionInput.Text),
                        "Custom scan-extension editor did not normalize and stage JFIF.");

                    var customRemoval =
                        customExtensionList!.Children
                            .OfType<Button>()
                            .FirstOrDefault(
                                button =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            button),
                                        "独自読み込み対象 .jfif を削除",
                                        StringComparison.Ordinal));
                    Require(
                        customRemoval is not null,
                        "Custom scan-extension editor did not expose the staged JFIF removal action.");
                    customRemoval!.RaiseEvent(
                        new RoutedEventArgs(
                            Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        !customExtensionList.Children
                            .OfType<Button>()
                            .Any(
                                button =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            button),
                                        "独自読み込み対象 .jfif を削除",
                                        StringComparison.Ordinal)),
                        "Custom scan-extension removal did not update the staged Settings state.");

                    Require(
                        settingsText.Any(block =>
                            string.Equals(
                                block.Text,
                                "設定",
                                StringComparison.Ordinal))
                        && settingsText.Any(block =>
                            string.Equals(
                                block.Text,
                                "表示",
                                StringComparison.Ordinal))
                        && settingsText.Any(block =>
                            string.Equals(
                                block.Text,
                                "読み込み対象",
                                StringComparison.Ordinal))
                        && settingsText.Any(block =>
                            string.Equals(
                                block.Text,
                                "パフォーマンスとキャッシュ",
                                StringComparison.Ordinal))
                        && settingsText.Any(block =>
                            string.Equals(
                                block.Text,
                                "ライブラリとデータ",
                                StringComparison.Ordinal))
                        && !advancedSettings.IsExpanded
                        && !settingsText.Any(block =>
                            block.IsEffectivelyVisible
                            && string.Equals(
                                block.Text,
                                "ディスク保持上限",
                                StringComparison.Ordinal))
                        && !settingsText.Any(block =>
                            block.IsEffectivelyVisible
                            && string.Equals(
                                block.Text,
                                "高速再表示用メモリ上限",
                                StringComparison.Ordinal)),
                        "Product Settings did not prioritize daily settings or hide advanced cache controls by default.");

                    if (iteration == 2)
                    {
                        window.Width = 900;
                        window.Height = 600;
                        Dispatcher.UIThread.RunJobs();
                        settingsScroll =
                            window.WorkspacePageForSmoke
                                .GetVisualDescendants()
                                .OfType<ScrollViewer>()
                                .First(
                                    scroll =>
                                        string.Equals(
                                            AutomationProperties.GetName(
                                                scroll),
                                            "設定スクロール",
                                            StringComparison.Ordinal));
                        settingsScroll.Offset =
                            new Vector(0, 0);
                        Dispatcher.UIThread.RunJobs();
                        CaptureVisualEvidence(
                            window,
                            "settings-900x600-text225");
                    }

                    if (iteration == 0)
                    {
                        window.Width = 1440;
                        window.Height = 900;
                        Dispatcher.UIThread.RunJobs();
                        CaptureVisualEvidence(
                            window,
                            "settings-1440x900");

                        // Resizing can rebuild the responsive Settings surface.
                        // Reacquire the live Expander so the expanded evidence
                        // cannot accidentally mutate a detached control.
                        advancedSettings =
                            window.WorkspacePageForSmoke
                                .GetVisualDescendants()
                                .OfType<Expander>()
                                .First(
                                    expander =>
                                        string.Equals(
                                            expander.Header as string,
                                            "詳細設定",
                                            StringComparison.Ordinal));
                        advancedSettings.IsExpanded = true;
                        advancedSettings.BringIntoView();
                        for (var renderPass = 0;
                             renderPass < 3;
                             renderPass++)
                        {
                            Dispatcher.UIThread.RunJobs();
                            AvaloniaHeadlessPlatform
                                .ForceRenderTimerTick();
                        }
                        Dispatcher.UIThread.RunJobs();

                        var advancedContent =
                            advancedSettings.Content
                                as Control
                            ?? throw new InvalidOperationException(
                                "Advanced Settings disclosure lost its content.");
                        var advancedSettingsText =
                            advancedContent
                                .GetVisualDescendants()
                                .OfType<TextBlock>()
                                .ToArray();
                        Require(
                            advancedSettings.IsExpanded
                            && advancedSettingsText.Any(block =>
                                string.Equals(
                                    block.Text,
                                    "ディスク保持上限",
                                    StringComparison.Ordinal))
                            && advancedSettingsText.Any(block =>
                                string.Equals(
                                    block.Text,
                                    "高速再表示用メモリ上限",
                                    StringComparison.Ordinal)),
                            "Advanced Settings disclosure did not expose its cache budget controls.");

                        CaptureVisualEvidence(
                            window,
                            "settings-advanced-1440x900");
                        if (visualOutputDirectory is not null)
                        {
                            var collapsedSettingsEvidence =
                                File.ReadAllBytes(
                                    Path.Combine(
                                        visualOutputDirectory,
                                        "settings-1440x900.png"));
                            var expandedSettingsEvidence =
                                File.ReadAllBytes(
                                    Path.Combine(
                                        visualOutputDirectory,
                                        "settings-advanced-1440x900.png"));
                            Require(
                                !collapsedSettingsEvidence.SequenceEqual(
                                    expandedSettingsEvidence),
                                "Expanded Settings visual evidence duplicated the collapsed capture.");
                        }

                        advancedSettings.IsExpanded = false;
                        window.Width = 900;
                        window.Height = 600;
                        Dispatcher.UIThread.RunJobs();
                        settingsScroll =
                            window.WorkspacePageForSmoke
                                .GetVisualDescendants()
                                .OfType<ScrollViewer>()
                                .First(
                                    scroll =>
                                        string.Equals(
                                            AutomationProperties.GetName(
                                                scroll),
                                            "設定スクロール",
                                            StringComparison.Ordinal));
                        settingsScroll.Offset =
                            new Vector(0, 0);
                        Dispatcher.UIThread.RunJobs();
                        CaptureVisualEvidence(
                            window,
                            "settings-900x600");

                        window.Width = 1440;
                        window.Height = 900;
                        Dispatcher.UIThread.RunJobs();
                    }

                    window.NavigateForSmoke(
                        "ライブラリ");
                    await window.NavigationRefreshForSmokeAsync();
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        !window.IsWorkspacePageVisibleForSmoke
                        && window.IsNavigationPaneVisibleForSmoke
                        && ReferenceEquals(
                            window.CurrentShell,
                            shellBeforeSettings),
                        "Returning from Settings did not restore browse navigation without rebuilding the viewer shell.");

                    var activeLibraryCard =
                        window.NavigationContentForSmoke?
                            .GetVisualDescendants()
                            .OfType<Border>()
                            .FirstOrDefault(
                                card =>
                                    string.Equals(
                                        AutomationProperties.GetAutomationId(
                                            card),
                                        $"library-card-{window.CurrentRuntime!.Library.Id}",
                                        StringComparison.Ordinal));
                    Require(
                        activeLibraryCard is not null
                        && activeLibraryCard
                            .GetVisualDescendants()
                            .OfType<Button>()
                            .Any(
                                button =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            button),
                                        "現在のライブラリを再スキャン",
                                        StringComparison.Ordinal)),
                        "Active Library navigation row did not keep its rescan utility inside the unified flat surface.");



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
                            .IsContextDetailVisible
                        && window.CurrentShell
                            .IsNoMatchStateVisibleForSmoke
                        && string.IsNullOrWhiteSpace(
                            window.StatusTextForSmoke),
                        "Filtered zero-result query replaced the Viewer shell, kept stale selection/Inspector state, or duplicated No Match in the status banner.");

                    var reviewFilters =
                        window.GetVisualDescendants()
                            .OfType<Button>()
                            .FirstOrDefault(
                                button =>
                                    string.Equals(
                                        button.Content as string,
                                        "フィルターを見直す",
                                        StringComparison.Ordinal))
                        ?? throw new InvalidOperationException(
                            "No Match did not expose a direct filter-review action.");
                    reviewFilters.RaiseEvent(
                        new RoutedEventArgs(
                            Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        window.BrowseControlsForSmoke is not null
                        && window.BrowseControlsForSmoke
                            .FilterFlyoutIsOpenForSmoke,
                        "No Match filter-review action did not open the existing Browse filter surface directly.");
                    window.BrowseControlsForSmoke
                        .CloseFilterFlyoutForSmoke();
                    Dispatcher.UIThread.RunJobs();

                    if (iteration == 0)
                    {
                        CaptureVisualEvidence(
                            window,
                            "no-match-1440x900");
                    }

                    var clearNoMatch =
                        window.GetVisualDescendants()
                            .OfType<Button>()
                            .FirstOrDefault(
                                button =>
                                    string.Equals(
                                        button.Content as string,
                                        "条件をすべて解除",
                                        StringComparison.Ordinal))
                        ?? throw new InvalidOperationException(
                            "No Match did not expose a direct clear action.");
                    clearNoMatch.RaiseEvent(
                        new RoutedEventArgs(
                            Button.ClickEvent));
                    for (var attempt = 0;
                         attempt < 500
                         && (window.CurrentRuntime!.AssetCount == 0
                             || window.CurrentShell is null
                             || window.CurrentShell
                                 .IsNoMatchStateVisibleForSmoke);
                         attempt++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        await Task.Delay(2);
                    }
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
                        && window.CurrentRuntime!.AssetCount > 0
                        && !window.CurrentShell
                            .IsNoMatchStateVisibleForSmoke,
                        "Clearing No Match from its primary recovery action rebuilt the Viewer shell/control or failed to restore data in place.");

                    await window.OpenLibraryAsync(
                        repeatedEmptyLibraryRoot);
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        string.Equals(
                            window.ProductShellState,
                            "EmptyLibrary",
                            StringComparison.Ordinal)
                        && window.CurrentRuntime is not null
                        && window.CurrentShell is null
                        && string.IsNullOrWhiteSpace(
                            window.StatusTextForSmoke),
                        "Truly empty library did not use the EmptyLibrary product state or duplicated its empty message in the status banner.");

                    if (iteration == 0)
                    {
                        CaptureVisualEvidence(
                            window,
                            "empty-library-1440x900");
                    }

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
                            StringComparison.Ordinal)
                        && window.StatusTextForSmoke.Contains(
                            "再スキャン完了",
                            StringComparison.Ordinal),
                        "Manual library rescan recreated the active runtime/shell or failed to report its final state.");

                    for (var attempt = 0;
                         attempt < 180
                         && !string.IsNullOrWhiteSpace(
                             window.StatusTextForSmoke);
                         attempt++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        await Task.Delay(10);
                    }
                    Require(
                        string.IsNullOrWhiteSpace(
                            window.StatusTextForSmoke),
                        "Completed rescan status remained permanently over the Browse canvas.");

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

                    var browseSearch =
                        window.GetVisualDescendants()
                            .OfType<TextBox>()
                            .FirstOrDefault(
                                control =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            control),
                                        "画像を検索",
                                        StringComparison.Ordinal))
                        ?? throw new InvalidOperationException(
                            "Browse search did not expose an accessible name.");
                    Require(
                        string.Equals(
                            AutomationProperties.GetAcceleratorKey(
                                browseSearch),
                            "Ctrl+F",
                            StringComparison.Ordinal),
                        "Browse search did not expose its Ctrl+F accelerator metadata.");
                    window.RaiseEvent(
                        new KeyEventArgs
                        {
                            RoutedEvent =
                                InputElement.KeyDownEvent,
                            Key = Key.F,
                            KeyModifiers =
                                KeyModifiers.Control
                        });
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        ReferenceEquals(
                            window.FocusManager.GetFocusedElement(),
                            browseSearch),
                        "Ctrl+F did not move focus into Browse search.");

                    window.BrowseControlsForSmoke!
                        .OpenDisplayFlyoutForSmoke();
                    Dispatcher.UIThread.RunJobs();

                    var thumbnailDensity =
                        window.GetVisualDescendants()
                            .OfType<Slider>()
                            .FirstOrDefault(
                                control =>
                                    string.Equals(
                                        AutomationProperties.GetName(
                                            control),
                                        "サムネイルサイズ",
                                        StringComparison.Ordinal));
                    Require(
                        thumbnailDensity is not null
                        && window.BrowseControlsForSmoke
                            .DisplayFlyoutIsOpenForSmoke
                        && window.BrowseControlsForSmoke
                            .DisplayFlyoutLayoutIsContainedForSmoke,
                        "Display flyout did not expose an accessible, contained thumbnail-density control.");
                    window.BrowseControlsForSmoke
                        .CloseDisplayFlyoutForSmoke();
                    Dispatcher.UIThread.RunJobs();

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
                                        == (viewport.Width < 1200)
                                    && window.NavigationPinVisibleForSmoke
                                        == (viewport.Width >= 1200)
                                    && window.IsNavigationPaneOverlayForSmoke
                                        == navigationVisible
                                    && window.BrowseControlsForSmoke is not null
                                    && window.BrowseControlsForSmoke
                                        .PrimaryToolbarIsContainedForSmoke
                                    && window.BrowseControlsForSmoke
                                        .FilterButtonIsVisibleForSmoke
                                    && window.BrowseControlsForSmoke
                                        .DisplayButtonIsVisibleForSmoke
                                    && window.BrowseControlsForSmoke
                                        .DisplayControlsGroupedForSmoke
                                    && window.BrowseControlsForSmoke
                                        .SearchUsesSharedThemeForSmoke
                                    && window.StatusSurfaceUsesHudForSmoke
                                    && !window.BrowseControlsForSmoke
                                        .FilterFlyoutIsOpenForSmoke
                                    && !window.BrowseControlsForSmoke
                                        .DisplayFlyoutIsOpenForSmoke,
                                    $"Responsive shell/navigation or primary toolbar containment regressed at {viewport.Width:N0}x{viewport.Height:N0}, {mode}, nav={(navigationVisible ? "open" : "closed")}.");

                                if ((iteration == 0 || iteration == 2)
                                    && mode == BrowseViewMode.Grid
                                    && navigationVisible
                                    && viewport.Width == 900d)
                                {
                                    CaptureVisualEvidence(
                                        window,
                                        iteration == 2
                                            ? "navigation-overlay-900x600-text225"
                                            : "navigation-overlay-900x600");
                                }

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

                                if (iteration == 0
                                    && mode == BrowseViewMode.Grid
                                    && !navigationVisible)
                                {
                                    var browseEvidenceName =
                                        viewport.Width switch
                                        {
                                            900d => "browse-900x600",
                                            1024d => "browse-1024x768",
                                            1440d => "browse-1440x900",
                                            1920d => "browse-1920x1080",
                                            _ => string.Empty
                                        };
                                    if (!string.IsNullOrWhiteSpace(
                                            browseEvidenceName))
                                    {
                                        CaptureVisualEvidence(
                                            window,
                                            browseEvidenceName);
                                    }

                                    if (viewport.Width == 900d)
                                    {
                                        window.BrowseControlsForSmoke
                                            .OpenFilterFlyoutForSmoke();
                                        Dispatcher.UIThread.RunJobs();
                                        Require(
                                            window.BrowseControlsForSmoke
                                                .FilterFlyoutIsOpenForSmoke
                                            && window.BrowseControlsForSmoke
                                                .FilterFlyoutLayoutIsContainedForSmoke,
                                            "Browse filter Flyout did not remain contained at 900x600.");
                                        CaptureVisualEvidence(
                                            window,
                                            "browse-filter-open-900x600");
                                        window.BrowseControlsForSmoke
                                            .CloseFilterFlyoutForSmoke();
                                        Dispatcher.UIThread.RunJobs();

                                        window.BrowseControlsForSmoke
                                            .OpenDisplayFlyoutForSmoke();
                                        Dispatcher.UIThread.RunJobs();
                                        Require(
                                            window.BrowseControlsForSmoke
                                                .DisplayFlyoutIsOpenForSmoke
                                            && window.BrowseControlsForSmoke
                                                .DisplayFlyoutLayoutIsContainedForSmoke,
                                            "Browse display-options Flyout did not remain contained at 900x600.");
                                        CaptureVisualEvidence(
                                            window,
                                            "browse-display-open-900x600");
                                        window.BrowseControlsForSmoke
                                            .CloseDisplayFlyoutForSmoke();
                                        Dispatcher.UIThread.RunJobs();

                                        var visualFilterAsset =
                                            await window.CurrentRuntime!
                                                .ViewerSession
                                                .GetAssetAsync(0);
                                        await window.CurrentRuntime
                                            .LibraryService
                                            .SetUserMetadataAsync(
                                                window.CurrentRuntime.Library.Id,
                                                visualFilterAsset.Id,
                                                new AssetUserMetadataUpdate(
                                                    Tags:
                                                        ["browse-filter-smoke"]));
                                        await window.BrowseControlsForSmoke
                                            .SetTagScopeAsync(
                                                "browse-filter-smoke");
                                        Dispatcher.UIThread.RunJobs();
                                        Require(
                                            window.BrowseControlsForSmoke
                                                .FilterButtonTextForSmoke
                                                .Contains(
                                                    "1",
                                                    StringComparison.Ordinal)
                                            && window.CurrentRuntime.AssetCount
                                                == 1
                                            && window.BrowseControlsForSmoke
                                                .ActiveChipsUseSharedThemeForSmoke,
                                            "Browse active-filter state was not surfaced with the shared chip design and a matching result.");
                                        CaptureVisualEvidence(
                                            window,
                                            "browse-active-filter-900x600");
                                        await window.BrowseControlsForSmoke
                                            .ClearTagScopesAsync();
                                        Dispatcher.UIThread.RunJobs();
                                    }
                                }

                                window.CurrentShell.GridViewer.SelectAsset(0);
                                await window.CurrentShell
                                    .ShowContextDetailAsync();
                                Dispatcher.UIThread.RunJobs();

                                // Avalonia can finalize the overlay width one
                                // render pass after async preview/metadata work,
                                // especially at 225% text scale. Wait for the
                                // same strict product geometry to settle rather
                                // than sampling a transient arrange state.
                                for (var inspectorSettleAttempt = 0;
                                     inspectorSettleAttempt < 40
                                     && (!window.CurrentShell.IsContextDetailVisible
                                         || window.CurrentShell.IsCompactInspectorLayout
                                             != (window.CurrentShell.Bounds.Width < 1600)
                                         || window.CurrentShell.ContextSurfaceBounds.Width
                                             is < 300 or > 380
                                         || window.CurrentShell.ContextDetail
                                             .IsCompactPresentationForSmoke
                                             != (window.CurrentShell.Bounds.Width < 1600)
                                         || !window.CurrentShell.ContextDetail.HasPreview);
                                     inspectorSettleAttempt++)
                                {
                                    Dispatcher.UIThread.RunJobs();
                                    AvaloniaHeadlessPlatform
                                        .ForceRenderTimerTick();
                                    await Task.Delay(1);
                                }
                                Dispatcher.UIThread.RunJobs();

                                Require(
                                    window.CurrentShell.IsContextDetailVisible
                                    && window.CurrentShell.IsCompactInspectorLayout
                                        == (window.CurrentShell.Bounds.Width < 1600)
                                    && window.CurrentShell.ContextSurfaceBounds.Width
                                        is >= 300 and <= 380
                                    && window.CurrentShell.ContextDetail
                                        .IsCompactPresentationForSmoke
                                        == (window.CurrentShell.Bounds.Width < 1600)
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

                                if (iteration == 0
                                    && mode == BrowseViewMode.Grid
                                    && !navigationVisible
                                    && viewport.Width == 900d)
                                {
                                    CaptureVisualEvidence(
                                        window,
                                        "inspector-900x600");
                                }

                                if (iteration == 2
                                    && mode == BrowseViewMode.Grid
                                    && !navigationVisible
                                    && viewport.Width == 900d)
                                {
                                    CaptureVisualEvidence(
                                        window,
                                        "inspector-900x600-text225");
                                }

                                if (iteration == 0
                                    && mode == BrowseViewMode.Grid
                                    && !navigationVisible
                                    && viewport.Width == 1440d)
                                {
                                    CaptureVisualEvidence(
                                        window,
                                        "inspector-1440x900-drawer");
                                }

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

                            if (window.CurrentShell.Bounds.Width >= 1600)
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
                                    && !window.CurrentShell.ContextDetail
                                        .IsCompactPresentationForSmoke
                                    && inspectorPinnedCanvasWidth
                                        < unpinnedClosedCanvasWidth - 250,
                                    $"Pinned Inspector did not dock beside the canvas at {viewport.Width:N0}x{viewport.Height:N0}, {mode}.");

                                if (iteration == 0
                                    && mode == BrowseViewMode.Grid
                                    && viewport.Width == 1920d)
                                {
                                    CaptureVisualEvidence(
                                        window,
                                        "inspector-1920x1080-pinned");
                                }

                                window.CurrentShell.SetInspectorPinnedForSmoke(false);
                                Dispatcher.UIThread.RunJobs();
                                Require(
                                    !window.CurrentShell.IsInspectorPinnedForSmoke
                                    && Math.Abs(
                                        window.CurrentShell.GridViewerBounds.Width
                                        - unpinnedClosedCanvasWidth) < 1,
                                    $"Unpinning Inspector did not restore overlay canvas width at {viewport.Width:N0}x{viewport.Height:N0}, {mode}.");
                                window.CurrentShell.HideContextDetail();
                            }

                            if (viewport.Width >= 1440)
                            {
                                window.SetNavigationPaneVisibleForSmoke(true);
                                window.SetNavigationPinnedForSmoke(true);
                                Dispatcher.UIThread.RunJobs();

                                var pinnedCanvasWidth =
                                    window.CurrentShell.GridViewerBounds.Width;
                                Require(
                                    window.IsNavigationPinnedForSmoke
                                    && !window.IsNavigationPaneOverlayForSmoke
                                    && window.PinnedNavigationUsesUnifiedSidebarForSmoke
                                    && window.NavigationPinVisibleForSmoke
                                    && ReferenceEquals(
                                        window.CurrentRuntime,
                                        runtimeBeforeNavigation)
                                    && pinnedCanvasWidth
                                        < unpinnedClosedCanvasWidth - 180,
                                    $"Pinned navigation did not become a unified sidebar beside the canvas at {viewport.Width:N0}x{viewport.Height:N0}, {mode}.");

                                if ((iteration == 0 || iteration == 2)
                                    && mode == BrowseViewMode.Grid
                                    && viewport.Width == 1440d)
                                {
                                    CaptureVisualEvidence(
                                        window,
                                        iteration == 2
                                            ? "navigation-pinned-1440x900-text225"
                                            : "navigation-pinned-1440x900");
                                }

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
                            .DisplayButtonIsVisibleForSmoke
                        && window.BrowseControlsForSmoke
                            .DisplayControlsGroupedForSmoke
                        && window.BrowseControlsForSmoke
                            .SearchUsesSharedThemeForSmoke
                        && !window.BrowseControlsForSmoke
                            .DisplayFlyoutIsOpenForSmoke,
                        "Minimum-width browse command bar clipped or escaped the workspace bounds.");

                    window.CurrentShell!.GridViewer.SelectAsset(0);
                    await window.CurrentShell.ShowContextDetailAsync();
                    Dispatcher.UIThread.RunJobs();

                    Require(
                        window.CurrentShell.IsCompactInspectorLayout
                        && window.CurrentShell.ContextSurfaceBounds.Width <= 340
                        && window.CurrentShell.ContextDetail
                            .IsCompactPresentationForSmoke
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
                                .SearchUsesSharedThemeForSmoke,
                            $"MainWindow responsive/layout virtualization or browse command containment regressed at {scaling:P0} render scaling.");
                    }

                    // The render-scaling matrix above intentionally
                    // ends at 225% in the baseline iteration. Normalize back
                    // to 1.0 before named 900x600 Viewer evidence so the
                    // baseline and text225 captures differ by text scale only.
                    window.SetRenderScaling(1.0);
                    Dispatcher.UIThread.RunJobs();
                    Require(
                        Math.Abs(
                            window.RenderScaling - 1.0) < 0.001,
                        "Focused-view visual evidence did not normalize render scaling to 100%.");

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

                    if (iteration is 0 or 2)
                    {
                        // The Viewer intentionally fades idle chrome, but
                        // acceptance evidence must show the discoverable
                        // controls a first-time user sees on entry.
                        var detailViewerType =
                            window.CurrentShell.DetailViewer.GetType();
                        detailViewerType
                            .GetMethod(
                                "RevealChromeForSmoke",
                                System.Reflection.BindingFlags.Instance
                                | System.Reflection.BindingFlags.NonPublic)
                            ?.Invoke(
                                window.CurrentShell.DetailViewer,
                                null);
                        Dispatcher.UIThread.RunJobs();
                        var chromeVisible =
                            detailViewerType
                                .GetProperty(
                                    "IsChromeVisibleForSmoke",
                                    System.Reflection.BindingFlags.Instance
                                    | System.Reflection.BindingFlags.NonPublic)
                                ?.GetValue(
                                    window.CurrentShell.DetailViewer)
                            is bool visible
                            && visible;
                        Require(
                            chromeVisible,
                            "Focused Viewer acceptance evidence did not expose its primary command chrome.");

                        var viewerFitButton =
                            window.GetVisualDescendants()
                                .OfType<Button>()
                                .FirstOrDefault(
                                    button =>
                                        string.Equals(
                                            AutomationProperties.GetAutomationId(
                                                button),
                                            "viewer.fit",
                                            StringComparison.Ordinal));
                        var expectedViewerCaptionSize =
                            12d
                            * (iteration == 2
                                ? 2.25
                                : 1.0);
                        Require(
                            viewerFitButton is not null
                            && Math.Abs(
                                viewerFitButton.FontSize
                                - expectedViewerCaptionSize) < 0.01
                            && Math.Abs(
                                LumineVisualMetrics.TextScaleFactor
                                - (iteration == 2
                                    ? 2.25
                                    : 1.0)) < 0.001,
                            $"Focused Viewer did not inherit the expected text scale: iteration={iteration}, metric={LumineVisualMetrics.TextScaleFactor:N2}, fitFont={viewerFitButton?.FontSize:N2}.");

                        CaptureVisualEvidence(
                            window,
                            iteration == 2
                                ? "focused-viewer-900x600-text225"
                                : "focused-viewer-900x600");

                        if (iteration == 2
                            && visualOutputDirectory is not null)
                        {
                            var baselineViewerEvidence =
                                File.ReadAllBytes(
                                    Path.Combine(
                                        visualOutputDirectory,
                                        "focused-viewer-900x600.png"));
                            var scaledViewerEvidence =
                                File.ReadAllBytes(
                                    Path.Combine(
                                        visualOutputDirectory,
                                        "focused-viewer-900x600-text225.png"));
                            Require(
                                !baselineViewerEvidence.SequenceEqual(
                                    scaledViewerEvidence),
                                "225% Focused Viewer visual evidence duplicated the 100% capture.");
                        }
                    }

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
        Exception? dataCleanupFailure = null;
        for (var dataCleanupAttempt = 0;
             dataCleanupAttempt < 40;
             dataCleanupAttempt++)
        {
            try
            {
                Directory.Delete(
                    repeatedDataRoot,
                    recursive: true);
                dataCleanupFailure = null;
                break;
            }
            catch (IOException exception)
                when (dataCleanupAttempt < 39)
            {
                dataCleanupFailure = exception;
                LibraryDatabase.ClearPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(25);
            }
        }

        if (Directory.Exists(repeatedDataRoot))
        {
            throw new InvalidOperationException(
                $"MainWindow lifecycle iteration {iteration} kept SQLite data handles locked beyond the bounded cleanup grace period.",
                dataCleanupFailure);
        }

        Directory.Delete(
            repeatedEmptyLibraryRoot,
            recursive: true);

        Environment.SetEnvironmentVariable(
            "LUMINE_TEXT_SCALE",
            previousTextScaleEnvironment);
        LumineVisualMetrics.ConfigureTextScaleFactor(
            baselineTextScale);
    }

    if (visualOutputDirectory is not null)
    {
        var expectedVisualEvidence =
            new[]
            {
                "design-system-scale-100",
                "design-system-scale-125",
                "design-system-scale-150",
                "design-system-scale-200",
                "design-system-scale-225",
                "welcome-1440x900",
                "loading-1440x900",
                "browse-900x600",
                "browse-1024x768",
                "browse-1440x900",
                "browse-1440x900-sidebar",
                "browse-1920x1080",
                "browse-filter-open-900x600",
                "browse-display-open-900x600",
                "browse-active-filter-900x600",
                "navigation-overlay-900x600",
                "navigation-overlay-900x600-text225",
                "navigation-pinned-1440x900",
                "navigation-pinned-1440x900-text225",
                "tags-assignment-1100x720",
                "tags-create-900x600-text225",
                "tags-create-custom-color-900x600-text225",
                "tags-edit-900x600-text225",
                "tags-edit-custom-color-900x600-text225",
                "dialog-confirm-danger",
                "dialog-confirm-danger-text225",
                "dialog-notify",
                "dialog-notify-text225",
                "inspector-organize-1100x720",
                "inspector-creative-1100x720",
                "inspector-publication-1100x720",
                "inspector-information-1100x720",
                "inspector-900x600",
                "inspector-900x600-text225",
                "inspector-1440x900-drawer",
                "inspector-1920x1080-pinned",
                "focused-viewer-900x600",
                "focused-viewer-900x600-text225",
                "settings-1440x900",
                "settings-advanced-1440x900",
                "settings-900x600",
                "settings-900x600-text225",
                "empty-library-1440x900",
                "no-match-1440x900",
                "error-1440x900",
                "error-900x600-text225"
            };
        var missingEvidence =
            expectedVisualEvidence
                .Where(
                    name =>
                        !visualEvidenceNames.Contains(
                            name))
                .ToArray();
        Require(
            missingEvidence.Length == 0
            && visualEvidenceNames.Count
                == expectedVisualEvidence.Length,
            "Visual regression evidence set was incomplete: "
            + string.Join(
                ", ",
                missingEvidence));
    }

    var formatExif =
        typeof(ContextualAssetDetailPanel).GetMethod(
            "FormatExif",
            BindingFlags.NonPublic
            | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "Inspector EXIF formatter was not found.");

    var formattedExif =
        formatExif.Invoke(
            null,
            [
                new AssetExifMetadata(
                    1,
                    3,
                    "Lumine Camera X",
                    "Lumine Lens 50mm",
                    "50.0 mm",
                    "f/2.8",
                    "1/125 sec.",
                    400,
                    "2026:10:05 23:45:00",
                    "N 35/1 41/1 1234/100",
                    "E 139/1 41/1 5678/100")
            ]) as string
        ?? throw new InvalidOperationException(
            "Inspector EXIF formatter returned no text.");

    Require(
        formattedExif.Contains(
            "カメラ: Lumine Camera X",
            StringComparison.Ordinal)
        && formattedExif.Contains(
            "レンズ: Lumine Lens 50mm",
            StringComparison.Ordinal)
        && formattedExif.Contains(
            "焦点距離: 50.0 mm",
            StringComparison.Ordinal)
        && formattedExif.Contains(
            "絞り: f/2.8",
            StringComparison.Ordinal)
        && formattedExif.Contains(
            "シャッター: 1/125 sec.",
            StringComparison.Ordinal)
        && formattedExif.Contains(
            "ISO: 400",
            StringComparison.Ordinal)
        && formattedExif.Contains(
            "撮影日時: 2026:10:05 23:45:00",
            StringComparison.Ordinal)
        && formattedExif.Contains(
            "GPS: N ",
            StringComparison.Ordinal),
        "Inspector did not present representative EXIF metadata with the restored information hierarchy.");

    var emptyExifText =
        formatExif.Invoke(
            null,
            [
                new AssetExifMetadata(
                    1,
                    3,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null)
            ]) as string;
    Require(
        string.Equals(
            emptyExifText,
            "EXIF情報はありません。",
            StringComparison.Ordinal),
        "Inspector did not render probed/no-EXIF state without noisy placeholder rows.");

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
        Exception? cleanupFailure = null;
        for (var cleanupAttempt = 0;
             cleanupAttempt < 40;
             cleanupAttempt++)
        {
            try
            {
                Directory.Delete(
                    root,
                    recursive: true);
                cleanupFailure = null;
                break;
            }
            catch (IOException exception)
                when (cleanupAttempt < 39)
            {
                cleanupFailure = exception;
                LibraryDatabase.ClearPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(25);
            }
        }

        if (Directory.Exists(root))
        {
            throw new IOException(
                "App smoke temporary database handles remained locked after the bounded cleanup grace period.",
                cleanupFailure);
        }
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
                    OverlayPopups = true
                });

    public override void Initialize() =>
        App.ApplyProductTheme(this);
}


internal sealed class InlineProgress<T>(
    Action<T> report) : IProgress<T>
{
    private readonly Action<T> _report =
        report
        ?? throw new ArgumentNullException(nameof(report));

    public void Report(T value) =>
        _report(value);
}
