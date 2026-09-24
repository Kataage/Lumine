using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumine.Viewer;

namespace Lumine.Viewer.Smoke;

internal static class Program
{
    public static async Task Main()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"lumine-viewer-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var thumbnailPath = Path.Combine(tempRoot, "thumb.png");
        await File.WriteAllBytesAsync(
            thumbnailPath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));

        try
        {
            await VerifyCursorPagingAsync();
            await VerifyRequestCoalescingAndCancellationAsync(thumbnailPath);

            await using var headless = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
            await headless.Dispatch(
                async () =>
                {
                    await VerifyDecodedCacheAsync(tempRoot, thumbnailPath);
                    await VerifyHeadlessVirtualizationCoreAsync(thumbnailPath);
                    return 0;
                },
                CancellationToken.None);

            Console.WriteLine(
                "Viewer smoke: cursor paging / bitmap bounds / cancellation / 100k virtualization OK");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task VerifyCursorPagingAsync()
    {
        var source = new FixturePageSource(100_000);
        using var provider = new CursorPagedViewerAssetProvider(
            source,
            new ViewerOptions
            {
                MetadataPageSize = 256,
                MetadataPageCacheSize = 8,
                CursorCheckpointStride = 16,
                CursorCheckpointLimit = 32
            });

        var first = await provider.GetAssetAsync(0);
        var last = await provider.GetAssetAsync(99_999);
        var middle = await provider.GetAssetAsync(50_000);
        var back = await provider.GetAssetAsync(10);

        Require(first.Id == 1, "First cursor-paged asset mismatch.");
        Require(last.Id == 100_000, "Last cursor-paged asset mismatch.");
        Require(middle.Id == 50_001, "Middle cursor-paged asset mismatch.");
        Require(back.Id == 11, "Backward cursor-paged lookup mismatch.");

        var diagnostics = provider.Diagnostics;
        Require(diagnostics.CachedPages <= 8, "Metadata page cache exceeded hard limit.");
        Require(diagnostics.CursorCheckpoints <= 32, "Cursor checkpoint cache exceeded hard limit.");
        Require(diagnostics.PagesFetched > 0, "Cursor pager fetched no pages.");
    }

    private static async Task VerifyDecodedCacheAsync(
        string tempRoot,
        string sourceThumbnail)
    {
        using var cache = new DecodedBitmapCache(entryLimit: 4, byteLimit: 16);

        for (var index = 0; index < 12; index++)
        {
            var copy = Path.Combine(tempRoot, $"decoded-{index:D2}.png");
            File.Copy(sourceThumbnail, copy);

            using var lease = await cache.AcquireAsync(copy);
            Require(lease.Bitmap.PixelSize.Width == 1, "Decoded bitmap width mismatch.");

            var diagnostics = cache.Diagnostics;
            Require(diagnostics.EntryCount <= 4, "Decoded bitmap entry hard limit was exceeded.");
            Require(diagnostics.EstimatedBytes <= 16, "Decoded bitmap byte hard limit was exceeded.");
        }

        var pinnedPath = Path.Combine(tempRoot, "decoded-pinned.png");
        File.Copy(sourceThumbnail, pinnedPath);
        var pinned = await cache.AcquireAsync(pinnedPath);
        cache.Dispose();

        Require(
            pinned.Bitmap.PixelSize.Width == 1,
            "Cache disposal invalidated an actively leased bitmap.");

        pinned.Dispose();
        Require(
            cache.Diagnostics.EntryCount == 0
            && cache.Diagnostics.EstimatedBytes == 0,
            "Leased bitmap was not released after disposed-cache lease completion.");
    }

    private static async Task VerifyRequestCoalescingAndCancellationAsync(
        string thumbnailPath)
    {
        var assetProvider = new DirectFixtureAssetProvider(100);
        var thumbnailProvider = new DelayedThumbnailProvider(
            thumbnailPath,
            TimeSpan.FromMilliseconds(250));

        await using var session = new ViewerSession(
            assetProvider,
            thumbnailProvider,
            new ViewerOptions
            {
                DecodedBitmapEntryLimit = 8,
                DecodedBitmapByteLimit = 8 * 1024 * 1024,
                PrefetchRows = 0
            });

        var asset = await session.GetAssetAsync(5);
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();

        var first = session.GetThumbnailAsync(
            asset,
            ViewerThumbnailPriority.Foreground,
            firstCancellation.Token).AsTask();
        var second = session.GetThumbnailAsync(
            asset,
            ViewerThumbnailPriority.Foreground,
            secondCancellation.Token).AsTask();

        await Task.Delay(25);
        firstCancellation.Cancel();
        secondCancellation.Cancel();

        await ExpectCancellationAsync(first);
        await ExpectCancellationAsync(second);

        for (var attempt = 0;
             attempt < 100 && session.Diagnostics.InFlightThumbnailRequests != 0;
             attempt++)
        {
            await Task.Delay(5);
        }

        var diagnostics = session.Diagnostics;
        Require(diagnostics.ThumbnailRequests == 1, "Identical visible requests were not coalesced.");
        Require(diagnostics.ThumbnailRequestsCoalesced >= 1, "Coalescing diagnostic was not recorded.");
        Require(diagnostics.ThumbnailRequestsCancelled >= 1, "Stale shared thumbnail request was not cancelled.");
        Require(diagnostics.InFlightThumbnailRequests == 0, "Cancelled thumbnail request remained in-flight.");
        Require(thumbnailProvider.Cancelled > 0, "Provider did not observe cancellation.");
    }

    private static async Task VerifyHeadlessVirtualizationCoreAsync(string thumbnailPath)
    {
        await using var session = new ViewerSession(
            new DirectFixtureAssetProvider(100_000),
            new ImmediateThumbnailProvider(thumbnailPath),
            new ViewerOptions
            {
                TileWidth = 160,
                TileHeight = 190,
                TileSpacing = 8,
                PrefetchRows = 0,
                DecodedBitmapEntryLimit = 64,
                DecodedBitmapByteLimit = 32L * 1024 * 1024
            });

        var viewer = new ThumbnailViewerControl(session);
        var window = new Window
        {
            Width = 1200,
            Height = 800,
            Content = viewer
        };

        window.Show();

        for (var attempt = 0;
             attempt < 250 && viewer.Diagnostics.ReadyTiles == 0;
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        Require(viewer.Diagnostics.ReadyTiles > 0, "Viewer rendered no decoded thumbnail.");
        Require(viewer.Columns >= 4, "Viewer did not adapt columns to viewport width.");
        Require(
            viewer.RealizedRowCount is > 0 and < 64,
            "100k viewer realized an unbounded row count.");
        Require(
            viewer.Diagnostics.AttachedTiles is > 0 and < 512,
            "100k viewer attached an unbounded tile count.");

        Require(viewer.SelectedAssetIndex == -1, "Viewer unexpectedly started with a selection.");

        var firstTile = viewer.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(
                border => Math.Abs(border.Width - 160) < 0.1
                    && Math.Abs(border.Height - 190) < 0.1)
            ?? throw new InvalidOperationException("No realized thumbnail tile was available for mouse selection.");

        var tileCenter = new Point(
            firstTile.Bounds.Width / 2,
            firstTile.Bounds.Height / 2);
        var windowPoint = firstTile.TranslatePoint(tileCenter, window)
            ?? throw new InvalidOperationException("Unable to map thumbnail tile to window coordinates.");

        window.MouseDown(windowPoint, MouseButton.Left);
        window.MouseUp(windowPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Require(
            viewer.SelectedAssetIndex is >= 0 and < 7,
            "Mouse click did not select a tile in the first realized row.");
        Require(
            viewer.SelectedRealizedTileCount == 1,
            "Mouse selection was not rendered on exactly one realized tile.");

        var wideColumns = viewer.Columns;
        window.Width = 760;
        for (var attempt = 0;
             attempt < 100 && viewer.Columns == wideColumns;
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        Require(
            viewer.Columns < wideColumns,
            "Viewer did not recompute columns after viewport resize.");

        window.Width = 1200;
        for (var attempt = 0;
             attempt < 100 && viewer.Columns != wideColumns;
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        Require(
            viewer.Columns == wideColumns,
            "Viewer did not restore column count after viewport resize.");

        viewer.SelectAsset(0);
        Dispatcher.UIThread.RunJobs();
        Require(viewer.SelectedAssetIndex == 0, "Viewer did not select first asset.");
        Require(
            viewer.SelectedRealizedTileCount == 1,
            "Viewer selection was not rendered on exactly one realized tile.");

        RaiseKey(viewer, Key.Right);
        Require(viewer.SelectedAssetIndex == 1, "Right-arrow navigation failed.");

        RaiseKey(viewer, Key.Down);
        Require(
            viewer.SelectedAssetIndex == 1 + viewer.Columns,
            "Down-arrow navigation failed.");

        RaiseKey(viewer, Key.Home);
        Require(viewer.SelectedAssetIndex == 0, "Home navigation failed.");

        RaiseKey(viewer, Key.End);
        Require(viewer.SelectedAssetIndex == 99_999, "End navigation failed.");

        viewer.SelectAsset(0);
        viewer.ScrollToAsset(99_900);
        await Task.Delay(75);
        Dispatcher.UIThread.RunJobs();

        Require(
            viewer.RealizedRowCount < 64,
            "Fast scroll caused row virtualization to expand with library size.");
        Require(
            viewer.Diagnostics.AttachedTiles < 512,
            "Fast scroll caused tile count to expand with library size.");
        Require(
            viewer.FirstVisibleAssetIndex is > 99_000,
            "Fast scroll did not move the visible viewport near the requested far asset.");

        window.Width = 760;
        for (var attempt = 0; attempt < 150; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (viewer.FirstVisibleAssetIndex is > 99_000)
            {
                break;
            }

            await Task.Delay(1);
        }

        Require(
            viewer.FirstVisibleAssetIndex is > 99_000,
            "Resize jumped from the current far viewport back to an offscreen selection.");

        viewer.SelectAsset(99_900, scrollIntoView: false);
        Dispatcher.UIThread.RunJobs();
        Require(
            viewer.SelectedRealizedTileCount == 1,
            "Far selection did not render after fast scroll.");

        window.Width = 900;
        for (var attempt = 0; attempt < 150; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (viewer.SelectedRealizedTileCount == 1)
            {
                break;
            }

            await Task.Delay(1);
        }

        Require(
            viewer.SelectedAssetIndex == 99_900
            && viewer.SelectedRealizedTileCount == 1,
            "Resize lost the selected viewport anchor near the end of a 100k library.");

        viewer.SelectAsset(99_999);
        Require(
            viewer.SelectedAssetIndex == 99_999,
            "Viewer selection did not reach final 100k asset.");

        window.Close();
    }

    private static void RaiseKey(ThumbnailViewerControl viewer, Key key)
    {
        viewer.RaiseEvent(
            new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = key
            });
    }

    private static async Task ExpectCancellationAsync(Task task)
    {
        try
        {
            await task;
            throw new InvalidOperationException("Expected cancellation did not occur.");
        }
        catch (OperationCanceledException)
        {
        }
    }
}

internal sealed class TestApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}

internal sealed class FixturePageSource(long count) : IViewerPageSource
{
    public long Count { get; } = count;

    public ValueTask<ViewerAssetPage> GetPageAsync(
        int limit,
        ViewerPageCursor? cursor = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var start = cursor is { } value
            ? value.AssetId
            : 0;

        if (start >= Count)
        {
            return ValueTask.FromResult(new ViewerAssetPage([], null));
        }

        var take = checked((int)Math.Min(limit, Count - start));
        var items = new ViewerAsset[take];

        for (var offset = 0; offset < take; offset++)
        {
            var index = start + offset;
            items[offset] = Fixture(index);
        }

        ViewerPageCursor? next = start + take < Count
            ? new ViewerPageCursor(Count - (start + take), start + take)
            : null;

        return ValueTask.FromResult(new ViewerAssetPage(items, next));
    }

    private static ViewerAsset Fixture(long index) =>
        new(
            index + 1,
            1,
            $"fixture/{index:D6}.jpg",
            $"asset-{index:D6}.jpg",
            10_000 + index,
            DateTimeOffset.UnixEpoch.AddSeconds(index).UtcDateTime.Ticks);
}

internal sealed class DirectFixtureAssetProvider(long count) : IViewerAssetProvider
{
    public long Count { get; } = count;

    public ValueTask<ViewerAsset> GetAssetAsync(
        long index,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if ((ulong)index >= (ulong)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return ValueTask.FromResult(
            new ViewerAsset(
                index + 1,
                1,
                $"fixture/{index:D6}.jpg",
                $"asset-{index:D6}.jpg",
                10_000 + index,
                DateTimeOffset.UnixEpoch.AddSeconds(index).UtcDateTime.Ticks));
    }
}

internal sealed class ImmediateThumbnailProvider(string path) : IViewerThumbnailProvider
{
    public ValueTask<ViewerThumbnail> RequestAsync(
        ViewerAsset asset,
        ViewerThumbnailPriority priority,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(
            new ViewerThumbnail(
                $"fixture-{asset.Id}",
                path,
                1,
                1));
    }
}

internal sealed class DelayedThumbnailProvider(
    string path,
    TimeSpan delay) : IViewerThumbnailProvider
{
    private int _cancelled;

    public int Cancelled => Volatile.Read(ref _cancelled);

    public async ValueTask<ViewerThumbnail> RequestAsync(
        ViewerAsset asset,
        ViewerThumbnailPriority priority,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelled);
            throw;
        }

        return new ViewerThumbnail(
            $"fixture-{asset.Id}",
            path,
            1,
            1);
    }
}
