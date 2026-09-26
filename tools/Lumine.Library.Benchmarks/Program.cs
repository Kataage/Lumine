using System.Globalization;
using Lumine.Diagnostics;
using Lumine.Library;

static string? ReadOption(string[] args, string name)
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

var countText = ReadOption(args, "--count") ?? "100000";
if (!int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
{
    throw new ArgumentException("--count must be a positive integer.");
}

var output = ReadOption(args, "--output")
    ?? Path.Combine("artifacts", "benchmarks", $"library-{count}.json");

var tempRoot = Path.Combine(Path.GetTempPath(), $"lumine-library-benchmark-{Guid.NewGuid():N}");
var libraryRoot = Path.Combine(tempRoot, "library");
var databasePath = Path.Combine(tempRoot, "library.db");
Directory.CreateDirectory(libraryRoot);

var recorder = new BenchmarkRecorder();
long databaseBytes = 0;
long peakWorkingSetBytes = 0;
long startingWorkingSetBytes = 0;
long peakAdditionalWorkingSetBytes = 0;
long retainedWorkingSetBytes = 0;
long retainedAdditionalWorkingSetBytes = 0;
long postGcHeapSizeBytes = 0;
long ingestAllocatedBytes = 0;
var pageCount = 0;
var traversed = 0;
var metadataPersisted = 0;

try
{
    var database = new LibraryDatabase(databasePath);

    using (recorder.Measure(CoreMetricNames.DatabaseOpenMigration))
    {
        await database.InitializeAsync();
    }

    var repository = new LibraryRepository(database);
    var library = await repository.RegisterLibraryAsync("Benchmark", libraryRoot);

    // Normalize the managed heap before measuring ingest. Database
    // initialization/migration/registration deliberately stay outside the
    // ingest metric; leaving their dead managed allocations for a later GC
    // makes hosted-runner peak working set depend on GC timing rather than on
    // the 100k ingest path itself.
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var allocatedBeforeIngest =
        GC.GetTotalAllocatedBytes(precise: true);
    var peakMonitor = PeakWorkingSetMonitor.Start();

    using (recorder.Measure(CoreMetricNames.LibraryBulkUpsert))
    {
        const int batchSize = 2048;
        await using var ingest = await repository.OpenIngestSessionAsync(library.Id);
        var batch = new List<AssetUpsert>(batchSize);

        foreach (var fixture in FixtureGenerator.Enumerate(count))
        {
            batch.Add(new AssetUpsert(
                fixture.RelativePath,
                fixture.FileSize,
                fixture.ModifiedAtUtc,
                fixture.Width,
                fixture.Height,
                fixture.Extension));

            if (batch.Count == batchSize)
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

    await peakMonitor.DisposeAsync();
    startingWorkingSetBytes = peakMonitor.StartingWorkingSetBytes;
    peakWorkingSetBytes = peakMonitor.PeakWorkingSetBytes;
    peakAdditionalWorkingSetBytes = peakMonitor.PeakAdditionalWorkingSetBytes;
    ingestAllocatedBytes = Math.Max(
        0,
        GC.GetTotalAllocatedBytes(precise: true)
            - allocatedBeforeIngest);

    // Separate transient GC-segment commitment from memory retained by the
    // completed ingest path. WorkingSet peak remains diagnostic/gated, while
    // this post-full-GC sample is the stable regression signal for retained
    // process residency.
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    await Task.Delay(50);
    retainedWorkingSetBytes = Environment.WorkingSet;
    retainedAdditionalWorkingSetBytes = Math.Max(
        0,
        retainedWorkingSetBytes - startingWorkingSetBytes);
    postGcHeapSizeBytes =
        GC.GetGCMemoryInfo(GCKind.FullBlocking).HeapSizeBytes;

    var metadataTarget = Math.Min(count, 10_000);
    using (recorder.Measure(CoreMetricNames.LibraryTechnicalMetadataPersist))
    {
        AssetCursor? metadataCursor = null;

        while (metadataPersisted < metadataTarget)
        {
            var page = await repository.GetAssetPageAsync(
                library.Id,
                Math.Min(500, metadataTarget - metadataPersisted),
                metadataCursor);

            if (page.Items.Count == 0)
            {
                break;
            }

            foreach (var asset in page.Items)
            {
                var identity = "sha256:" + asset.Id
                    .ToString("x", CultureInfo.InvariantCulture)
                    .PadLeft(64, '0');

                var stored = await repository.UpdateTechnicalMetadataAsync(
                    library.Id,
                    asset.Id,
                    asset.SourceRevision,
                    asset.FileSize,
                    asset.ModifiedAtUtc.UtcDateTime.Ticks,
                    new AssetTechnicalMetadata(
                        asset.Width ?? 1,
                        asset.Height ?? 1,
                        asset.Width ?? 1,
                        asset.Height ?? 1,
                        false,
                        asset.Format ?? asset.Extension,
                        identity));

                if (!stored)
                {
                    throw new InvalidOperationException(
                        $"Technical metadata persistence rejected fixture asset {asset.Id}.");
                }

                metadataPersisted++;
            }

            metadataCursor = page.NextCursor;
            if (metadataCursor is null)
            {
                break;
            }
        }
    }

    if (metadataPersisted != metadataTarget)
    {
        throw new InvalidOperationException(
            $"Persisted technical metadata for {metadataPersisted:N0} assets, expected {metadataTarget:N0}.");
    }

    using (recorder.Measure(CoreMetricNames.DatabaseReopen))
    {
        LibraryDatabase.ClearPools();
        var reopenedDatabase = new LibraryDatabase(databasePath);
        await reopenedDatabase.InitializeAsync();
        repository = new LibraryRepository(reopenedDatabase);

        _ = await repository.GetLibraryAsync(library.Id)
            ?? throw new InvalidOperationException("Library was not available after database reopen.");
    }

    using (recorder.Measure(CoreMetricNames.LibraryQuery))
    {
        var firstPage = await repository.GetAssetPageAsync(library.Id, 200);
        if (firstPage.Items.Count == 0)
        {
            throw new InvalidOperationException("Library benchmark returned an empty first page.");
        }
    }

    using (recorder.Measure(CoreMetricNames.LibraryKeysetTraversal))
    {
        AssetCursor? cursor = null;

        do
        {
            var page = await repository.GetAssetPageAsync(library.Id, 512, cursor);
            traversed += page.Items.Count;
            pageCount++;
            cursor = page.NextCursor;
        }
        while (cursor is not null);
    }

    if (traversed != count)
    {
        throw new InvalidOperationException($"Traversed {traversed} assets, expected {count}.");
    }

    LibraryDatabase.ClearPools();
    databaseBytes = new FileInfo(databasePath).Length;

    await recorder.WriteJsonAsync(
        output,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kind"] = "library-core",
            ["fixture_asset_count"] = count.ToString(CultureInfo.InvariantCulture),
            ["page_count"] = pageCount.ToString(CultureInfo.InvariantCulture),
            ["traversed_asset_count"] = traversed.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_persist_count"] = metadataPersisted.ToString(CultureInfo.InvariantCulture),
            ["database_bytes"] = databaseBytes.ToString(CultureInfo.InvariantCulture),
            ["starting_working_set_bytes"] = startingWorkingSetBytes.ToString(CultureInfo.InvariantCulture),
            ["peak_working_set_bytes"] = peakWorkingSetBytes.ToString(CultureInfo.InvariantCulture),
            ["peak_additional_working_set_bytes"] = peakAdditionalWorkingSetBytes.ToString(CultureInfo.InvariantCulture),
            ["retained_working_set_bytes"] = retainedWorkingSetBytes.ToString(CultureInfo.InvariantCulture),
            ["retained_additional_working_set_bytes"] = retainedAdditionalWorkingSetBytes.ToString(CultureInfo.InvariantCulture),
            ["post_gc_heap_size_bytes"] = postGcHeapSizeBytes.ToString(CultureInfo.InvariantCulture),
            ["ingest_allocated_bytes"] = ingestAllocatedBytes.ToString(CultureInfo.InvariantCulture),
            ["paging"] = "keyset:modified_at_utc_ticks,id"
        });

    Console.WriteLine($"Library benchmark: {count:N0} assets");
    Console.WriteLine($"Keyset pages: {pageCount:N0}");
    Console.WriteLine($"Technical metadata persisted: {metadataPersisted:N0}");
    Console.WriteLine($"Ingest peak working set: {peakWorkingSetBytes / 1048576d:N1} MiB (+{peakAdditionalWorkingSetBytes / 1048576d:N1} MiB)");
    Console.WriteLine($"Ingest retained working set: {retainedWorkingSetBytes / 1048576d:N1} MiB (+{retainedAdditionalWorkingSetBytes / 1048576d:N1} MiB), post-GC heap={postGcHeapSizeBytes / 1048576d:N1} MiB");
    Console.WriteLine($"Database: {databaseBytes:N0} bytes");
    Console.WriteLine($"Result: {Path.GetFullPath(output)}");
}
finally
{
    LibraryDatabase.ClearPools();
    if (Directory.Exists(tempRoot))
    {
        Directory.Delete(tempRoot, recursive: true);
    }
}
