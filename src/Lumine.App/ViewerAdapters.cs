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
                asset.Format,
                asset.SourceContentSha256,
                asset.RawWidth,
                asset.RawHeight,
                asset.HasAlpha))
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
    private readonly LibraryService? _library;
    private readonly long _libraryId;

    public ImageViewerThumbnailProvider(
        ThumbnailPipeline pipeline,
        string libraryRoot)
        : this(
            pipeline,
            libraryRoot,
            library: null,
            libraryId: 0)
    {
    }

    public ImageViewerThumbnailProvider(
        ThumbnailPipeline pipeline,
        string libraryRoot,
        LibraryService? library,
        long libraryId)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);

        if (library is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        }

        _library = library;
        _libraryId = libraryId;
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
            ViewerImageMetadataBridge.CreateThumbnailSource(
                asset,
                sourcePath),
            ThumbnailProfiles.GridSmall,
            priority == ViewerThumbnailPriority.Foreground
                ? ThumbnailPriority.Foreground
                : ThumbnailPriority.Background,
            cancellationToken).ConfigureAwait(false);

        await ViewerImageMetadataBridge.PersistAsync(
            _library,
            _libraryId,
            asset,
            sourcePath,
            result.SourceMetadata,
            cancellationToken).ConfigureAwait(false);

        return new ViewerThumbnail(
            result.CacheKey,
            result.CachePath,
            result.Width,
            result.Height,
            ViewerImageMetadataBridge.ToViewerMetadata(
                result.SourceMetadata));
    }
}

internal sealed class ImageViewerDetailProvider : IViewerDetailProvider
{
    private readonly ThumbnailPipeline _thumbnailPipeline;
    private readonly string _libraryRoot;
    private readonly string _libraryRootPrefix;
    private readonly LibraryService? _library;
    private readonly long _libraryId;

    public ImageViewerDetailProvider(
        ThumbnailPipeline thumbnailPipeline,
        string libraryRoot)
        : this(
            thumbnailPipeline,
            libraryRoot,
            library: null,
            libraryId: 0)
    {
    }

    public ImageViewerDetailProvider(
        ThumbnailPipeline thumbnailPipeline,
        string libraryRoot,
        LibraryService? library,
        long libraryId)
    {
        _thumbnailPipeline = thumbnailPipeline
            ?? throw new ArgumentNullException(nameof(thumbnailPipeline));
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);

        if (library is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        }

        _library = library;
        _libraryId = libraryId;
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
            ViewerImageMetadataBridge.CreateThumbnailSource(
                asset,
                sourcePath),
            ThumbnailProfiles.DetailPreview,
            ThumbnailPriority.Foreground,
            cancellationToken).ConfigureAwait(false);

        await ViewerImageMetadataBridge.PersistAsync(
            _library,
            _libraryId,
            asset,
            sourcePath,
            result.SourceMetadata,
            cancellationToken).ConfigureAwait(false);

        return new ViewerThumbnail(
            result.CacheKey,
            result.CachePath,
            result.Width,
            result.Height,
            ViewerImageMetadataBridge.ToViewerMetadata(
                result.SourceMetadata));
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
            asset.ModifiedAtUtcTicks,
            asset.SourceContentSha256);

        var persisted = asset.PersistedSourceMetadata;
        var info = await FullResolutionDecoder.ProbeAsync(
            source,
            cancellationToken).ConfigureAwait(false);

        if (info.ContentSha256 is not null
            && info.RawWidth is > 0
            && info.RawHeight is > 0
            && !string.IsNullOrWhiteSpace(info.Format))
        {
            await ViewerImageMetadataBridge.PersistAsync(
                _library,
                _libraryId,
                asset,
                sourcePath,
                new SourceTechnicalMetadata(
                    info.Width,
                    info.Height,
                    info.RawWidth.Value,
                    info.RawHeight.Value,
                    info.HasAlpha,
                    info.Format,
                    info.ContentSha256),
                cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (info.EstimatedRgbaBytes > maxDecodedBytes)
        {
            throw new FullResolutionBudgetExceededException(
                info.Width,
                info.Height,
                info.EstimatedRgbaBytes,
                maxDecodedBytes);
        }

        var bitmap = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () => CreateOriginalBitmap(info, cancellationToken));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await FullResolutionDecoder.DecodeAsync(
                source,
                maxDecodedBytes,
                stripe =>
                {
                    var expectedRowBytes = checked(info.Width * 4);
                    var endY = checked(stripe.Y + stripe.Height);

                    if (stripe.Y < 0
                        || stripe.Height <= 0
                        || stripe.Width != info.Width
                        || stripe.RowBytes != expectedRowBytes
                        || endY > info.Height)
                    {
                        throw new InvalidOperationException(
                            $"Full-resolution stripe escaped the probed bitmap bounds: stripe={stripe.Width}x{stripe.Height}@{stripe.Y}, rowBytes={stripe.RowBytes}; bitmap={info.Width}x{info.Height}, rowBytes={expectedRowBytes}.");
                    }

                    using var framebuffer = bitmap.Lock();
                    if (framebuffer.RowBytes < stripe.RowBytes)
                    {
                        throw new InvalidOperationException(
                            $"Full-resolution framebuffer stride {framebuffer.RowBytes} is smaller than stripe stride {stripe.RowBytes}.");
                    }

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
                expectedInfo: info,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return new ViewerOriginalBitmap(
                bitmap,
                new ViewerDetailMetadata(
                    info.Width,
                    info.Height,
                    info.HasAlpha,
                    info.Format
                    ?? asset.Format
                    ?? Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant(),
                    asset.FileSize,
                    info.EstimatedRgbaBytes));
        }
        catch
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => bitmap.Dispose());
            throw;
        }
    }

    internal static Avalonia.Media.Imaging.WriteableBitmap CreateOriginalBitmap(
        FullResolutionInfo info,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);
        cancellationToken.ThrowIfCancellationRequested();

        return new Avalonia.Media.Imaging.WriteableBitmap(
            new Avalonia.PixelSize(info.Width, info.Height),
            new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormats.Rgba8888,
            Avalonia.Platform.AlphaFormat.Unpremul);
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


