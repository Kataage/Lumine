using System.Diagnostics;
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

var pairedWarmMetadataAb = args.Contains(
    "--paired-warm-metadata-ab",
    StringComparer.Ordinal);

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
var metadataBatchPersisted = 0;
var metadataBatchTransactionCount = 0;
var metadataPageFetchCount = 0;
long metadataPageFetchTicks = 0;
long metadataWriteTicks = 0;
long metadataSlowWriteCount = 0;
long metadataMaxWriteTicks = 0;
long metadataConnectionOpenTicks = 0;
long metadataBeginTicks = 0;
long metadataGuardedUpdateTicks = 0;
long metadataUpsertTicks = 0;
long metadataCommitTicks = 0;
long metadataMaxCommitTicks = 0;
long metadataCommitOver8msTicks = 0;
// Exclusive COMMIT-duration buckets measured inside the original guarded
// per-asset SQLite transaction. These do not change the strict 8s gate.
long[] metadataCommitLatencyBuckets = new long[5];
// 10 fixed windows of 1,000 actual individual commits: diagnose whether
// the 32ms+ tail is clustered or periodic, without extra I/O inside the gate.
long[] metadataCommitWindowTicks = new long[10];
long[] metadataCommitWindowSlow32Count = new long[10];
// Bounded 1-based ordinals retain precise sparse spikes without an
// allocation/logging operation on each individual COMMIT hot path.
int[] metadataCommitSlow32Ordinals = new int[256];
var metadataCommitSlow32Stored = 0;
var metadataCommitSlow32Overflow = 0;
var metadataObservedWrites = 0;
var searchIndexed = 0;
var pairedWarmAbRows = 0;
var pairedWarmAbBatchGroups = 0;
var pairedWarmAbMeasurements = new Dictionary<string, double>(
    StringComparer.Ordinal);

string PairedWarmPhase(string key) =>
    pairedWarmAbMeasurements.TryGetValue(key, out var ms)
        ? ms.ToString("F3", CultureInfo.InvariantCulture)
        : "not_run";

