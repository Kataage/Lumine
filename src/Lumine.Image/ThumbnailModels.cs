using Lumine.Core;

namespace Lumine.Image;

public enum ThumbnailPriority
{
    Interactive = 0,
    Foreground = 1,
    Background = 2
}

public enum ThumbnailStorageMode
{
    PersistentDisk = 0,
    MemoryOnly = 1
}

public sealed record ThumbnailProfile(
    string Id,
    int MaxWidth,
    int MaxHeight,
    int Quality,
    int Version,
    bool LinearLight = true);

public static class ThumbnailProfiles
{
    public static ThumbnailProfile GridSmall { get; } =
        new("grid-small", 256, 256, 80, 2, LinearLight: false);

    public static ThumbnailProfile GridMedium { get; } =
        new("grid-medium", 512, 512, 82, 2, LinearLight: false);

    public static ThumbnailProfile DetailPreview { get; } =
        new("detail-preview", 1600, 1600, 85, 2, LinearLight: false);

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
    string? SourceIdentity = null)
{
    public SourceTechnicalMetadata? PersistedMetadata =>
        SourceWidth is > 0
        && SourceHeight is > 0
        && RawWidth is > 0
        && RawHeight is > 0
        && HasAlpha.HasValue
        && !string.IsNullOrWhiteSpace(Format)
        && IsSourceIdentity(SourceIdentity)
            ? new SourceTechnicalMetadata(
                SourceWidth.Value,
                SourceHeight.Value,
                RawWidth.Value,
                RawHeight.Value,
                HasAlpha.Value,
                Format!,
                SourceIdentity!,
                false,
                0)
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
            SourceIdentity = metadata.SourceIdentity
        };

    private static bool IsSourceIdentity(string? value) =>
        Lumine.Core.FileSourceIdentityProbe.IsValid(value);
}

public sealed record ThumbnailResult(
    string CacheKey,
    string CachePath,
    bool CacheHit,
    int Width,
    int Height,
    long CacheFileBytes,
    SourceTechnicalMetadata? SourceMetadata = null,
    byte[]? EncodedBytes = null);

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
    long SourceOpenCancellations,
    long MetadataProbes,
    long MetadataBytesHashed,
    long MetadataMemoryHits,
    long MetadataFastIdentityHits,
    long MetadataNtfsUsnIdentityHits,
    long MetadataWindowsFileIdIdentityHits,
    long MetadataFullHashFallbacks);

public readonly record struct ThumbnailMemoryCacheStats(
    int EntryCount,
    long EncodedBytes,
    long ByteLimit,
    long HitCount);

public readonly record struct ThumbnailCacheMaintenanceDiagnosticsSnapshot(
    long RunsScheduled,
    long RunsStarted,
    long RunsCompleted,
    long RunsCancelled,
    long RunsFailed,
    long ForegroundPreemptions,
    long FilesDeleted,
    long BytesDeleted,
    long InterruptedWritesDeleted,
    long LastBytesAfter,
    string? LastError);

public sealed class ThumbnailPipelineOptions
{
    public static int DefaultWorkerCount { get; } =
        CoreResourcePolicy.Default.ThumbnailWorkerCount;

    public int WorkerCount { get; init; } =
        CoreResourcePolicy.Default.ThumbnailWorkerCount;

    public int QueueCapacity { get; init; } =
        CoreResourcePolicy.Default.ThumbnailQueueCapacity;

    public int MaxForegroundBurst { get; init; } =
        CoreResourcePolicy.Default.ThumbnailForegroundBurst;

    public TimeSpan CacheMaintenanceQuietPeriod { get; init; } =
        TimeSpan.FromSeconds(2);

    public ThumbnailStorageMode StorageMode { get; init; } =
        ThumbnailStorageMode.PersistentDisk;

    public long EncodedMemoryByteLimit { get; init; } =
        CoreResourcePolicy.Default.EncodedThumbnailMemoryByteLimit;

    public static ThumbnailPipelineOptions FromResourcePolicy(
        CoreResourcePolicy policy,
        ThumbnailStorageMode storageMode = ThumbnailStorageMode.PersistentDisk)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return new ThumbnailPipelineOptions
        {
            WorkerCount = policy.ThumbnailWorkerCount,
            QueueCapacity = policy.ThumbnailQueueCapacity,
            MaxForegroundBurst = policy.ThumbnailForegroundBurst,
            StorageMode = storageMode,
            EncodedMemoryByteLimit = policy.EncodedThumbnailMemoryByteLimit
        };
    }
}
