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

static async Task WaitUntilAsync(
    Func<Task<bool>> predicate,
    string failure,
    TimeSpan? timeout = null)
{
    var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

    while (DateTime.UtcNow < deadline)
    {
        if (await predicate())
        {
            return;
        }

        await Task.Delay(20);
    }

    throw new InvalidOperationException(failure);
}

if (!OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException(
        "Filesystem benchmark requires Windows.");
}

var output = ReadOption(args, "--output")
    ?? "artifacts/benchmarks/filesystem-sync.json";
var countText = ReadOption(args, "--count");
var count = countText is null
    ? 128
    : int.Parse(countText, CultureInfo.InvariantCulture);

if (count is < 32 or > 512)
{
    throw new ArgumentOutOfRangeException(
        nameof(count),
        "Filesystem burst count must be between 32 and 512.");
}

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    $"lumine-fs-bench-{Guid.NewGuid():N}");
var libraryRoot = Path.Combine(tempRoot, "library");
var databasePath = Path.Combine(tempRoot, "data", "library.db");

Directory.CreateDirectory(libraryRoot);

var recorder = new BenchmarkRecorder();
var peak = PeakWorkingSetMonitor.Start();

try
{
    var database = new LibraryDatabase(databasePath);
    await database.InitializeAsync();

    var repository = new LibraryRepository(database);
    var library = await repository.RegisterLibraryAsync(
        "Filesystem benchmark",
        libraryRoot);

    var syncService = new WindowsLibrarySyncService(database);
    await using var sync = await syncService.StartAsync(library.Id);

    var baselineReconciliations = sync.Diagnostics.Reconciliations;

    using (recorder.Measure("filesystem.create_burst"))
    {
        var writes = new Task[count];

        for (var index = 0; index < count; index++)
        {
            var path = Path.Combine(
                libraryRoot,
                $"asset-{index:D4}.jpg");
            writes[index] = File.WriteAllBytesAsync(
                path,
                [1, 2, 3, 4]);
        }

        await Task.WhenAll(writes);

        await WaitUntilAsync(
            async () => await repository.CountAssetsAsync(library.Id) == count,
            "Create burst did not reach the database.");
    }

    var tracked = new AssetInfo[count];
    for (var index = 0; index < count; index++)
    {
        tracked[index] = await repository.GetAssetAsync(
            library.Id,
            $"asset-{index:D4}.jpg")
            ?? throw new InvalidOperationException(
                $"Created benchmark asset {index} was not indexed.");
    }

    var modifyCount = count / 2;
    using (recorder.Measure("filesystem.modify_burst"))
    {
        var writes = new Task[modifyCount];

        for (var index = 0; index < modifyCount; index++)
        {
            var path = Path.Combine(
                libraryRoot,
                $"asset-{index:D4}.jpg");
            writes[index] = File.WriteAllBytesAsync(
                path,
                [1, 2, 3, 4, 5, 6, 7, 8]);
        }

        await Task.WhenAll(writes);

        await WaitUntilAsync(
            async () =>
            {
                for (var index = 0; index < modifyCount; index++)
                {
                    var asset = await repository.GetAssetAsync(
                        library.Id,
                        $"asset-{index:D4}.jpg");

                    if (asset is null
                        || asset.SourceRevision <= tracked[index].SourceRevision
                        || asset.FileSize != 8)
                    {
                        return false;
                    }
                }

                return true;
            },
            "Modify burst did not update every asset.");
    }

    var renameCount = count / 2;
    using (recorder.Measure("filesystem.rename_burst"))
    {
        for (var index = 0; index < renameCount; index++)
        {
            File.Move(
                Path.Combine(libraryRoot, $"asset-{index:D4}.jpg"),
                Path.Combine(libraryRoot, $"renamed-{index:D4}.jpg"));
        }

        await WaitUntilAsync(
            async () =>
            {
                for (var index = 0; index < renameCount; index++)
                {
                    var oldAsset = await repository.GetAssetAsync(
                        library.Id,
                        $"asset-{index:D4}.jpg");
                    var renamed = await repository.GetAssetAsync(
                        library.Id,
                        $"renamed-{index:D4}.jpg");

                    if (oldAsset is not null
                        || renamed is null
                        || renamed.Id != tracked[index].Id)
                    {
                        return false;
                    }
                }

                return true;
            },
            "Rename burst did not preserve every source asset identity.");
    }

    using (recorder.Measure("filesystem.delete_burst"))
    {
        foreach (var path in Directory.EnumerateFiles(
                     libraryRoot,
                     "*.jpg",
                     SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
        }

        await WaitUntilAsync(
            async () => await repository.CountAssetsAsync(library.Id) == 0,
            "Delete burst did not drain the database.");
    }

    await WaitUntilAsync(
        () => Task.FromResult(sync.Diagnostics.QueueDepth == 0),
        "Filesystem event queue did not drain.");

    await Task.Delay(200);

    var diagnostics = sync.Diagnostics;

    if (diagnostics.Reconciliations != baselineReconciliations)
    {
        throw new InvalidOperationException(
            $"Normal file burst unexpectedly used reconciliation: before={baselineReconciliations} after={diagnostics.Reconciliations}.");
    }

    if (diagnostics.Overflows != 0)
    {
        throw new InvalidOperationException(
            $"Normal file burst overflowed the bounded watcher queue {diagnostics.Overflows} time(s).");
    }

    await peak.DisposeAsync();

    await recorder.WriteJsonAsync(
        output,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["burst_count"] = count.ToString(CultureInfo.InvariantCulture),
            ["events_observed"] = diagnostics.EventsObserved.ToString(CultureInfo.InvariantCulture),
            ["events_applied"] = diagnostics.EventsApplied.ToString(CultureInfo.InvariantCulture),
            ["events_coalesced"] = diagnostics.EventsCoalesced.ToString(CultureInfo.InvariantCulture),
            ["overflows"] = diagnostics.Overflows.ToString(CultureInfo.InvariantCulture),
            ["reconciliations"] = diagnostics.Reconciliations.ToString(CultureInfo.InvariantCulture),
            ["reconcile_failures"] = diagnostics.ReconcileFailures.ToString(CultureInfo.InvariantCulture),
            ["renames"] = diagnostics.RenameOperations.ToString(CultureInfo.InvariantCulture),
            ["deletes"] = diagnostics.Deletes.ToString(CultureInfo.InvariantCulture),
            ["upserts"] = diagnostics.Upserts.ToString(CultureInfo.InvariantCulture),
            ["last_apply_latency_ms"] = diagnostics.LastApplyLatencyMs.ToString("F3", CultureInfo.InvariantCulture),
            ["max_apply_latency_ms"] = diagnostics.MaxApplyLatencyMs.ToString("F3", CultureInfo.InvariantCulture),
            ["queue_depth"] = diagnostics.QueueDepth.ToString(CultureInfo.InvariantCulture),
            ["peak_working_set_bytes"] = peak.PeakWorkingSetBytes.ToString(CultureInfo.InvariantCulture),
            ["peak_additional_working_set_bytes"] = peak.PeakAdditionalWorkingSetBytes.ToString(CultureInfo.InvariantCulture)
        });

    Console.WriteLine(
        $"Filesystem benchmark: {count} files, events={diagnostics.EventsObserved:N0}, " +
        $"max latency={diagnostics.MaxApplyLatencyMs:N1} ms, " +
        $"peak={peak.PeakWorkingSetBytes / 1024d / 1024d:N1} MiB");
}
finally
{
    try
    {
        await peak.DisposeAsync();
    }
    catch
    {
    }

    LibraryDatabase.ClearPools();

    if (Directory.Exists(tempRoot))
    {
        Directory.Delete(tempRoot, recursive: true);
    }
}
