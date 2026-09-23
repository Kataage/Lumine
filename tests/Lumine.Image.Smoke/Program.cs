using Lumine.Image;
using NetVips;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static byte[] CreateHeifFixture(Enums.ForeignHeifCompression compression)
{
    using var blank = NetVips.Image.Black(8, 6, bands: 3);
    using var image = blank.Copy(interpretation: Enums.Interpretation.Srgb);

    return image.HeifsaveBuffer(
        q: 80,
        compression: compression,
        effort: 0,
        keep: Enums.ForeignKeep.None);
}

static Task WriteAnimatedGifAsync(string path) =>
    File.WriteAllBytesAsync(
        path,
        Convert.FromBase64String(
            "R0lGODlhMAAgAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACgAAACwAAAAAMAAgAAAIQQABCBxIsKDBgwgTKlzIsKHDhxAjSpxIsaLFixgzatzIsaPHjyBDihxJsqTJkyhTqlzJsqXLlzBjypxJs6bNmQEBACH5BAEKAAEALAAAAAAwACAAgQAA/wAAAAAAAAAAAAhBAAEIHEiwoMGDCBMqXMiwocOHECNKnEixosWLGDNq3Mixo8ePIEOKHEmypMmTKFOqXMmypcuXMGPKnEmzps2ZAQEAOw=="));

static void WriteP3ProfileJpeg(string path)
{
    using var blank = NetVips.Image.Black(96, 64, bands: 3);
    using var greenValues = blank.NewFromImage([0, 240, 0]);
    using var green = greenValues.Copy(interpretation: Enums.Interpretation.Srgb);
    using var p3 = green.IccTransform("p3", inputProfile: "srgb");

    p3.Jpegsave(
        path,
        q: 95,
        keep: Enums.ForeignKeep.Icc);
}

static ThumbnailSource SourceFor(long assetId, long revision, string path)
{
    var info = new FileInfo(path);
    return new ThumbnailSource(
        assetId,
        revision,
        path,
        info.Length,
        info.LastWriteTimeUtc.Ticks);
}

static void WriteRgb(string path, int width = 320, int height = 200)
{
    using var black = NetVips.Image.Black(width, height, bands: 3);
    using var rgb = black.Copy(interpretation: Enums.Interpretation.Srgb);
    rgb.WriteToFile(path);
}

static void WriteRgbaPng(string path)
{
    using var blank = NetVips.Image.Black(240, 160, bands: 4);
    using var rgbaValues = blank.NewFromImage(SmokeConstants.RgbaPixel);
    using var rgba = rgbaValues.Copy(interpretation: Enums.Interpretation.Srgb);
    rgba.WriteToFile(path);
}

var capabilities = VipsCapabilities.Probe();
Require(capabilities.JpegLoad, "Bundled libvips has no JPEG loader.");
Require(capabilities.PngLoad, "Bundled libvips has no PNG loader.");
Require(capabilities.WebpLoad, "Bundled libvips has no WebP loader.");
Require(capabilities.WebpSave, "Bundled libvips has no WebP saver.");
Require(capabilities.GifLoad, "Bundled libvips has no GIF loader.");

var root = Path.Combine(Path.GetTempPath(), $"lumine-image-smoke-{Guid.NewGuid():N}");
var sourceRoot = Path.Combine(root, "sources");
var cacheRoot = Path.Combine(root, "cache");
Directory.CreateDirectory(sourceRoot);

