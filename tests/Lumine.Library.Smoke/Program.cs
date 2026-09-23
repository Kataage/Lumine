using Lumine.Library;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var tempRoot = Path.Combine(Path.GetTempPath(), $"lumine-library-smoke-{Guid.NewGuid():N}");
var libraryRoot = Path.Combine(tempRoot, "library");
var databasePath = Path.Combine(tempRoot, "data", "library.db");

Directory.CreateDirectory(Path.Combine(libraryRoot, "nested"));
await File.WriteAllBytesAsync(Path.Combine(libraryRoot, "a.jpg"), [1, 2, 3]);
await File.WriteAllBytesAsync(Path.Combine(libraryRoot, "b.png"), [4, 5]);
await File.WriteAllBytesAsync(Path.Combine(libraryRoot, "nested", "c.webp"), [6]);
await File.WriteAllBytesAsync(Path.Combine(libraryRoot, "nested", "ignored.txt"), [7]);

try
{
    var database = new LibraryDatabase(databasePath);
    await database.InitializeAsync();

    var repository = new LibraryRepository(database);
    var library = await repository.RegisterLibraryAsync("Smoke", libraryRoot);
    var scanner = new LibraryScanner(repository);
    var scan = await scanner.ScanAsync(library.Id, batchSize: 2);

    Require(scan.Completed, "Initial scan did not complete.");
    Require(scan.Discovered == 3, $"Expected 3 image assets, got {scan.Discovered}.");
    Require(await repository.CountAssetsAsync(library.Id) == 3, "Initial asset count mismatch.");

    var first = await repository.GetAssetAsync(library.Id, "a.jpg")
        ?? throw new InvalidOperationException("a.jpg was not indexed.");
    var stableId = first.Id;

    await repository.UpsertAssetsAsync(
        library.Id,
        [
            new AssetUpsert(
                "a.jpg",
                999,
                DateTimeOffset.UtcNow,
                1920,
                1080,
                "jpeg")
        ]);

    var updated = await repository.GetAssetAsync(library.Id, "a.jpg")
        ?? throw new InvalidOperationException("Updated a.jpg was not found.");
    Require(updated.Id == stableId, "Asset identity changed during path-stable update.");
    Require(updated.Width == 1920 && updated.Height == 1080, "Technical metadata update failed.");

    const int fixtureCount = 2500;
    const int batchSize = 250;
    var batch = new List<AssetUpsert>(batchSize);
    var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    for (var index = 0; index < fixtureCount; index++)
    {
        batch.Add(new AssetUpsert(
            $"fixture/{index % 20:D2}/asset-{index:D6}.jpg",
            1000 + index,
            baseTime.AddSeconds(index),
            512 + index % 100,
            512 + index % 80,
            "jpeg"));

        if (batch.Count == batchSize)
        {
            await repository.UpsertAssetsAsync(library.Id, batch);
            batch.Clear();
        }
    }

    if (batch.Count > 0)
    {
        await repository.UpsertAssetsAsync(library.Id, batch);
    }

    var expectedCount = 3L + fixtureCount;
    Require(await repository.CountAssetsAsync(library.Id) == expectedCount, "Fixture insert count mismatch.");

    var seen = new HashSet<long>();
    AssetCursor? cursor = null;
    var traversed = 0;

    do
    {
        var page = await repository.GetAssetPageAsync(library.Id, 137, cursor);
        foreach (var asset in page.Items)
        {
            Require(seen.Add(asset.Id), "Keyset pagination returned a duplicate asset.");
            traversed++;
        }

        cursor = page.NextCursor;
    }
    while (cursor is not null);

    Require(traversed == expectedCount, $"Keyset traversal returned {traversed}, expected {expectedCount}.");

    var beforeRollback = await repository.CountAssetsAsync(library.Id);
    try
    {
        await repository.UpsertAssetsAsync(
            library.Id,
            [
                new AssetUpsert("rollback/valid.jpg", 1, DateTimeOffset.UtcNow),
                new AssetUpsert("rollback/invalid.jpg", -1, DateTimeOffset.UtcNow)
            ]);
        throw new InvalidOperationException("Invalid transaction unexpectedly succeeded.");
    }
    catch (ArgumentOutOfRangeException)
    {
    }

    Require(
        await repository.CountAssetsAsync(library.Id) == beforeRollback,
        "Failed batch left a partial transaction behind.");

    LibraryDatabase.ClearPools();

    var reopenedDatabase = new LibraryDatabase(databasePath);
    await reopenedDatabase.InitializeAsync();
    var reopenedRepository = new LibraryRepository(reopenedDatabase);

    var reopenedLibrary = await reopenedRepository.GetLibraryAsync(library.Id)
        ?? throw new InvalidOperationException("Library was not available after reopening.");
    Require(reopenedLibrary.LastScanCompletedAtUtc is not null, "Completed scan checkpoint was not persisted.");
    Require(
        await reopenedRepository.CountAssetsAsync(library.Id) == expectedCount,
        "Reopened database did not expose the existing asset index.");

    Console.WriteLine($"Library smoke test: {expectedCount:N0} assets, keyset pagination/restart/rollback OK");
}
finally
{
    LibraryDatabase.ClearPools();
    if (Directory.Exists(tempRoot))
    {
        Directory.Delete(tempRoot, recursive: true);
    }
}
