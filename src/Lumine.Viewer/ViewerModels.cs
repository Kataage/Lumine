namespace Lumine.Viewer;

public enum ViewerThumbnailPriority
{
    Foreground = 0,
    Background = 1
}

public readonly record struct ViewerPageCursor(
    long ModifiedAtUtcTicks,
    long AssetId);

public sealed record ViewerAsset(
    long Id,
    long SourceRevision,
    string RelativePath,
    string DisplayName,
    long FileSize,
    long ModifiedAtUtcTicks);

public sealed record ViewerAssetPage(
    IReadOnlyList<ViewerAsset> Items,
    ViewerPageCursor? NextCursor);

public sealed record ViewerThumbnail(
    string CacheKey,
    string CachePath,
    int Width,
    int Height);

public interface IViewerPageSource
{
    long Count { get; }

    ValueTask<ViewerAssetPage> GetPageAsync(
        int limit,
        ViewerPageCursor? cursor = null,
        CancellationToken cancellationToken = default);
}

public interface IViewerAssetProvider
{
    long Count { get; }

    ValueTask<ViewerAsset> GetAssetAsync(
        long index,
        CancellationToken cancellationToken = default);
}

public interface IViewerThumbnailProvider
{
    ValueTask<ViewerThumbnail> RequestAsync(
        ViewerAsset asset,
        ViewerThumbnailPriority priority,
        CancellationToken cancellationToken = default);
}

public sealed class ViewerOptions
{
    public double TileWidth { get; init; } = 184;

    public double TileHeight { get; init; } = 216;

    public double TileSpacing { get; init; } = 8;

    public int PrefetchRows { get; init; } = 1;

    public TimeSpan PrefetchDelay { get; init; } = TimeSpan.FromMilliseconds(40);

    public int MetadataPageSize { get; init; } = 256;

    public int MetadataPageCacheSize { get; init; } = 8;

    public int CursorCheckpointStride { get; init; } = 16;

    public int CursorCheckpointLimit { get; init; } = 128;

    public int DecodedBitmapEntryLimit { get; init; } = 96;

    public long DecodedBitmapByteLimit { get; init; } = 96L * 1024 * 1024;
}

public readonly record struct ViewerPagingDiagnostics(
    long PagesFetched,
    int CachedPages,
    int CursorCheckpoints);

public readonly record struct ViewerRuntimeDiagnostics(
    long ThumbnailRequests,
    long ThumbnailRequestsCoalesced,
    long ThumbnailRequestsCancelled,
    long ThumbnailRequestsFailed,
    int InFlightThumbnailRequests,
    int AttachedTiles,
    int DecodedBitmapEntries,
    long DecodedBitmapBytes);
