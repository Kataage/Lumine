using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Lumine.Diagnostics;
using Lumine.Library;
using Lumine.Viewer;

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

        var thumbnailPath = Path.Combine(tempRoot, "thumb.png");
        await File.WriteAllBytesAsync(
            thumbnailPath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));

        var recorder = new BenchmarkRecorder();
        long peakWorkingSetBytes = 0;
        long peakAdditionalWorkingSetBytes = 0;
        var maxRealizedRows = 0;
        var maxAttachedTiles = 0;
        ViewerRuntimeDiagnostics finalDiagnostics = default;
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
                    var thumbnailProvider = new DelayedBenchmarkThumbnailProvider(
                        thumbnailPath,
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

                        await Task.Delay(60);
                        Dispatcher.UIThread.RunJobs();
                        Observe(viewer);
                    }

                    viewer.SelectAsset(count - 1);
                    Observe(viewer);

                    finalColumns = viewer.Columns;

                    window.Close();
                    await WaitForViewerIdleAsync(session);
                    finalDiagnostics = viewer.Diagnostics;

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
                        maxRealizedRows = Math.Max(
                            maxRealizedRows,
                            control.RealizedRowCount);
                        maxAttachedTiles = Math.Max(
                            maxAttachedTiles,
                            control.Diagnostics.AttachedTiles);
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
                    ["thumbnail_requests"] = finalDiagnostics.ThumbnailRequests.ToString(CultureInfo.InvariantCulture),
                    ["thumbnail_requests_coalesced"] = finalDiagnostics.ThumbnailRequestsCoalesced.ToString(CultureInfo.InvariantCulture),
                    ["thumbnail_requests_cancelled"] = finalDiagnostics.ThumbnailRequestsCancelled.ToString(CultureInfo.InvariantCulture),
                    ["inflight_thumbnail_requests"] = finalDiagnostics.InFlightThumbnailRequests.ToString(CultureInfo.InvariantCulture),
                    ["final_attached_tiles"] = finalDiagnostics.AttachedTiles.ToString(CultureInfo.InvariantCulture),
                    ["decoded_bitmap_entries"] = finalDiagnostics.DecodedBitmapEntries.ToString(CultureInfo.InvariantCulture),
                    ["decoded_bitmap_bytes"] = finalDiagnostics.DecodedBitmapBytes.ToString(CultureInfo.InvariantCulture),
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

    private static async Task WaitForViewerIdleAsync(ViewerSession session)
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            Dispatcher.UIThread.RunJobs();

            var diagnostics = session.Diagnostics;
            if (diagnostics.AttachedTiles == 0
                && diagnostics.InFlightThumbnailRequests == 0)
            {
                return;
            }

            await Task.Delay(1);
        }

        var final = session.Diagnostics;
        throw new InvalidOperationException(
            $"Viewer did not become idle after teardown: attached={final.AttachedTiles}, inflight={final.InFlightThumbnailRequests}.");
    }

    private static async Task WaitForRealizationAsync(ThumbnailViewerControl viewer)
    {
        for (var attempt = 0; attempt < 250; attempt++)
        {
            Dispatcher.UIThread.RunJobs();

            if (viewer.RealizedRowCount > 0
                && viewer.Diagnostics.AttachedTiles > 0)
            {
                return;
            }

            await Task.Delay(1);
        }

        throw new InvalidOperationException(
            "Viewer did not realize its first viewport within the benchmark window.");
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
    string path,
    TimeSpan delay) : IViewerThumbnailProvider
{
    public async ValueTask<ViewerThumbnail> RequestAsync(
        ViewerAsset asset,
        ViewerThumbnailPriority priority,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(delay, cancellationToken);

        return new ViewerThumbnail(
            $"fixture-{asset.Id}",
            path,
            1,
            1);
    }
}
