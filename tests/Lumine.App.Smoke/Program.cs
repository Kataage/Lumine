using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Lumine.App;
using Lumine.Core;
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

var root = Path.Combine(
    Path.GetTempPath(),
    $"lumine-app-smoke-{Guid.NewGuid():N}");
var libraryRoot = Path.Combine(root, "library");
var cacheRoot = Path.Combine(root, "cache");
var databasePath = Path.Combine(root, "library.db");
var sourcePath = Path.Combine(libraryRoot, "adapter-source.png");
var replacementPath = Path.Combine(root, "adapter-replacement.png");

Directory.CreateDirectory(libraryRoot);

try
{
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
