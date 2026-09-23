using System.Globalization;
using Lumine.Diagnostics;
using Lumine.Image;
using NetVips;

static string? ReadOption(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.Ordinal))
        {
            return args[index + 1];
        }
    }

    return null;
}

var output = ReadOption(args, "--output")
    ?? Path.Combine("artifacts", "benchmarks", "image-thumbnail.json");

var requestCountText = ReadOption(args, "--count") ?? "64";
if (!int.TryParse(
        requestCountText,
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var requestCount)
    || requestCount <= 0)
{
    throw new ArgumentException("--count must be a positive integer.");
}

var root = Path.Combine(Path.GetTempPath(), $"lumine-image-benchmark-{Guid.NewGuid():N}");
var sourcePath = Path.Combine(root, "source.jpg");
var cacheRoot = Path.Combine(root, "cache");
Directory.CreateDirectory(root);

var recorder = new BenchmarkRecorder();
long peakWorkingSetBytes = 0;
long peakAdditionalWorkingSetBytes = 0;
long batchPeakWorkingSetBytes = 0;
long batchPeakAdditionalWorkingSetBytes = 0;
long cacheBytes = 0;
long cacheFiles = 0;

try
{
    using (var blank = NetVips.Image.Black(2400, 1600, bands: 3))
    using (var source = blank.Copy(interpretation: Enums.Interpretation.Srgb))
    {
        source.WriteToFile(sourcePath + "[Q=90]");
    }

    var info = new FileInfo(sourcePath);
    var cache = new ThumbnailCache(cacheRoot);

    await using var pipeline = new ThumbnailPipeline(
        cache,
        new ThumbnailPipelineOptions
        {
            WorkerCount = ThumbnailPipelineOptions.DefaultWorkerCount,
            QueueCapacity = 64
        });

    var firstSource = new ThumbnailSource(
        1,
        1,
        sourcePath,
        info.Length,
        info.LastWriteTimeUtc.Ticks);

    var peak = PeakWorkingSetMonitor.Start();
    using (recorder.Measure(CoreMetricNames.ThumbnailGenerate))
    {
        _ = await pipeline.RequestAsync(
            firstSource,
            ThumbnailProfiles.GridMedium);
    }
    await peak.DisposeAsync();
    peakWorkingSetBytes = peak.PeakWorkingSetBytes;
    peakAdditionalWorkingSetBytes = peak.PeakAdditionalWorkingSetBytes;

    File.Delete(sourcePath);

    using (recorder.Measure(CoreMetricNames.ThumbnailCacheHit))
    {
        for (var index = 0; index < 1000; index++)
        {
            var hit = await pipeline.RequestAsync(
                firstSource,
                ThumbnailProfiles.GridMedium);

            if (!hit.CacheHit)
            {
                throw new InvalidOperationException("Warm benchmark unexpectedly missed cache.");
            }
        }
    }

    using (var blank = NetVips.Image.Black(2400, 1600, bands: 3))
    using (var source = blank.Copy(interpretation: Enums.Interpretation.Srgb))
    {
        source.WriteToFile(sourcePath + "[Q=90]");
    }

    info.Refresh();

    var batchPeak = PeakWorkingSetMonitor.Start();
    using (recorder.Measure(CoreMetricNames.ThumbnailBatchGenerate))
    {
        var tasks = new Task<ThumbnailResult>[requestCount];
        for (var index = 0; index < requestCount; index++)
        {
            var request = new ThumbnailSource(
                10_000 + index,
                1,
                sourcePath,
                info.Length,
                info.LastWriteTimeUtc.Ticks);

            tasks[index] = pipeline.RequestAsync(
                request,
                ThumbnailProfiles.GridSmall,
                index < 8
                    ? ThumbnailPriority.Foreground
                    : ThumbnailPriority.Background);
        }

        await Task.WhenAll(tasks);
    }
    await batchPeak.DisposeAsync();
    batchPeakWorkingSetBytes = batchPeak.PeakWorkingSetBytes;
    batchPeakAdditionalWorkingSetBytes = batchPeak.PeakAdditionalWorkingSetBytes;

    var diagnostics = pipeline.Diagnostics;
    var stats = await cache.GetStatsAsync();
    cacheBytes = stats.TotalBytes;
    cacheFiles = stats.FileCount;

    await recorder.WriteJsonAsync(
        output,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kind"] = "image-thumbnail-core",
            ["batch_request_count"] = requestCount.ToString(CultureInfo.InvariantCulture),
            ["worker_count"] = pipeline.WorkerCount.ToString(CultureInfo.InvariantCulture),
            ["queue_capacity"] = pipeline.QueueCapacity.ToString(CultureInfo.InvariantCulture),
            ["cache_hits"] = diagnostics.CacheHits.ToString(CultureInfo.InvariantCulture),
            ["cache_misses"] = diagnostics.CacheMisses.ToString(CultureInfo.InvariantCulture),
            ["generated"] = diagnostics.Generated.ToString(CultureInfo.InvariantCulture),
            ["source_opens"] = diagnostics.SourceOpens.ToString(CultureInfo.InvariantCulture),
            ["cache_files"] = cacheFiles.ToString(CultureInfo.InvariantCulture),
            ["cache_bytes"] = cacheBytes.ToString(CultureInfo.InvariantCulture),
            ["peak_working_set_bytes"] = peakWorkingSetBytes.ToString(CultureInfo.InvariantCulture),
            ["peak_additional_working_set_bytes"] = peakAdditionalWorkingSetBytes.ToString(CultureInfo.InvariantCulture),
            ["batch_peak_working_set_bytes"] = batchPeakWorkingSetBytes.ToString(CultureInfo.InvariantCulture),
            ["batch_peak_additional_working_set_bytes"] = batchPeakAdditionalWorkingSetBytes.ToString(CultureInfo.InvariantCulture),
            ["vips_tracked_mem_highwater_bytes"] = NetVips.Stats.MemHighwater.ToString(CultureInfo.InvariantCulture),
            ["vips_open_files"] = NetVips.Stats.Files.ToString(CultureInfo.InvariantCulture),
            ["vips_operation_cache_size"] = NetVips.Cache.Size.ToString(CultureInfo.InvariantCulture),
            ["vips_concurrency"] = NetVips.NetVips.Concurrency.ToString(CultureInfo.InvariantCulture),
            ["cache_format"] = "webp"
        });

    Console.WriteLine($"Image benchmark: batch={requestCount}, cache files={cacheFiles}, cache bytes={cacheBytes}");
    Console.WriteLine($"Result: {Path.GetFullPath(output)}");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}