try
{
    var jpgPath = Path.Combine(sourceRoot, "sample.jpg");
    var pngPath = Path.Combine(sourceRoot, "alpha.png");
    var webpPath = Path.Combine(sourceRoot, "sample.webp");
    var gifPath = Path.Combine(sourceRoot, "sample.gif");
    var orientedPath = Path.Combine(sourceRoot, "oriented.jpg");
    var corruptSourcePath = Path.Combine(sourceRoot, "corrupt-source.jpg");
    var changedPath = Path.Combine(sourceRoot, "changed.jpg");
    var concurrentPath = Path.Combine(sourceRoot, "concurrent.jpg");
    var p3Path = Path.Combine(sourceRoot, "profile-p3.jpg");

    WriteRgb(jpgPath);
    WriteRgbaPng(pngPath);
    WriteRgb(webpPath);
    await WriteAnimatedGifAsync(gifPath);
    WriteRgb(corruptSourcePath);
    WriteRgb(changedPath, 800, 600);
    WriteRgb(concurrentPath, 1200, 800);
    WriteP3ProfileJpeg(p3Path);

    using (var orientationBlank = NetVips.Image.Black(120, 60, bands: 3))
    using (var baseImage = orientationBlank.Copy(interpretation: Enums.Interpretation.Srgb))
    using (var oriented = baseImage.Mutate(
               image => image.Set(GValue.GIntType, "orientation", 6)))
    {
        oriented.WriteToFile(orientedPath);
    }

    var cache = new ThumbnailCache(cacheRoot);
    Require(NetVips.Cache.Max == 0, "libvips operation cache was not disabled.");
    Require(NetVips.Cache.MaxFiles == 0, "libvips file operation cache was not disabled.");
    Require(
        NetVips.NetVips.Concurrency == VipsRuntimePolicy.ThumbnailVipsConcurrency,
        "libvips concurrency does not match Image Core resource policy.");
    Require(
        NetVips.Cache.MaxMem <= VipsRuntimePolicy.ThumbnailTrackedMemoryLimitBytes,
        "libvips tracked-memory cache exceeds Image Core policy.");
    await using var pipeline = new ThumbnailPipeline(
        cache,
        new ThumbnailPipelineOptions
        {
            WorkerCount = 2,
            QueueCapacity = 8
        });

    Require(pipeline.WorkerCount == 2, "Worker bound was not applied.");
    Require(pipeline.QueueCapacity == 8, "Queue bound was not applied.");
    Require(pipeline.MaxForegroundBurst == 8, "Foreground fairness bound was not applied.");

    long assetId = 1;
    foreach (var sourcePath in new[] { jpgPath, pngPath, webpPath, gifPath })
    {
        var source = SourceFor(assetId++, 1, sourcePath);
        var result = await pipeline.RequestAsync(source, ThumbnailProfiles.GridSmall);

        Require(!result.CacheHit, $"First {Path.GetExtension(sourcePath)} request unexpectedly hit cache.");
        Require(File.Exists(result.CachePath), "Generated thumbnail was not persisted.");
        Require(result.Width <= ThumbnailProfiles.GridSmall.MaxWidth, "Thumbnail width exceeded profile.");
        Require(result.Height <= ThumbnailProfiles.GridSmall.MaxHeight, "Thumbnail height exceeded profile.");

        using var cachedImage = NetVips.Image.NewFromFile(result.CachePath);
        Require(
            cachedImage.Interpretation == Enums.Interpretation.Srgb,
            "Cached thumbnail is not normalized to sRGB.");
        cachedImage.Invalidate();
    }

    var gifStaticSource = SourceFor(9, 1, gifPath);
    var gifStaticResult = await pipeline.RequestAsync(
        gifStaticSource,
        ThumbnailProfiles.GridSmall);
    Require(
        gifStaticResult.Width == 48 && gifStaticResult.Height == 32,
        $"Animated GIF preview must use only the first frame; got {gifStaticResult.Width}x{gifStaticResult.Height}.");

    var p3Source = SourceFor(11, 1, p3Path);
    var p3Result = await pipeline.RequestAsync(
        p3Source,
        ThumbnailProfiles.GridSmall);
    using (var p3Cached = NetVips.Image.NewFromFile(p3Result.CachePath))
    using (var redBand = p3Cached.ExtractBand(0))
    using (var greenBand = p3Cached.ExtractBand(1))
    using (var blueBand = p3Cached.ExtractBand(2))
    {
        var redMean = redBand.Avg();
        var greenMean = greenBand.Avg();
        var blueMean = blueBand.Avg();

        Require(
            redMean < 40 && greenMean > 180 && blueMean < 40,
            $"Embedded P3 profile was not normalized to sRGB pixels: R={redMean:F1}, G={greenMean:F1}, B={blueMean:F1}.");
        p3Cached.Invalidate();
    }

    var alphaSource = SourceFor(2, 1, pngPath);
    var alphaResult = await pipeline.RequestAsync(alphaSource, ThumbnailProfiles.GridSmall);
    using (var alphaCached = NetVips.Image.NewFromFile(alphaResult.CachePath))
    {
        Require(alphaCached.Bands >= 4, "PNG alpha channel was not preserved in WebP cache.");
        alphaCached.Invalidate();
    }

    var orientationSource = SourceFor(10, 1, orientedPath);
    var orientationResult = await pipeline.RequestAsync(
        orientationSource,
        new ThumbnailProfile("orientation", 100, 100, 82, 1));
    Require(
        orientationResult.Height > orientationResult.Width,
        "EXIF orientation was not applied before thumbnail sizing.");

    var persistentSource = SourceFor(20, 1, jpgPath);
    var persistentFirst = await pipeline.RequestAsync(
        persistentSource,
        ThumbnailProfiles.GridMedium);
    Require(!persistentFirst.CacheHit, "Persistent cache first request unexpectedly hit.");

    var diagnosticsBeforeHit = pipeline.Diagnostics;
    File.Delete(jpgPath);

    var persistentHit = await pipeline.RequestAsync(
        persistentSource,
        ThumbnailProfiles.GridMedium);
    Require(persistentHit.CacheHit, "Warm cache request missed after original deletion.");
    Require(
        pipeline.Diagnostics.SourceOpens == diagnosticsBeforeHit.SourceOpens,
        "Cache hit touched the original source.");

    await using (var restartedPipeline = new ThumbnailPipeline(
                     new ThumbnailCache(cacheRoot),
                     new ThumbnailPipelineOptions
                     {
                         WorkerCount = 1,
                         QueueCapacity = 2
                     }))
    {
        var restartedHit = await restartedPipeline.RequestAsync(
            persistentSource,
            ThumbnailProfiles.GridMedium);
        Require(restartedHit.CacheHit, "Fresh pipeline/cache instance did not reuse persistent thumbnail.");
        Require(
            restartedPipeline.Diagnostics.SourceOpens == 0,
            "Fresh pipeline persistent hit touched the deleted original source.");
    }

    var corruptSource = SourceFor(30, 1, corruptSourcePath);
    var corruptFirst = await pipeline.RequestAsync(
        corruptSource,
        ThumbnailProfiles.GridSmall);
    await File.WriteAllBytesAsync(corruptFirst.CachePath, [1, 2, 3, 4, 5]);

    var corruptRecovered = await pipeline.RequestAsync(
        corruptSource,
        ThumbnailProfiles.GridSmall);
    Require(!corruptRecovered.CacheHit, "Corrupt cache entry was accepted as a hit.");
    Require(corruptRecovered.CacheFileBytes > 5, "Corrupt cache entry was not regenerated.");

    var changedV1 = SourceFor(40, 1, changedPath);
    var changedFirst = await pipeline.RequestAsync(
        changedV1,
        ThumbnailProfiles.GridSmall);

    await Task.Delay(20);
    WriteRgb(changedPath, 640, 480);
    File.SetLastWriteTimeUtc(changedPath, DateTime.UtcNow.AddSeconds(1));
    var changedV2 = SourceFor(40, 2, changedPath);
    var changedSecond = await pipeline.RequestAsync(
        changedV2,
        ThumbnailProfiles.GridSmall);

    Require(!changedSecond.CacheHit, "Changed source reused stale thumbnail.");
    Require(
        !string.Equals(changedFirst.CacheKey, changedSecond.CacheKey, StringComparison.Ordinal),
        "Source revision did not change thumbnail cache key.");
    Require(
        !string.Equals(changedFirst.CachePath, changedSecond.CachePath, StringComparison.Ordinal),
        "Source revision did not move to a new cache path.");

    var concurrentSource = SourceFor(45, 1, concurrentPath);
    var concurrentResults = await Task.WhenAll(
        Enumerable.Range(0, 8)
            .Select(_ => pipeline.RequestAsync(
                concurrentSource,
                ThumbnailProfiles.GridMedium)));

    Require(
        concurrentResults.All(result =>
            string.Equals(
                result.CachePath,
                concurrentResults[0].CachePath,
                StringComparison.Ordinal)),
        "Concurrent requests did not converge on one persistent cache path.");
    Require(
        !Directory.EnumerateFiles(cacheRoot, "*.tmp.webp", SearchOption.AllDirectories).Any(),
        "Concurrent generation left interrupted temporary files.");

    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        try
        {
            _ = await pipeline.RequestAsync(
                changedV2,
                ThumbnailProfiles.GridSmall,
                cancellationToken: cancelled.Token);
            throw new InvalidOperationException("Cancelled thumbnail request unexpectedly succeeded.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    if (capabilities.AvifRoundTrip)
    {
        var avifPath = Path.Combine(sourceRoot, "sample.avif");
        await File.WriteAllBytesAsync(
            avifPath,
            CreateHeifFixture(Enums.ForeignHeifCompression.Av1));
        var avifResult = await pipeline.RequestAsync(
            SourceFor(50, 1, avifPath),
            ThumbnailProfiles.GridSmall);
        Require(File.Exists(avifResult.CachePath), "AVIF capability was reported but AVIF smoke failed.");
    }

    if (capabilities.HeicRoundTrip)
    {
        var heicPath = Path.Combine(sourceRoot, "sample.heic");
        await File.WriteAllBytesAsync(
            heicPath,
            CreateHeifFixture(Enums.ForeignHeifCompression.Hevc));
        var heicResult = await pipeline.RequestAsync(
            SourceFor(51, 1, heicPath),
            ThumbnailProfiles.GridSmall);
        Require(File.Exists(heicResult.CachePath), "HEIC capability was reported but HEIC smoke failed.");
    }

    var statsBeforePrune = await cache.GetStatsAsync();
    Require(statsBeforePrune.FileCount >= 6, "Expected persisted thumbnail cache entries.");
    Require(statsBeforePrune.TotalBytes > 0, "Thumbnail cache accounting returned zero bytes.");

    var fakeInterrupted = Path.Combine(cacheRoot, "dead.tmp.webp");
    await File.WriteAllBytesAsync(fakeInterrupted, [1, 2, 3]);
    _ = new ThumbnailCache(cacheRoot);
    Require(
        File.Exists(fakeInterrupted),
        "Fresh temporary write was deleted as if it were interrupted.");

    File.SetLastWriteTimeUtc(
        fakeInterrupted,
        DateTime.UtcNow - ThumbnailCache.InterruptedWriteGracePeriod - TimeSpan.FromMinutes(1));
    _ = new ThumbnailCache(cacheRoot);
    Require(!File.Exists(fakeInterrupted), "Stale interrupted thumbnail write was not cleaned up.");

    var prune = await cache.PruneAsync(0);
    Require(prune.FilesDeleted > 0, "Thumbnail prune removed no files.");
    Require(prune.BytesAfter == 0, "Thumbnail prune did not enforce zero-byte target.");

    Console.WriteLine(
        $"Image smoke: jpeg/png/webp/gif OK; HEIF ops load={capabilities.HeifLoadOperation}, save={capabilities.HeifSaveOperation}, " +
        $"AVIF={capabilities.AvifRoundTrip}, HEIC={capabilities.HeicRoundTrip}; " +
        $"hits={pipeline.Diagnostics.CacheHits}, generated={pipeline.Diagnostics.Generated}, source-opens={pipeline.Diagnostics.SourceOpens}");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

static class SmokeConstants
{
    public static readonly int[] RgbaPixel = [20, 80, 160, 128];
}
