using System.Runtime.InteropServices;
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

static IntPtr CreateNotifyRecord(uint action, string name, out uint bytes)
{
    var nameBytes = System.Text.Encoding.Unicode.GetBytes(name);
    bytes = checked((uint)(12 + nameBytes.Length));
    var buffer = Marshal.AllocHGlobal(checked((int)bytes));
    Marshal.WriteInt32(buffer, 0, 0);
    Marshal.WriteInt32(buffer, 4, checked((int)action));
    Marshal.WriteInt32(buffer, 8, nameBytes.Length);
    Marshal.Copy(nameBytes, 0, IntPtr.Add(buffer, 12), nameBytes.Length);
    return buffer;
}

static void VerifyNativeBufferParser()
{
    var now = DateTimeOffset.UtcNow;

    var overflow = WindowsDirectoryChangeWatcher.ParseBuffer(
        IntPtr.Zero,
        0,
        now);
    Require(
        overflow.Count == 1
        && overflow[0].Kind == DirectoryChangeKind.Overflow,
        "Zero-byte ReadDirectoryChangesW completion was not classified as overflow.");

    string? pendingOld = null;
    var oldBuffer = CreateNotifyRecord(4, "old-name.jpg", out var oldBytes);

    try
    {
        var old = WindowsDirectoryChangeWatcher.ParseBuffer(
            oldBuffer,
            oldBytes,
            now,
            ref pendingOld,
            flushPendingRename: false);

        Require(old.Count == 0, "Rename-old record was emitted before a pairing opportunity.");
        Require(
            string.Equals(pendingOld, "old-name.jpg", StringComparison.Ordinal),
            "Rename-old state was not retained across native buffers.");
    }
    finally
    {
        Marshal.FreeHGlobal(oldBuffer);
    }

    var newBuffer = CreateNotifyRecord(5, "new-name.jpg", out var newBytes);

    try
    {
        var renamed = WindowsDirectoryChangeWatcher.ParseBuffer(
            newBuffer,
            newBytes,
            now,
            ref pendingOld,
            flushPendingRename: false);

        Require(
            renamed.Count == 1
            && renamed[0].Kind == DirectoryChangeKind.Renamed
            && renamed[0].OldRelativePath == "old-name.jpg"
            && renamed[0].RelativePath == "new-name.jpg",
            "Cross-buffer rename pair was not reconstructed.");
        Require(pendingOld is null, "Rename pairing left stale pending state.");
    }
    finally
    {
        Marshal.FreeHGlobal(newBuffer);
    }
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
    VerifyNativeBufferParser();

    var uncProbe = WindowsUsnJournal.Query(@"\\invalid-lumine-test\share");
    Require(
        !uncProbe.Available,
        "UNC path unexpectedly reported local NTFS USN availability.");

    var database = new LibraryDatabase(databasePath);
    await database.InitializeAsync();

    var repository = new LibraryRepository(database);
    var library = await repository.RegisterLibraryAsync(
        "Filesystem smoke",
        libraryRoot);

    Require(
        !WindowsFilesystemSemantics.IsCaseSensitiveDirectory(libraryRoot),
        "CI temporary library unexpectedly uses per-directory case sensitivity.");

    var syncService = new WindowsLibrarySyncService(database);
    var journalBefore = WindowsUsnJournal.Query(libraryRoot);

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

        // Use an image-looking directory name so removal cannot be
        // misclassified from its extension alone.
        var directory = Path.Combine(libraryRoot, "album.jpg");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(
            Path.Combine(directory, "nested.jpg"),
            [9, 8, 7]);

        _ = await WaitForAsync(
            () => repository.GetAssetAsync(
                library.Id,
                "album.jpg/nested.jpg"),
            static asset => asset.FileSize == 3,
            "Directory creation fallback did not reconcile nested image.");

        Directory.Delete(directory, recursive: true);
        await WaitUntilAsync(
            async () => await repository.GetAssetAsync(
                library.Id,
                "album.jpg/nested.jpg") is null,
            "Directory removal fallback did not reconcile stale nested asset.");

    // Batched ordering: deleting a destination and then renaming a source
    // into that same path must leave the renamed source present.
    var barrierSourcePath = Path.Combine(libraryRoot, "barrier-source.jpg");
    var barrierDestinationPath = Path.Combine(libraryRoot, "barrier-destination.jpg");
    await File.WriteAllBytesAsync(barrierSourcePath, [1, 1, 1]);
    await File.WriteAllBytesAsync(barrierDestinationPath, [2, 2, 2, 2]);

    _ = await WaitForAsync(
        () => repository.GetAssetAsync(library.Id, "barrier-source.jpg"),
        static asset => asset.FileSize == 3,
        "Barrier source was not indexed.");
    _ = await WaitForAsync(
        () => repository.GetAssetAsync(library.Id, "barrier-destination.jpg"),
        static asset => asset.FileSize == 4,
        "Barrier destination was not indexed.");

    var barrierSource = await repository.GetAssetAsync(
        library.Id,
        "barrier-source.jpg")
        ?? throw new InvalidOperationException("Barrier source disappeared before rename.");

    File.Delete(barrierDestinationPath);
    File.Move(barrierSourcePath, barrierDestinationPath);

    var barrierFinal = await WaitForAsync(
        () => repository.GetAssetAsync(library.Id, "barrier-destination.jpg"),
        asset => asset.Id == barrierSource.Id && asset.FileSize == 3,
        "Delete-then-rename ordering was not preserved across the batched debounce window.");

    Require(
        barrierFinal.Id == barrierSource.Id,
        "Batched rename barrier lost stable source identity.");
    Require(
        await repository.GetAssetAsync(library.Id, "barrier-source.jpg") is null,
        "Batched rename barrier retained the old source path.");

    File.Delete(barrierDestinationPath);
    await WaitUntilAsync(
        async () => await repository.GetAssetAsync(
            library.Id,
            "barrier-destination.jpg") is null,
        "Batched rename barrier cleanup was not applied.");

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
                restarted.BootstrapMode == LibrarySyncBootstrapMode.UsnDelta,
                $"Available NTFS USN journal did not perform delta catch-up: {restarted.CatchUp?.Reason}");
            Require(
                restarted.CatchUp is { AppliedAsDelta: true, RequiresReconcile: false },
                "USN delta catch-up did not report a clean applied delta.");
        }
        else
        {
            Require(
                restarted.BootstrapMode == LibrarySyncBootstrapMode.ReconcileFallback,
                "Unsupported USN environment did not use reconciliation fallback.");
        }
    }

    var offlineAsset = await repository.GetAssetAsync(library.Id, "offline.jpg")
        ?? throw new InvalidOperationException("Offline catch-up asset disappeared.");

    var offlineRenamedPath = Path.Combine(libraryRoot, "offline-renamed.jpg");
    File.Move(offlinePath, offlineRenamedPath);

    await using (var renameRestart = await syncService.StartAsync(library.Id))
    {
        var offlineRenamed = await WaitForAsync(
            () => repository.GetAssetAsync(library.Id, "offline-renamed.jpg"),
            static asset => asset.FileSize == 5,
            "Restart recovery did not catch an offline rename.");

        Require(
            await repository.GetAssetAsync(library.Id, "offline.jpg") is null,
            "Offline rename left the old path indexed.");

        if (journalBefore.Available)
        {
            Require(
                renameRestart.BootstrapMode == LibrarySyncBootstrapMode.UsnDelta,
                $"Available NTFS USN journal did not replay offline rename: {renameRestart.CatchUp?.Reason}");
            Require(
                offlineRenamed.Id == offlineAsset.Id,
                "USN offline rename did not preserve stable asset identity.");
        }
    }

    if (journalBefore.Available)
    {
        var validState = await repository.GetOrCreateSyncStateAsync(library.Id);
        Require(
            validState.NextUsn is not null,
            "Valid NTFS shutdown did not persist a USN checkpoint.");

        await repository.UpdateUsnCheckpointAsync(
            library.Id,
            "0000000000000000",
            validState.NextUsn,
            reconcileRequired: false,
            watcherStoppedAtUtc: validState.WatcherStoppedAtUtc,
            error: null);

        var gapPath = Path.Combine(libraryRoot, "journal-gap.jpg");
        await File.WriteAllBytesAsync(gapPath, [4, 4, 4, 4]);

        await using var gapRecovery = await syncService.StartAsync(library.Id);
        Require(
            gapRecovery.BootstrapMode == LibrarySyncBootstrapMode.ReconcileFallback,
            "Journal identifier mismatch did not force reconciliation fallback.");
        Require(
            gapRecovery.CatchUp is { RequiresReconcile: true },
            "Journal identifier mismatch was not reported as a USN gap.");

        _ = await WaitForAsync(
            () => repository.GetAssetAsync(library.Id, "journal-gap.jpg"),
            static asset => asset.FileSize == 4,
            "Journal-gap reconciliation did not recover the offline file.");
    }

    // Explicit watcher overflow must force a safe reconciliation.
    var overflowPath = Path.Combine(libraryRoot, "overflow.jpg");
    await File.WriteAllBytesAsync(overflowPath, [7, 7, 7]);
    var reconcile = new LibraryReconciler(repository);
    var overflowSeed = await reconcile.ReconcileAsync(library.Id);
    Require(overflowSeed.Completed, "Overflow seed reconciliation failed.");
    Require(
        await repository.GetAssetAsync(library.Id, "overflow.jpg") is not null,
        "Overflow seed asset was not indexed.");

    File.Delete(overflowPath);

    var libraryInfo = await repository.GetLibraryAsync(library.Id)
        ?? throw new InvalidOperationException("Library disappeared before overflow test.");

    await using (var overflowProcessor = new LibraryChangeProcessor(
                     library.Id,
                     libraryInfo,
                     repository,
                     reconcile))
    {
        overflowProcessor.Publish(
            [
                new DirectoryChange(
                    DirectoryChangeKind.Overflow,
                    string.Empty,
                    null,
                    DateTimeOffset.UtcNow)
            ]);

        await WaitUntilAsync(
            async () =>
            {
                var diagnostics = overflowProcessor.Diagnostics;
                var asset = await repository.GetAssetAsync(library.Id, "overflow.jpg");
                return diagnostics.Reconciliations > 0 && asset is null;
            },
            "Explicit watcher overflow did not reconcile stale state.");
    }

    await repository.UpsertAssetsAsync(
        library.Id,
        [
            new AssetUpsert(
                "collision-source.jpg",
                10,
                DateTimeOffset.UtcNow,
                Format: "jpg"),
            new AssetUpsert(
                "collision-destination.jpg",
                20,
                DateTimeOffset.UtcNow.AddSeconds(1),
                Format: "jpg")
        ]);

    var collisionSource = await repository.GetAssetAsync(
        library.Id,
        "collision-source.jpg")
        ?? throw new InvalidOperationException("Rename-collision source fixture missing.");

    var collisionDestination = await repository.GetAssetAsync(
        library.Id,
        "collision-destination.jpg")
        ?? throw new InvalidOperationException("Rename-collision destination fixture missing.");

    Require(
        collisionSource.Id != collisionDestination.Id,
        "Rename-collision fixtures unexpectedly shared identity.");

    var collisionRenamed = await repository.RenameAssetAsync(
        library.Id,
        "collision-source.jpg",
        new AssetUpsert(
            "collision-destination.jpg",
            30,
            DateTimeOffset.UtcNow.AddSeconds(2),
            Format: "jpg"));

    Require(collisionRenamed, "Rename into an existing destination was not applied.");

    var collisionAfter = await repository.GetAssetAsync(
        library.Id,
        "collision-destination.jpg")
        ?? throw new InvalidOperationException("Rename destination disappeared.");

    Require(
        collisionAfter.Id == collisionSource.Id,
        "Rename replacement did not preserve source identity.");
    Require(
        collisionAfter.Id != collisionDestination.Id,
        "Rename replacement incorrectly preserved overwritten destination identity.");
    Require(
        await repository.GetAssetAsync(library.Id, "collision-source.jpg") is null,
        "Rename replacement retained the old source path.");

    // Remove DB-only collision fixture before filesystem reconciliation.
    Require(
        await repository.RemoveAssetAsync(library.Id, "collision-destination.jpg"),
        "Rename-collision fixture cleanup failed.");

    // Reconciliation deletes stale rows only after a complete walk.
    await File.WriteAllBytesAsync(
        Path.Combine(libraryRoot, "stale.jpg"),
        [1]);
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