internal static class ViewerImageMetadataBridge
{
    public static ThumbnailSource CreateThumbnailSource(
        ViewerAsset asset,
        string sourcePath)
    {
        var metadata = asset.PersistedSourceMetadata;

        return new ThumbnailSource(
            asset.Id,
            asset.SourceRevision,
            sourcePath,
            asset.FileSize,
            asset.ModifiedAtUtcTicks,
            metadata?.Width,
            metadata?.Height,
            metadata?.RawWidth,
            metadata?.RawHeight,
            metadata?.HasAlpha,
            metadata?.Format,
            metadata?.ContentSha256);
    }

    public static ViewerSourceTechnicalMetadata? ToViewerMetadata(
        SourceTechnicalMetadata? metadata) =>
        metadata is null
            ? null
            : new ViewerSourceTechnicalMetadata(
                metadata.Width,
                metadata.Height,
                metadata.RawWidth,
                metadata.RawHeight,
                metadata.HasAlpha,
                metadata.Format,
                metadata.ContentSha256);

    public static async ValueTask PersistAsync(
        LibraryService? library,
        long libraryId,
        ViewerAsset asset,
        string sourcePath,
        SourceTechnicalMetadata? metadata,
        CancellationToken cancellationToken)
    {
        if (library is null || metadata is null)
        {
            return;
        }

        var existing = asset.PersistedSourceMetadata;
        if (existing is not null
            && existing.Width == metadata.Width
            && existing.Height == metadata.Height
            && existing.RawWidth == metadata.RawWidth
            && existing.RawHeight == metadata.RawHeight
            && existing.HasAlpha == metadata.HasAlpha
            && string.Equals(
                existing.Format,
                metadata.Format,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                existing.ContentSha256,
                metadata.ContentSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var persisted = await library.UpdateTechnicalMetadataAsync(
            libraryId,
            asset.Id,
            asset.SourceRevision,
            asset.FileSize,
            asset.ModifiedAtUtcTicks,
            new AssetTechnicalMetadata(
                metadata.Width,
                metadata.Height,
                metadata.RawWidth,
                metadata.RawHeight,
                metadata.HasAlpha,
                metadata.Format,
                metadata.ContentSha256),
            cancellationToken).ConfigureAwait(false);

        if (!persisted)
        {
            throw new ImageSourceChangedException(
                sourcePath,
                "Library source revision advanced before technical metadata could be committed.");
        }
    }
}
