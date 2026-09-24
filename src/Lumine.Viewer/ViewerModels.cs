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
    long ModifiedAtUtcTicks,
    int? Width = null,
    int? Height = null,
    string? Format = null);

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
    long TileLoadFailures,
    int InFlightThumbnailRequests,
    int AttachedTiles,
    int ReadyTiles,
    int DecodedBitmapEntries,
    long DecodedBitmapBytes,
    int ActiveBitmapDecodes,
    int PeakConcurrentBitmapDecodes);

public enum ViewerDetailLoadState
{
    Empty = 0,
    LoadingPreview = 1,
    PreviewReady = 2,
    LoadingOriginal = 3,
    OriginalReady = 4,
    Error = 5
}

public sealed record ViewerDetailMetadata(
    int Width,
    int Height,
    bool HasAlpha,
    string? Format,
    long FileSize,
    long EstimatedRgbaBytes);

public sealed class ViewerOriginalBitmap : IDisposable
{
    private Avalonia.Media.Imaging.Bitmap? _bitmap;

    public ViewerOriginalBitmap(
        Avalonia.Media.Imaging.Bitmap bitmap,
        ViewerDetailMetadata metadata)
    {
        _bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    public Avalonia.Media.Imaging.Bitmap Bitmap =>
        _bitmap ?? throw new ObjectDisposedException(nameof(ViewerOriginalBitmap));

    public ViewerDetailMetadata Metadata { get; }

    public void Dispose()
    {
        var bitmap = Interlocked.Exchange(ref _bitmap, null);
        if (bitmap is null)
        {
            return;
        }

        // Avalonia composition can retain the previous Image.Source until the
        // next render commit. Disposing the platform bitmap synchronously here
        // can race that commit during rapid navigation/window teardown.
        Avalonia.Threading.Dispatcher.UIThread.Post(
            bitmap.Dispose,
            Avalonia.Threading.DispatcherPriority.Background);
    }
}

public interface IViewerDetailProvider
{
    ValueTask<ViewerThumbnail> RequestPreviewAsync(
        ViewerAsset asset,
        CancellationToken cancellationToken = default);

    ValueTask<ViewerDetailMetadata> ProbeOriginalAsync(
        ViewerAsset asset,
        CancellationToken cancellationToken = default);

    Task<ViewerOriginalBitmap> LoadOriginalAsync(
        ViewerAsset asset,
        long maxDecodedBytes,
        CancellationToken cancellationToken = default);
}

public sealed class ViewerDetailOptions
{
    public long PreviewDecodedByteLimit { get; init; } =
        48L * 1024 * 1024;

    public int PreviewDecodedEntryLimit { get; init; } = 4;

    public long OriginalDecodedByteLimit { get; init; } =
        256L * 1024 * 1024;

    public double MinZoom { get; init; } = 0.05;

    public double MaxZoom { get; init; } = 16;

    public double ZoomStep { get; init; } = 1.25;
}

public sealed record ViewerDetailSnapshot(
    long SelectedIndex,
    ViewerAsset? Asset,
    ViewerDetailMetadata? Metadata,
    ViewerDetailLoadState State,
    Avalonia.Media.Imaging.Bitmap? Bitmap,
    bool IsOriginal,
    string? ErrorMessage,
    long SelectionVersion);
