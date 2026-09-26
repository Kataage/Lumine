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

static void WriteP3ProfilePng(string path)
{
    using var blank = NetVips.Image.Black(256, 192, bands: 4);
    using var values = blank.NewFromImage([32, 220, 64, 180]);
    using var srgb = values.Copy(interpretation: Enums.Interpretation.Srgb);
    using var p3 = srgb.IccTransform("p3", inputProfile: "srgb");

    p3.Pngsave(
        path,
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

static void WriteSolidJpeg(string path, int value)
{
    using var blank = NetVips.Image.Black(320, 200, bands: 3);
    using var values = blank.NewFromImage([value, value, value]);
    using var image = values.Copy(interpretation: Enums.Interpretation.Srgb);
    image.Jpegsave(path, q: 90);
}

static void PadToLength(string path, long length)
{
    using var stream = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Write,
        FileShare.None);
    if (stream.Length > length)
    {
        throw new InvalidOperationException(
            "Cannot pad a file to a smaller length.");
    }

    stream.SetLength(length);
}

static async Task VerifyRecommendedAccessPolicyAsync(
    string path,
    FullResolutionAccessPolicy expected,
    string label)
{
    var file = new FileInfo(path);
    using var prepared =
        await FullResolutionDecoder.PrepareAsync(
            new FullResolutionSource(
                path,
                file.Length,
                file.LastWriteTimeUtc.Ticks));

    Require(
        prepared.RecommendedAccessPolicy == expected,
        $"{label} adaptive access policy was {prepared.RecommendedAccessPolicy}; expected {expected}.");
}

static async Task VerifyFullResolutionAsync(
    string path,
    int expectedWidth,
    int expectedHeight,
    string label)
{
    var file = new FileInfo(path);
    var source = new FullResolutionSource(
        path,
        file.Length,
        file.LastWriteTimeUtc.Ticks);
    var info = await FullResolutionDecoder.ProbeAsync(source);

    Require(
        info.Width == expectedWidth && info.Height == expectedHeight,
        $"{label} full-resolution probe mismatch: {info.Width}x{info.Height}.");

    var rows = 0;
    await FullResolutionDecoder.DecodeAsync(
        source,
        32L * 1024 * 1024,
        stripe => rows += stripe.Height,
        stripeHeight: 29,
        expectedInfo: info);

    Require(
        rows == expectedHeight,
        $"{label} full-resolution decode returned {rows} rows; expected {expectedHeight}.");
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
    var disguisedPngPath = Path.Combine(sourceRoot, "png-with-jpg-extension.jpg");
    var webpPath = Path.Combine(sourceRoot, "sample.webp");
    var gifPath = Path.Combine(sourceRoot, "sample.gif");
    var tiffPath = Path.Combine(sourceRoot, "sample.tiff");
    var orientedPath = Path.Combine(sourceRoot, "oriented.jpg");
    var corruptSourcePath = Path.Combine(sourceRoot, "corrupt-source.jpg");
    var changedPath = Path.Combine(sourceRoot, "changed.jpg");
    var identityPath = Path.Combine(sourceRoot, "identity.jpg");
    var identityReplacementPath = Path.Combine(sourceRoot, "identity-replacement.jpg");
    var concurrentPath = Path.Combine(sourceRoot, "concurrent.jpg");
    var p3Path = Path.Combine(sourceRoot, "profile-p3.jpg");
    var p3PngPath = Path.Combine(sourceRoot, "profile-p3.png");
    var cancellationPath = Path.Combine(sourceRoot, "cancellation.png");
    var detailCachePath = Path.Combine(sourceRoot, "detail-cache.jpg");
    var sourceChangePath = Path.Combine(sourceRoot, "source-change.jpg");

    WriteRgb(jpgPath);
    WriteRgbaPng(pngPath);
    File.Copy(pngPath, disguisedPngPath);
    WriteRgb(webpPath);
    await WriteAnimatedGifAsync(gifPath);
    WriteRgb(tiffPath);
    WriteRgb(corruptSourcePath);
    WriteRgb(changedPath, 800, 600);
    WriteRgb(concurrentPath, 1200, 800);
    WriteP3ProfileJpeg(p3Path);
    WriteP3ProfilePng(p3PngPath);
    WriteRgb(cancellationPath, 6000, 6000);
    WriteRgb(detailCachePath, 2200, 1400);
    WriteRgb(sourceChangePath, 320, 200);

    Require(
        FullResolutionDecoder.ProductionAccessPolicy
            == FullResolutionAccessPolicy.Adaptive,
        "Production full-resolution access policy is not Adaptive.");
    await VerifyRecommendedAccessPolicyAsync(
        jpgPath,
        FullResolutionAccessPolicy.Random,
        "JPEG");
    await VerifyRecommendedAccessPolicyAsync(
        pngPath,
        FullResolutionAccessPolicy.Sequential,
        "PNG");
    await VerifyRecommendedAccessPolicyAsync(
        webpPath,
        FullResolutionAccessPolicy.Random,
        "WebP");
    await VerifyRecommendedAccessPolicyAsync(
        tiffPath,
        FullResolutionAccessPolicy.Random,
        "TIFF");
    await VerifyRecommendedAccessPolicyAsync(
        p3Path,
        FullResolutionAccessPolicy.Random,
        "JPEG ICC");
    await VerifyRecommendedAccessPolicyAsync(
        p3PngPath,
        FullResolutionAccessPolicy.Random,
        "PNG ICC");

    using (var orientationBlank = NetVips.Image.Black(120, 60, bands: 3))
    using (var baseImage = orientationBlank.Copy(interpretation: Enums.Interpretation.Srgb))
    using (var oriented = baseImage.Mutate(
               image => image.Set(GValue.GIntType, "orientation", 6)))
    {
        oriented.WriteToFile(orientedPath);
    }

    await VerifyRecommendedAccessPolicyAsync(
        orientedPath,
        FullResolutionAccessPolicy.Random,
        "EXIF-oriented JPEG");

    var disguisedInfo = new FileInfo(disguisedPngPath);
    using (var disguisedSnapshot = await ImageSourceSnapshot.OpenAsync(
               disguisedPngPath,
               disguisedInfo.Length,
               disguisedInfo.LastWriteTimeUtc.Ticks))
    {
        Require(
            string.Equals(
                disguisedSnapshot.Metadata.Format,
                "png",
                StringComparison.Ordinal),
            $"Actual loader format was not detected for extension-mismatched PNG: {disguisedSnapshot.Metadata.Format}.");
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
    foreach (var sourcePath in new[]
             {
                 jpgPath,
                 pngPath,
                 webpPath,
                 gifPath,
                 tiffPath
             })
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
    Require(
        persistentFirst.SourceMetadata is not null,
        "Persistent cache generation did not return source technical metadata.");
    persistentSource = persistentSource.WithMetadata(
        persistentFirst.SourceMetadata!);

    var diagnosticsBeforeHit = pipeline.Diagnostics;
    File.Delete(jpgPath);

    var persistentHit = await pipeline.RequestAsync(
        persistentSource,
        ThumbnailProfiles.GridMedium);
    Require(persistentHit.CacheHit, "Warm cache request missed after original deletion.");
    Require(
        pipeline.Diagnostics.SourceOpens == diagnosticsBeforeHit.SourceOpens
        && pipeline.Diagnostics.MetadataProbes == diagnosticsBeforeHit.MetadataProbes,
        "Warm cache hit touched or reprobed the original source.");

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
            restartedPipeline.Diagnostics.SourceOpens == 0
            && restartedPipeline.Diagnostics.MetadataProbes == 0,
            "Fresh pipeline persistent hit touched the deleted original source.");
    }

    var detailPersistentSource = SourceFor(21, 1, detailCachePath);
    var detailPersistentFirst = await pipeline.RequestAsync(
        detailPersistentSource,
        ThumbnailProfiles.DetailPreview);
    Require(
        !detailPersistentFirst.CacheHit,
        "Detail preview first request unexpectedly hit cache.");
    Require(
        detailPersistentFirst.SourceMetadata is not null,
        "Detail preview generation did not return source metadata.");
    detailPersistentSource = detailPersistentSource.WithMetadata(
        detailPersistentFirst.SourceMetadata!);

    var detailDiagnosticsBeforeHit = pipeline.Diagnostics;
    File.Delete(detailCachePath);

    var detailPersistentHit = await pipeline.RequestAsync(
        detailPersistentSource,
        ThumbnailProfiles.DetailPreview);
    Require(
        detailPersistentHit.CacheHit,
        "Detail preview did not prefer persistent cache after original disappeared.");
    Require(
        pipeline.Diagnostics.SourceOpens == detailDiagnosticsBeforeHit.SourceOpens
        && pipeline.Diagnostics.MetadataProbes == detailDiagnosticsBeforeHit.MetadataProbes,
        "Warm Detail preview cache hit touched or reprobed the original source.");

    await using (var restartedDetailPipeline = new ThumbnailPipeline(
                     new ThumbnailCache(cacheRoot),
                     new ThumbnailPipelineOptions
                     {
                         WorkerCount = 1,
                         QueueCapacity = 2
                     }))
    {
        var restartedDetailHit = await restartedDetailPipeline.RequestAsync(
            detailPersistentSource,
            ThumbnailProfiles.DetailPreview);
        Require(
            restartedDetailHit.CacheHit,
            "Restarted pipeline did not reuse persistent Detail preview.");
        Require(
            restartedDetailPipeline.Diagnostics.SourceOpens == 0
            && restartedDetailPipeline.Diagnostics.MetadataProbes == 0,
            "Restarted Detail preview cache hit touched the missing original.");
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

    WriteSolidJpeg(identityPath, 24);
    WriteSolidJpeg(identityReplacementPath, 220);
    var identityLength = Math.Max(
        new FileInfo(identityPath).Length,
        new FileInfo(identityReplacementPath).Length);
    PadToLength(identityPath, identityLength);
    PadToLength(identityReplacementPath, identityLength);
    var identityTimestamp = DateTime.UtcNow.AddMinutes(-5);
    File.SetLastWriteTimeUtc(identityPath, identityTimestamp);
    File.SetLastWriteTimeUtc(identityReplacementPath, identityTimestamp);

    var identityStat = new FileInfo(identityPath);
    using var identitySnapshot = await ImageSourceSnapshot.OpenAsync(
        identityPath,
        identityStat.Length,
        identityStat.LastWriteTimeUtc.Ticks);
    var identityMetadata = identitySnapshot.Metadata;
    identitySnapshot.Dispose();

    var replacementBytes = await File.ReadAllBytesAsync(
        identityReplacementPath);
    await File.WriteAllBytesAsync(
        identityPath,
        replacementBytes);
    File.SetLastWriteTimeUtc(
        identityPath,
        identityTimestamp);

    var replacedStat = new FileInfo(identityPath);
    Require(
        replacedStat.Length == identityStat.Length
        && replacedStat.LastWriteTimeUtc.Ticks
            == identityStat.LastWriteTimeUtc.Ticks,
        "Same-stat replacement fixture did not preserve size/mtime.");

    try
    {
        await FullResolutionDecoder.DecodeAsync(
            new FullResolutionSource(
                identityPath,
                identityStat.Length,
                identityStat.LastWriteTimeUtc.Ticks,
                identityMetadata.SourceIdentity),
            32L * 1024 * 1024,
            _ => { });
        throw new InvalidOperationException(
            "Same-size/mtime source replacement was not rejected by content identity.");
    }
    catch (FullResolutionSourceChangedException)
    {
    }

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
            throw new InvalidOperationException("Pre-cancelled thumbnail request unexpectedly succeeded.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    using (var nativeCancelled = new CancellationTokenSource())
    {
        var opensBeforeCancellation = pipeline.Diagnostics.SourceOpens;
        var cancellationSource = SourceFor(46, 1, cancellationPath);
        var cancellationTask = pipeline.RequestAsync(
            cancellationSource,
            ThumbnailProfiles.DetailPreview,
            cancellationToken: nativeCancelled.Token);

        for (var attempt = 0;
             attempt < 2000 && pipeline.Diagnostics.SourceOpens == opensBeforeCancellation;
             attempt++)
        {
            await Task.Delay(1);
        }

        Require(
            pipeline.Diagnostics.SourceOpens > opensBeforeCancellation,
            "Cancellation smoke never reached native source evaluation.");

        nativeCancelled.Cancel();

        try
        {
            _ = await cancellationTask;
            throw new InvalidOperationException("In-flight native thumbnail cancellation unexpectedly completed.");
        }
        catch (OperationCanceledException)
        {
        }

        Require(
            !Directory.EnumerateFiles(cacheRoot, "*.tmp.webp", SearchOption.AllDirectories).Any(),
            "Native cancellation left a temporary cache file behind.");
    }

    var orientedFullSource = new FullResolutionSource(
        orientedPath,
        new FileInfo(orientedPath).Length,
        File.GetLastWriteTimeUtc(orientedPath).Ticks);
    var orientedFullInfo = await FullResolutionDecoder.ProbeAsync(
        orientedFullSource);
    Require(
        orientedFullInfo.Width == 60 && orientedFullInfo.Height == 120,
        $"Full-resolution EXIF orientation was not applied: {orientedFullInfo.Width}x{orientedFullInfo.Height}.");

    var orientedRows = 0;
    var orientedStripeCount = 0;
    await FullResolutionDecoder.DecodeAsync(
        orientedFullSource,
        16L * 1024 * 1024,
        stripe =>
        {
            Require(
                stripe.Y == orientedRows,
                "Full-resolution stripes were not emitted in top-to-bottom order.");
            Require(
                stripe.Width == 60,
                "Full-resolution oriented stripe width mismatch.");
            Require(
                stripe.RowBytes == stripe.Width * 4,
                "Full-resolution stripe row-byte contract mismatch.");

            orientedRows += stripe.Height;
            orientedStripeCount++;
        },
        stripeHeight: 17);
    Require(
        orientedRows == 120 && orientedStripeCount > 1,
        "Full-resolution oriented decode did not stream the complete image.");

    var alphaFullSource = new FullResolutionSource(
        pngPath,
        new FileInfo(pngPath).Length,
        File.GetLastWriteTimeUtc(pngPath).Ticks);
    var alphaFullInfo = await FullResolutionDecoder.ProbeAsync(alphaFullSource);
    Require(alphaFullInfo.HasAlpha, "Full-resolution PNG probe lost alpha metadata.");

    byte[]? alphaFirstStripe = null;
    await FullResolutionDecoder.DecodeAsync(
        alphaFullSource,
        16L * 1024 * 1024,
        stripe => alphaFirstStripe ??= stripe.RgbaBytes,
        stripeHeight: 32);
    Require(
        alphaFirstStripe is { Length: > 4 }
        && alphaFirstStripe[3] is >= 120 and <= 136,
        "Full-resolution PNG decode did not preserve straight alpha.");

    var p3FullSource = new FullResolutionSource(
        p3Path,
        new FileInfo(p3Path).Length,
        File.GetLastWriteTimeUtc(p3Path).Ticks);
    byte[]? p3FirstStripe = null;
    await FullResolutionDecoder.DecodeAsync(
        p3FullSource,
        16L * 1024 * 1024,
        stripe => p3FirstStripe ??= stripe.RgbaBytes,
        stripeHeight: 16);
    Require(
        p3FirstStripe is { Length: > 4 }
        && p3FirstStripe[0] < 50
        && p3FirstStripe[1] > 170
        && p3FirstStripe[2] < 50,
        "Full-resolution embedded P3 profile was not normalized to sRGB.");

    var p3PngFullSource = new FullResolutionSource(
        p3PngPath,
        new FileInfo(p3PngPath).Length,
        File.GetLastWriteTimeUtc(p3PngPath).Ticks);
    byte[]? p3PngFirstStripe = null;
    await FullResolutionDecoder.DecodeAsync(
        p3PngFullSource,
        16L * 1024 * 1024,
        stripe => p3PngFirstStripe ??= stripe.RgbaBytes,
        stripeHeight: 23);
    Require(
        p3PngFirstStripe is { Length: > 4 }
        && p3PngFirstStripe[0] < 80
        && p3PngFirstStripe[1] > 140
        && p3PngFirstStripe[2] < 100
        && p3PngFirstStripe[3] is >= 170 and <= 190,
        "Full-resolution PNG ICC Random fallback did not preserve sRGB-normalized color and alpha.");

    var webpFullSource = new FullResolutionSource(
        webpPath,
        new FileInfo(webpPath).Length,
        File.GetLastWriteTimeUtc(webpPath).Ticks);
    var webpFullInfo = await FullResolutionDecoder.ProbeAsync(webpFullSource);
    Require(
        webpFullInfo.Width == 320 && webpFullInfo.Height == 200,
        $"Full-resolution WebP probe mismatch: {webpFullInfo.Width}x{webpFullInfo.Height}.");

    var webpRows = 0;
    await FullResolutionDecoder.DecodeAsync(
        webpFullSource,
        16L * 1024 * 1024,
        stripe => webpRows += stripe.Height,
        stripeHeight: 31);
    Require(
        webpRows == 200,
        "Full-resolution WebP decode did not stream the complete image.");

    await VerifyFullResolutionAsync(corruptSourcePath, 320, 200, "JPEG");
    await VerifyFullResolutionAsync(tiffPath, 320, 200, "TIFF");

    var gifFullSource = new FullResolutionSource(
        gifPath,
        new FileInfo(gifPath).Length,
        File.GetLastWriteTimeUtc(gifPath).Ticks);
    var gifFullInfo = await FullResolutionDecoder.ProbeAsync(gifFullSource);
    Require(
        gifFullInfo.Width == 48 && gifFullInfo.Height == 32,
        "Animated GIF full-resolution fallback must use the first frame.");

    try
    {
        await FullResolutionDecoder.DecodeAsync(
            new FullResolutionSource(
                changedPath,
                new FileInfo(changedPath).Length,
                File.GetLastWriteTimeUtc(changedPath).Ticks),
            1_000,
            _ => { });
        throw new InvalidOperationException(
            "Full-resolution decode ignored the hard byte budget.");
    }
    catch (FullResolutionBudgetExceededException exception)
    {
        Require(
            exception.RequiredBytes > exception.BudgetBytes,
            "Full-resolution budget exception reported invalid byte accounting.");
    }

    var staleIdentityFile = new FileInfo(sourceChangePath);
    var staleIdentitySource = new FullResolutionSource(
        sourceChangePath,
        staleIdentityFile.Length,
        staleIdentityFile.LastWriteTimeUtc.Ticks);
    var staleIdentityInfo =
        await FullResolutionDecoder.ProbeAsync(staleIdentitySource);

    await Task.Delay(20);
    WriteRgb(sourceChangePath, 640, 480);
    File.SetLastWriteTimeUtc(
        sourceChangePath,
        DateTime.UtcNow.AddSeconds(2));

    var staleIdentityStripes = 0;
    try
    {
        await FullResolutionDecoder.DecodeAsync(
            staleIdentitySource,
            32L * 1024 * 1024,
            _ => staleIdentityStripes++,
            expectedInfo: staleIdentityInfo);
        throw new InvalidOperationException(
            "Full-resolution decode accepted a source that changed after probe.");
    }
    catch (FullResolutionSourceChangedException)
    {
    }

    Require(
        staleIdentityStripes == 0,
        "Changed source emitted pixels before its identity mismatch was rejected.");

    var replacementFile = new FileInfo(sourceChangePath);
    var replacementSource = new FullResolutionSource(
        sourceChangePath,
        replacementFile.Length,
        replacementFile.LastWriteTimeUtc.Ticks);
    var mismatchedProbeStripes = 0;

    try
    {
        await FullResolutionDecoder.DecodeAsync(
            replacementSource,
            32L * 1024 * 1024,
            _ => mismatchedProbeStripes++,
            expectedInfo: staleIdentityInfo);
        throw new InvalidOperationException(
            "Full-resolution decode accepted dimensions that no longer match the probed bitmap.");
    }
    catch (FullResolutionSourceChangedException)
    {
    }

    Require(
        mismatchedProbeStripes == 0,
        "Probe/decode dimension mismatch emitted pixels before rejection.");

    using (var fullCancellation = new CancellationTokenSource())
    {
        var stripesObserved = 0;
        var fullCancellationTask = FullResolutionDecoder.DecodeAsync(
            new FullResolutionSource(
                cancellationPath,
                new FileInfo(cancellationPath).Length,
                File.GetLastWriteTimeUtc(cancellationPath).Ticks),
            200L * 1024 * 1024,
            _ =>
            {
                stripesObserved++;
                if (stripesObserved == 1)
                {
                    fullCancellation.Cancel();
                }
            },
            stripeHeight: 32,
            cancellationToken: fullCancellation.Token);

        try
        {
            await fullCancellationTask;
            throw new InvalidOperationException(
                "Full-resolution strip decode ignored in-flight cancellation.");
        }
        catch (OperationCanceledException)
        {
        }

        Require(
            stripesObserved == 1,
            $"Full-resolution cancellation continued for {stripesObserved} stripes.");
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
        await VerifyFullResolutionAsync(avifPath, 8, 6, "AVIF");
        await VerifyRecommendedAccessPolicyAsync(
            avifPath,
            FullResolutionAccessPolicy.Random,
            "AVIF");
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
        await VerifyFullResolutionAsync(heicPath, 8, 6, "HEIC");
        await VerifyRecommendedAccessPolicyAsync(
            heicPath,
            FullResolutionAccessPolicy.Random,
            "HEIC");
    }

    var statsBeforePrune = await cache.GetStatsAsync();
    Require(statsBeforePrune.FileCount >= 6, "Expected persisted thumbnail cache entries.");
    Require(statsBeforePrune.TotalBytes > 0, "Thumbnail cache accounting returned zero bytes.");

    var fakeInterrupted = Path.Combine(cacheRoot, "dead.tmp.webp");
    await File.WriteAllBytesAsync(fakeInterrupted, [1, 2, 3]);
    _ = new ThumbnailCache(cacheRoot);
    Require(
        File.Exists(fakeInterrupted),
        "Cache construction unexpectedly scanned/deleted temporary files.");

    var freshRecovered = await cache.RecoverInterruptedWritesAsync();
    Require(freshRecovered == 0, "Fresh temporary write was treated as interrupted.");
    Require(File.Exists(fakeInterrupted), "Fresh temporary write was deleted as interrupted.");

    File.SetLastWriteTimeUtc(
        fakeInterrupted,
        DateTime.UtcNow - ThumbnailCache.InterruptedWriteGracePeriod - TimeSpan.FromMinutes(1));
    var staleRecovered = await cache.RecoverInterruptedWritesAsync();
    Require(staleRecovered == 1, "Stale interrupted thumbnail write was not reported as recovered.");
    Require(!File.Exists(fakeInterrupted), "Stale interrupted thumbnail write was not cleaned up.");

    var partialTarget = Math.Max(1, statsBeforePrune.TotalBytes - 1);
    var partialPrune = await cache.PruneAsync(partialTarget);
    Require(partialPrune.FilesDeleted > 0, "Bounded partial prune removed no files.");
    Require(
        partialPrune.BytesAfter <= partialTarget,
        "Bounded partial prune did not reach its disk budget.");

    var prune = await cache.PruneAsync(0);
    Require(prune.FilesDeleted > 0, "Thumbnail zero-budget prune removed no remaining files.");
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
