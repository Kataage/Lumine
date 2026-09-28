using System.Globalization;

namespace Lumine.Core;

public sealed record ResourcePolicySettings
{
    public int? ThumbnailWorkerCount { get; init; }

    public int? ThumbnailQueueCapacity { get; init; }

    public int? ThumbnailForegroundBurst { get; init; }

    public int? DecodedThumbnailEntryLimit { get; init; }

    public long? DecodedThumbnailByteLimit { get; init; }

    public int? DetailPreviewEntryLimit { get; init; }

    public long? DetailPreviewByteLimit { get; init; }

    public long? DetailOriginalByteLimit { get; init; }

    public long? ThumbnailCacheByteLimit { get; init; }

    public long? VipsTrackedMemoryLimitBytes { get; init; }
}

public sealed record CoreResourcePolicy
{
    public const int MaxThumbnailWorkerCount = 8;
    public const int MaxThumbnailQueueCapacity = 4096;
    public const int MaxThumbnailForegroundBurst = 64;
    public const int MaxDecodedThumbnailEntryLimit = 512;
    public const long MaxDecodedThumbnailByteLimit = 1024L * 1024 * 1024;
    public const int MaxDetailPreviewEntryLimit = 32;
    public const long MaxDetailPreviewByteLimit = 1024L * 1024 * 1024;
    public const long MaxDetailOriginalByteLimit = 2L * 1024 * 1024 * 1024;
    public const long MaxThumbnailCacheByteLimit = 256L * 1024 * 1024 * 1024;
    public const long MaxVipsTrackedMemoryLimitBytes = 512L * 1024 * 1024;

    private const int DefaultThumbnailQueueCapacity = 256;
    private const int DefaultThumbnailForegroundBurst = 8;
    private const int DefaultDecodedThumbnailEntryLimit = 96;
    private const long DefaultDecodedThumbnailByteLimit = 96L * 1024 * 1024;
    private const int DefaultDetailPreviewEntryLimit = 4;
    private const long DefaultDetailPreviewByteLimit = 48L * 1024 * 1024;
    private const long DefaultDetailOriginalByteLimit = 256L * 1024 * 1024;
    private const long DefaultThumbnailCacheByteLimit = 8L * 1024 * 1024 * 1024;
    private const long DefaultVipsTrackedMemoryLimitBytes = 64L * 1024 * 1024;

    private CoreResourcePolicy(
        int processorCount,
        int thumbnailWorkerCount,
        int thumbnailQueueCapacity,
        int thumbnailForegroundBurst,
        int decodedThumbnailEntryLimit,
        long decodedThumbnailByteLimit,
        int detailPreviewEntryLimit,
        long detailPreviewByteLimit,
        long detailOriginalByteLimit,
        long thumbnailCacheByteLimit,
        long vipsTrackedMemoryLimitBytes,
        int vipsConcurrency)
    {
        ProcessorCount = processorCount;
        ThumbnailWorkerCount = thumbnailWorkerCount;
        ThumbnailQueueCapacity = thumbnailQueueCapacity;
        ThumbnailForegroundBurst = thumbnailForegroundBurst;
        DecodedThumbnailEntryLimit = decodedThumbnailEntryLimit;
        DecodedThumbnailByteLimit = decodedThumbnailByteLimit;
        DetailPreviewEntryLimit = detailPreviewEntryLimit;
        DetailPreviewByteLimit = detailPreviewByteLimit;
        DetailOriginalByteLimit = detailOriginalByteLimit;
        ThumbnailCacheByteLimit = thumbnailCacheByteLimit;
        VipsTrackedMemoryLimitBytes = vipsTrackedMemoryLimitBytes;
        VipsConcurrency = vipsConcurrency;
    }

    public static CoreResourcePolicy Default { get; } = Resolve();

    public int ProcessorCount { get; }

    public int ThumbnailWorkerCount { get; }

    public int ThumbnailQueueCapacity { get; }

    public int ThumbnailForegroundBurst { get; }

    public int DecodedThumbnailEntryLimit { get; }

    public long DecodedThumbnailByteLimit { get; }

    public int DetailPreviewEntryLimit { get; }

    public long DetailPreviewByteLimit { get; }

    public long DetailOriginalByteLimit { get; }

    public long ThumbnailCacheByteLimit { get; }

    public long VipsTrackedMemoryLimitBytes { get; }

    public int VipsConcurrency { get; }

