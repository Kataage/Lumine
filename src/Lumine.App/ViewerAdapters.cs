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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
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
                asset.ModifiedAtUtc.UtcDateTime.Ticks,
                asset.Width,
                asset.Height,
                asset.Format))
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

internal sealed class ImageViewerDetailProvider : IViewerDetailProvider
{
    private readonly ThumbnailPipeline _thumbnailPipeline;
    private readonly string _libraryRoot;
    private readonly string _libraryRootPrefix;

    public ImageViewerDetailProvider(
        ThumbnailPipeline thumbnailPipeline,
        string libraryRoot)
    {
        _thumbnailPipeline = thumbnailPipeline
            ?? throw new ArgumentNullException(nameof(thumbnailPipeline));
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);

        _libraryRoot = Path.GetFullPath(libraryRoot);
        _libraryRootPrefix =
            _libraryRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    public async ValueTask<ViewerThumbnail> RequestPreviewAsync(
        ViewerAsset asset,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = ResolveSourcePath(asset);
        var result = await _thumbnailPipeline.RequestAsync(
            new ThumbnailSource(
                asset.Id,
                asset.SourceRevision,
                sourcePath,
                asset.FileSize,
                asset.ModifiedAtUtcTicks),
            ThumbnailProfiles.DetailPreview,
            ThumbnailPriority.Foreground,
            cancellationToken).ConfigureAwait(false);

        return new ViewerThumbnail(
            result.CacheKey,
            result.CachePath,
            result.Width,
            result.Height);
    }

    public async ValueTask<ViewerDetailMetadata> ProbeOriginalAsync(
        ViewerAsset asset,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = ResolveSourcePath(asset);
        var info = await FullResolutionDecoder.ProbeAsync(
            new FullResolutionSource(
                sourcePath,
                asset.FileSize,
                asset.ModifiedAtUtcTicks),
            cancellationToken).ConfigureAwait(false);

        return new ViewerDetailMetadata(
            info.Width,
            info.Height,
            info.HasAlpha,
            asset.Format ?? Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant(),
            asset.FileSize,
            info.EstimatedRgbaBytes);
    }

    public async Task<ViewerOriginalBitmap> LoadOriginalAsync(
        ViewerAsset asset,
        long maxDecodedBytes,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = ResolveSourcePath(asset);
        var source = new FullResolutionSource(
            sourcePath,
            asset.FileSize,
            asset.ModifiedAtUtcTicks);
        var info = await FullResolutionDecoder.ProbeAsync(
            source,
            cancellationToken).ConfigureAwait(false);

        if (info.EstimatedRgbaBytes > maxDecodedBytes)
        {
            throw new FullResolutionBudgetExceededException(
                info.Width,
                info.Height,
                info.EstimatedRgbaBytes,
                maxDecodedBytes);
        }

        var bitmap = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () => new Avalonia.Media.Imaging.WriteableBitmap(
                new Avalonia.PixelSize(info.Width, info.Height),
                new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormats.Rgba8888,
                Avalonia.Platform.AlphaFormat.Unpremul));

        try
        {
            await FullResolutionDecoder.DecodeAsync(
                source,
                maxDecodedBytes,
                stripe =>
                {
                    using var framebuffer = bitmap.Lock();

                    for (var row = 0; row < stripe.Height; row++)
                    {
                        var destination = IntPtr.Add(
                            framebuffer.Address,
                            checked((stripe.Y + row) * framebuffer.RowBytes));
                        System.Runtime.InteropServices.Marshal.Copy(
                            stripe.RgbaBytes,
                            checked(row * stripe.RowBytes),
                            destination,
                            stripe.RowBytes);
                    }
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return new ViewerOriginalBitmap(
                bitmap,
                new ViewerDetailMetadata(
                    info.Width,
                    info.Height,
                    info.HasAlpha,
                    asset.Format ?? Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant(),
                    asset.FileSize,
                    info.EstimatedRgbaBytes));
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private string ResolveSourcePath(ViewerAsset asset)
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

        return sourcePath;
    }
}
