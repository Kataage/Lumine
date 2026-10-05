using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumine.Core;
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
            VerifyResourcePolicyMapping();
            await VerifyCursorPagingAsync();
            await VerifyBackgroundForegroundCoalescingAsync(thumbnailPath);
            await VerifyInteractivePromotionAsync(thumbnailPath);
            await VerifyRequestCoalescingAndCancellationAsync(thumbnailPath);
            await VerifyViewerSessionShutdownAsync(thumbnailPath);

            await using var headless = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
            await headless.Dispatch(
                async () =>
                {
                    await VerifyDecodedCacheAsync(tempRoot, thumbnailPath);
                    await VerifyDecodedCacheAsyncShutdown(
                        thumbnailPath);
                    await VerifyPrefetchWaitsForVisibleTilesAsync(
                        thumbnailPath);
                    await VerifyHeadlessVirtualizationCoreAsync(thumbnailPath);
                    await VerifyBrowsePresentationParityAsync(thumbnailPath);
                    await VerifyThumbnailFailureRecoveryAsync(thumbnailPath);
                    await VerifyDetailViewerAsync(thumbnailPath);
                    await VerifyDetailObserverIsolationAsync(
                        thumbnailPath);
                    await VerifyDetailSessionShutdownAsync(
                        thumbnailPath);
                    await VerifyOriginalReleaseFenceBlocksAdmissionAsync(
                        thumbnailPath);
                    await VerifyDetailSelectionCallerCancellationAsync(thumbnailPath);
                    await VerifyUnknownMetadataPromotionAsync(thumbnailPath);
                    await VerifyUnknownMetadataPromotionReversalAsync(thumbnailPath);
                    await VerifyOriginalFailureKeepsFitAsync(thumbnailPath);
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

    private static void VerifyResourcePolicyMapping()
    {
        var policy = CoreResourcePolicy.Resolve(
            new ResourcePolicySettings
            {
                DecodedThumbnailEntryLimit = 40,
                DecodedThumbnailByteLimit = 72L * 1024 * 1024,
                DetailPreviewEntryLimit = 3,
                DetailPreviewByteLimit = 36L * 1024 * 1024,
                DetailOriginalByteLimit = 320L * 1024 * 1024
            },
            processorCount: 8);

        var viewer = ViewerOptions.FromResourcePolicy(policy);
        var detail = ViewerDetailOptions.FromResourcePolicy(policy);

        var productDefaults = new ViewerOptions();
        Require(
            Math.Abs(
                productDefaults.TileWidth
                - productDefaults.TileHeight) < 0.001,
            "Default product grid is no longer square.");

        Require(
            viewer.DecodedBitmapEntryLimit
                == policy.DecodedThumbnailEntryLimit
            && viewer.DecodedBitmapByteLimit
                == policy.DecodedThumbnailByteLimit,
            "Viewer resource options drifted from the Core policy.");
        Require(
            detail.PreviewDecodedEntryLimit
                == policy.DetailPreviewEntryLimit
            && detail.PreviewDecodedByteLimit
                == policy.DetailPreviewByteLimit
            && detail.OriginalDecodedByteLimit
                == policy.DetailOriginalByteLimit,
            "Detail resource options drifted from the Core policy.");
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

        var batchIds =
            await provider.GetAssetIdsAsync(
                new long[]
                {
                    0,
                    1,
                    255,
                    256,
                    50_000,
                    99_999
                });
        Require(
            batchIds.SequenceEqual(
                new long[]
                {
                    1,
                    2,
                    256,
                    257,
                    50_001,
                    100_000
                }),
            "Cursor-paged batch selection ID resolution lost result order or page boundaries.");

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

        using (var memoryPayloadCache =
            new DecodedBitmapCache(
                entryLimit: 2,
                byteLimit: 16))
        {
            var encoded =
                await File.ReadAllBytesAsync(
                    sourceThumbnail);
            var thumbnail =
                new ViewerThumbnail(
                    "memory-payload-smoke",
                    string.Empty,
                    1,
                    1,
                    EncodedBytes: encoded);

            using var lease =
                await memoryPayloadCache.AcquireAsync(
                    thumbnail);

            Require(
                lease.Bitmap.PixelSize.Width == 1
                && lease.Bitmap.PixelSize.Height == 1,
                "Decoded bitmap cache could not consume an in-memory thumbnail payload.");
            Require(
                memoryPayloadCache.Diagnostics.EntryCount
                    == 1,
                "In-memory thumbnail payload was not admitted to the decoded bitmap cache.");
        }

        using (var oversizeCache = new DecodedBitmapCache(
                   entryLimit: 2,
                   byteLimit: 3))
        {
            try
            {
                using var unexpected = await oversizeCache.AcquireAsync(
                    sourceThumbnail);
                throw new InvalidOperationException(
                    "Per-bitmap byte-limit violation incorrectly entered capacity waiting.");
            }
            catch (InvalidOperationException exception)
                when (exception.Message.Contains(
                    "above cache limit",
                    StringComparison.Ordinal))
            {
            }
        }

        var pressurePaths = Enumerable.Range(0, 5)
            .Select(index =>
            {
                var path = Path.Combine(
                    tempRoot,
                    $"decoded-pressure-{index:D2}.png");
                File.Copy(sourceThumbnail, path);
                return path;
            })
            .ToArray();

        var pressureCache = new DecodedBitmapCache(
            entryLimit: 2,
            byteLimit: 8);

        var firstPinned = await pressureCache.AcquireAsync(
            pressurePaths[0]);
        var secondPinned = await pressureCache.AcquireAsync(
            pressurePaths[1]);

        var waitingForCapacity = pressureCache.AcquireAsync(
            pressurePaths[2]);
        await Task.Delay(40);

        Require(
            !waitingForCapacity.IsCompleted,
            "Fully pinned cache pressure failed immediately instead of waiting for capacity.");

        firstPinned.Dispose();

        var admittedAfterRelease = await waitingForCapacity.WaitAsync(
            TimeSpan.FromSeconds(2));
        Require(
            admittedAfterRelease.Bitmap.PixelSize.Width == 1,
            "Waiting cache admission did not recover after a lease was released.");

        using (var cancellation = new CancellationTokenSource())
        {
            var cancelledWait = pressureCache.AcquireAsync(
                pressurePaths[3],
                cancellation.Token);
            await Task.Delay(40);

            Require(
                !cancelledWait.IsCompleted,
                "Pinned-cache cancellation regression never entered capacity wait.");

            cancellation.Cancel();
            await ExpectCancellationAsync(cancelledWait);
        }

        var disposedWait = pressureCache.AcquireAsync(
            pressurePaths[4]);
        await Task.Delay(40);
        Require(
            !disposedWait.IsCompleted,
            "Pinned-cache disposal regression never entered capacity wait.");

        pressureCache.Dispose();

        try
        {
            _ = await disposedWait;
            throw new InvalidOperationException(
                "Disposed cache left a capacity waiter alive.");
        }
        catch (ObjectDisposedException)
        {
        }

        secondPinned.Dispose();
        admittedAfterRelease.Dispose();

        Require(
            pressureCache.Diagnostics.EntryCount == 0
            && pressureCache.Diagnostics.EstimatedBytes == 0,
            "Disposed pressure cache retained leased bitmap state after release.");
    }

    private static async Task VerifyDecodedCacheAsyncShutdown(
        string sourceThumbnail)
    {
        using var decodeStarted = new ManualResetEventSlim();
        var cache = new DecodedBitmapCache(
            entryLimit: 2,
            byteLimit: 16,
            decodeBitmap:
                (path, cancellationToken) =>
                {
                    decodeStarted.Set();
                    cancellationToken.WaitHandle.WaitOne();
                    cancellationToken.ThrowIfCancellationRequested();

                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read);
                    return new Bitmap(stream);
                });

        var acquire = cache.AcquireAsync(sourceThumbnail);

        Require(
            decodeStarted.Wait(TimeSpan.FromSeconds(2)),
            "Async cache shutdown regression never entered decode work.");

        var dispose = cache.DisposeAsync().AsTask();
        await dispose.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            using var unexpected = await acquire;
            throw new InvalidOperationException(
                "Cache shutdown left an active decode admitted.");
        }
        catch (ObjectDisposedException)
        {
        }

        Require(
            cache.Diagnostics.ActiveDecodes == 0,
            "DecodedBitmapCache.DisposeAsync returned before active decode drained.");
    }

    private static async Task VerifyBackgroundForegroundCoalescingAsync(
        string thumbnailPath)
    {
        var assetProvider = new DirectFixtureAssetProvider(100);
        var thumbnailProvider = new DelayedThumbnailProvider(
            thumbnailPath,
            TimeSpan.FromMilliseconds(120));

        await using var session = new ViewerSession(
            assetProvider,
            thumbnailProvider,
            new ViewerOptions
            {
                DecodedBitmapEntryLimit = 8,
                DecodedBitmapByteLimit = 8 * 1024 * 1024,
                PrefetchRows = 0
            });

        var asset = await session.GetAssetAsync(7);
        var background = session.GetThumbnailAsync(
            asset,
            ViewerThumbnailPriority.Background).AsTask();

        await thumbnailProvider.Started.WaitAsync(
            TimeSpan.FromSeconds(2));

        var foreground = session.GetThumbnailAsync(
            asset,
            ViewerThumbnailPriority.Foreground).AsTask();

        var results = await Task.WhenAll(
            background,
            foreground);

        Require(
            string.Equals(
                results[0].CacheKey,
                results[1].CacheKey,
                StringComparison.Ordinal),
            "Background-to-foreground coalescing returned different thumbnail work.");
        Require(
            session.Diagnostics.ThumbnailRequests == 1,
            "Foreground visibility restarted an existing background thumbnail request.");
        Require(
            session.Diagnostics.ThumbnailRequestsCoalesced >= 1,
            "Background-to-foreground coalescing diagnostic was not recorded.");
        Require(
            session.Diagnostics.ThumbnailRequestsCancelled == 0,
            "Background-to-foreground promotion cancelled useful in-flight work.");
        Require(
            thumbnailProvider.Cancelled == 0,
            "Thumbnail provider observed cancellation during background-to-foreground reuse.");
        Require(
            thumbnailProvider.Active == 0,
            "Background-to-foreground coalesced request did not drain.");
    }

    private static async Task VerifyInteractivePromotionAsync(
        string thumbnailPath)
    {
        var assetProvider =
            new DirectFixtureAssetProvider(100);
        var thumbnailProvider =
            new PriorityGatedThumbnailProvider(
                thumbnailPath);

        await using var session =
            new ViewerSession(
                assetProvider,
                thumbnailProvider,
                new ViewerOptions
                {
                    DecodedBitmapEntryLimit = 8,
                    DecodedBitmapByteLimit =
                        8 * 1024 * 1024,
                    PrefetchRows = 0
                });

        var asset =
            await session.GetAssetAsync(11);
        var foreground =
            session.GetThumbnailAsync(
                asset,
                ViewerThumbnailPriority.Foreground)
                .AsTask();

        await thumbnailProvider.ForegroundStarted
            .WaitAsync(TimeSpan.FromSeconds(2));

        var interactive =
            session.GetThumbnailAsync(
                asset,
                ViewerThumbnailPriority.Interactive)
                .AsTask();

        await thumbnailProvider.InteractiveStarted
            .WaitAsync(TimeSpan.FromSeconds(2));

        Require(
            session.Diagnostics.ThumbnailRequests == 2,
            "Interactive selection incorrectly inherited the queued Grid request.");
        Require(
            thumbnailProvider.ForegroundRequests == 1
            && thumbnailProvider.InteractiveRequests == 1,
            "ViewerSession did not issue distinct Grid and Interactive provider requests.");

        thumbnailProvider.Release();

        var results =
            await Task.WhenAll(
                foreground,
                interactive);

        Require(
            string.Equals(
                results[0].CacheKey,
                results[1].CacheKey,
                StringComparison.Ordinal),
            "Interactive promotion changed the selected asset thumbnail identity.");
        Require(
            thumbnailProvider.Cancelled == 0,
            "Interactive promotion cancelled useful existing Grid work.");
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

        await thumbnailProvider.Started.WaitAsync(
            TimeSpan.FromSeconds(2));

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

    private static async Task VerifyViewerSessionShutdownAsync(
        string thumbnailPath)
    {
        var provider = new DelayedThumbnailProvider(
            thumbnailPath,
            TimeSpan.FromMilliseconds(500));
        var session = new ViewerSession(
            new DirectFixtureAssetProvider(10),
            provider,
            new ViewerOptions
            {
                PrefetchRows = 0,
                DecodedBitmapEntryLimit = 4,
                DecodedBitmapByteLimit = 4 * 1024 * 1024
            });

        var asset = await session.GetAssetAsync(0);
        var first = session.GetThumbnailAsync(
            asset,
            ViewerThumbnailPriority.Foreground).AsTask();
        var second = session.GetThumbnailAsync(
            asset,
            ViewerThumbnailPriority.Foreground).AsTask();

        for (var attempt = 0;
             attempt < 300 && provider.Active == 0;
             attempt++)
        {
            await Task.Delay(1);
        }

        Require(
            provider.Active == 1,
            "Viewer shutdown regression never entered provider work.");

        var firstDispose = session.DisposeAsync().AsTask();
        var secondDispose = session.DisposeAsync().AsTask();
        await Task.WhenAll(firstDispose, secondDispose);

        await ExpectCancellationAsync(first);
        await ExpectCancellationAsync(second);

        Require(
            provider.Active == 0,
            "ViewerSession.DisposeAsync returned before provider work drained.");
        Require(
            session.Diagnostics.InFlightThumbnailRequests == 0,
            "ViewerSession.DisposeAsync left coalesced requests registered.");

        try
        {
            _ = await session.GetThumbnailAsync(
                asset,
                ViewerThumbnailPriority.Foreground);
            throw new InvalidOperationException(
                "Disposed ViewerSession admitted new thumbnail work.");
        }
        catch (ObjectDisposedException)
        {
        }

        var assetProvider = new DelayedAssetProvider(
            count: 4,
            delay: TimeSpan.FromMilliseconds(500));
        var assetSession = new ViewerSession(
            assetProvider,
            new ImmediateThumbnailProvider(thumbnailPath),
            new ViewerOptions
            {
                PrefetchRows = 0,
                DecodedBitmapEntryLimit = 4,
                DecodedBitmapByteLimit = 4 * 1024 * 1024
            });

        var assetLoad = assetSession.GetAssetAsync(0).AsTask();

        for (var attempt = 0;
             attempt < 300 && assetProvider.Active == 0;
             attempt++)
        {
            await Task.Delay(1);
        }

        Require(
            assetProvider.Active == 1,
            "Viewer asset shutdown regression never entered asset work.");

        var assetDisposeA =
            assetSession.DisposeAsync().AsTask();
        var assetDisposeB =
            assetSession.DisposeAsync().AsTask();

        await Task.WhenAll(
            assetDisposeA,
            assetDisposeB);
        await ExpectCancellationAsync(assetLoad);

        Require(
            assetProvider.Active == 0,
            "ViewerSession.DisposeAsync returned before active asset work drained.");
        Require(
            assetProvider.Cancelled > 0,
            "Active asset provider did not observe Viewer shutdown cancellation.");

        try
        {
            _ = await assetSession.GetAssetAsync(1);
            throw new InvalidOperationException(
                "Disposed ViewerSession admitted new asset work.");
        }
        catch (ObjectDisposedException)
        {
        }

        using var decodeStarted = new ManualResetEventSlim();
        using var decodeCancelled = new ManualResetEventSlim();
        var allowDecodeExit =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var shutdownCache = new DecodedBitmapCache(
            entryLimit: 2,
            byteLimit: 4 * 1024 * 1024,
            decodeBitmap:
                (path, cancellationToken) =>
                {
                    decodeStarted.Set();
                    cancellationToken.WaitHandle.WaitOne();
                    decodeCancelled.Set();
                    allowDecodeExit.Task.GetAwaiter().GetResult();
                    cancellationToken.ThrowIfCancellationRequested();

                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read);
                    return new Bitmap(stream);
                });

        var cacheSession = new ViewerSession(
            new DirectFixtureAssetProvider(1),
            new ImmediateThumbnailProvider(thumbnailPath),
            new ViewerOptions
            {
                PrefetchRows = 0,
                DecodedBitmapEntryLimit = 2,
                DecodedBitmapByteLimit = 4 * 1024 * 1024
            },
            shutdownCache);

        var activeDecode =
            cacheSession.BitmapCache.AcquireAsync(thumbnailPath);

        Require(
            decodeStarted.Wait(TimeSpan.FromSeconds(2)),
            "ViewerSession cache shutdown integration never entered decode.");

        var cacheSessionDispose =
            cacheSession.DisposeAsync().AsTask();

        Require(
            decodeCancelled.Wait(TimeSpan.FromSeconds(2)),
            "ViewerSession shutdown did not cancel active bitmap decode.");
        Require(
            !cacheSessionDispose.IsCompleted,
            "ViewerSession.DisposeAsync returned before bitmap decode drained.");

        allowDecodeExit.TrySetResult();
        await cacheSessionDispose.WaitAsync(
            TimeSpan.FromSeconds(2));

        try
        {
            using var unexpected = await activeDecode;
            throw new InvalidOperationException(
                "ViewerSession shutdown admitted a cancelled bitmap decode.");
        }
        catch (ObjectDisposedException)
        {
        }

        Require(
            shutdownCache.Diagnostics.ActiveDecodes == 0,
            "ViewerSession.DisposeAsync did not drain active bitmap decode.");
    }

    private static async Task VerifyPrefetchWaitsForVisibleTilesAsync(
        string thumbnailPath)
    {
        var provider =
            new GatedPriorityThumbnailProvider(
                thumbnailPath);
        await using var session =
            new ViewerSession(
                new DirectFixtureAssetProvider(1_000),
                provider,
                new ViewerOptions
                {
                    TileWidth = 120,
                    TileHeight = 120,
                    TileSpacing = 8,
                    PrefetchRows = 1,
                    PrefetchDelay =
                        TimeSpan.FromMilliseconds(1),
                    DecodedBitmapEntryLimit = 64,
                    DecodedBitmapByteLimit =
                        32L * 1024 * 1024
                });

        var viewer =
            new ThumbnailViewerControl(session);
        var window =
            new Window
            {
                Width = 900,
                Height = 600,
                Content = viewer
            };

        window.Show();

        for (var attempt = 0;
             attempt < 500
             && provider.ForegroundRequests == 0;
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        Require(
            provider.ForegroundRequests > 0,
            "Prefetch scheduling smoke never started visible foreground work.");

        for (var attempt = 0;
             attempt < 75;
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        Require(
            provider.BackgroundRequests == 0,
            "Background prefetch started while visible tiles were still loading.");

        provider.ReleaseForeground();

        for (var attempt = 0;
             attempt < 1_000
             && (viewer.Diagnostics.AttachedTiles == 0
                 || viewer.Diagnostics.ReadyTiles
                    < viewer.Diagnostics.AttachedTiles
                 || provider.BackgroundRequests == 0);
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        Require(
            viewer.Diagnostics.AttachedTiles > 0
            && viewer.Diagnostics.ReadyTiles
                >= viewer.Diagnostics.AttachedTiles,
            "Visible tiles did not reach ready state after foreground release.");
        Require(
            provider.BackgroundRequests > 0,
            "Background prefetch did not resume after visible tiles became ready.");

        window.Close();
        Dispatcher.UIThread.RunJobs();
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

        Require(
            !ThumbnailViewerControl.ResolveTileActionVisibilityForSmoke(
                ViewerLayoutMode.Grid,
                selected: false,
                hovered: false,
                focused: false)
            && !ThumbnailViewerControl.ResolveTileActionVisibilityForSmoke(
                ViewerLayoutMode.Grid,
                selected: true,
                hovered: false,
                focused: false)
            && ThumbnailViewerControl.ResolveTileActionVisibilityForSmoke(
                ViewerLayoutMode.Grid,
                selected: false,
                hovered: true,
                focused: false)
            && ThumbnailViewerControl.ResolveTileActionVisibilityForSmoke(
                ViewerLayoutMode.Grid,
                selected: false,
                hovered: false,
                focused: true)
            && !ThumbnailViewerControl.ResolveTileActionVisibilityForSmoke(
                ViewerLayoutMode.List,
                selected: false,
                hovered: false,
                focused: false)
            && ThumbnailViewerControl.ResolveTileActionVisibilityForSmoke(
                ViewerLayoutMode.List,
                selected: false,
                hovered: true,
                focused: false)
            && ThumbnailViewerControl.ResolveTileActionVisibilityForSmoke(
                ViewerLayoutMode.List,
                selected: false,
                hovered: false,
                focused: true),
            "Thumbnail secondary actions must stay contextual while hover/focus remains discoverable.");

        Require(
            ThumbnailViewerControl.ResolveTileVisualStateForSmoke(
                selected: false,
                hovered: false,
                pressed: false,
                focused: false)
                == "Neutral"
            && ThumbnailViewerControl.ResolveTileVisualStateForSmoke(
                selected: false,
                hovered: true,
                pressed: false,
                focused: false)
                == "Hover"
            && ThumbnailViewerControl.ResolveTileVisualStateForSmoke(
                selected: false,
                hovered: true,
                pressed: true,
                focused: false)
                == "Pressed"
            && ThumbnailViewerControl.ResolveTileVisualStateForSmoke(
                selected: true,
                hovered: false,
                pressed: false,
                focused: false)
                == "Selected"
            && ThumbnailViewerControl.ResolveTileVisualStateForSmoke(
                selected: true,
                hovered: true,
                pressed: false,
                focused: false)
                == "SelectedHover"
            && ThumbnailViewerControl.ResolveTileVisualStateForSmoke(
                selected: false,
                hovered: false,
                pressed: false,
                focused: true)
                == "Focus",
            "Viewer tile interaction-state precedence drifted across neutral/hover/pressed/selected/focus states.");

        // A short scroll away/back must reuse the existing decoded-cache
        // entry instead of showing a blank tile while the full async
        // thumbnail pipeline runs again.
        for (var attempt = 0;
             attempt < 250
             && !viewer.IsAssetReady(0);
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        Require(
            viewer.IsAssetReady(0)
            && viewer.IsAssetWarmForSmoke(0),
            "Initial first-row thumbnail did not establish a warm-return descriptor.");

        var firstAssetDetached = false;
        for (var rowOffset = 2;
             rowOffset <= 32
             && !firstAssetDetached;
             rowOffset++)
        {
            viewer.ScrollToAsset(
                Math.Min(
                    viewer.AssetCount - 1,
                    checked(
                        (long)viewer.Columns
                        * rowOffset)));

            for (var attempt = 0;
                 attempt < 20;
                 attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                firstAssetDetached =
                    viewer.GetAssetFocusTarget(0)
                    is null;
                if (firstAssetDetached)
                {
                    break;
                }

                await Task.Delay(1);
            }
        }

        Require(
            firstAssetDetached
            && viewer.IsAssetWarmForSmoke(0),
            "Scroll-away acceptance did not actually virtualize the warm first-row thumbnail.");

        var warmHitsBeforeReturn =
            viewer.WarmTileHitCountForSmoke;
        var warmReturnWatch =
            Stopwatch.StartNew();
        viewer.ScrollToAsset(0);

        for (var attempt = 0;
             attempt < 100
             && !viewer.IsAssetReady(0);
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        warmReturnWatch.Stop();
        Require(
            viewer.IsAssetReady(0)
            && viewer.WarmTileHitCountForSmoke
                > warmHitsBeforeReturn
            && warmReturnWatch.Elapsed
                < TimeSpan.FromMilliseconds(150),
            $"Warm scroll-back did not synchronously reuse the decoded thumbnail within the interactive budget: {warmReturnWatch.Elapsed.TotalMilliseconds:N1} ms.");

        var selectAllWatch =
            Stopwatch.StartNew();
        viewer.SelectAll();
        selectAllWatch.Stop();
        Require(
            viewer.SelectedAssetCount == 100_000
            && viewer.SelectionRangeCount == 1
            && viewer.SelectedAssetIndices[0] == 0
            && viewer.SelectedAssetIndices[99_999] == 99_999
            && selectAllWatch.Elapsed
                < TimeSpan.FromMilliseconds(500),
            $"100k Select-All did not remain one fast compact range: {selectAllWatch.Elapsed.TotalMilliseconds:N1} ms.");

        viewer.ClearSelection();
        Require(
            viewer.SelectedAssetCount == 0
            && viewer.SelectionRangeCount == 0,
            "Clearing a compact Select-All did not release selection ranges.");

        viewer.SelectAsset(0);
        var largeRangeWatch =
            Stopwatch.StartNew();
        viewer.SelectAsset(
            99_999,
            scrollIntoView: false,
            ViewerSelectionMode.Range);
        largeRangeWatch.Stop();
        Require(
            viewer.SelectedAssetCount == 100_000
            && viewer.SelectionRangeCount == 1
            && largeRangeWatch.Elapsed
                < TimeSpan.FromMilliseconds(500),
            $"100k Shift-range selection did not remain one fast compact range: {largeRangeWatch.Elapsed.TotalMilliseconds:N1} ms.");
        viewer.ClearSelection();

        var firstTile = viewer.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(
                border => Math.Abs(border.Width - 160) < 0.1
                    && Math.Abs(border.Height - 190) < 0.1)
            ?? throw new InvalidOperationException("No realized thumbnail tile was available for mouse selection.");

        var actionGeometry =
            viewer.GetRealizedTileActionGeometryForSmoke(0);
        Require(
            actionGeometry.Count == 2,
            "Thumbnail hover overlay did not expose the expected two primary icon actions.");

        var overlayButtons =
            viewer.GetVisualDescendants()
                .OfType<Button>()
                .Where(
                    button =>
                        button.Content
                            is Avalonia.Controls.Shapes.Path)
                .ToArray();
        Require(
            overlayButtons.Length >= 2
            && overlayButtons.All(
                button =>
                    !string.IsNullOrWhiteSpace(
                        AutomationProperties.GetName(
                            button)))
            && overlayButtons.Any(
                button =>
                    string.Equals(
                        AutomationProperties.GetAcceleratorKey(
                            button),
                        "I",
                        StringComparison.Ordinal))
            && overlayButtons.Any(
                button =>
                    string.Equals(
                        AutomationProperties.GetAcceleratorKey(
                            button),
                        "Enter",
                        StringComparison.Ordinal)),
            "Thumbnail icon actions lost accessible names or accelerator metadata.");

        foreach (var geometry in actionGeometry)
        {
            Require(
                geometry.ButtonBounds.Left >= 0
                && geometry.ButtonBounds.Top >= 0
                && geometry.ButtonBounds.Right <= 160
                && geometry.ButtonBounds.Bottom <= 190,
                "Thumbnail action button escaped the tile bounds.");

            var buttonCenterX =
                geometry.ButtonBounds.X
                + (geometry.ButtonBounds.Width / 2);
            var buttonCenterY =
                geometry.ButtonBounds.Y
                + (geometry.ButtonBounds.Height / 2);
            var iconCenterX =
                geometry.IconBounds.X
                + (geometry.IconBounds.Width / 2);
            var iconCenterY =
                geometry.IconBounds.Y
                + (geometry.IconBounds.Height / 2);

            Require(
                Math.Abs(
                    buttonCenterX
                    - iconCenterX) <= 1
                && Math.Abs(
                    buttonCenterY
                    - iconCenterY) <= 1,
                "Thumbnail overlay icon is not visually centered inside its button.");
        }

        Require(
            Math.Abs(
                actionGeometry[0].ButtonBounds.Width
                - actionGeometry[1].ButtonBounds.Width) < 0.1
            && Math.Abs(
                actionGeometry[0].ButtonBounds.Height
                - actionGeometry[1].ButtonBounds.Height) < 0.1
            && Math.Abs(
                actionGeometry[0].ButtonBounds.Y
                - actionGeometry[1].ButtonBounds.Y) < 0.1,
            "Thumbnail action buttons do not share a consistent size/alignment.");

        var tileCenter = new Point(
            firstTile.Bounds.Width / 2,
            firstTile.Bounds.Height / 2);
        var windowPoint = firstTile.TranslatePoint(tileCenter, window)
            ?? throw new InvalidOperationException("Unable to map thumbnail tile to window coordinates.");

        ThumbnailViewerControl.ViewerAssetContextRequestedEventArgs?
            contextRequest = null;
        viewer.AssetContextRequested +=
            (_, request) =>
                contextRequest = request;
        window.MouseDown(windowPoint, MouseButton.Right);
        window.MouseUp(windowPoint, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Require(
            contextRequest is not null
            && contextRequest.Index == 0
            && viewer.SelectedAssetCount == 1
            && viewer.SelectedAssetIndex == 0,
            "Right-click did not preserve the clicked thumbnail as the context action target.");

        window.MouseDown(windowPoint, MouseButton.Left);
        window.MouseUp(windowPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Require(
            viewer.SelectedAssetIndex is >= 0 and < 7,
            "Mouse click did not select a tile in the first realized row.");
        Require(
            viewer.SelectedRealizedTileCount == 1,
            "Mouse selection was not rendered on exactly one realized tile.");

        viewer.ClearSelection();
        Require(viewer.SelectedAssetIndex == -1, "Viewer did not clear selection.");
        RaiseKey(viewer, Key.A);
        Require(
            viewer.SelectedAssetIndex == -1,
            "Non-navigation key unexpectedly created a selection.");
        RaiseKey(viewer, Key.Right);
        Require(
            viewer.SelectedAssetIndex == 0,
            "Keyboard navigation from no selection did not start at the first asset.");

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

        viewer.SelectAsset(
            2,
            scrollIntoView: false,
            ViewerSelectionMode.Toggle);
        Require(
            viewer.SelectedAssetCount == 2
            && viewer.SelectedAssetIndices.SequenceEqual([0L, 2L]),
            "Ctrl-style toggle selection did not preserve the existing selection.");

        viewer.SelectAsset(
            5,
            scrollIntoView: false,
            ViewerSelectionMode.Range);
        Require(
            viewer.SelectedAssetCount == 4
            && viewer.SelectedAssetIndices.SequenceEqual([2L, 3L, 4L, 5L])
            && viewer.SelectedAssetIndex == 5,
            "Shift-style range selection did not follow the current query order/anchor.");

        viewer.ClearSelection();
        Require(
            viewer.SelectedAssetCount == 0
            && viewer.SelectedAssetIndex == -1,
            "Viewer did not clear multi-selection state.");

        viewer.SelectAsset(0);

        long invokedIndex = -1;
        long detailIndex = -1;
        void OnAssetInvoked(object? _, long index) =>
            invokedIndex = index;
        void OnAssetDetailRequested(object? _, long index) =>
            detailIndex = index;
        viewer.AssetInvoked += OnAssetInvoked;
        viewer.AssetDetailRequested += OnAssetDetailRequested;

        RaiseKey(viewer, Key.I);
        Require(
            detailIndex == 0,
            "I did not request details for the selected image.");

        RaiseKey(viewer, Key.Enter);
        Require(
            invokedIndex == 0,
            "Enter did not invoke the selected image.");
        invokedIndex = -1;

        RaiseKey(viewer, Key.Space);
        Require(
            invokedIndex == 0,
            "Space did not invoke the selected image.");

        viewer.AssetInvoked -= OnAssetInvoked;
        viewer.AssetDetailRequested -= OnAssetDetailRequested;

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

        viewer.SelectAsset(99_900, scrollIntoView: true);
        for (var attempt = 0;
             attempt < 150 && viewer.SelectedRealizedTileCount != 1;
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        Require(
            viewer.SelectedRealizedTileCount == 1,
            "Far selection did not render within the bounded realization window.");

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

        viewer.ScrollToAsset(99_999);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            Dispatcher.UIThread.RunJobs();

            if (viewer.FirstVisibleAssetIndex is { } firstVisible
                && viewer.LastVisibleAssetIndex is { } lastVisible
                && firstVisible <= 99_999
                && lastVisible >= 99_999
                && viewer.IsAssetReady(99_999))
            {
                break;
            }

            await Task.Delay(1);
        }

        Require(
            viewer.FirstVisibleAssetIndex is { } finalFirst
            && viewer.LastVisibleAssetIndex is { } finalLast
            && finalFirst <= 99_999
            && finalLast >= 99_999
            && viewer.IsAssetReady(99_999),
            "ScrollIntoView did not make the final asset visible and ready.");

        Require(
            99_999 - viewer.FirstVisibleAssetIndex!.Value
                > (long)viewer.Columns * 3,
            "Final-asset smoke no longer exercises a viewport where the target is more than three rows after the first visible asset.");

        var columnsBeforeDpi = viewer.Columns;
        var selectedBeforeDpi = viewer.SelectedAssetIndex;
        window.SetRenderScaling(2.0);
        Dispatcher.UIThread.RunJobs();

        Require(
            Math.Abs(window.RenderScaling - 2.0) < 0.001,
            "Headless DPI scaling change was not applied.");
        Require(
            viewer.Columns == columnsBeforeDpi,
            "DPI scaling incorrectly changed DIP-based Viewer column count.");
        Require(
            viewer.SelectedAssetIndex == selectedBeforeDpi,
            "DPI scaling changed Viewer selection.");
        Require(
            viewer.FirstVisibleAssetIndex is > 99_000,
            "DPI scaling lost the far viewport anchor in a 100k library.");

        window.SetRenderScaling(1.0);
        Dispatcher.UIThread.RunJobs();
        Require(
            viewer.Columns == columnsBeforeDpi,
            "Restoring DPI scaling changed DIP-based Viewer column count.");

        window.Close();
        Dispatcher.UIThread.RunJobs();

        await viewer.DrainBitmapReleasesAsync()
            .WaitAsync(TimeSpan.FromSeconds(2));

        Require(
            viewer.Diagnostics.AttachedTiles == 0
            && viewer.Diagnostics.ReadyTiles == 0,
            "Viewer close did not detach all thumbnail tiles before visual-release drain completed.");
    }

    private static async Task VerifyBrowsePresentationParityAsync(
        string thumbnailPath)
    {
        await using var session =
            new ViewerSession(
                new DirectFixtureAssetProvider(
                    1,
                    organizationMetadata: true),
                new ImmediateThumbnailProvider(
                    thumbnailPath),
                new ViewerOptions
                {
                    TileWidth = 180,
                    TileHeight = 180,
                    TileSpacing = 8,
                    PrefetchRows = 0
                });

        var viewer =
            new ThumbnailViewerControl(session);
        var window =
            new Window
            {
                Width = 760,
                Height = 500,
                Content = viewer
            };

        window.Show();

        for (var attempt = 0;
             attempt < 250
             && !viewer.IsAssetReady(0);
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        var gridPresentation =
            viewer.GetRealizedTilePresentationForSmoke(0);
        Require(
            gridPresentation.Primary == "asset-000000.jpg"
            && gridPresentation.Secondary.Contains(
                "MB",
                StringComparison.Ordinal)
            && gridPresentation.Organization.Contains(
                "♥",
                StringComparison.Ordinal)
            && gridPresentation.Organization.Contains(
                "★★★★",
                StringComparison.Ordinal)
            && gridPresentation.ActionCount == 2,
            "Grid browse parity lost filename/size/favorite/rating or direct detail/open actions.");

        viewer.SetLayout(
            ViewerLayoutMode.List,
            densityLevel: 1);

        for (var attempt = 0;
             attempt < 250
             && (!viewer.IsAssetReady(0)
                 || viewer.RealizedRowCount == 0);
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        var listPresentation =
            viewer.GetRealizedTilePresentationForSmoke(0);
        Require(
            listPresentation.Primary == "asset-000000.jpg"
            && listPresentation.Secondary.Contains(
                "fixture/organized",
                StringComparison.Ordinal)
            && listPresentation.Secondary.Contains(
                "MB",
                StringComparison.Ordinal)
            && listPresentation.Secondary.Contains(
                "♥",
                StringComparison.Ordinal)
            && listPresentation.Secondary.Contains(
                "★★★★",
                StringComparison.Ordinal)
            && listPresentation.ActionCount == 2,
            "List browse parity lost folder/file-size/organization information or direct detail/open actions.");

        foreach (var width in new[] { 520d, 1200d })
        {
            window.Width = width;
            for (var attempt = 0;
                 attempt < 100
                 && !viewer.IsAssetReady(0);
                 attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(1);
            }

            var responsivePresentation =
                viewer.GetRealizedTilePresentationForSmoke(0);
            Require(
                responsivePresentation.ActionCount == 2
                && !string.IsNullOrWhiteSpace(
                    responsivePresentation.Secondary),
                $"List browse information/actions were lost at {width:N0} DIP width.");
        }

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task VerifyThumbnailFailureRecoveryAsync(
        string thumbnailPath)
    {
        foreach (var layoutMode in
                 new[]
                 {
                     ViewerLayoutMode.Grid,
                     ViewerLayoutMode.List
                 })
        {
            var thumbnailProvider =
                new ControlledFailureThumbnailProvider(
                    thumbnailPath);

            await using var session =
                new ViewerSession(
                    new DirectFixtureAssetProvider(1),
                    thumbnailProvider,
                    new ViewerOptions
                    {
                        TileWidth = 180,
                        TileHeight = 180,
                        TileSpacing = 8,
                        PrefetchRows = 0,
                        DecodedBitmapEntryLimit = 8,
                        DecodedBitmapByteLimit =
                            8L * 1024 * 1024
                    });

            var viewer =
                new ThumbnailViewerControl(
                    session,
                    layoutMode,
                    densityLevel: 1);
            var window =
                new Window
                {
                    Width = 760,
                    Height = 500,
                    Content = viewer
                };

            window.Show();

            for (var attempt = 0;
                 attempt < 250
                 && !viewer
                     .IsRealizedTileFailedForSmoke(0);
                 attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(1);
            }

            Require(
                viewer.IsRealizedTileFailedForSmoke(0)
                && !viewer.IsAssetReady(0),
                $"{layoutMode} thumbnail failure did not enter an explicit failed state.");

            var failedPresentation =
                viewer.GetRealizedTilePresentationForSmoke(
                    0);
            var failureReason =
                viewer.GetRealizedTileFailureReasonForSmoke(
                    0);

            Require(
                failedPresentation.Secondary.Contains(
                    "読み込めません",
                    StringComparison.Ordinal)
                && string.Equals(
                    failureReason,
                    "画像ファイルを読み込めませんでした。",
                    StringComparison.Ordinal)
                && !failureReason.Contains(
                    "private",
                    StringComparison.OrdinalIgnoreCase),
                $"{layoutMode} thumbnail failure exposed raw exception detail or lost the user-facing failure label.");

            viewer.SelectAsset(0);
            Require(
                viewer.SelectedAssetIndex == 0,
                $"{layoutMode} failed tile could not remain selectable.");

            var requestsBeforeRetry =
                thumbnailProvider.Requests;
            thumbnailProvider.AllowSuccess();
            viewer.RetryRealizedTileForSmoke(0);

            for (var attempt = 0;
                 attempt < 250
                 && !viewer.IsAssetReady(0);
                 attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(1);
            }

            Require(
                viewer.IsAssetReady(0)
                && !viewer
                    .IsRealizedTileFailedForSmoke(0)
                && thumbnailProvider.Requests
                    == requestsBeforeRetry + 1,
                $"{layoutMode} thumbnail retry did not recover through exactly one bounded user retry.");

            var recoveredPresentation =
                viewer.GetRealizedTilePresentationForSmoke(
                    0);
            Require(
                recoveredPresentation.Primary
                    == "asset-000000.jpg"
                && recoveredPresentation.ActionCount == 2,
                $"{layoutMode} thumbnail retry did not restore the normal browse presentation/actions.");

            window.Close();
            Dispatcher.UIThread.RunJobs();
            await viewer.DrainBitmapReleasesAsync()
                .WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static async Task VerifyDetailViewerAsync(string previewPath)
    {
        var assets = new DirectFixtureAssetProvider(3);
        var provider = new DelayedDetailProvider(
            previewPath,
            TimeSpan.FromMilliseconds(160));

        await using var detailSession = new ViewerDetailSession(
            assets,
            provider,
            new ViewerDetailOptions
            {
                PreviewDecodedEntryLimit = 2,
                PreviewDecodedByteLimit = 4L * 1024 * 1024,
                OriginalDecodedByteLimit = 8L * 1024 * 1024,
                MinZoom = 0.05,
                MaxZoom = 8,
                ZoomStep = 1.25
            });

        await using var gridSession = new ViewerSession(
            assets,
            new ImmediateThumbnailProvider(previewPath),
            new ViewerOptions
            {
                PrefetchRows = 0,
                DecodedBitmapEntryLimit = 8,
                DecodedBitmapByteLimit = 8L * 1024 * 1024
            });

        var grid = new ThumbnailViewerControl(gridSession);
        var detail = new DetailViewerControl(detailSession);
        detail.BindGrid(grid);

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("320,*")
        };
        layout.Children.Add(grid);
        Grid.SetColumn(detail, 1);
        layout.Children.Add(detail);

        var window = new Window
        {
            Width = 1200,
            Height = 800,
            Content = layout
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var fullScreenRequests = 0;
        var closeRequests = 0;
        detail.FullScreenToggleRequested +=
            (_, _) =>
                fullScreenRequests++;
        detail.CloseRequested +=
            (_, _) =>
                closeRequests++;

        RaiseKey(detail, Key.F11);
        Require(
            fullScreenRequests == 1,
            "F11 did not request focused-view full-screen toggle.");

        detail.RequestCloseForSmoke();
        Require(
            closeRequests == 1,
            "Integrated focused-view close command did not raise CloseRequested.");

        await detail.ActualSizeAsync();
        await detail.SetZoomAsync(2);
        Require(
            provider.OriginalRequests == 0,
            "Detail attempted original load with no selection.");

        grid.SelectAsset(1);
        await WaitForDetailAsync(
            detailSession,
            snapshot =>
                snapshot.SelectedIndex == 1
                && snapshot.State == ViewerDetailLoadState.PreviewReady);

        Require(
            provider.PreviewRequests == 1,
            "Detail selection did not request exactly one persistent preview.");
        Require(
            provider.OriginalRequests == 0,
            "Detail selection eagerly loaded the original before 1:1/high zoom.");
        Require(
            !detail.IsOriginal,
            "Detail incorrectly reported original residency while preview-only.");
        Require(
            detail.MetadataText.Contains("1024×768", StringComparison.Ordinal)
            && detail.MetadataText.Contains("png", StringComparison.Ordinal)
            && detail.IsMetadataInVisualTreeForSmoke
            && !detail.IsMetadataVisibleForSmoke,
            "Detail preview metadata was not available as hidden viewer UI without opening original.");

        detail.ToggleMetadataForSmoke();
        Dispatcher.UIThread.RunJobs();
        Require(
            detail.IsMetadataVisibleForSmoke
            && detail.MetadataBoundsInControlForSmoke.Width > 0
            && detail.MetadataBoundsInControlForSmoke.Height > 0,
            "Viewer info command did not expose metadata in the visual tree.");
        detail.ToggleMetadataForSmoke();
        Dispatcher.UIThread.RunJobs();
        Require(
            !detail.IsMetadataVisibleForSmoke,
            "Viewer info command did not hide metadata.");

        var densityOriginalBefore = provider.OriginalRequests;
        await detail.SetZoomAsync(0.5);
        Require(
            provider.OriginalRequests == densityOriginalBefore + 1
            && detailSession.Snapshot.IsOriginal,
            "Detail kept stretching a preview after requested source-pixel density exceeded the preview bitmap.");

        var sliderStartZoom = detail.Zoom;
        var sliderStart =
            detail.ZoomSliderValueForSmoke;
        await detail.SetZoomSliderForSmokeAsync(70);
        Dispatcher.UIThread.RunJobs();
        Require(
            Math.Abs(detail.Zoom - sliderStartZoom) > 0.001
            && Math.Abs(
                detail.ZoomSliderValueForSmoke - 70) < 0.5,
            "Viewer zoom slider did not drive the existing zoom pipeline.");

        var sliderBeforeWheel =
            detail.ZoomSliderValueForSmoke;
        await detail.ApplyWheelZoomForSmokeAsync(1);
        Dispatcher.UIThread.RunJobs();
        Require(
            detail.ZoomSliderValueForSmoke > sliderBeforeWheel,
            "Wheel zoom did not synchronize the visible zoom slider.");

        detail.Fit();
        Dispatcher.UIThread.RunJobs();

        await detailSession.SelectAsync(0);
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 0
                && snapshot.State == ViewerDetailLoadState.PreviewReady);
        await detailSession.SelectAsync(1);
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 1
                && snapshot.State == ViewerDetailLoadState.PreviewReady);

        var raceStartZoom = detail.Zoom;
        var raceFirst = detail.ZoomByAsync(detailSession.Options.ZoomStep);
        var raceSecond = detail.ZoomByAsync(detailSession.Options.ZoomStep);
        await Task.WhenAll(raceFirst, raceSecond);

        var expectedRaceZoom = Math.Clamp(
            raceStartZoom
            * detailSession.Options.ZoomStep
            * detailSession.Options.ZoomStep,
            detailSession.Options.MinZoom,
            detailSession.Options.MaxZoom);
        Require(
            Math.Abs(detail.Zoom - expectedRaceZoom) < 0.001,
            $"Concurrent zoom commands collapsed or completed out of order: actual={detail.Zoom}, expected={expectedRaceZoom}.");

        await detail.SelectAsync(0);
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 0
                && snapshot.State == ViewerDetailLoadState.PreviewReady);
        await detail.SelectAsync(1);
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 1
                && snapshot.State == ViewerDetailLoadState.PreviewReady);

        var originalBefore = provider.OriginalRequests;
        var originalFirst = detailSession.EnsureOriginalAsync();
        var originalSecond = detailSession.EnsureOriginalAsync();
        await Task.WhenAll(originalFirst, originalSecond);

        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.State == ViewerDetailLoadState.OriginalReady);

        Require(
            provider.OriginalRequests == originalBefore + 1,
            "Concurrent original requests were not coalesced.");
        Require(detailSession.Snapshot.IsOriginal, "Full-resolution original did not become active.");

        await detail.ActualSizeAsync();
        Require(
            Math.Abs(detail.Zoom - 1) < 0.001,
            "Actual-size command did not set 1:1 zoom.");

        var twoXDisplay = DetailViewerControl.CalculateDisplaySize(
            new PixelSize(
                detailSession.Snapshot.Metadata!.Width,
                detailSession.Snapshot.Metadata.Height),
            1,
            2);
        Require(
            Math.Abs(twoXDisplay.Width - 512) < 0.001
            && Math.Abs(twoXDisplay.Height - 384) < 0.001,
            "Actual-size DPI conversion did not preserve one source pixel per physical pixel at 200% scaling.");

        await detail.SetZoomAsync(2);
        Dispatcher.UIThread.RunJobs();
        Require(
            Math.Abs(detail.Zoom - 2) < 0.001
            && detail.CanPanForSmoke,
            "Detail zoom command did not create a draggable oversized image.");

        Require(
            detail.HasPanCursorForSmoke,
            "Zoomed oversized image did not expose a visible pan affordance.");

        detail.PanBy(80, 60);
        Dispatcher.UIThread.RunJobs();
        Require(
            detail.PanOffset.X > 0 || detail.PanOffset.Y > 0,
            "Detail pan did not change scroll offset at high zoom.");

        RaiseKey(detail, Key.Right);
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 2
                && snapshot.State == ViewerDetailLoadState.PreviewReady);
        Dispatcher.UIThread.RunJobs();

        Require(
            detail.Zoom < 2
            && detail.PanOffset.X < 0.001
            && detail.PanOffset.Y < 0.001,
            "Previous/next navigation retained stale zoom or pan instead of fitting the new asset.");

        await detail.SelectAsync(1);
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 1
                && snapshot.State == ViewerDetailLoadState.PreviewReady);

        detail.Fit();
        Dispatcher.UIThread.RunJobs();
        Require(
            detail.Zoom > 0
            && detail.Zoom <= detailSession.Options.MaxZoom,
            "Detail fit produced an invalid zoom.");

        var wheelStartZoom = detail.Zoom;
        var wheelStartPan = detail.PanOffset;
        await detail.ApplyWheelZoomForSmokeAsync(1);
        Dispatcher.UIThread.RunJobs();
        Require(
            detail.Zoom > wheelStartZoom,
            "Focused-view wheel gesture did not zoom in.");

        var afterZoom = detail.Zoom;
        var afterZoomPan = detail.PanOffset;
        await detail.ApplyWheelZoomForSmokeAsync(0);
        Dispatcher.UIThread.RunJobs();
        Require(
            Math.Abs(detail.Zoom - afterZoom) < 0.001
            && detail.PanOffset == afterZoomPan,
            "A non-vertical wheel gesture mutated focused-view pan/zoom state.");

        detail.Fit();
        Dispatcher.UIThread.RunJobs();
        Require(
            detail.PanOffset.X < 0.001
            && detail.PanOffset.Y < 0.001,
            $"Fit did not clear pan after wheel zoom: before={wheelStartPan}, after={detail.PanOffset}.");

        var keyboardZoomStart =
            detail.Zoom;
        RaiseKey(
            detail,
            Key.OemPlus);
        for (var attempt = 0;
             attempt < 100
             && detail.Zoom <= keyboardZoomStart;
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }
        Require(
            detail.Zoom > keyboardZoomStart,
            "Viewer '+' shortcut did not zoom in.");

        var keyboardZoomPeak =
            detail.Zoom;
        RaiseKey(
            detail,
            Key.OemMinus);
        for (var attempt = 0;
             attempt < 100
             && detail.Zoom >= keyboardZoomPeak;
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }
        Require(
            detail.Zoom < keyboardZoomPeak,
            "Viewer '-' shortcut did not zoom out.");

        detail.Fit();
        Dispatcher.UIThread.RunJobs();

        var viewportBounds =
            detail.ViewportBoundsInControlForSmoke;
        var imageBounds =
            detail.ImageBoundsInControlForSmoke;
        var imageCenterX =
            imageBounds.X
            + (imageBounds.Width / 2);
        var imageCenterY =
            imageBounds.Y
            + (imageBounds.Height / 2);
        var viewportCenterX =
            viewportBounds.X
            + (viewportBounds.Width / 2);
        var viewportCenterY =
            viewportBounds.Y
            + (viewportBounds.Height / 2);

        Require(
            Math.Abs(
                imageCenterX
                - viewportCenterX) <= 1.5
            && Math.Abs(
                imageCenterY
                - viewportCenterY) <= 1.5,
            "Fit image did not start visually centered in the Viewer viewport.");

        var toolbarBounds =
            detail.ToolbarBoundsInControlForSmoke;
        var utilityBounds =
            detail.UtilityBoundsInControlForSmoke;
        var previousBounds =
            detail.PreviousBoundsInControlForSmoke;
        var nextBounds =
            detail.NextBoundsInControlForSmoke;
        var closeBounds =
            detail.CloseButtonBoundsInControlForSmoke;
        var zoomSliderBounds =
            detail.ZoomSliderBoundsInControlForSmoke;

        Require(
            detail.UsesLightweightChromeForSmoke,
            "Focused Viewer chrome regressed to heavy bordered command blocks.");

        Require(
            toolbarBounds.Top >= viewportBounds.Top
            && toolbarBounds.Bottom <= viewportBounds.Top + 96
            && utilityBounds.Top >= viewportBounds.Top
            && utilityBounds.Bottom <= viewportBounds.Top + 96
            && utilityBounds.Right <= viewportBounds.Right
            && toolbarBounds.Right + 4 <= utilityBounds.Left
            && previousBounds.Left >= viewportBounds.Left
            && previousBounds.Right <= viewportBounds.Left + 96
            && nextBounds.Right <= viewportBounds.Right
            && nextBounds.Left >= viewportBounds.Right - 96
            && closeBounds.Left >= utilityBounds.Left
            && closeBounds.Right <= utilityBounds.Right
            && closeBounds.Top >= utilityBounds.Top
            && closeBounds.Bottom <= utilityBounds.Bottom
            && zoomSliderBounds.Left >= toolbarBounds.Left
            && zoomSliderBounds.Right <= toolbarBounds.Right
            && zoomSliderBounds.Top >= toolbarBounds.Top
            && zoomSliderBounds.Bottom <= toolbarBounds.Bottom,
            "Viewer command groups escaped their safe bands, overlapped, or mixed window commands into the zoom group.");

        detail.FocusCloseForSmoke();
        detail.FadeChromeForSmoke();
        Require(
            detail.IsChromeVisibleForSmoke,
            "Viewer chrome auto-hid while a chrome command held keyboard focus.");

        detail.Focus();
        detail.FadeChromeForSmoke();
        Require(
            detail.IsChromeNonBlockingForSmoke,
            "Idle Viewer chrome remained visible or intercepted the image surface.");

        detail.RevealChromeForSmoke();
        Require(
            detail.IsChromeVisibleForSmoke,
            "Viewer chrome did not fully return after interaction.");

        await detailSession.SelectAsync(0);
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 0
                && snapshot.State == ViewerDetailLoadState.PreviewReady);

        var cancelledBefore = provider.CancelledOriginals;
        var staleOriginal = detailSession.EnsureOriginalAsync();

        for (var attempt = 0;
             attempt < 300 && provider.ActiveOriginalLoads == 0;
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        Require(
            provider.ActiveOriginalLoads > 0,
            "Rapid-navigation test never started original decode.");

        await detailSession.SelectAsync(2);
        await staleOriginal;

        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 2
                && snapshot.State == ViewerDetailLoadState.PreviewReady);

        Require(
            provider.CancelledOriginals > cancelledBefore,
            "Rapid navigation did not cancel stale original decode.");

        await detailSession.SelectAsync(0);
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 0
                && snapshot.State == ViewerDetailLoadState.PreviewReady);

        var firstCrossSelectionOriginal = detailSession.EnsureOriginalAsync();
        for (var attempt = 0;
             attempt < 300 && provider.ActiveOriginalLoads == 0;
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        Require(
            provider.ActiveOriginalLoads == 1,
            "Cross-selection admission test never started the first original.");

        await detailSession.SelectAsync(1);
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 1
                && snapshot.State == ViewerDetailLoadState.PreviewReady);

        var secondCrossSelectionOriginal = detailSession.EnsureOriginalAsync();
        await Task.WhenAll(
            firstCrossSelectionOriginal,
            secondCrossSelectionOriginal);

        Require(
            provider.PeakActiveOriginalLoads == 1,
            $"Full-resolution providers overlapped across selections: peak={provider.PeakActiveOriginalLoads}.");

        // Return to a different preview state before exercising Grid-driven
        // selection. The prior assertion intentionally leaves index 1 in
        // OriginalReady, so selecting index 1 again would be a no-op.
        await detailSession.SelectAsync(0);
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 0
                && snapshot.State == ViewerDetailLoadState.PreviewReady);

        grid.SelectAsset(1);
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 1
                && snapshot.State == ViewerDetailLoadState.PreviewReady);

        RaiseKey(detail, Key.Right);
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 2
                && snapshot.State == ViewerDetailLoadState.PreviewReady);
        Require(
            grid.SelectedAssetIndex == 2,
            "Detail previous/next navigation did not synchronize Grid selection.");

        await detail.SelectAsync(0);
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 0
                && snapshot.State == ViewerDetailLoadState.PreviewReady);
        await detail.SetZoomAsync(2);
        detail.PanBy(80, 60);
        Dispatcher.UIThread.RunJobs();

        await Task.Run(
            async () => await detailSession.SelectAsync(1));
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 1
                && snapshot.State == ViewerDetailLoadState.PreviewReady);
        Dispatcher.UIThread.RunJobs();

        Require(
            detail.Zoom < 2
            && detail.PanOffset.X < 0.001
            && detail.PanOffset.Y < 0.001,
            "External session selection retained stale Detail zoom or pan.");
        Require(
            grid.SelectedAssetIndex == 1,
            "Background session selection did not synchronize Grid on the UI dispatcher.");

        await detail.ActualSizeAsync();
        Require(
            detailSession.Snapshot.IsOriginal,
            "Reattach test did not establish an original before detaching Detail.");

        layout.Children.Remove(detail);
        Dispatcher.UIThread.RunJobs();

        Require(
            detailSession.Snapshot.State == ViewerDetailLoadState.Empty
            && detailSession.Snapshot.Bitmap is null,
            "Temporary Detail detach did not release the active image lifetime.");

        layout.Children.Add(detail);
        Grid.SetColumn(detail, 1);
        Dispatcher.UIThread.RunJobs();

        await WaitForDetailAsync(
            detailSession,
            snapshot =>
                snapshot.SelectedIndex == grid.SelectedAssetIndex
                && snapshot.State == ViewerDetailLoadState.PreviewReady);

        grid.SelectAsset(0);
        await WaitForDetailAsync(
            detailSession,
            static snapshot =>
                snapshot.SelectedIndex == 0
                && snapshot.State == ViewerDetailLoadState.PreviewReady);
        Require(
            detail.SelectedAssetIndex == 0,
            "Detail did not restore Grid selection synchronization after reattach.");

        await using (var budgetSession = new ViewerDetailSession(
                         assets,
                         provider,
                         new ViewerDetailOptions
                         {
                             PreviewDecodedEntryLimit = 2,
                             PreviewDecodedByteLimit = 4L * 1024 * 1024,
                             OriginalDecodedByteLimit = 1024
                         }))
        {
            await budgetSession.SelectAsync(0);
            await WaitForDetailAsync(
                budgetSession,
                static snapshot =>
                    snapshot.State == ViewerDetailLoadState.PreviewReady);

            await budgetSession.EnsureOriginalAsync();
            var budgetSnapshot = budgetSession.Snapshot;

            Require(
                budgetSnapshot.State == ViewerDetailLoadState.PreviewReady
                && budgetSnapshot.Bitmap is not null
                && !budgetSnapshot.IsOriginal
                && !string.IsNullOrWhiteSpace(budgetSnapshot.ErrorMessage),
                "Original budget failure did not safely retain preview state.");
        }

        await detail.ActualSizeAsync();
        Require(
            detailSession.Snapshot.IsOriginal,
            "Detail detach test did not establish an original bitmap.");

        detail.UnbindGrid();
        window.Close();
        Dispatcher.UIThread.RunJobs();

        Require(
            detailSession.Snapshot.State == ViewerDetailLoadState.Empty
            && detailSession.Snapshot.Bitmap is null,
            "Detaching Detail did not release the selected bitmap lifetime.");
    }

    private static async Task VerifyOriginalReleaseFenceBlocksAdmissionAsync(
        string previewPath)
    {
        var provider = new DelayedDetailProvider(
            previewPath,
            TimeSpan.Zero);
        await using var session = new ViewerDetailSession(
            new DirectFixtureAssetProvider(2),
            provider,
            new ViewerDetailOptions
            {
                PreviewDecodedEntryLimit = 2,
                PreviewDecodedByteLimit = 4L * 1024 * 1024,
                OriginalDecodedByteLimit = 8L * 1024 * 1024
            });

        var releaseFence =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseObserved =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        session.SetOriginalReleaseHandler(
            original =>
            {
                releaseObserved.TrySetResult();
                return original.DisposeAfterAsync(
                    releaseFence.Task);
            });

        await session.SelectAsync(0);
        await session.EnsureOriginalAsync();

        Require(
            provider.OriginalRequests == 1
            && session.Snapshot.IsOriginal,
            "Release-fence test did not establish the first original.");

        await session.SelectAsync(1);
        await releaseObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        var secondOriginal =
            session.EnsureOriginalAsync();

        Require(
            !secondOriginal.IsCompleted,
            "Second original admission ignored the pending visual release fence.");
        Require(
            provider.OriginalRequests == 1,
            "Second original provider load started before the prior visual release fence completed.");

        releaseFence.TrySetResult();

        await secondOriginal.WaitAsync(
            TimeSpan.FromSeconds(2));

        Require(
            provider.OriginalRequests == 2
            && session.Snapshot.IsOriginal
            && session.Snapshot.SelectedIndex == 1,
            "Second original did not proceed after the visual release fence completed.");
    }

    private static async Task VerifyDetailSessionShutdownAsync(
        string previewPath)
    {
        var provider = new BlockingPreviewDetailProvider(
            previewPath);
        var session = new ViewerDetailSession(
            new DirectFixtureAssetProvider(1),
            provider);

        var selection = session.SelectAsync(0);
        await provider.Started.WaitAsync(
            TimeSpan.FromSeconds(2));

        var firstDispose = session.DisposeAsync().AsTask();
        var secondDispose = session.DisposeAsync().AsTask();

        await provider.CancellationObserved.WaitAsync(
            TimeSpan.FromSeconds(2));

        Require(
            !firstDispose.IsCompleted
            && !secondDispose.IsCompleted,
            "Concurrent Detail DisposeAsync returned before active selection work drained.");

        provider.AllowExit();

        await Task.WhenAll(
            firstDispose,
            secondDispose).WaitAsync(
                TimeSpan.FromSeconds(2));
        await selection.WaitAsync(
            TimeSpan.FromSeconds(2));

        Require(
            provider.ActivePreviewLoads == 0,
            "Detail DisposeAsync returned before preview provider work drained.");
        Require(
            session.PreviewBitmapCache.Diagnostics.ActiveDecodes == 0,
            "Detail DisposeAsync returned before preview cache decode drained.");
    }

    private static async Task VerifyDetailObserverIsolationAsync(
        string previewPath)
    {
        var session = new ViewerDetailSession(
            new DirectFixtureAssetProvider(1),
            new DelayedDetailProvider(
                previewPath,
                TimeSpan.FromMilliseconds(10)));

        var stateNotifications = 0;
        var selectionNotifications = 0;

        session.StateChanged += static (_, _) =>
            throw new InvalidOperationException(
                "Synthetic StateChanged observer failure.");
        session.StateChanged += (_, _) =>
            Interlocked.Increment(ref stateNotifications);
        session.SelectedIndexChanged += static (_, _) =>
            throw new InvalidOperationException(
                "Synthetic SelectedIndexChanged observer failure.");
        session.SelectedIndexChanged += (_, _) =>
            Interlocked.Increment(ref selectionNotifications);

        await session.SelectAsync(0);
        await WaitForDetailAsync(
            session,
            static snapshot =>
                snapshot.State == ViewerDetailLoadState.PreviewReady);

        Require(
            stateNotifications >= 2,
            "Throwing StateChanged observer prevented later observers or state publication.");
        Require(
            selectionNotifications == 1,
            "Throwing SelectedIndexChanged observer interrupted selection publication.");

        await session.DisposeAsync();
    }

    private static async Task VerifyDetailSelectionCallerCancellationAsync(
        string previewPath)
    {
        var assets = new DirectFixtureAssetProvider(1);
        var provider = new DelayedDetailProvider(
            previewPath,
            TimeSpan.FromMilliseconds(10),
            previewDelay: TimeSpan.FromMilliseconds(120));

        await using var session = new ViewerDetailSession(
            assets,
            provider);

        using var cancellation = new CancellationTokenSource();
        var cancelledSelection = session.SelectAsync(
            0,
            cancellation.Token);

        for (var attempt = 0;
             attempt < 300 && provider.ActivePreviewLoads == 0;
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        Require(
            provider.ActivePreviewLoads == 1,
            "Caller-cancellation regression never entered preview loading.");

        cancellation.Cancel();
        await ExpectCancellationAsync(cancelledSelection);

        Require(
            session.Snapshot.SelectedIndex == 0
            && session.Snapshot.State == ViewerDetailLoadState.Error
            && session.Snapshot.Bitmap is null,
            $"Caller-cancelled selection was not left retryable: state={session.Snapshot.State}, index={session.Snapshot.SelectedIndex}.");
        Require(
            provider.CancelledPreviews > 0,
            "Preview provider did not observe caller cancellation.");

        await session.SelectAsync(0);
        await WaitForDetailAsync(
            session,
            static snapshot =>
                snapshot.SelectedIndex == 0
                && snapshot.State == ViewerDetailLoadState.PreviewReady
                && snapshot.Bitmap is not null);

        Require(
            provider.PreviewRequests >= 2,
            "Retrying the same asset after caller cancellation was incorrectly short-circuited.");

        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();

        await ExpectCancellationAsync(
            session.SelectAsync(0, preCancelled.Token));

        Require(
            session.Snapshot.State == ViewerDetailLoadState.PreviewReady
            && session.Snapshot.SelectedIndex == 0,
            "A pre-cancelled no-op selection corrupted the existing ready snapshot.");

        using var completedCaller = new CancellationTokenSource();
        session.Clear();
        await session.SelectAsync(0, completedCaller.Token);
        await WaitForDetailAsync(
            session,
            static snapshot =>
                snapshot.SelectedIndex == 0
                && snapshot.State == ViewerDetailLoadState.PreviewReady
                && snapshot.Bitmap is not null);

        completedCaller.Cancel();

        await session.EnsureOriginalAsync();
        await WaitForDetailAsync(
            session,
            static snapshot =>
                snapshot.State == ViewerDetailLoadState.OriginalReady
                && snapshot.IsOriginal);

        Require(
            session.Snapshot.IsOriginal,
            "Cancelling a caller token after SelectAsync completed poisoned the active selection lifetime.");

        session.Clear();
        await session.SelectAsync(0);
        await WaitForDetailAsync(
            session,
            static snapshot =>
                snapshot.State == ViewerDetailLoadState.PreviewReady);

        var originalRequestsBeforeCancelledWait =
            provider.OriginalRequests;
        using (var cancelledOriginalWait =
               new CancellationTokenSource())
        {
            var originalWait = session.EnsureOriginalAsync(
                cancelledOriginalWait.Token);

            for (var attempt = 0;
                 attempt < 300 && provider.ActiveOriginalLoads == 0;
                 attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(1);
            }

            Require(
                provider.ActiveOriginalLoads == 1,
                "Original caller-cancellation matrix never started the shared original load.");

            cancelledOriginalWait.Cancel();
            await ExpectCancellationAsync(originalWait);
        }

        await WaitForDetailAsync(
            session,
            static snapshot =>
                snapshot.State == ViewerDetailLoadState.OriginalReady
                && snapshot.IsOriginal);

        Require(
            provider.OriginalRequests
                == originalRequestsBeforeCancelledWait + 1,
            "Cancelling an EnsureOriginalAsync waiter cancelled or duplicated the shared selection load.");

        session.Clear();
        await session.SelectAsync(0);
        var originalRequestsBeforePreCancelled =
            provider.OriginalRequests;

        using (var preCancelledOriginal =
               new CancellationTokenSource())
        {
            preCancelledOriginal.Cancel();
            await ExpectCancellationAsync(
                session.EnsureOriginalAsync(
                    preCancelledOriginal.Token));
        }

        Require(
            session.Snapshot.State
                == ViewerDetailLoadState.PreviewReady
            && provider.OriginalRequests
                == originalRequestsBeforePreCancelled,
            "Pre-cancelled EnsureOriginalAsync mutated state or started a provider load.");

        using var cancelledCommand = new CancellationTokenSource();
        cancelledCommand.Cancel();

        var detail = new DetailViewerControl(session);
        var zoomBefore = detail.Zoom;

        await ExpectCancellationAsync(
            detail.SetZoomAsync(2, cancelledCommand.Token));
        Require(
            Math.Abs(detail.Zoom - zoomBefore) < 0.001,
            "Pre-cancelled SetZoomAsync mutated zoom.");

        await ExpectCancellationAsync(
            detail.ZoomByAsync(1.25, cancelledCommand.Token));
        Require(
            Math.Abs(detail.Zoom - zoomBefore) < 0.001,
            "Pre-cancelled ZoomByAsync mutated zoom.");

        await ExpectCancellationAsync(
            detail.ActualSizeAsync(cancelledCommand.Token));
        Require(
            Math.Abs(detail.Zoom - zoomBefore) < 0.001,
            "Pre-cancelled ActualSizeAsync mutated zoom.");

        await ExpectCancellationAsync(
            session.EnsureOriginalAsync(cancelledCommand.Token));
        Require(
            session.Snapshot.State == ViewerDetailLoadState.PreviewReady,
            "Pre-cancelled EnsureOriginalAsync mutated the current preview state.");
    }

    private static async Task VerifyUnknownMetadataPromotionAsync(
        string previewPath)
    {
        var assets = new DirectFixtureAssetProvider(
            1,
            width: null,
            height: null);
        var provider = new DelayedDetailProvider(
            previewPath,
            TimeSpan.FromMilliseconds(30),
            originalWidth: 16,
            originalHeight: 16);

        await using var session = new ViewerDetailSession(
            assets,
            provider,
            new ViewerDetailOptions
            {
                PreviewDecodedEntryLimit = 2,
                PreviewDecodedByteLimit = 4L * 1024 * 1024,
                OriginalDecodedByteLimit = 4L * 1024 * 1024,
                MinZoom = 0.05,
                MaxZoom = 1000,
                ZoomStep = 1.25
            });

        var detail = new DetailViewerControl(session);
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = detail
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        await detail.SelectAsync(0);
        await WaitForDetailAsync(
            session,
            static snapshot =>
                snapshot.SelectedIndex == 0
                && snapshot.State == ViewerDetailLoadState.PreviewReady);

        detail.Fit();
        var previewZoom = detail.Zoom;
        var expectedPromotedZoom = Math.Clamp(
            DetailViewerControl.CalculatePromotedZoom(
                session.Snapshot.Bitmap!.PixelSize,
                new PixelSize(16, 16),
                Math.Clamp(
                    previewZoom
                    * session.Options.ZoomStep
                    * session.Options.ZoomStep,
                    session.Options.MinZoom,
                    session.Options.MaxZoom)),
            session.Options.MinZoom,
            session.Options.MaxZoom);

        var firstZoom = detail.ZoomByAsync(session.Options.ZoomStep);
        var secondZoom = detail.ZoomByAsync(session.Options.ZoomStep);
        await Task.WhenAll(firstZoom, secondZoom);

        Require(
            session.Snapshot.IsOriginal,
            "Unknown-metadata zoom did not promote the preview to the original.");
        Require(
            Math.Abs(detail.Zoom - expectedPromotedZoom) < 0.001,
            $"Preview-to-original promotion changed visual scale: actual={detail.Zoom}, expected={expectedPromotedZoom}.");

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task VerifyUnknownMetadataPromotionReversalAsync(
        string previewPath)
    {
        var assets = new DirectFixtureAssetProvider(
            1,
            width: null,
            height: null);
        var provider = new DelayedDetailProvider(
            previewPath,
            TimeSpan.FromMilliseconds(120),
            originalWidth: 16,
            originalHeight: 16);

        await using var session = new ViewerDetailSession(
            assets,
            provider,
            new ViewerDetailOptions
            {
                PreviewDecodedEntryLimit = 2,
                PreviewDecodedByteLimit = 4L * 1024 * 1024,
                OriginalDecodedByteLimit = 4L * 1024 * 1024,
                MinZoom = 0.01,
                MaxZoom = 1000,
                ZoomStep = 1.25
            });

        var detail = new DetailViewerControl(session);
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = detail
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        await detail.SelectAsync(0);
        await WaitForDetailAsync(
            session,
            static snapshot =>
                snapshot.SelectedIndex == 0
                && snapshot.State == ViewerDetailLoadState.PreviewReady);

        await detail.SetZoomAsync(0.8);
        var previewBasis = session.Snapshot.Bitmap!.PixelSize;

        var promote = detail.ZoomByAsync(session.Options.ZoomStep);

        for (var attempt = 0;
             attempt < 300 && provider.ActiveOriginalLoads == 0;
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        Require(
            provider.ActiveOriginalLoads == 1,
            "Reverse zoom race never started the original decode.");

        await detail.ZoomByAsync(0.5);
        var previewZoomAfterReversal = detail.Zoom;

        Require(
            Math.Abs(previewZoomAfterReversal - 0.5) < 0.001,
            $"Reverse zoom command did not commit in preview space: {previewZoomAfterReversal}.");

        var expectedOriginalZoom = Math.Clamp(
            DetailViewerControl.CalculatePromotedZoom(
                previewBasis,
                new PixelSize(16, 16),
                previewZoomAfterReversal),
            session.Options.MinZoom,
            session.Options.MaxZoom);

        await promote;
        await WaitForDetailAsync(
            session,
            static snapshot =>
                snapshot.State == ViewerDetailLoadState.OriginalReady);

        for (var attempt = 0;
             attempt < 300
             && Math.Abs(detail.Zoom - expectedOriginalZoom) >= 0.001;
             attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }

        Require(
            Math.Abs(detail.Zoom - expectedOriginalZoom) < 0.001,
            $"Late original reinterpreted committed preview zoom: actual={detail.Zoom}, expected={expectedOriginalZoom}.");

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task VerifyOriginalFailureKeepsFitAsync(
        string previewPath)
    {
        var assets = new DirectFixtureAssetProvider(
            1,
            width: 16,
            height: 12);
        var provider = new DelayedDetailProvider(
            previewPath,
            TimeSpan.FromMilliseconds(10),
            originalWidth: 16,
            originalHeight: 12);

        await using var session = new ViewerDetailSession(
            assets,
            provider,
            new ViewerDetailOptions
            {
                PreviewDecodedEntryLimit = 2,
                PreviewDecodedByteLimit = 4L * 1024 * 1024,
                OriginalDecodedByteLimit = 128,
                MinZoom = 0.05,
                MaxZoom = 100,
                ZoomStep = 1.25
            });

        var detail = new DetailViewerControl(session);
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = detail
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        await detail.SelectAsync(0);
        await WaitForDetailAsync(
            session,
            static snapshot =>
                snapshot.SelectedIndex == 0
                && snapshot.State == ViewerDetailLoadState.PreviewReady);

        detail.Fit();
        Require(detail.IsFitMode, "Detail did not start the failure test in Fit mode.");

        await detail.SetZoomAsync(1);

        Require(
            session.Snapshot.State == ViewerDetailLoadState.PreviewReady
            && !session.Snapshot.IsOriginal
            && !string.IsNullOrWhiteSpace(session.Snapshot.ErrorMessage),
            "Original failure did not safely retain the preview.");
        Require(
            detail.IsFitMode,
            "Failed original promotion incorrectly disabled Fit mode.");

        var beforeResize = detail.Zoom;
        window.Width = 480;
        window.Height = 360;
        Dispatcher.UIThread.RunJobs();

        Require(
            detail.IsFitMode,
            "Window resize after original failure left Fit mode.");
        Require(
            detail.Zoom < beforeResize,
            $"Fit did not react to resize after original failure: before={beforeResize}, after={detail.Zoom}.");

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<ViewerDetailSnapshot> WaitForDetailAsync(
        ViewerDetailSession session,
        Func<ViewerDetailSnapshot, bool> predicate)
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            var snapshot = session.Snapshot;

            if (predicate(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(1);
        }

        throw new InvalidOperationException(
            $"Detail viewer did not reach expected state; current={session.Snapshot.State}, index={session.Snapshot.SelectedIndex}.");
    }

    private static void RaiseKey(InputElement viewer, Key key)
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
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApplication>()
            .UseHarfBuzz()
            .UseSkia()
            .UseHeadless(
                new AvaloniaHeadlessPlatformOptions
                {
                    UseHeadlessDrawing = false,
                    OverlayPopups = false
                });

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

internal sealed class DelayedAssetProvider(
    long count,
    TimeSpan delay) : IViewerAssetProvider
{
    private int _active;
    private int _cancelled;

    public long Count { get; } = count;

    public int Active => Volatile.Read(ref _active);

    public int Cancelled => Volatile.Read(ref _cancelled);

    public async ValueTask<ViewerAsset> GetAssetAsync(
        long index,
        CancellationToken cancellationToken = default)
    {
        if ((ulong)index >= (ulong)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Interlocked.Increment(ref _active);
        try
        {
            try
            {
                await Task.Delay(
                    delay,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancelled);
                throw;
            }

            return new ViewerAsset(
                index + 1,
                1,
                $"fixture/{index:D6}.jpg",
                $"asset-{index:D6}.jpg",
                10_000 + index,
                DateTimeOffset.UnixEpoch
                    .AddSeconds(index)
                    .UtcDateTime.Ticks);
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
}

internal sealed class DirectFixtureAssetProvider(
    long count,
    int? width = 1024,
    int? height = 768,
    string? format = "png",
    bool organizationMetadata = false) : IViewerAssetProvider
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
                organizationMetadata
                    ? $"fixture/organized/asset-{index:D6}.jpg"
                    : $"fixture/{index:D6}.jpg",
                $"asset-{index:D6}.jpg",
                organizationMetadata
                    ? 5L * 1024 * 1024
                    : 10_000 + index,
                DateTimeOffset.UnixEpoch.AddSeconds(index).UtcDateTime.Ticks,
                width,
                height,
                format,
                CreatedAtUtcTicks:
                    DateTimeOffset.UnixEpoch
                        .AddSeconds(index)
                        .UtcDateTime.Ticks,
                Rating:
                    organizationMetadata
                        ? 4
                        : null,
                Favorite:
                    organizationMetadata));
    }
}

internal sealed class GatedPriorityThumbnailProvider(
    string path) : IViewerThumbnailProvider
{
    private readonly TaskCompletionSource _foregroundRelease =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _foregroundRequests;
    private int _backgroundRequests;

    public int ForegroundRequests =>
        Volatile.Read(ref _foregroundRequests);

    public int BackgroundRequests =>
        Volatile.Read(ref _backgroundRequests);

    public void ReleaseForeground() =>
        _foregroundRelease.TrySetResult();

    public async ValueTask<ViewerThumbnail> RequestAsync(
        ViewerAsset asset,
        ViewerThumbnailPriority priority,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (priority == ViewerThumbnailPriority.Foreground)
        {
            Interlocked.Increment(
                ref _foregroundRequests);
            await _foregroundRelease.Task
                .WaitAsync(cancellationToken);
        }
        else
        {
            Interlocked.Increment(
                ref _backgroundRequests);
        }

        return new ViewerThumbnail(
            $"prefetch-gate-{asset.Id}",
            path,
            1,
            1);
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

internal sealed class ControlledFailureThumbnailProvider(
    string path) : IViewerThumbnailProvider
{
    private int _requests;
    private int _allowSuccess;

    public int Requests =>
        Volatile.Read(ref _requests);

    public void AllowSuccess() =>
        Interlocked.Exchange(
            ref _allowSuccess,
            1);

    public ValueTask<ViewerThumbnail> RequestAsync(
        ViewerAsset asset,
        ViewerThumbnailPriority priority,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _requests);

        if (Volatile.Read(ref _allowSuccess) == 0)
        {
            throw new IOException(
                @"C:\private\fixture\thumbnail.png could not be decoded.");
        }

        return ValueTask.FromResult(
            new ViewerThumbnail(
                $"fixture-{asset.Id}",
                path,
                1,
                1));
    }
}

internal sealed class PriorityGatedThumbnailProvider(
    string path) : IViewerThumbnailProvider
{
    private readonly TaskCompletionSource _foregroundStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _interactiveStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _foregroundRequests;
    private int _interactiveRequests;
    private int _cancelled;

    public Task ForegroundStarted =>
        _foregroundStarted.Task;

    public Task InteractiveStarted =>
        _interactiveStarted.Task;

    public int ForegroundRequests =>
        Volatile.Read(ref _foregroundRequests);

    public int InteractiveRequests =>
        Volatile.Read(ref _interactiveRequests);

    public int Cancelled =>
        Volatile.Read(ref _cancelled);

    public void Release() =>
        _release.TrySetResult();

    public async ValueTask<ViewerThumbnail> RequestAsync(
        ViewerAsset asset,
        ViewerThumbnailPriority priority,
        CancellationToken cancellationToken = default)
    {
        if (priority == ViewerThumbnailPriority.Interactive)
        {
            Interlocked.Increment(
                ref _interactiveRequests);
            _interactiveStarted.TrySetResult();
        }
        else if (priority == ViewerThumbnailPriority.Foreground)
        {
            Interlocked.Increment(
                ref _foregroundRequests);
            _foregroundStarted.TrySetResult();
        }

        try
        {
            await _release.Task.WaitAsync(
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(
                ref _cancelled);
            throw;
        }

        return new ViewerThumbnail(
            $"priority-gate-{asset.Id}",
            path,
            1,
            1);
    }
}

internal sealed class DelayedThumbnailProvider(
    string path,
    TimeSpan delay) : IViewerThumbnailProvider
{
    private readonly TaskCompletionSource _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _cancelled;
    private int _active;

    public int Cancelled => Volatile.Read(ref _cancelled);

    public int Active => Volatile.Read(ref _active);

    public Task Started => _started.Task;

    public async ValueTask<ViewerThumbnail> RequestAsync(
        ViewerAsset asset,
        ViewerThumbnailPriority priority,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _active);
        _started.TrySetResult();

        try
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
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
}

internal sealed class BlockingPreviewDetailProvider(
    string previewPath) : IViewerDetailProvider
{
    private readonly TaskCompletionSource _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _cancellationObserved =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _allowExit =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _activePreviewLoads;

    public Task Started => _started.Task;

    public Task CancellationObserved =>
        _cancellationObserved.Task;

    public int ActivePreviewLoads =>
        Volatile.Read(ref _activePreviewLoads);

    public void AllowExit() =>
        _allowExit.TrySetResult();

    public async ValueTask<ViewerThumbnail> RequestPreviewAsync(
        ViewerAsset asset,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _activePreviewLoads);
        _started.TrySetResult();

        using var registration =
            cancellationToken.Register(
                () => _cancellationObserved.TrySetResult());

        try
        {
            await _allowExit.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            return new ViewerThumbnail(
                $"blocking-preview-{asset.Id}",
                previewPath,
                1,
                1);
        }
        finally
        {
            Interlocked.Decrement(ref _activePreviewLoads);
        }
    }

    public Task<ViewerOriginalBitmap> LoadOriginalAsync(
        ViewerAsset asset,
        long maxDecodedBytes,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Blocking preview provider does not support original loading.");
}

internal sealed class DelayedDetailProvider(
    string previewPath,
    TimeSpan originalDelay,
    int originalWidth = 1024,
    int originalHeight = 768,
    TimeSpan? previewDelay = null) : IViewerDetailProvider
{
    private int _previewRequests;
    private int _cancelledPreviews;
    private int _activePreviewLoads;
    private int _originalRequests;
    private int _cancelledOriginals;
    private int _activeOriginalLoads;
    private int _peakActiveOriginalLoads;

    public int PreviewRequests => Volatile.Read(ref _previewRequests);

    public int CancelledPreviews => Volatile.Read(ref _cancelledPreviews);

    public int ActivePreviewLoads => Volatile.Read(ref _activePreviewLoads);

    public int OriginalRequests => Volatile.Read(ref _originalRequests);

    public int CancelledOriginals => Volatile.Read(ref _cancelledOriginals);

    public int ActiveOriginalLoads => Volatile.Read(ref _activeOriginalLoads);

    public int PeakActiveOriginalLoads =>
        Volatile.Read(ref _peakActiveOriginalLoads);

    public async ValueTask<ViewerThumbnail> RequestPreviewAsync(
        ViewerAsset asset,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _previewRequests);
        Interlocked.Increment(ref _activePreviewLoads);

        try
        {
            if (previewDelay is { } delay && delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref _cancelledPreviews);
                    throw;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            return new ViewerThumbnail(
                $"detail-preview-{asset.Id}",
                previewPath,
                1,
                1);
        }
        finally
        {
            Interlocked.Decrement(ref _activePreviewLoads);
        }
    }

    public async Task<ViewerOriginalBitmap> LoadOriginalAsync(
        ViewerAsset asset,
        long maxDecodedBytes,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _originalRequests);
        var active = Interlocked.Increment(ref _activeOriginalLoads);
        UpdatePeakActiveOriginalLoads(active);

        try
        {
            var required = checked(
                (long)originalWidth * originalHeight * 4L);
            if (required > maxDecodedBytes)
            {
                throw new InvalidOperationException(
                    $"Original requires {required:N0} bytes, above detail budget {maxDecodedBytes:N0}.");
            }

            try
            {
                await Task.Delay(originalDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancelledOriginals);
                await Task.Delay(
                    TimeSpan.FromMilliseconds(80),
                    CancellationToken.None);
                throw;
            }

            var bitmap = await Dispatcher.UIThread.InvokeAsync(
                () => new WriteableBitmap(
                    new PixelSize(originalWidth, originalHeight),
                    new Vector(96, 96),
                    PixelFormats.Rgba8888,
                    AlphaFormat.Unpremul));

            return new ViewerOriginalBitmap(
                bitmap,
                new ViewerDetailMetadata(
                    originalWidth,
                    originalHeight,
                    true,
                    "png",
                    asset.FileSize,
                    required));
        }
        finally
        {
            Interlocked.Decrement(ref _activeOriginalLoads);
        }
    }

    private void UpdatePeakActiveOriginalLoads(int active)
    {
        while (true)
        {
            var current = Volatile.Read(ref _peakActiveOriginalLoads);
            if (active <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(
                    ref _peakActiveOriginalLoads,
                    active,
                    current) == current)
            {
                return;
            }
        }
    }
}