    public IReadOnlyDictionary<string, string> ToDiagnosticMetadata() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["resource.processor_count"] =
                ProcessorCount.ToString(CultureInfo.InvariantCulture),
            ["resource.thumbnail_workers"] =
                ThumbnailWorkerCount.ToString(CultureInfo.InvariantCulture),
            ["resource.thumbnail_queue_capacity"] =
                ThumbnailQueueCapacity.ToString(CultureInfo.InvariantCulture),
            ["resource.thumbnail_foreground_burst"] =
                ThumbnailForegroundBurst.ToString(CultureInfo.InvariantCulture),
            ["resource.decoded_thumbnail_entries"] =
                DecodedThumbnailEntryLimit.ToString(CultureInfo.InvariantCulture),
            ["resource.decoded_thumbnail_bytes"] =
                DecodedThumbnailByteLimit.ToString(CultureInfo.InvariantCulture),
            ["resource.detail_preview_entries"] =
                DetailPreviewEntryLimit.ToString(CultureInfo.InvariantCulture),
            ["resource.detail_preview_bytes"] =
                DetailPreviewByteLimit.ToString(CultureInfo.InvariantCulture),
            ["resource.detail_original_bytes"] =
                DetailOriginalByteLimit.ToString(CultureInfo.InvariantCulture),
            ["resource.thumbnail_cache_bytes"] =
                ThumbnailCacheByteLimit.ToString(CultureInfo.InvariantCulture),
            ["resource.vips_tracked_memory_bytes"] =
                VipsTrackedMemoryLimitBytes.ToString(CultureInfo.InvariantCulture),
            ["resource.vips_concurrency"] =
                VipsConcurrency.ToString(CultureInfo.InvariantCulture)
        };

    public static CoreResourcePolicy Resolve(
        ResourcePolicySettings? settings = null,
        int? processorCount = null)
    {
        settings ??= new ResourcePolicySettings();

        var processors = processorCount ?? Environment.ProcessorCount;
        if (processors <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processorCount),
                "Processor count must be positive.");
        }

        var defaultWorkers = Math.Clamp(
            processors / 2,
            1,
            4);
        var workers = ResolveInt(
            settings.ThumbnailWorkerCount,
            defaultWorkers,
            1,
            MaxThumbnailWorkerCount,
            nameof(settings.ThumbnailWorkerCount));
        var queueCapacity = ResolveInt(
            settings.ThumbnailQueueCapacity,
            DefaultThumbnailQueueCapacity,
            1,
            MaxThumbnailQueueCapacity,
            nameof(settings.ThumbnailQueueCapacity));
        var foregroundBurst = ResolveInt(
            settings.ThumbnailForegroundBurst,
            DefaultThumbnailForegroundBurst,
            1,
            MaxThumbnailForegroundBurst,
            nameof(settings.ThumbnailForegroundBurst));
        var decodedEntries = ResolveInt(
            settings.DecodedThumbnailEntryLimit,
            DefaultDecodedThumbnailEntryLimit,
            1,
            MaxDecodedThumbnailEntryLimit,
            nameof(settings.DecodedThumbnailEntryLimit));
        var decodedBytes = ResolveLong(
            settings.DecodedThumbnailByteLimit,
            DefaultDecodedThumbnailByteLimit,
            4L * 1024 * 1024,
            MaxDecodedThumbnailByteLimit,
            nameof(settings.DecodedThumbnailByteLimit));
        var previewEntries = ResolveInt(
            settings.DetailPreviewEntryLimit,
            DefaultDetailPreviewEntryLimit,
            1,
            MaxDetailPreviewEntryLimit,
            nameof(settings.DetailPreviewEntryLimit));
        var previewBytes = ResolveLong(
            settings.DetailPreviewByteLimit,
            DefaultDetailPreviewByteLimit,
            4L * 1024 * 1024,
            MaxDetailPreviewByteLimit,
            nameof(settings.DetailPreviewByteLimit));
        var originalBytes = ResolveLong(
            settings.DetailOriginalByteLimit,
            DefaultDetailOriginalByteLimit,
            16L * 1024 * 1024,
            MaxDetailOriginalByteLimit,
            nameof(settings.DetailOriginalByteLimit));
        var cacheBytes = ResolveLong(
            settings.ThumbnailCacheByteLimit,
            DefaultThumbnailCacheByteLimit,
            64L * 1024 * 1024,
            MaxThumbnailCacheByteLimit,
            nameof(settings.ThumbnailCacheByteLimit));
        var vipsTrackedBytes = ResolveLong(
            settings.VipsTrackedMemoryLimitBytes,
            DefaultVipsTrackedMemoryLimitBytes,
            4L * 1024 * 1024,
            MaxVipsTrackedMemoryLimitBytes,
            nameof(settings.VipsTrackedMemoryLimitBytes));

        var vipsConcurrency = Math.Clamp(
            checked((int)(
                ((long)processors + workers - 1)
                / workers)),
            1,
            4);

        return new CoreResourcePolicy(
            processors,
            workers,
            queueCapacity,
            foregroundBurst,
            decodedEntries,
            decodedBytes,
            previewEntries,
            previewBytes,
            originalBytes,
            cacheBytes,
            vipsTrackedBytes,
            vipsConcurrency);
    }

    private static int ResolveInt(
        int? value,
        int fallback,
        int minimum,
        int maximum,
        string parameterName)
    {
        var resolved = value ?? fallback;
        if (resolved < minimum || resolved > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Value must be between {minimum:N0} and {maximum:N0}.");
        }

        return resolved;
    }

    private static long ResolveLong(
        long? value,
        long fallback,
        long minimum,
        long maximum,
        string parameterName)
    {
        var resolved = value ?? fallback;
        if (resolved < minimum || resolved > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Value must be between {minimum:N0} and {maximum:N0} bytes.");
        }

        return resolved;
    }
}
