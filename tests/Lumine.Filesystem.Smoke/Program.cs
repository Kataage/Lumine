using Lumine.Library;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task<T> WaitForAsync<T>(
    Func<Task<T?>> read,
    Func<T, bool> predicate,
    string failure,
    TimeSpan? timeout = null)
    where T : class
{
    var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(8));

    while (DateTime.UtcNow < deadline)
    {
        var value = await read();
        if (value is not null && predicate(value))
        {
            return value;
        }

        await Task.Delay(25);
    }

    throw new InvalidOperationException(failure);
}

static async Task WaitUntilAsync(
    Func<Task<bool>> predicate,
    string failure,
    TimeSpan? timeout = null)
{
    var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(8));

    while (DateTime.UtcNow < deadline)
    {
        if (await predicate())
        {
            return;
        }

        await Task.Delay(25);
    }

    throw new InvalidOperationException(failure);
}

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("Filesystem sync smoke skipped: Windows-only.");
    return;
}

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    $"lumine-fs-smoke-{Guid.NewGuid():N}");
var libraryRoot = Path.Combine(tempRoot, "library");
var databasePath = Path.Combine(tempRoot, "data", "library.db");

Directory.CreateDirectory(libraryRoot);
await File.WriteAllBytesAsync(
    Path.Combine(libraryRoot, "initial.jpg"),
    [1, 2, 3]);

