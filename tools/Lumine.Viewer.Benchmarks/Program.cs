using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumine.Diagnostics;
using Lumine.Image;
using Lumine.Library;
using Lumine.Viewer;
using NetVips;

namespace Lumine.Viewer.Benchmarks;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var output = ReadOption(args, "--output")
            ?? Path.Combine("artifacts", "benchmarks", "viewer-100000.json");

        var countText = ReadOption(args, "--count") ?? "100000";
        if (!long.TryParse(
                countText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var count)
            || count <= 0)
        {
            throw new ArgumentException("--count must be a positive integer.");
        }

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"lumine-viewer-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var thumbnailPaths = CreateThumbnailFixtures(tempRoot, 96);

        var recorder = new BenchmarkRecorder();
        long peakWorkingSetBytes = 0;
        long peakAdditionalWorkingSetBytes = 0;
        var maxRealizedRows = 0;
        var maxAttachedTiles = 0;
        var maxReadyTiles = 0;
        var maxDecodedBitmapEntries = 0;
        long maxDecodedBitmapBytes = 0;
        var maxConcurrentBitmapDecodes = 0;
        ViewerRuntimeDiagnostics finalDiagnostics = default;
        ViewerTileReadinessDiagnostics finalTileReadiness = default;
        ScrollProbeResult forwardWheel = default;
        ScrollProbeResult reverseWheel = default;
        var finalColumns = 0;
        long cursorEndSeekPages = 0;
        long cursorRandomSeekPages = 0;
        var cursorCachedPages = 0;
        var cursorCheckpoints = 0;

        try
        {
            await using var headless = HeadlessUnitTestSession.StartNew(typeof(BenchmarkApplication));
            var peak = PeakWorkingSetMonitor.Start();

            await headless.Dispatch(
                async () =>
                {
                    VerifyThumbnailFixture(thumbnailPaths[0]);
                    var thumbnailProvider = new DelayedBenchmarkThumbnailProvider(
                        thumbnailPaths,
                        TimeSpan.FromMilliseconds(12));

                    await using var session = new ViewerSession(
                        new FixtureAssetProvider(count),
                        thumbnailProvider,
                        new ViewerOptions
                        {
                            TileWidth = 160,
                            TileHeight = 190,
                            TileSpacing = 8,
                            PrefetchRows = 1,
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

                    using (recorder.Measure(CoreMetricNames.ViewerFirstPaint))
                    {
                        window.Show();
                        await WaitForRealizationAsync(viewer);
                    }

                    Observe(viewer);

                    using (recorder.Measure(CoreMetricNames.ViewerFirstViewportReady))
                    {
                        await WaitForViewportReadyAsync(viewer);
                    }

                    Observe(viewer);

                    // One row per small offset change reproduces normal
                    // browsing more closely than the legacy 40 random
                    // ScrollToAsset jumps. Capture both cold and reverse
                    // navigation, including first-frame blank tiles.
                    forwardWheel =
                        await MeasureSmallScrollAsync(
                            viewer,
                            reverse: false);
                    reverseWheel =
                        await MeasureSmallScrollAsync(
                            viewer,
                            reverse: true);
                    Observe(viewer);

                    using (recorder.Measure(CoreMetricNames.ViewerFastScrollRefresh))
                    {
                        const int jumps = 40;
                        for (var jump = 0; jump < jumps; jump++)
                        {
                            var fraction = jump / (double)(jumps - 1);
                            var index = Math.Min(
                                count - 1,
                                (long)Math.Round((count - 1) * fraction));

                            viewer.ScrollToAsset(index);
                            Dispatcher.UIThread.RunJobs();
                            Observe(viewer);
                            await Task.Delay(1);
                        }

                        await WaitForViewportReadyAsync(viewer);
                        Observe(viewer);
                    }

                    viewer.SelectAsset(count - 1);
                    Observe(viewer);

                    finalColumns = viewer.Columns;

                    window.Close();
                    await WaitForViewerIdleAsync(session);
                    finalDiagnostics = viewer.Diagnostics;
                    finalTileReadiness = viewer.TileReadiness;

                    if (finalDiagnostics.AttachedTiles != 0)
                    {
                        throw new InvalidOperationException(
                            $"Viewer teardown left {finalDiagnostics.AttachedTiles} tile(s) attached.");
                    }

                    if (finalDiagnostics.InFlightThumbnailRequests != 0)
                    {
                        throw new InvalidOperationException(
                            $"Viewer teardown left {finalDiagnostics.InFlightThumbnailRequests} thumbnail request(s) in-flight.");
                    }

                    return 0;

                    void Observe(ThumbnailViewerControl control)
                    {
                        var diagnostics = control.Diagnostics;

                        maxRealizedRows = Math.Max(
                            maxRealizedRows,
                            control.RealizedRowCount);
                        maxAttachedTiles = Math.Max(
                            maxAttachedTiles,
                            diagnostics.AttachedTiles);
                        maxReadyTiles = Math.Max(
                            maxReadyTiles,
                            diagnostics.ReadyTiles);
                        maxDecodedBitmapEntries = Math.Max(
                            maxDecodedBitmapEntries,
                            diagnostics.DecodedBitmapEntries);
                        maxDecodedBitmapBytes = Math.Max(
                            maxDecodedBitmapBytes,
                            diagnostics.DecodedBitmapBytes);
                        maxConcurrentBitmapDecodes = Math.Max(
                            maxConcurrentBitmapDecodes,
                            diagnostics.PeakConcurrentBitmapDecodes);
                    }
                },
                CancellationToken.None);

            await peak.DisposeAsync();
            peakWorkingSetBytes = peak.PeakWorkingSetBytes;
            peakAdditionalWorkingSetBytes = peak.PeakAdditionalWorkingSetBytes;

            if (count == 100_000)
            {
                var cursor = await MeasureLibraryCursorIntegrationAsync(
                    recorder,
                    tempRoot,
                    count);
                cursorEndSeekPages = cursor.EndSeekPages;
                cursorRandomSeekPages = cursor.RandomSeekPages;
                cursorCachedPages = cursor.CachedPages;
                cursorCheckpoints = cursor.Checkpoints;
            }

            await recorder.WriteJsonAsync(
                output,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["kind"] = "viewer-core",
                    ["asset_count"] = count.ToString(CultureInfo.InvariantCulture),
                    ["columns"] = finalColumns.ToString(CultureInfo.InvariantCulture),
                    ["max_realized_rows"] = maxRealizedRows.ToString(CultureInfo.InvariantCulture),
                    ["max_attached_tiles"] = maxAttachedTiles.ToString(CultureInfo.InvariantCulture),
                    ["max_ready_tiles"] = maxReadyTiles.ToString(CultureInfo.InvariantCulture),
                    ["max_decoded_bitmap_entries"] = maxDecodedBitmapEntries.ToString(CultureInfo.InvariantCulture),
                    ["max_decoded_bitmap_bytes"] = maxDecodedBitmapBytes.ToString(CultureInfo.InvariantCulture),
                    ["max_concurrent_bitmap_decodes"] = maxConcurrentBitmapDecodes.ToString(CultureInfo.InvariantCulture),
                    ["thumbnail_requests"] = finalDiagnostics.ThumbnailRequests.ToString(CultureInfo.InvariantCulture),
                    ["thumbnail_requests_coalesced"] = finalDiagnostics.ThumbnailRequestsCoalesced.ToString(CultureInfo.InvariantCulture),
                    ["thumbnail_requests_cancelled"] = finalDiagnostics.ThumbnailRequestsCancelled.ToString(CultureInfo.InvariantCulture),
                    ["thumbnail_requests_failed"] = finalDiagnostics.ThumbnailRequestsFailed.ToString(CultureInfo.InvariantCulture),
                    ["tile_load_failures"] = finalDiagnostics.TileLoadFailures.ToString(CultureInfo.InvariantCulture),
                    // UI bitmap assignment, not a compositor-present fence:
                    // keep these distinct from source thumbnail requests.
                    ["tile_load_started"] = finalTileReadiness.Started.ToString(CultureInfo.InvariantCulture),
                    ["tile_ui_ready"] = finalTileReadiness.Ready.ToString(CultureInfo.InvariantCulture),
                    ["tile_ui_ready_bitmap_cache_hits"] = finalTileReadiness.ReadyFromBitmapCache.ToString(CultureInfo.InvariantCulture),
                    ["tile_detached_before_ready"] = finalTileReadiness.CancelledBeforeReady.ToString(CultureInfo.InvariantCulture),
                    ["tile_attach_to_ui_ready_mean_ms"] = finalTileReadiness.MeanAttachToReadyMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                    ["tile_attach_to_ui_ready_max_ms"] = finalTileReadiness.MaxAttachToReadyMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                    ["tile_metadata_mean_ms"] = finalTileReadiness.MeanMetadataMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                    ["tile_thumbnail_source_mean_ms"] = finalTileReadiness.MeanThumbnailSourceMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                    ["tile_bitmap_acquire_mean_ms"] = finalTileReadiness.MeanBitmapAcquireMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                    ["small_scroll_forward_steps"] = forwardWheel.Steps.ToString(CultureInfo.InvariantCulture),
                    ["small_scroll_forward_missing_on_first_frame"] = forwardWheel.MissingOnFirstFrame.ToString(CultureInfo.InvariantCulture),
                    ["small_scroll_forward_max_ui_ready_wait_ms"] = forwardWheel.MaxWaitMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                    ["small_scroll_forward_warm_bitmap_hits"] = forwardWheel.WarmBitmapHits.ToString(CultureInfo.InvariantCulture),
                    ["small_scroll_reverse_steps"] = reverseWheel.Steps.ToString(CultureInfo.InvariantCulture),
                    ["small_scroll_reverse_missing_on_first_frame"] = reverseWheel.MissingOnFirstFrame.ToString(CultureInfo.InvariantCulture),
                    ["small_scroll_reverse_max_ui_ready_wait_ms"] = reverseWheel.MaxWaitMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                    ["small_scroll_reverse_warm_bitmap_hits"] = reverseWheel.WarmBitmapHits.ToString(CultureInfo.InvariantCulture),
                    ["last_tile_load_error"] = finalDiagnostics.LastTileLoadError ?? string.Empty,
                    ["inflight_thumbnail_requests"] = finalDiagnostics.InFlightThumbnailRequests.ToString(CultureInfo.InvariantCulture),
                    ["final_attached_tiles"] = finalDiagnostics.AttachedTiles.ToString(CultureInfo.InvariantCulture),
                    ["final_ready_tiles"] = finalDiagnostics.ReadyTiles.ToString(CultureInfo.InvariantCulture),
                    ["decoded_bitmap_entries"] = finalDiagnostics.DecodedBitmapEntries.ToString(CultureInfo.InvariantCulture),
                    ["decoded_bitmap_bytes"] = finalDiagnostics.DecodedBitmapBytes.ToString(CultureInfo.InvariantCulture),
                    ["active_bitmap_decodes"] = finalDiagnostics.ActiveBitmapDecodes.ToString(CultureInfo.InvariantCulture),
                    ["peak_concurrent_bitmap_decodes"] = finalDiagnostics.PeakConcurrentBitmapDecodes.ToString(CultureInfo.InvariantCulture),
                    ["bitmap_decode_concurrency_limit"] = DecodedBitmapCache.DecodeConcurrencyLimit.ToString(CultureInfo.InvariantCulture),
                    ["peak_working_set_bytes"] = peakWorkingSetBytes.ToString(CultureInfo.InvariantCulture),
                    ["peak_additional_working_set_bytes"] = peakAdditionalWorkingSetBytes.ToString(CultureInfo.InvariantCulture),
                    ["cursor_end_seek_pages"] = cursorEndSeekPages.ToString(CultureInfo.InvariantCulture),
                    ["cursor_random_seek_pages"] = cursorRandomSeekPages.ToString(CultureInfo.InvariantCulture),
                    ["cursor_cached_pages"] = cursorCachedPages.ToString(CultureInfo.InvariantCulture),
                    ["cursor_checkpoints"] = cursorCheckpoints.ToString(CultureInfo.InvariantCulture)
                });

            Console.WriteLine(
                $"Viewer benchmark: assets={count:N0}, realized rows max={maxRealizedRows}, tiles max={maxAttachedTiles}");
            Console.WriteLine($"Result: {Path.GetFullPath(output)}");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private readonly record struct ScrollProbeResult(
        int Steps,
        int MissingOnFirstFrame,
        double MaxWaitMilliseconds,
        long WarmBitmapHits);

    private static async Task<ScrollProbeResult> MeasureSmallScrollAsync(
        ThumbnailViewerControl viewer,
        bool reverse)
    {
        var scroller = viewer.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Small-scroll benchmark could not locate gallery ScrollViewer.");
        var maximumOffset = Math.Max(
            0,
            scroller.Extent.Height - scroller.Viewport.Height);
        // Advance by approximately one visual row, including spacing.
        // This uses the same ScrollViewer offset path as wheel, touch
        // and scrollbar motion, not random ScrollIntoView seeks.
        const double step = 198;
        const int stepCount = 3;
        if (maximumOffset < step * stepCount)
        {
            throw new InvalidOperationException(
                "Small-scroll benchmark requires more scrollable rows.");
        }

        var missing = 0;
        var maxWait = 0.0;
        var beforeWarmHits =
            viewer.TileReadiness.ReadyFromBitmapCache;

        for (var i = 0; i < stepCount; i++)
        {
            var nextOffset = Math.Clamp(
                scroller.Offset.Y + (reverse ? -step : step),
                0,
                maximumOffset);
            scroller.Offset = new Vector(
                scroller.Offset.X,
                nextOffset);
            Dispatcher.UIThread.RunJobs();

            // Sample immediately after layout/row realization to find
            // whether the next viewport momentarily has empty tiles.
            missing += CountUnreadyVisibleTiles(viewer);
            var wait = Stopwatch.StartNew();
            for (var attempt = 0;
                 attempt < 1500
                 && CountUnreadyVisibleTiles(viewer) != 0;
                 attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(1);
            }

            wait.Stop();
            if (CountUnreadyVisibleTiles(viewer) != 0)
            {
                var viewport = viewer.ViewportReadiness;
                var diagnostic = viewer.Diagnostics;
                throw new InvalidOperationException(
                    $"Small-scroll { (reverse ? "reverse" : "forward") } step {i + 1} left unloaded visible tiles: "
                    + $"offset={scroller.Offset.Y:F1}, visible={viewport.VisibleTiles}, unready={viewport.UnreadyTiles}, "
                    + $"attached={diagnostic.AttachedTiles}, ready={diagnostic.ReadyTiles}, "
                    + $"realizedRows={viewer.RealizedRowCount}, "
                    + $"first={viewer.FirstVisibleAssetIndex}, last={viewer.LastVisibleAssetIndex}, "
                    + $"sourceInFlight={diagnostic.InFlightThumbnailRequests}, activeDecodes={diagnostic.ActiveBitmapDecodes}.");
            }

            maxWait = Math.Max(
                maxWait,
                wait.Elapsed.TotalMilliseconds);
        }

        return new ScrollProbeResult(
            stepCount,
            missing,
            maxWait,
            viewer.TileReadiness.ReadyFromBitmapCache
                - beforeWarmHits);
    }

    private static int CountUnreadyVisibleTiles(
        ThumbnailViewerControl viewer)
    {
        var actual = viewer.ViewportReadiness;
        // A viewport without a single realized intersecting tile is
        // not a successful empty viewport; the layout may be pending.
        return actual.VisibleTiles > 0
            ? actual.UnreadyTiles
            : 1;
    }

    private static async Task<CursorIntegrationResult> MeasureLibraryCursorIntegrationAsync(
        BenchmarkRecorder recorder,
        string tempRoot,
        long count)
    {
        var databasePath = Path.Combine(tempRoot, "viewer-cursor.db");
        var libraryRoot = Path.Combine(tempRoot, "viewer-cursor-root");
        Directory.CreateDirectory(libraryRoot);

        var database = new LibraryDatabase(databasePath);
        await database.InitializeAsync();

        var repository = new LibraryRepository(database);
        var library = await repository.RegisterLibraryAsync(
            "Viewer benchmark",
            libraryRoot);

        const int ingestBatchSize = 4096;
        await using (var ingest = await repository.OpenIngestSessionAsync(library.Id))
        {
            var batch = new List<AssetUpsert>(ingestBatchSize);
            var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            for (long index = 0; index < count; index++)
            {
                batch.Add(new AssetUpsert(
                    $"fixture/{index:D8}.jpg",
                    100_000 + index,
                    baseTime.AddSeconds(index),
                    512,
                    512,
                    "jpeg"));

                if (batch.Count == ingestBatchSize)
                {
                    await ingest.WriteBatchAsync(batch);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await ingest.WriteBatchAsync(batch);
            }
        }

        LibraryDatabase.ClearPools();

        var service = new LibraryService(databasePath);
        await service.InitializeAsync();

        using var provider = new CursorPagedViewerAssetProvider(
            new LibraryBenchmarkPageSource(service, library.Id, count),
            new ViewerOptions
            {
                MetadataPageSize = 256,
                MetadataPageCacheSize = 8,
                CursorCheckpointStride = 16,
                CursorCheckpointLimit = 128
            });

        using (recorder.Measure("viewer.cursor_seek_end"))
        {
            var last = await provider.GetAssetAsync(count - 1);
            if (last.Id != 1)
            {
                throw new InvalidOperationException(
                    $"100k cursor end seek resolved asset {last.Id}, expected oldest asset id 1.");
            }
        }

        var afterEnd = provider.Diagnostics;
        var pagesAfterEnd = afterEnd.PagesFetched;

        var randomIndexes = new long[]
        {
            5_000, 95_000, 12_500, 87_500, 25_000,
            75_000, 33_333, 66_666, 1_000, 99_000,
            40_000, 60_000, 20_000, 80_000, 10_000,
            90_000, 45_000, 55_000, 30_000, 70_000,
            2_500, 97_500, 15_000, 85_000, 35_000,
            65_000, 22_500, 77_500, 42_500, 57_500,
            7_500, 92_500, 27_500, 72_500, 47_500,
            52_500, 17_500, 82_500, 37_500, 62_500
        };

        using (recorder.Measure("viewer.cursor_random_seek"))
        {
            foreach (var index in randomIndexes)
            {
                _ = await provider.GetAssetAsync(Math.Min(index, count - 1));
            }
        }

        var final = provider.Diagnostics;

        LibraryDatabase.ClearPools();

        return new CursorIntegrationResult(
            pagesAfterEnd,
            final.PagesFetched - pagesAfterEnd,
            final.CachedPages,
            final.CursorCheckpoints);
    }

    private static async Task WaitForViewportReadyAsync(ThumbnailViewerControl viewer)
    {
        for (var attempt = 0; attempt < 1500; attempt++)
        {
            Dispatcher.UIThread.RunJobs();

            var diagnostics = viewer.Diagnostics;
            if (diagnostics.AttachedTiles > 0
                && diagnostics.ReadyTiles == diagnostics.AttachedTiles
                && diagnostics.ActiveBitmapDecodes == 0)
            {
                return;
            }

            await Task.Delay(1);
        }

        var final = viewer.Diagnostics;
        throw new InvalidOperationException(
            $"Viewer viewport did not become image-ready: attached={final.AttachedTiles}, ready={final.ReadyTiles}, decodes={final.ActiveBitmapDecodes}, inflight={final.InFlightThumbnailRequests}.");
    }

    private static async Task WaitForViewerIdleAsync(ViewerSession session)
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            Dispatcher.UIThread.RunJobs();

            var diagnostics = session.Diagnostics;
            if (diagnostics.AttachedTiles == 0
                && diagnostics.ReadyTiles == 0
                && diagnostics.InFlightThumbnailRequests == 0
                && diagnostics.ActiveBitmapDecodes == 0)
            {
                return;
            }

            await Task.Delay(1);
        }

        var final = session.Diagnostics;
        throw new InvalidOperationException(
            $"Viewer did not become idle after teardown: attached={final.AttachedTiles}, ready={final.ReadyTiles}, inflight={final.InFlightThumbnailRequests}, decodes={final.ActiveBitmapDecodes}.");
    }

    private static async Task WaitForRealizationAsync(ThumbnailViewerControl viewer)
    {
        for (var attempt = 0; attempt < 250; attempt++)
        {
            Dispatcher.UIThread.RunJobs();

            if (viewer.RealizedRowCount > 0
                && viewer.Diagnostics.AttachedTiles > 0
                && viewer.Diagnostics.ReadyTiles > 0)
            {
                return;
            }

            await Task.Delay(1);
        }

        throw new InvalidOperationException(
            "Viewer did not render its first decoded thumbnail within the benchmark window.");
    }

    private static void VerifyThumbnailFixture(string path)
    {
        using (var vips = NetVips.Image.NewFromFile(path))
        {
            if (vips.Width != 512 || vips.Height != 512)
            {
                throw new InvalidOperationException(
                    $"libvips fixture dimensions were {vips.Width}x{vips.Height}, expected 512x512.");
            }

            vips.Invalidate();
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using var avalonia = new Bitmap(stream);

        if (avalonia.PixelSize.Width != 512 || avalonia.PixelSize.Height != 512)
        {
            throw new InvalidOperationException(
                $"Avalonia fixture dimensions were {avalonia.PixelSize.Width}x{avalonia.PixelSize.Height}, expected 512x512.");
        }
    }

    private static string[] CreateThumbnailFixtures(string tempRoot, int count)
    {
        VipsRuntimePolicy.EnsureConfigured();

        var seedPath = Path.Combine(tempRoot, "thumb-seed.webp");
        using (var blank = NetVips.Image.Black(512, 512, bands: 3))
        using (var values = blank.NewFromImage([48, 112, 196]))
        using (var srgb = values.Copy(interpretation: Enums.Interpretation.Srgb))
        {
            srgb.Webpsave(
                seedPath,
                q: 82,
                smartSubsample: true,
                keep: Enums.ForeignKeep.None);
        }

        var result = new string[count];
        for (var index = 0; index < count; index++)
        {
            var path = Path.Combine(tempRoot, $"thumb-{index:D3}.webp");
            File.Copy(seedPath, path);
            result[index] = path;
        }

        File.Delete(seedPath);
        return result;
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}

internal readonly record struct CursorIntegrationResult(
    long EndSeekPages,
    long RandomSeekPages,
    int CachedPages,
    int Checkpoints);

internal sealed class LibraryBenchmarkPageSource(
    LibraryService library,
    long libraryId,
    long count) : IViewerPageSource
{
    public long Count { get; } = count;

    public async ValueTask<ViewerAssetPage> GetPageAsync(
        int limit,
        ViewerPageCursor? cursor = null,
        CancellationToken cancellationToken = default)
    {
        AssetCursor? libraryCursor = cursor is { } value
            ? new AssetCursor(value.ModifiedAtUtcTicks, value.AssetId)
            : null;

        var page = await library.GetAssetPageAsync(
            libraryId,
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

internal sealed class BenchmarkApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<BenchmarkApplication>()
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

internal sealed class FixtureAssetProvider(long count) : IViewerAssetProvider
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
                $"fixture/{index:D8}.jpg",
                $"asset-{index:D8}.jpg",
                100_000 + index,
                DateTimeOffset.UnixEpoch.AddSeconds(index).UtcDateTime.Ticks));
    }
}

internal sealed class DelayedBenchmarkThumbnailProvider(
    IReadOnlyList<string> paths,
    TimeSpan delay) : IViewerThumbnailProvider
{
    public async ValueTask<ViewerThumbnail> RequestAsync(
        ViewerAsset asset,
        ViewerThumbnailPriority priority,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(delay, cancellationToken);

        var path = paths[checked((int)(asset.Id % paths.Count))];
        return new ViewerThumbnail(
            $"fixture-{asset.Id}",
            path,
            512,
            512);
    }
}