void ObserveMetadataWrite(TechnicalMetadataWriteTimings timing)
{
    metadataConnectionOpenTicks += timing.ConnectionOpenTicks;
    metadataBeginTicks += timing.BeginTransactionTicks;
    metadataGuardedUpdateTicks += timing.GuardedAssetUpdateTicks;
    metadataUpsertTicks += timing.MetadataUpsertTicks;
    metadataCommitTicks += timing.CommitTicks;
    metadataMaxCommitTicks = Math.Max(
        metadataMaxCommitTicks, timing.CommitTicks);
    var commitMs = timing.CommitTicks / (double)TimeSpan.TicksPerMillisecond;
    var bucket = commitMs < 1 ? 0
        : commitMs < 8 ? 1
        : commitMs < 32 ? 2
        : commitMs < 128 ? 3
        : 4;
    metadataCommitLatencyBuckets[bucket]++;
    var windowIndex = metadataObservedWrites / 1_000;
    if ((uint)windowIndex < metadataCommitWindowTicks.Length)
    {
        metadataCommitWindowTicks[windowIndex] += timing.CommitTicks;
        if (commitMs >= 32)
        {
            metadataCommitWindowSlow32Count[windowIndex]++;
            if (metadataCommitSlow32Stored < metadataCommitSlow32Ordinals.Length)
            {
                metadataCommitSlow32Ordinals[metadataCommitSlow32Stored++] = metadataObservedWrites + 1;
            }
            else
            {
                metadataCommitSlow32Overflow++;
            }
        }
    }
    if (commitMs >= 8)
    {
        metadataCommitOver8msTicks += timing.CommitTicks;
    }
    metadataObservedWrites++;
}


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
            var pageStart = Stopwatch.GetTimestamp();
            var page = await repository.GetAssetPageAsync(
                library.Id,
                Math.Min(500, metadataTarget - metadataPersisted),
                metadataCursor);
            metadataPageFetchTicks += Stopwatch.GetElapsedTime(pageStart).Ticks;
            metadataPageFetchCount++;

            if (page.Items.Count == 0)
            {
                break;
            }

            foreach (var asset in page.Items)
            {
                var identity = "sha256:" + asset.Id
                    .ToString("x", CultureInfo.InvariantCulture)
                    .PadLeft(64, '0');

                var writeStart = Stopwatch.GetTimestamp();
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
                        identity),
                    timingObserver: ObserveMetadataWrite);
                var writeTicks = Stopwatch.GetElapsedTime(writeStart).Ticks;
                metadataWriteTicks += writeTicks;
                metadataMaxWriteTicks = Math.Max(metadataMaxWriteTicks, writeTicks);
                if (writeTicks >= TimeSpan.TicksPerMillisecond * 8)
                {
                    metadataSlowWriteCount++;
                }

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

    if (metadataPersisted != metadataTarget
        || metadataObservedWrites != metadataTarget)
    {
        throw new InvalidOperationException(
            $"Persisted technical metadata for {metadataPersisted:N0} assets, expected {metadataTarget:N0}.");
    }

    // New, separately reported production API experiment. Keep the original
    // 10,000 individually awaited, revision-guarded writes and their unchanged
    // <=8s acceptance gate above as the control. This additional pass reuses
    // the actual bounded repository method, not fixture-only direct SQL.
    // It is a warm second pass: do not claim a head-to-head cold speedup.
    using (recorder.Measure(CoreMetricNames.LibraryTechnicalMetadataBatchPersist))
    {
        AssetCursor? batchCursor = null;
        while (metadataBatchPersisted < metadataTarget)
        {
            var page = await repository.GetAssetPageAsync(
                library.Id,
                Math.Min(500, metadataTarget - metadataBatchPersisted),
                batchCursor);
            if (page.Items.Count == 0)
            {
                break;
            }

            for (var offset = 0; offset < page.Items.Count;
                 offset += LibraryRepository.MaxTechnicalMetadataBatchSize)
            {
                var updates = page.Items
                    .Skip(offset)
                    .Take(LibraryRepository.MaxTechnicalMetadataBatchSize)
                    .Select(asset => new TechnicalMetadataBatchItem(
                        asset.Id,
                        asset.SourceRevision,
                        asset.FileSize,
                        asset.ModifiedAtUtc.UtcDateTime.Ticks,
                        new AssetTechnicalMetadata(
                            asset.Width ?? 1,
                            asset.Height ?? 1,
                            asset.RawWidth ?? asset.Width ?? 1,
                            asset.RawHeight ?? asset.Height ?? 1,
                            asset.HasAlpha ?? false,
                            asset.Format ?? asset.Extension,
                            "sha256:" + asset.Id
                                .ToString("x", CultureInfo.InvariantCulture)
                                .PadLeft(64, '0'))))
                    .ToArray();

                var results = await repository.UpdateTechnicalMetadataBatchAsync(
                    library.Id, updates);
                if (results.Count != updates.Length
                    || results.Any(static accepted => !accepted))
                {
                    throw new InvalidOperationException(
                        "Bounded technical metadata batch rejected a current fixture source.");
                }

                metadataBatchPersisted += results.Count;
                metadataBatchTransactionCount++;
            }

            batchCursor = page.NextCursor;
            if (batchCursor is null)
            {
                break;
            }
        }
    }

    if (metadataBatchPersisted != metadataTarget)
    {
        throw new InvalidOperationException(
            $"Bounded metadata batch persisted {metadataBatchPersisted:N0} assets, expected {metadataTarget:N0}.");
    }

    // Paired ABBA diagnostic, opt-in on the 100k warm fixture only.  Unlike
    // the original gate, both APIs below rewrite the exact same immutable
    // source-guarded rows after the first full batch pass.  The B-A reversal
    // attenuates monotonic warming and background runner load, but this is
    // still a WARM within-run comparison, not cold-vs-cold evidence.
    if (pairedWarmMetadataAb)
    {
        var pairedItems = new List<TechnicalMetadataBatchItem>(metadataTarget);
        AssetCursor? pairedCursor = null;
        while (pairedItems.Count < metadataTarget)
        {
            var page = await repository.GetAssetPageAsync(
                library.Id,
                Math.Min(500, metadataTarget - pairedItems.Count),
                pairedCursor);
            if (page.Items.Count == 0)
            {
                break;
            }

            foreach (var asset in page.Items)
            {
                var identity = asset.SourceIdentity
                    ?? throw new InvalidOperationException(
                        "Paired warm A/B source was missing persisted identity.");
                pairedItems.Add(new TechnicalMetadataBatchItem(
                    asset.Id,
                    asset.SourceRevision,
                    asset.FileSize,
                    asset.ModifiedAtUtc.UtcDateTime.Ticks,
                    new AssetTechnicalMetadata(
                        asset.Width ?? 1,
                        asset.Height ?? 1,
                        asset.RawWidth ?? asset.Width ?? 1,
                        asset.RawHeight ?? asset.Height ?? 1,
                        asset.HasAlpha ?? false,
                        asset.Format ?? asset.Extension,
                        identity)));
            }

            pairedCursor = page.NextCursor;
            if (pairedCursor is null)
            {
                break;
            }
        }

        if (pairedItems.Count != metadataTarget)
        {
            throw new InvalidOperationException(
                $"Paired warm A/B gathered {pairedItems.Count:N0} source-guarded records, expected {metadataTarget:N0}.");
        }

        pairedWarmAbRows = pairedItems.Count;
        foreach (var phase in new[]
                 {
                     "single_first", "batch_first",
                     "batch_second", "single_second"
                 })
        {
            var isBatch = phase.StartsWith(
                "batch_", StringComparison.Ordinal);
            var start = Stopwatch.GetTimestamp();
            using (recorder.Measure(
                       "library.technical_metadata_warm_ab_" + phase))
            {
                if (isBatch)
                {
                    foreach (var chunk in pairedItems.Chunk(
                                 LibraryRepository.MaxTechnicalMetadataBatchSize))
                    {
                        var accepted = await repository.UpdateTechnicalMetadataBatchAsync(
                            library.Id, chunk);
                        if (accepted.Count != chunk.Length
                            || accepted.Any(static success => !success))
                        {
                            throw new InvalidOperationException(
                                "Paired warm A/B batch rejected a current source.");
                        }
                        pairedWarmAbBatchGroups++;
                    }
                }
                else
                {
                    foreach (var item in pairedItems)
                    {
                        var accepted = await repository.UpdateTechnicalMetadataAsync(
                            library.Id,
                            item.AssetId,
                            item.ExpectedSourceRevision,
                            item.ExpectedFileSize,
                            item.ExpectedModifiedAtUtcTicks,
                            item.Metadata);
                        if (!accepted)
                        {
                            throw new InvalidOperationException(
                                "Paired warm A/B individual write rejected a current source.");
                        }
                    }
                }
            }

            pairedWarmAbMeasurements.Add(
                phase,
                Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    using (recorder.Measure(CoreMetricNames.LibrarySearchIndexRebuild))
    {
        searchIndexed = await repository.RebuildSearchIndexAsync(
            library.Id);
    }

    if (searchIndexed != count)
    {
        throw new InvalidOperationException(
            $"Search index rebuild covered {searchIndexed:N0} assets, expected {count:N0}.");
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


    var searchFixture = FixtureGenerator.Create(count - 1);
    var searchAsset = await repository.GetAssetAsync(
        library.Id,
        searchFixture.RelativePath)
        ?? throw new InvalidOperationException(
            "Search benchmark target was not found.");

    _ = await repository.SetUserMetadataAsync(
        library.Id,
        searchAsset.Id,
        new AssetUserMetadataUpdate(
            Rating: 5,
            Favorite: true,
            Notes: "猫耳 メイド reference benchmark",
            StatusLabel: "reviewed",
            ColorLabel: "blue",
            Tags: ["推し", "blue sky"]));

    using (recorder.Measure(CoreMetricNames.LibrarySearchAscii))
    {
        var page = await repository.GetAssetPageAsync(
            library.Id,
            new AssetQuery(
                SearchText:
                    Path.GetFileNameWithoutExtension(
                        searchFixture.FileName)[6..]),
            20);
        if (!page.Items.Any(asset => asset.Id == searchAsset.Id))
        {
            throw new InvalidOperationException(
                "100k ASCII search benchmark did not find its target.");
        }
    }

    using (recorder.Measure(CoreMetricNames.LibrarySearchJapaneseShort))
    {
        var page = await repository.GetAssetPageAsync(
            library.Id,
            new AssetQuery(SearchText: "猫"),
            20);
        if (page.Items.Count != 1
            || page.Items[0].Id != searchAsset.Id)
        {
            throw new InvalidOperationException(
                "100k single-character Japanese fallback benchmark did not find exactly its target.");
        }
    }

    using (recorder.Measure(CoreMetricNames.LibrarySearchTagFilter))
    {
        var page = await repository.GetAssetPageAsync(
            library.Id,
            new AssetQuery(
                RequiredTags: ["推し"],
                Favorite: true,
                MinRating: 5),
            20);
        if (page.Items.Count != 1
            || page.Items[0].Id != searchAsset.Id)
        {
            throw new InvalidOperationException(
                "100k exact-tag/filter benchmark did not find exactly its target.");
        }
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
            ["technical_metadata_batch_persist_count"] = metadataBatchPersisted.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_batch_transaction_count"] = metadataBatchTransactionCount.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_warm_ab_enabled"] = pairedWarmMetadataAb ? "true" : "false",
            ["technical_metadata_warm_ab_rows_per_phase"] = pairedWarmAbRows.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_warm_ab_batch_groups_total"] = pairedWarmAbBatchGroups.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_warm_ab_single_first_ms"] = PairedWarmPhase("single_first"),
            ["technical_metadata_warm_ab_batch_first_ms"] = PairedWarmPhase("batch_first"),
            ["technical_metadata_warm_ab_batch_second_ms"] = PairedWarmPhase("batch_second"),
            ["technical_metadata_warm_ab_single_second_ms"] = PairedWarmPhase("single_second"),
            ["technical_metadata_page_fetch_count"] = metadataPageFetchCount.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_page_fetch_ticks"] = metadataPageFetchTicks.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_update_ticks"] = metadataWriteTicks.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_update_max_ticks"] = metadataMaxWriteTicks.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_update_over_8ms_count"] = metadataSlowWriteCount.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_phase_observed_write_count"] = metadataObservedWrites.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_connection_open_ticks"] = metadataConnectionOpenTicks.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_begin_transaction_ticks"] = metadataBeginTicks.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_guarded_asset_update_ticks"] = metadataGuardedUpdateTicks.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_metadata_upsert_ticks"] = metadataUpsertTicks.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_commit_ticks"] = metadataCommitTicks.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_commit_max_ticks"] = metadataMaxCommitTicks.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_commit_over_8ms_ticks"] = metadataCommitOver8msTicks.ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_commit_bucket_lt_1ms"] = metadataCommitLatencyBuckets[0].ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_commit_bucket_1_to_lt_8ms"] = metadataCommitLatencyBuckets[1].ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_commit_bucket_8_to_lt_32ms"] = metadataCommitLatencyBuckets[2].ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_commit_bucket_32_to_lt_128ms"] = metadataCommitLatencyBuckets[3].ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_commit_bucket_ge_128ms"] = metadataCommitLatencyBuckets[4].ToString(CultureInfo.InvariantCulture),
            ["technical_metadata_commit_window_1000_ticks_csv"] = string.Join(",", metadataCommitWindowTicks.Select(static ticks => ticks.ToString(CultureInfo.InvariantCulture))),
            ["technical_metadata_commit_window_1000_ge_32ms_csv"] = string.Join(",", metadataCommitWindowSlow32Count.Select(static count => count.ToString(CultureInfo.InvariantCulture))),
            ["technical_metadata_commit_ge_32ms_ordinals_csv"] = string.Join(",", metadataCommitSlow32Ordinals.Take(metadataCommitSlow32Stored)),
            ["technical_metadata_commit_ge_32ms_ordinals_overflow"] = metadataCommitSlow32Overflow.ToString(CultureInfo.InvariantCulture),
            ["search_indexed_asset_count"] = searchIndexed.ToString(CultureInfo.InvariantCulture),
            ["search_index"] = "fts5:trigram-detail-none+cjk-bigram",
            ["search_fixture_asset_id"] = searchAsset.Id.ToString(CultureInfo.InvariantCulture),
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
    Console.WriteLine($"Technical metadata bounded batch: {metadataBatchPersisted:N0} rows / {metadataBatchTransactionCount:N0} commits (second, warm pass).");
    if (pairedWarmMetadataAb)
    {
        Console.WriteLine(
            $"Paired WARM metadata A-B-B-A (same {pairedWarmAbRows:N0} rows): " +
            $"single1={PairedWarmPhase("single_first")} ms; " +
            $"batch1={PairedWarmPhase("batch_first")} ms; " +
            $"batch2={PairedWarmPhase("batch_second")} ms; " +
            $"single2={PairedWarmPhase("single_second")} ms. " +
            "No cold/OS-cache equivalence claim.");
    }
    Console.WriteLine($"Technical metadata phase durations: pages={TimeSpan.FromTicks(metadataPageFetchTicks).TotalMilliseconds:N1} ms ({metadataPageFetchCount:N0} fetches), updates={TimeSpan.FromTicks(metadataWriteTicks).TotalMilliseconds:N1} ms, slow writes >=8ms={metadataSlowWriteCount:N0}, max write={TimeSpan.FromTicks(metadataMaxWriteTicks).TotalMilliseconds:N1} ms.");
    Console.WriteLine($"Technical metadata production write internals: opens={TimeSpan.FromTicks(metadataConnectionOpenTicks).TotalMilliseconds:N1} ms, begin={TimeSpan.FromTicks(metadataBeginTicks).TotalMilliseconds:N1} ms, guarded UPDATE={TimeSpan.FromTicks(metadataGuardedUpdateTicks).TotalMilliseconds:N1} ms, UPSERT={TimeSpan.FromTicks(metadataUpsertTicks).TotalMilliseconds:N1} ms, COMMIT={TimeSpan.FromTicks(metadataCommitTicks).TotalMilliseconds:N1} ms.");
    Console.WriteLine(
        $"Individual metadata SQLite COMMIT tail: " +
        $"<1ms={metadataCommitLatencyBuckets[0]:N0}, " +
        $"[1,8)ms={metadataCommitLatencyBuckets[1]:N0}, " +
        $"[8,32)ms={metadataCommitLatencyBuckets[2]:N0}, " +
        $"[32,128)ms={metadataCommitLatencyBuckets[3]:N0}, " +
        $">=128ms={metadataCommitLatencyBuckets[4]:N0}, " +
        $"max={TimeSpan.FromTicks(metadataMaxCommitTicks).TotalMilliseconds:N1}ms, " +
        $">=8ms cumulative={TimeSpan.FromTicks(metadataCommitOver8msTicks).TotalMilliseconds:N1}ms.");
    Console.WriteLine(
        $"Individual SQLite COMMIT 1k windows (ms): {string.Join(",", metadataCommitWindowTicks.Select(static ticks => (ticks / (double)TimeSpan.TicksPerMillisecond).ToString("F1", CultureInfo.InvariantCulture)))}; " +
        $">=32ms spikes/window: {string.Join(",", metadataCommitWindowSlow32Count)}.");
    Console.WriteLine(
        $"Individual SQLite COMMIT >=32ms ordinal positions (1-based, first 256): " +
        $"{string.Join(",", metadataCommitSlow32Ordinals.Take(metadataCommitSlow32Stored))}; " +
        $"overflow={metadataCommitSlow32Overflow}.");
    Console.WriteLine($"Search index rebuilt: {searchIndexed:N0} assets");
    Console.WriteLine("Search fixtures: ASCII partial / Japanese 1-char fallback / exact tag+filter OK");
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