try
{
    var database = new LibraryDatabase(databasePath);
    await database.InitializeAsync();

    var repository = new LibraryRepository(database);
    var library = await repository.RegisterLibraryAsync(
        "Filesystem smoke",
        libraryRoot);

    var syncService = new WindowsLibrarySyncService(database);
    var journal = new WindowsUsnJournal();
    var journalBefore = journal.Query(libraryRoot);

    long initialId;
    long initialRevision;

    await using (var sync = await syncService.StartAsync(library.Id))
    {
        var initial = await WaitForAsync(
            () => repository.GetAssetAsync(library.Id, "initial.jpg"),
            static asset => asset.FileSize == 3,
            "Bootstrap reconciliation did not index the initial asset.");

        initialId = initial.Id;
        initialRevision = initial.SourceRevision;

        var livePath = Path.Combine(libraryRoot, "live.jpg");
        var createStarted = DateTime.UtcNow;
        await File.WriteAllBytesAsync(livePath, [10, 20, 30, 40]);

        var created = await WaitForAsync(
            () => repository.GetAssetAsync(library.Id, "live.jpg"),
            static asset => asset.FileSize == 4,
            "ReadDirectoryChangesW create event was not applied.");

        Require(
            DateTime.UtcNow - createStarted < TimeSpan.FromSeconds(4),
            "Incremental create latency exceeded four seconds.");

        await Task.Delay(50);
        await File.WriteAllBytesAsync(livePath, [1, 2, 3, 4, 5, 6]);
        File.SetLastWriteTimeUtc(
            livePath,
            DateTime.UtcNow.AddSeconds(1));

        var modified = await WaitForAsync(
            () => repository.GetAssetAsync(library.Id, "live.jpg"),
            asset => asset.SourceRevision > created.SourceRevision
                && asset.FileSize == 6,
            "ReadDirectoryChangesW modify event did not advance source revision.");

        var renamedPath = Path.Combine(libraryRoot, "renamed.jpg");
        File.Move(livePath, renamedPath);

        var renamed = await WaitForAsync(
            () => repository.GetAssetAsync(library.Id, "renamed.jpg"),
            asset => asset.Id == modified.Id,
            "Rename event did not preserve stable asset identity.");

        Require(
            await repository.GetAssetAsync(library.Id, "live.jpg") is null,
            "Old path survived rename.");

        File.Delete(renamedPath);
        await WaitUntilAsync(
            async () => await repository.GetAssetAsync(
                library.Id,
                "renamed.jpg") is null,
            "Delete event was not applied.");

        var directory = Path.Combine(libraryRoot, "album");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(
            Path.Combine(directory, "nested.jpg"),
            [9, 8, 7]);

        _ = await WaitForAsync(
            () => repository.GetAssetAsync(
                library.Id,
                "album/nested.jpg"),
            static asset => asset.FileSize == 3,
            "Directory creation fallback did not reconcile nested image.");

        Directory.Delete(directory, recursive: true);
        await WaitUntilAsync(
            async () => await repository.GetAssetAsync(
                library.Id,
                "album/nested.jpg") is null,
            "Directory removal fallback did not reconcile stale nested asset.");

        // No periodic idle reconciliation: after the directory-triggered
        // fallback settles, idle time must not keep increasing the count.
        await Task.Delay(250);
        var idleBefore = sync.Diagnostics.Reconciliations;
        await Task.Delay(500);
        var idleAfter = sync.Diagnostics.Reconciliations;
        Require(
            idleBefore == idleAfter,
            "Idle watcher performed an unexpected periodic reconciliation.");

        var diagnostics = sync.Diagnostics;
        Require(diagnostics.EventsObserved > 0, "Watcher observed no filesystem events.");
        Require(diagnostics.EventsApplied > 0, "Watcher applied no incremental events.");
        Require(diagnostics.Upserts > 0, "Watcher performed no incremental upserts.");
        Require(diagnostics.Deletes > 0, "Watcher performed no incremental deletes.");
        Require(diagnostics.MaxApplyLatencyMs < 4_000, "Watcher apply latency exceeded four seconds.");
    }

    var stoppedState = await repository.GetOrCreateSyncStateAsync(library.Id);
    Require(
        stoppedState.WatcherStoppedAtUtc is not null,
        "Shutdown did not persist watcher stop checkpoint.");

    var offlinePath = Path.Combine(libraryRoot, "offline.jpg");
    await File.WriteAllBytesAsync(offlinePath, [5, 4, 3, 2, 1]);

    await using (var restarted = await syncService.StartAsync(library.Id))
    {
        _ = await WaitForAsync(
            () => repository.GetAssetAsync(library.Id, "offline.jpg"),
            static asset => asset.FileSize == 5,
            "Restart recovery did not catch an offline file creation.");

        if (journalBefore.Available
            && stoppedState.UsnJournalId is not null
            && stoppedState.NextUsn is not null)
        {
            Require(
                restarted.BootstrapMode is LibrarySyncBootstrapMode.UsnDelta
                    or LibrarySyncBootstrapMode.ReconcileFallback,
                "NTFS restart did not use USN delta or an explicit safe reconciliation fallback.");
        }
        else
        {
            Require(
                restarted.BootstrapMode == LibrarySyncBootstrapMode.ReconcileFallback,
                "Unsupported USN environment did not use reconciliation fallback.");
        }
    }

    // Reconciliation deletes stale rows only after a complete walk.
    await File.WriteAllBytesAsync(
        Path.Combine(libraryRoot, "stale.jpg"),
        [1]);
    var reconcile = new LibraryReconciler(repository);
    var complete = await reconcile.ReconcileAsync(library.Id);
    Require(complete.Completed, "Complete reconciliation unexpectedly failed.");
    Require(
        await repository.GetAssetAsync(library.Id, "stale.jpg") is not null,
        "Complete reconciliation failed to index stale fixture.");

    File.Delete(Path.Combine(libraryRoot, "stale.jpg"));
    var deleteReconcile = await reconcile.ReconcileAsync(library.Id);
    Require(deleteReconcile.Completed, "Deletion reconciliation unexpectedly failed.");
    Require(deleteReconcile.Deleted > 0, "Complete reconciliation deleted no stale rows.");
    Require(
        await repository.GetAssetAsync(library.Id, "stale.jpg") is null,
        "Complete reconciliation retained a missing asset.");

    // Partial enumeration must never perform destructive cleanup.
    var protectedAsset = await repository.GetAssetAsync(library.Id, "initial.jpg")
        ?? throw new InvalidOperationException("Initial asset disappeared before partial-reconcile test.");

    Directory.Delete(libraryRoot, recursive: true);
    var partial = await reconcile.ReconcileAsync(library.Id);
    Require(!partial.Completed, "Missing root was incorrectly considered a complete reconciliation.");
    Require(
        await repository.GetAssetAsync(library.Id, "initial.jpg") is { Id: var afterId }
        && afterId == protectedAsset.Id,
        "Partial reconciliation destructively removed a tracked asset.");

    Require(
        protectedAsset.Id == initialId
        && protectedAsset.SourceRevision == initialRevision,
        "Unchanged initial asset identity/revision changed during synchronization smoke.");

    Console.WriteLine(
        $"Filesystem sync smoke: watcher + reconcile + restart recovery OK; USN available={journalBefore.Available}");
}
finally
{
    LibraryDatabase.ClearPools();

    if (Directory.Exists(tempRoot))
    {
        Directory.Delete(tempRoot, recursive: true);
    }
}
