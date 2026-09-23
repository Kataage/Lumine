using Lumine.Image;
using Lumine.Library;
using Lumine.Viewer;

namespace Lumine.App;

internal sealed class LibraryViewerPageSource : IViewerPageSource
{
    private readonly LibraryService _library;
    private readonly long _libraryId;

    public LibraryViewerPageSource(
        LibraryService library,
        long libraryId,
        long assetCount)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        ArgumentOutOfRangeException.ThrowIfNegative(libraryId);
        ArgumentOutOfRangeException.ThrowIfNegative(assetCount);

        _libraryId = libraryId;
        Count = assetCount;
    }

    public long Count { get; }

    public async ValueTask<ViewerAssetPage> GetPageAsync(
        int limit,
        ViewerPageCursor? cursor = null,
        CancellationToken cancellationToken = default)
    {
        AssetCursor? libraryCursor = cursor is { } value
            ? new AssetCursor(value.ModifiedAtUtcTicks, value.AssetId)
            : null;

        var page = await _library.GetAssetPageAsync(
            _libraryId,
            limit,
            libraryCursor,
            cancellationToken).ConfigureAwait(false);

        var items = page.Items
            .Select(static asset => new ViewerAsset(
                asset.Id,
                asset.SourceRevision,
                asset.RelativePath,
                asset.FileName,
                asset.FileSize,
                asset.ModifiedAtUtc.UtcDateTime.Ticks))
            .ToArray();

        ViewerPageCursor? next = page.NextCursor is { } nextCursor
            ? new ViewerPageCursor(
                nextCursor.ModifiedAtUtcTicks,
                nextCursor.Id)
            : null;

        return new ViewerAssetPage(items, next);
    }
}

internal sealed class ImageViewerThumbnailProvider : IViewerThumbnailProvider
{
    private readonly ThumbnailPipeline _pipeline;
    private readonly string _libraryRoot;
    private readonly string _libraryRootPrefix;

    public ImageViewerThumbnailProvider(
        ThumbnailPipeline pipeline,
        string libraryRoot)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);

        _libraryRoot = Path.GetFullPath(libraryRoot);
        _libraryRootPrefix =
            _libraryRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    public async ValueTask<ViewerThumbnail> RequestAsync(
        ViewerAsset asset,
        ViewerThumbnailPriority priority,
        CancellationToken cancellationToken = default)
    {
        var relative = asset.RelativePath.Replace(
            '/',
            Path.DirectorySeparatorChar);
        var sourcePath = Path.GetFullPath(
            Path.Combine(_libraryRoot, relative));

        if (!sourcePath.StartsWith(
                _libraryRootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Viewer asset escaped registered library root: {asset.RelativePath}");
        }

        var result = await _pipeline.RequestAsync(
            new ThumbnailSource(
                asset.Id,
                asset.SourceRevision,
                sourcePath,
                asset.FileSize,
                asset.ModifiedAtUtcTicks),
            ThumbnailProfiles.GridSmall,
            priority == ViewerThumbnailPriority.Foreground
                ? ThumbnailPriority.Foreground
                : ThumbnailPriority.Background,
            cancellationToken).ConfigureAwait(false);

        return new ViewerThumbnail(
            result.CacheKey,
            result.CachePath,
            result.Width,
            result.Height);
    }
}
