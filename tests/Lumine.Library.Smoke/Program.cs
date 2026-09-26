using System.Globalization;
using Lumine.Library;
using Microsoft.Data.Sqlite;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task CreateLegacyV1DatabaseAsync(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

    await using var connection = new SqliteConnection(
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        PRAGMA journal_mode=WAL;

        CREATE TABLE schema_migrations (
            version INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            applied_at_utc_ticks INTEGER NOT NULL
        );

        INSERT INTO schema_migrations(version, name, applied_at_utc_ticks)
        VALUES (1, 'initial-library-core', 1);

        CREATE TABLE libraries (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            root_path TEXT NOT NULL,
            root_path_key TEXT NOT NULL UNIQUE,
            created_at_utc_ticks INTEGER NOT NULL,
            updated_at_utc_ticks INTEGER NOT NULL,
            last_scan_completed_at_utc_ticks INTEGER NULL
        );

        CREATE TABLE folders (
            id INTEGER PRIMARY KEY,
            library_id INTEGER NOT NULL,
            relative_path TEXT NOT NULL,
            relative_path_key TEXT NOT NULL,
            created_at_utc_ticks INTEGER NOT NULL,
            UNIQUE(library_id, relative_path_key),
            FOREIGN KEY(library_id) REFERENCES libraries(id) ON DELETE CASCADE
        );

        CREATE TABLE assets (
            id INTEGER PRIMARY KEY,
            library_id INTEGER NOT NULL,
            folder_id INTEGER NULL,
            relative_path TEXT NOT NULL,
            relative_path_key TEXT NOT NULL,
            file_name TEXT NOT NULL,
            extension TEXT NOT NULL,
            file_size INTEGER NOT NULL CHECK(file_size >= 0),
            modified_at_utc_ticks INTEGER NOT NULL,
            width INTEGER NULL CHECK(width IS NULL OR width > 0),
            height INTEGER NULL CHECK(height IS NULL OR height > 0),
            format TEXT NULL,
            created_at_utc_ticks INTEGER NOT NULL,
            updated_at_utc_ticks INTEGER NOT NULL,
            UNIQUE(library_id, relative_path_key),
            FOREIGN KEY(library_id) REFERENCES libraries(id) ON DELETE CASCADE,
            FOREIGN KEY(folder_id) REFERENCES folders(id) ON DELETE SET NULL
        );

        CREATE INDEX idx_assets_library_modified_id
            ON assets(library_id, modified_at_utc_ticks DESC, id DESC);

        CREATE INDEX idx_assets_library_folder
            ON assets(library_id, folder_id);

        INSERT INTO libraries(
            id, name, root_path, root_path_key,
            created_at_utc_ticks, updated_at_utc_ticks,
            last_scan_completed_at_utc_ticks)
        VALUES (1, 'Legacy', 'C:/legacy', 'C:/LEGACY', 1, 1, 1);

        INSERT INTO assets(
            id, library_id, folder_id,
            relative_path, relative_path_key,
            file_name, extension,
            file_size, modified_at_utc_ticks,
            width, height, format,
            created_at_utc_ticks, updated_at_utc_ticks)
        VALUES (
            42, 1, NULL,
            'legacy.jpg', 'LEGACY.JPG',
            'legacy.jpg', 'jpg',
            123, 10,
            640, 480, 'jpeg',
            1, 1);
        """;
    await command.ExecuteNonQueryAsync();
}

static async Task CreateFutureSchemaDatabaseAsync(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

    await using var connection = new SqliteConnection(
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        CREATE TABLE schema_migrations (
            version INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            applied_at_utc_ticks INTEGER NOT NULL
        );

        INSERT INTO schema_migrations(version, name, applied_at_utc_ticks)
        VALUES
            (1, 'initial-library-core', 1),
            (2, 'harden-library-core-invariants', 2),
            (3, 'incremental-filesystem-sync', 3),
            (4, 'persist-source-technical-metadata', 4),
            (5, 'future-schema', 5);
        """;
    await command.ExecuteNonQueryAsync();
}

static async Task CreateUntrackedProductDatabaseAsync(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

    await using var connection = new SqliteConnection(
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        CREATE TABLE schema_migrations (
            version INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            applied_at_utc_ticks INTEGER NOT NULL
        );

        CREATE TABLE libraries (
            id INTEGER PRIMARY KEY
        );
        """;
    await command.ExecuteNonQueryAsync();
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
    Require(LibraryDatabase.SupportedSchemaVersion == 4, "Unexpected Library schema version.");

    await using (var walConnection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
    {
        await walConnection.OpenAsync();
        await using var wal = walConnection.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode;";
        var journalMode = Convert.ToString(await wal.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        Require(
            string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase),
            $"Expected WAL journal mode, found '{journalMode}'.");
    }

    var repository = new LibraryRepository(database);
    var library = await repository.RegisterLibraryAsync("Smoke", libraryRoot);
    var scanner = new LibraryScanner(repository);
    var scan = await scanner.ScanAsync(library.Id, batchSize: 2);

    Require(scan.Completed, "Initial scan did not complete.");
    Require(scan.Skipped == 0 && scan.FailureSamples.Count == 0, "Clean scan reported failures.");
    Require(scan.Discovered == 3, $"Expected 3 image assets, got {scan.Discovered}.");
    Require(await repository.CountAssetsAsync(library.Id) == 3, "Initial asset count mismatch.");

    var scannedLibrary = await repository.GetLibraryAsync(library.Id)
        ?? throw new InvalidOperationException("Library disappeared after scan.");
    Require(scannedLibrary.ScanState == LibraryScanState.Complete, "Completed scan state was not persisted.");
    Require(scannedLibrary.LastScanCompletedAtUtc is not null, "Completed scan timestamp was not persisted.");

    var first = await repository.GetAssetAsync(library.Id, "a.jpg")
        ?? throw new InvalidOperationException("a.jpg was not indexed.");
    var stableId = first.Id;
    var stableRevision = first.SourceRevision;

    await repository.UpsertAssetsAsync(
        library.Id,
        [
            new AssetUpsert(
                "a.jpg",
                first.FileSize,
                first.ModifiedAtUtc,
                1920,
                1080,
                "jpeg")
        ]);

    var enriched = await repository.GetAssetAsync(library.Id, "a.jpg")
        ?? throw new InvalidOperationException("Enriched a.jpg was not found.");
    Require(enriched.Id == stableId, "Asset identity changed during path-stable metadata enrichment.");
    Require(enriched.SourceRevision == stableRevision, "Metadata enrichment changed source revision.");
    Require(enriched.Width == 1920 && enriched.Height == 1080, "Technical metadata enrichment failed.");

    var sameStat = await repository.GetAssetAsync(library.Id, "b.png")
        ?? throw new InvalidOperationException("b.png was not indexed.");
    var technicalSha = "sha256:" + new string('a', 64);
    Require(
        await repository.UpdateTechnicalMetadataAsync(
            library.Id,
            sameStat.Id,
            sameStat.SourceRevision,
            sameStat.FileSize,
            sameStat.ModifiedAtUtc.UtcDateTime.Ticks,
            new AssetTechnicalMetadata(
                640,
                480,
                640,
                480,
                true,
                "png",
                technicalSha)),
        "Technical metadata update was rejected for the current source revision.");

    var technical = await repository.GetAssetAsync(library.Id, "b.png")
        ?? throw new InvalidOperationException("Technical metadata asset disappeared.");
    Require(
        technical.Width == 640
        && technical.Height == 480
        && technical.RawWidth == 640
        && technical.RawHeight == 480
        && technical.HasAlpha == true
        && technical.SourceIdentity == technicalSha,
        "Persistent source technical metadata did not round-trip.");

    await repository.UpsertAssetsAsync(
        library.Id,
        [
            new AssetUpsert(
                "b.png",
                technical.FileSize,
                technical.ModifiedAtUtc,
                Format: "png",
                ForceSourceRevision: true)
        ]);

    var forcedRevision = await repository.GetAssetAsync(library.Id, "b.png")
        ?? throw new InvalidOperationException("Forced-revision asset disappeared.");
    Require(
        forcedRevision.SourceRevision == technical.SourceRevision + 1,
        "Explicit same-stat source change did not advance source_revision.");
    Require(
        forcedRevision.Width is null
        && forcedRevision.Height is null
        && forcedRevision.RawWidth is null
        && forcedRevision.RawHeight is null
        && forcedRevision.HasAlpha is null
        && forcedRevision.SourceIdentity is null,
        "Explicit same-stat source change retained stale technical metadata.");
    Require(
        !await repository.UpdateTechnicalMetadataAsync(
            library.Id,
            forcedRevision.Id,
            technical.SourceRevision,
            forcedRevision.FileSize,
            forcedRevision.ModifiedAtUtc.UtcDateTime.Ticks,
            new AssetTechnicalMetadata(
                640,
                480,
                640,
                480,
                true,
                "png",
                technicalSha)),
        "Stale source revision was allowed to overwrite technical metadata.");

    await repository.UpsertAssetsAsync(
        library.Id,
        [
            new AssetUpsert(
                "a.jpg",
                enriched.FileSize + 1,
                enriched.ModifiedAtUtc.AddSeconds(1),
                Format: "jpg")
        ]);

    var sourceChanged = await repository.GetAssetAsync(library.Id, "a.jpg")
        ?? throw new InvalidOperationException("Source-changed a.jpg was not found.");
    Require(sourceChanged.Id == stableId, "Source update replaced stable row identity.");
    Require(sourceChanged.SourceRevision == stableRevision + 1, "Source revision did not advance.");
    Require(sourceChanged.Width is null && sourceChanged.Height is null, "Stale derived dimensions survived source change.");

    var beforeRollback = await repository.CountAssetsAsync(library.Id);
    var rollbackBatch = new List<AssetUpsert>(65);
    for (var index = 0; index < 64; index++)
    {
        rollbackBatch.Add(new AssetUpsert(
            $"rollback/valid-{index:D2}.jpg",
            1,
            DateTimeOffset.UtcNow,
            Format: "jpeg"));
    }

    rollbackBatch.Add(new AssetUpsert(
        "rollback/sql-failure.jpg",
        1,
        DateTimeOffset.UtcNow,
        Format: new string('x', 65)));

    try
    {
        await repository.UpsertAssetsAsync(library.Id, rollbackBatch);
        throw new InvalidOperationException("SQL constraint failure unexpectedly succeeded.");
    }
    catch (SqliteException)
    {
    }

    Require(
        await repository.CountAssetsAsync(library.Id) == beforeRollback,
        "SQL failure after a prior write command left a partial transaction behind.");
    Require(
        await repository.GetAssetAsync(library.Id, "rollback/valid-00.jpg") is null,
        "A row written before SQL failure survived rollback.");

    Require(await repository.RemoveAssetAsync(library.Id, "a.jpg"), "Asset removal failed.");
    await repository.UpsertAssetsAsync(
        library.Id,
        [
            new AssetUpsert(
                "a.jpg",
                4,
                DateTimeOffset.UtcNow,
                Format: "jpg")
        ]);

    var readded = await repository.GetAssetAsync(library.Id, "a.jpg")
        ?? throw new InvalidOperationException("Re-added a.jpg was not found.");
    Require(readded.Id > stableId, "Deleted asset row id was reused.");
    Require(readded.SourceRevision == 1, "Re-added asset did not start a fresh source identity.");

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

    var callerThread = Environment.CurrentManagedThreadId;
    var workerThread = LibraryBackgroundExecution.RunAsync(
        _ => Task.FromResult(Environment.CurrentManagedThreadId)).GetAwaiter().GetResult();
    Require(workerThread != callerThread, "Library background boundary executed inline on the caller thread.");

    var service = new LibraryService(databasePath);
    await service.InitializeAsync();
    var servicePage = await service.GetAssetPageAsync(library.Id, 10);
    Require(servicePage.Items.Count == 10, "LibraryService background query boundary failed.");

    var previousCompletedAt = (await repository.GetLibraryAsync(library.Id))!.LastScanCompletedAtUtc;
    Directory.Delete(libraryRoot, recursive: true);

    var partialScan = await scanner.ScanAsync(library.Id);
    Require(!partialScan.Completed, "Missing library root was incorrectly marked complete.");
    Require(partialScan.Skipped > 0, "Partial scan did not report a filesystem failure.");
    Require(partialScan.FailureSamples.Count > 0, "Partial scan did not retain a bounded failure sample.");

    var partialLibrary = await repository.GetLibraryAsync(library.Id)
        ?? throw new InvalidOperationException("Library disappeared after partial scan.");
    Require(partialLibrary.ScanState == LibraryScanState.Partial, "Partial scan state was not persisted.");
    Require(
        partialLibrary.LastScanCompletedAtUtc == previousCompletedAt,
        "Partial scan destroyed the last known complete scan checkpoint.");

    LibraryDatabase.ClearPools();

    var reopenedDatabase = new LibraryDatabase(databasePath);
    await reopenedDatabase.InitializeAsync();
    var reopenedRepository = new LibraryRepository(reopenedDatabase);
    Require(
        await reopenedRepository.CountAssetsAsync(library.Id) == expectedCount,
        "Reopened database did not expose the existing asset index.");

    var legacyPath = Path.Combine(tempRoot, "legacy-v1.db");
    await CreateLegacyV1DatabaseAsync(legacyPath);
    var legacyDatabase = new LibraryDatabase(legacyPath);
    await legacyDatabase.InitializeAsync();

    await using (var legacyConnection = new SqliteConnection($"Data Source={legacyPath};Pooling=False"))
    {
        await legacyConnection.OpenAsync();
        await using var migration = legacyConnection.CreateCommand();
        migration.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        Require(Convert.ToInt32(await migration.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 4, "v1 database did not migrate to v4.");

        await using var asset = legacyConnection.CreateCommand();
        asset.CommandText = "SELECT id, source_revision, width, height, observed_generation FROM assets WHERE relative_path = 'legacy.jpg';";
        await using var reader = await asset.ExecuteReaderAsync();
        Require(await reader.ReadAsync(), "Legacy asset disappeared during migration.");
        Require(reader.GetInt64(0) == 42, "Migration changed existing stable asset id.");
        Require(reader.GetInt64(1) == 1, "Migrated asset source revision was not initialized.");
        Require(reader.GetInt32(2) == 640 && reader.GetInt32(3) == 480, "Migration lost technical metadata.");
        Require(reader.GetInt64(4) == 0, "Migrated legacy asset should start outside any reconciliation generation.");
    }

    var futurePath = Path.Combine(tempRoot, "future.db");
    await CreateFutureSchemaDatabaseAsync(futurePath);
    var futureDatabase = new LibraryDatabase(futurePath);
    try
    {
        await futureDatabase.InitializeAsync();
        throw new InvalidOperationException("Future schema was incorrectly accepted.");
    }
    catch (LibrarySchemaException)
    {
    }

    await using (var futureConnection = new SqliteConnection($"Data Source={futurePath};Pooling=False"))
    {
        await futureConnection.OpenAsync();
        await using var journal = futureConnection.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode;";
        var mode = Convert.ToString(await journal.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        Require(
            !string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase),
            "Rejected future schema was modified before fail-closed validation.");
    }

    var untrackedPath = Path.Combine(tempRoot, "untracked.db");
    await CreateUntrackedProductDatabaseAsync(untrackedPath);
    var untrackedDatabase = new LibraryDatabase(untrackedPath);
    try
    {
        await untrackedDatabase.InitializeAsync();
        throw new InvalidOperationException("Existing product tables without migration history were accepted.");
    }
    catch (LibrarySchemaException)
    {
    }

    Console.WriteLine(
        $"Library hardening smoke: {expectedCount:N0} assets, rollback/migration/partial-scan/source-revision/identity OK");
}
finally
{
    LibraryDatabase.ClearPools();
    if (Directory.Exists(tempRoot))
    {
        Directory.Delete(tempRoot, recursive: true);
    }
}
