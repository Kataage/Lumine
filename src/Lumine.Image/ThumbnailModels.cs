namespace Lumine.Image;

public enum ThumbnailPriority
{
    Foreground = 0,
    Background = 1
}

public sealed record ThumbnailProfile(
    string Id,
    int MaxWidth,
    int MaxHeight,
    int Quality,
    int Version);

public static class ThumbnailProfiles
{
    public static ThumbnailProfile GridSmall { get; } =
        new("grid-small", 256, 256, 80, 1);

    public static ThumbnailProfile GridMedium { get; } =
        new("grid-medium", 512, 512, 82, 1);

    public static ThumbnailProfile DetailPreview { get; } =
        new("detail-preview", 1600, 1600, 85, 1);

    public static IReadOnlyList<ThumbnailProfile> All { get; } =
        [GridSmall, GridMedium, DetailPreview];
}

public sealed record ThumbnailSource(
    long AssetId,
    long SourceRevision,
    string SourcePath,
    long FileSize,
    long ModifiedAtUtcTicks,
    int? SourceWidth = null,
    int? SourceHeight = null,
    int? RawWidth = null,
    int? RawHeight = null,
    bool? HasAlpha = null,
    string? Format = null,
    string? ContentSha256 = null)
{
    public SourceTechnicalMetadata? PersistedMetadata =>
        SourceWidth is > 0
        && SourceHeight is > 0
        && RawWidth is > 0
        && RawHeight is > 0
        && HasAlpha.HasValue
        && !string.IsNullOrWhiteSpace(Format)
        && IsSha256(ContentSha256)
            ? new SourceTechnicalMetadata(
                SourceWidth.Value,
                SourceHeight.Value,
                RawWidth.Value,
                RawHeight.Value,
                HasAlpha.Value,
                Format!,
                ContentSha256!)
            : null;

    public ThumbnailSource WithMetadata(SourceTechnicalMetadata metadata) =>
        this with
        {
            SourceWidth = metadata.Width,
            SourceHeight = metadata.Height,
            RawWidth = metadata.RawWidth,
            RawHeight = metadata.RawHeight,
            HasAlpha = metadata.HasAlpha,
            Format = metadata.Format,
            ContentSha256 = metadata.ContentSha256
        };

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(static character => Uri.IsHexDigit(character));
}

public sealed record ThumbnailResult(
    string CacheKey,
    string CachePath,
    bool CacheHit,
    int Width,
    int Height,
    long CacheFileBytes,
    SourceTechnicalMetadata? SourceMetadata = null);

public sealed record ThumbnailCacheStats(
    long FileCount,
    long TotalBytes,
    long InterruptedWriteCount);

public sealed record ThumbnailPruneResult(
    long FilesBefore,
    long BytesBefore,
    long FilesDeleted,
    long BytesDeleted,
    long FilesAfter,
    long BytesAfter);

public readonly record struct ThumbnailDiagnosticsSnapshot(
    long CacheHits,
    long CacheMisses,
    long Generated,
    long Failed,
    long SourceOpens,
    long MetadataProbes,
    long MetadataBytesHashed);

public sealed class ThumbnailPipelineOptions
{
    public static int DefaultWorkerCount { get; } =
        Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    public int WorkerCount { get; init; } = DefaultWorkerCount;

    public int QueueCapacity { get; init; } = 256;

    public int MaxForegroundBurst { get; init; } = 8;
}
