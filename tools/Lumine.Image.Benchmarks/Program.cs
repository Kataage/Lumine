using System.Globalization;
using Lumine.Core;
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

var root = Path.Combine(
    Path.GetTempPath(),
    $"lumine-image-benchmark-{Guid.NewGuid():N}");
var sourcePath = Path.Combine(root, "source.jpg");
var cacheRoot = Path.Combine(root, "cache");
var probeRoot = Path.Combine(root, "metadata-probes");
var probeTemplatePath = Path.Combine(root, "probe-template.jpg");
Directory.CreateDirectory(root);
Directory.CreateDirectory(probeRoot);

var recorder = new BenchmarkRecorder();
long peakWorkingSetBytes = 0;
long peakAdditionalWorkingSetBytes = 0;
long batchPeakWorkingSetBytes = 0;
long batchPeakAdditionalWorkingSetBytes = 0;
long cacheBytes = 0;
long cacheFiles = 0;
const int metadataProbeFixtureCount = 10_000;
long metadataProbeFixtureBytes = 0;
long metadataProbeFixtureBytesHashed = 0;
var metadataProbeFixtureFastIdentityHits = 0;
var metadataProbeFixtureFullHashFallbacks = 0;

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
    ThumbnailResult firstResult;
    using (recorder.Measure(CoreMetricNames.ThumbnailGenerate))
    {
        firstResult = await pipeline.RequestAsync(
            firstSource,
            ThumbnailProfiles.GridMedium);
    }

    _ = firstResult.SourceMetadata
        ?? throw new InvalidOperationException(
            "Cold thumbnail generation did not return source metadata.");

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
                throw new InvalidOperationException(
                    "Warm benchmark unexpectedly missed cache.");
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
    batchPeakAdditionalWorkingSetBytes =
        batchPeak.PeakAdditionalWorkingSetBytes;

    // Build 10,000 distinct image files outside the measured section. The
    // benchmark then measures real per-file open/header/identity overhead
    // instead of repeatedly hitting one already-hot source file.
    using (var blank = NetVips.Image.Black(512, 384, bands: 3))
    using (var values = blank.NewFromImage([55, 120, 205]))
    using (var probe = values.Copy(interpretation: Enums.Interpretation.Srgb))
    {
        probe.Jpegsave(
            probeTemplatePath,
            q: 90,
            keep: Enums.ForeignKeep.None);
    }

    for (var index = 0; index < metadataProbeFixtureCount; index++)
    {
        File.Copy(
            probeTemplatePath,
            Path.Combine(
                probeRoot,
                $"probe-{index:D5}.jpg"));
    }

    using (recorder.Measure(CoreMetricNames.SourceMetadataProbeBatch))
    {
        for (var index = 0; index < metadataProbeFixtureCount; index++)
        {
            var path = Path.Combine(
                probeRoot,
                $"probe-{index:D5}.jpg");
            var probeInfo = new FileInfo(path);

            using var snapshot = await ImageSourceSnapshot.OpenAsync(
                path,
                probeInfo.Length,
                probeInfo.LastWriteTimeUtc.Ticks);

            if (snapshot.Metadata.Width != 512
                || snapshot.Metadata.Height != 384
                || !string.Equals(
                    snapshot.Metadata.Format,
                    "jpeg",
                    StringComparison.Ordinal)
                || !FileSourceIdentityProbe.IsValid(
                    snapshot.Metadata.SourceIdentity))
            {
                throw new InvalidOperationException(
                    "Source metadata probe benchmark returned invalid metadata.");
            }

            metadataProbeFixtureBytes += probeInfo.Length;
            metadataProbeFixtureBytesHashed += snapshot.Metadata.BytesHashed;

            if (snapshot.Metadata.UsedFullHash)
            {
                metadataProbeFixtureFullHashFallbacks++;
            }
            else
            {
                metadataProbeFixtureFastIdentityHits++;
            }
        }
    }

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
            ["metadata_probes"] = diagnostics.MetadataProbes.ToString(CultureInfo.InvariantCulture),
            ["metadata_bytes_hashed"] = diagnostics.MetadataBytesHashed.ToString(CultureInfo.InvariantCulture),
            ["metadata_memory_hits"] = diagnostics.MetadataMemoryHits.ToString(CultureInfo.InvariantCulture),
            ["metadata_fast_identity_hits"] = diagnostics.MetadataFastIdentityHits.ToString(CultureInfo.InvariantCulture),
            ["metadata_full_hash_fallbacks"] = diagnostics.MetadataFullHashFallbacks.ToString(CultureInfo.InvariantCulture),
            ["metadata_probe_fixture_count"] = metadataProbeFixtureCount.ToString(CultureInfo.InvariantCulture),
            ["metadata_probe_fixture_bytes"] = metadataProbeFixtureBytes.ToString(CultureInfo.InvariantCulture),
            ["metadata_probe_fixture_fast_identity_hits"] = metadataProbeFixtureFastIdentityHits.ToString(CultureInfo.InvariantCulture),
            ["metadata_probe_fixture_full_hash_fallbacks"] = metadataProbeFixtureFullHashFallbacks.ToString(CultureInfo.InvariantCulture),
            ["metadata_probe_fixture_bytes_hashed"] = metadataProbeFixtureBytesHashed.ToString(CultureInfo.InvariantCulture),
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

    Console.WriteLine(
        $"Image benchmark: batch={requestCount}, cache files={cacheFiles}, cache bytes={cacheBytes}");
    Console.WriteLine(
        $"10k distinct metadata probes: fast={metadataProbeFixtureFastIdentityHits}, hash-fallback={metadataProbeFixtureFullHashFallbacks}, logical-bytes={metadataProbeFixtureBytes:N0}, hashed-bytes={metadataProbeFixtureBytesHashed:N0}");
    Console.WriteLine($"Result: {Path.GetFullPath(output)}");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}
