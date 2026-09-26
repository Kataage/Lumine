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
                asset.SourceIdentity,
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

        var (result, effectiveAsset) =
            await ViewerImageMetadataBridge.RequestThumbnailWithRepairAsync(
                _pipeline,
                _library,
                _libraryId,
                asset,
                sourcePath,
                ThumbnailProfiles.GridSmall,
                priority == ViewerThumbnailPriority.Foreground
                    ? ThumbnailPriority.Foreground
                    : ThumbnailPriority.Background,
                cancellationToken).ConfigureAwait(false);

        return new ViewerThumbnail(
            result.CacheKey,
            result.CachePath,
            result.Width,
            result.Height,
            ViewerImageMetadataBridge.ToViewerMetadata(
                result.SourceMetadata,
                effectiveAsset.SourceRevision));
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
        var (result, effectiveAsset) =
            await ViewerImageMetadataBridge.RequestThumbnailWithRepairAsync(
                _thumbnailPipeline,
                _library,
                _libraryId,
                asset,
                sourcePath,
                ThumbnailProfiles.DetailPreview,
                ThumbnailPriority.Foreground,
                cancellationToken).ConfigureAwait(false);

        return new ViewerThumbnail(
            result.CacheKey,
            result.CachePath,
            result.Width,
            result.Height,
            ViewerImageMetadataBridge.ToViewerMetadata(
                result.SourceMetadata,
                effectiveAsset.SourceRevision));
    }

    public async Task<ViewerOriginalBitmap> LoadOriginalAsync(
        ViewerAsset asset,
        long maxDecodedBytes,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = ResolveSourcePath(asset);
        var (source, info, effectiveAsset) =
            await ViewerImageMetadataBridge.ProbeOriginalWithRepairAsync(
                _library,
                _libraryId,
                asset,
                sourcePath,
                cancellationToken).ConfigureAwait(false);

        if (info.SourceIdentity is not null
            && info.RawWidth is > 0
            && info.RawHeight is > 0
            && !string.IsNullOrWhiteSpace(info.Format))
        {
            await ViewerImageMetadataBridge.PersistAsync(
                _library,
                _libraryId,
                effectiveAsset,
                sourcePath,
                new SourceTechnicalMetadata(
                    info.Width,
                    info.Height,
                    info.RawWidth.Value,
                    info.RawHeight.Value,
                    info.HasAlpha,
                    info.Format,
                    info.SourceIdentity,
                    false,
                    0),
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
                    ?? effectiveAsset.Format
                    ?? Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant(),
                    effectiveAsset.FileSize,
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
    private const int PersistedMetadataCacheLimit = 8192;

    private static readonly object PersistedMetadataGate = new();
    private static readonly Dictionary<PersistedMetadataKey, LinkedListNode<PersistedMetadataKey>>
        PersistedMetadata = [];
    private static readonly LinkedList<PersistedMetadataKey> PersistedMetadataLru = [];
    private static long _metadataPersistenceWrites;

    internal static long MetadataPersistenceWrites =>
        Interlocked.Read(ref _metadataPersistenceWrites);

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
            metadata?.SourceIdentity);
    }

    public static ViewerSourceTechnicalMetadata? ToViewerMetadata(
        SourceTechnicalMetadata? metadata,
        long sourceRevision) =>
        metadata is null
            ? null
            : new ViewerSourceTechnicalMetadata(
                sourceRevision,
                metadata.Width,
                metadata.Height,
                metadata.RawWidth,
                metadata.RawHeight,
                metadata.HasAlpha,
                metadata.Format,
                metadata.SourceIdentity);

    public static async ValueTask<(ThumbnailResult Result, ViewerAsset Asset)>
        RequestThumbnailWithRepairAsync(
            ThumbnailPipeline pipeline,
            LibraryService? library,
            long libraryId,
            ViewerAsset asset,
            string sourcePath,
            ThumbnailProfile profile,
            ThumbnailPriority priority,
            CancellationToken cancellationToken)
    {
        var current = asset;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var result = await pipeline.RequestAsync(
                    CreateThumbnailSource(current, sourcePath),
                    profile,
                    priority,
                    cancellationToken).ConfigureAwait(false);

                await PersistAsync(
                    library,
                    libraryId,
                    current,
                    sourcePath,
                    result.SourceMetadata,
                    cancellationToken).ConfigureAwait(false);

                return (result, current);
            }
            catch (ImageSourceChangedException)
                when (library is not null && attempt == 0)
            {
                current = await RefreshChangedSourceAsync(
                    library,
                    libraryId,
                    current,
                    sourcePath,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        throw new ImageSourceChangedException(
            sourcePath,
            "Source identity changed repeatedly while retrying thumbnail generation.");
    }

    public static async Task<(
        FullResolutionSource Source,
        FullResolutionInfo Info,
        ViewerAsset Asset)> ProbeOriginalWithRepairAsync(
            LibraryService? library,
            long libraryId,
            ViewerAsset asset,
            string sourcePath,
            CancellationToken cancellationToken)
    {
        var current = asset;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var source = new FullResolutionSource(
                sourcePath,
                current.FileSize,
                current.ModifiedAtUtcTicks,
                current.SourceIdentity);

            try
            {
                var info = await FullResolutionDecoder.ProbeAsync(
                    source,
                    cancellationToken).ConfigureAwait(false);
                return (source, info, current);
            }
            catch (FullResolutionSourceChangedException)
                when (library is not null && attempt == 0)
            {
                current = await RefreshChangedSourceAsync(
                    library,
                    libraryId,
                    current,
                    sourcePath,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        throw new FullResolutionSourceChangedException(
            sourcePath,
            "Source identity changed repeatedly while retrying original probe.");
    }

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
                existing.SourceIdentity,
                metadata.SourceIdentity,
                StringComparison.Ordinal))
        {
            RememberPersisted(
                libraryId,
                asset,
                metadata);
            return;
        }

        var key = PersistedMetadataKey.From(
            libraryId,
            asset,
            metadata);

        if (IsRemembered(key))
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
                metadata.SourceIdentity),
            cancellationToken).ConfigureAwait(false);

        if (!persisted)
        {
            throw new ImageSourceChangedException(
                sourcePath,
                "Library source revision advanced before technical metadata could be committed.");
        }

        Interlocked.Increment(ref _metadataPersistenceWrites);
        RememberPersisted(key);
    }

    private static async Task<ViewerAsset> RefreshChangedSourceAsync(
        LibraryService library,
        long libraryId,
        ViewerAsset stale,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var current = await library.GetAssetAsync(
            libraryId,
            stale.RelativePath,
            cancellationToken).ConfigureAwait(false);

        if (current is not null
            && current.SourceRevision != stale.SourceRevision)
        {
            return ToViewerAsset(current);
        }

        var info = new FileInfo(sourcePath);
        info.Refresh();

        if (!info.Exists)
        {
            throw new FileNotFoundException(
                "Image source disappeared while repairing its source revision.",
                sourcePath);
        }

        await library.RefreshAssetSourceAsync(
            libraryId,
            stale.RelativePath,
            info.Length,
            info.LastWriteTimeUtc.Ticks,
            cancellationToken).ConfigureAwait(false);

        var refreshed = await library.GetAssetAsync(
            libraryId,
            stale.RelativePath,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Library source disappeared while refreshing its source revision.");

        if (refreshed.SourceRevision <= stale.SourceRevision)
        {
            throw new ImageSourceChangedException(
                sourcePath,
                "Source refresh did not advance the persisted source revision.");
        }

        return ToViewerAsset(refreshed);
    }

    private static ViewerAsset ToViewerAsset(AssetInfo asset) =>
        new(
            asset.Id,
            asset.SourceRevision,
            asset.RelativePath,
            asset.FileName,
            asset.FileSize,
            asset.ModifiedAtUtc.UtcDateTime.Ticks,
            asset.Width,
            asset.Height,
            asset.Format,
            asset.SourceIdentity,
            asset.RawWidth,
            asset.RawHeight,
            asset.HasAlpha);

    private static bool IsRemembered(PersistedMetadataKey key)
    {
        lock (PersistedMetadataGate)
        {
            if (!PersistedMetadata.TryGetValue(key, out var node))
            {
                return false;
            }

            PersistedMetadataLru.Remove(node);
            PersistedMetadataLru.AddFirst(node);
            return true;
        }
    }

    private static void RememberPersisted(
        long libraryId,
        ViewerAsset asset,
        SourceTechnicalMetadata metadata) =>
        RememberPersisted(
            PersistedMetadataKey.From(
                libraryId,
                asset,
                metadata));

    private static void RememberPersisted(PersistedMetadataKey key)
    {
        lock (PersistedMetadataGate)
        {
            if (PersistedMetadata.TryGetValue(key, out var existing))
            {
                PersistedMetadataLru.Remove(existing);
                PersistedMetadataLru.AddFirst(existing);
                return;
            }

            var node = PersistedMetadataLru.AddFirst(key);
            PersistedMetadata.Add(key, node);

            while (PersistedMetadata.Count > PersistedMetadataCacheLimit)
            {
                var last = PersistedMetadataLru.Last;
                if (last is null)
                {
                    break;
                }

                PersistedMetadataLru.RemoveLast();
                PersistedMetadata.Remove(last.Value);
            }
        }
    }

    private readonly record struct PersistedMetadataKey(
        long LibraryId,
        long AssetId,
        long SourceRevision,
        string SourceIdentity,
        int Width,
        int Height,
        int RawWidth,
        int RawHeight,
        bool HasAlpha,
        string Format)
    {
        public static PersistedMetadataKey From(
            long libraryId,
            ViewerAsset asset,
            SourceTechnicalMetadata metadata) =>
            new(
                libraryId,
                asset.Id,
                asset.SourceRevision,
                metadata.SourceIdentity,
                metadata.Width,
                metadata.Height,
                metadata.RawWidth,
                metadata.RawHeight,
                metadata.HasAlpha,
                metadata.Format.ToLowerInvariant());
    }
}
