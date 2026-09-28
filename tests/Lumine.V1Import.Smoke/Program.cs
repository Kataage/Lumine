using System.Security.Cryptography;
using Lumine.Library;
using Microsoft.Data.Sqlite;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task CreateV1FixtureAsync(
    string databasePath,
    string libraryRoot,
    int rating = 5,
    string notes = "legacy note 猫耳",
    string colorLabel = "blue")
{
    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    Directory.CreateDirectory(Path.Combine(libraryRoot, "nested"));
    await File.WriteAllBytesAsync(
        Path.Combine(libraryRoot, "nested", "a.jpg"),
        [1, 2, 3, 4]);
    await File.WriteAllBytesAsync(
        Path.Combine(libraryRoot, "b.png"),
        [5, 6, 7]);

    await using var connection = new SqliteConnection(
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
    await connection.OpenAsync();

    await using (var schema = connection.CreateCommand())
    {
        schema.CommandText =
            """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE _migrations (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                applied_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            INSERT INTO _migrations(id, name) VALUES
                (1, '001_initial_schema.sql'),
                (2, '002_indexes.sql'),
                (3, '003_perf_indexes.sql'),
                (4, '004_exif_columns.sql'),
                (5, '005_lazy_metadata.sql'),
                (6, '006_creative_workflow.sql');

            CREATE TABLE libraries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                root_path TEXT NOT NULL UNIQUE,
                is_enabled BOOLEAN NOT NULL DEFAULT 1,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                last_scanned_at DATETIME
            );

            CREATE TABLE folders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                library_id INTEGER NOT NULL,
                path TEXT NOT NULL UNIQUE,
                parent_path TEXT,
                is_excluded BOOLEAN NOT NULL DEFAULT 0,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE assets (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                library_id INTEGER NOT NULL,
                folder_path TEXT NOT NULL,
                file_name TEXT NOT NULL,
                file_path TEXT NOT NULL UNIQUE,
                extension TEXT NOT NULL,
                file_size INTEGER NOT NULL DEFAULT 0,
                created_at_fs DATETIME,
                modified_at_fs DATETIME,
                width INTEGER NOT NULL DEFAULT 0,
                height INTEGER NOT NULL DEFAULT 0,
                mime_type TEXT,
                hash_blake3 TEXT,
                thumb_status TEXT NOT NULL DEFAULT 'none',
                rating INTEGER NOT NULL DEFAULT 0,
                status_label TEXT NOT NULL DEFAULT 'unsorted',
                is_favorite BOOLEAN NOT NULL DEFAULT 0,
                color_label TEXT,
                indexed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                camera_model TEXT NOT NULL DEFAULT '',
                lens_model TEXT NOT NULL DEFAULT '',
                focal_length TEXT NOT NULL DEFAULT '',
                aperture TEXT NOT NULL DEFAULT '',
                shutter_speed TEXT NOT NULL DEFAULT '',
                iso INTEGER NOT NULL DEFAULT 0,
                exif_date TEXT NOT NULL DEFAULT '',
                gps_latitude TEXT NOT NULL DEFAULT '',
                gps_longitude TEXT NOT NULL DEFAULT '',
                metadata_loaded INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE asset_notes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_id INTEGER NOT NULL UNIQUE,
                content TEXT NOT NULL DEFAULT '',
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE tags (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                color TEXT NOT NULL DEFAULT '',
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE asset_tags (
                asset_id INTEGER NOT NULL,
                tag_id INTEGER NOT NULL,
                PRIMARY KEY (asset_id, tag_id)
            );

            CREATE TABLE works (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                description TEXT NOT NULL DEFAULT ''
            );

            INSERT INTO works(title, description)
            VALUES ('legacy work', 'intentionally unsupported');

            INSERT INTO folders(
                id, library_id, path, parent_path, is_excluded)
            VALUES(1, 1, 'excluded', NULL, 1);
            """;
        await schema.ExecuteNonQueryAsync();
    }

    await using (var library = connection.CreateCommand())
    {
        library.CommandText =
            """
            INSERT INTO libraries(
                id, name, root_path, is_enabled)
            VALUES(1, 'Legacy Library', $root, 0);
            """;
        library.Parameters.AddWithValue("$root", libraryRoot);
        await library.ExecuteNonQueryAsync();
    }

    var firstPath = Path.Combine(
        libraryRoot,
        "nested",
        "a.jpg");
    var secondPath = Path.Combine(
        libraryRoot,
        "b.png");

    await using (var assets = connection.CreateCommand())
    {
        assets.CommandText =
            """
            INSERT INTO assets(
                id, library_id, folder_path, file_name,
                file_path, extension, file_size,
                modified_at_fs, width, height, mime_type,
                rating, status_label, is_favorite,
                color_label, updated_at,
                camera_model, metadata_loaded)
            VALUES(
                10, 1, 'nested', 'a.jpg',
                $first, 'jpg', 4,
                '2026-01-02T03:04:05Z', 4096, 2160, 'image/jpeg',
                $rating, 'reviewed', 1,
                $color, '2026-01-02T03:04:06Z',
                'legacy-camera', 1
            );

            INSERT INTO assets(
                id, library_id, folder_path, file_name,
                file_path, extension, file_size,
                modified_at_fs, width, height, mime_type,
                rating, status_label, is_favorite,
                color_label, updated_at)
            VALUES(
                11, 1, '', 'b.png',
                $second, 'png', 3,
                '2026-01-03T03:04:05Z', 800, 600, 'image/png',
                0, 'unsorted', 0,
                NULL, '2026-01-03T03:04:06Z'
            );
            """;
        assets.Parameters.AddWithValue("$first", firstPath);
        assets.Parameters.AddWithValue("$second", secondPath);
        assets.Parameters.AddWithValue("$rating", rating);
        assets.Parameters.AddWithValue("$color", colorLabel);
        await assets.ExecuteNonQueryAsync();
    }

    await using (var userData = connection.CreateCommand())
    {
        userData.CommandText =
            """
            INSERT INTO asset_notes(asset_id, content)
            VALUES(10, $notes);

            INSERT INTO tags(id, name, color)
            VALUES
                (1, '推し', '#ff00ff'),
                (2, 'blue sky', '');

            INSERT INTO asset_tags(asset_id, tag_id)
            VALUES
                (10, 1),
                (10, 2);
            """;
        userData.Parameters.AddWithValue("$notes", notes);
        await userData.ExecuteNonQueryAsync();
    }

    await using var checkpoint = connection.CreateCommand();
    checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
    await checkpoint.ExecuteNonQueryAsync();
}

static async Task<Dictionary<string, byte[]>> CaptureSourceArtifactsAsync(
    string databasePath)
{
    var result = new Dictionary<string, byte[]>(
        StringComparer.OrdinalIgnoreCase);

    foreach (var path in new[]
             {
                 databasePath,
                 databasePath + "-wal",
                 databasePath + "-shm"
             })
    {
        if (File.Exists(path))
        {
            result[Path.GetFileName(path)] =
                await File.ReadAllBytesAsync(path);
        }
    }

    return result;
}

static void RequireArtifactsEqual(
    IReadOnlyDictionary<string, byte[]> before,
    IReadOnlyDictionary<string, byte[]> after)
{
    Require(
        before.Count == after.Count,
        "v1 source sidecar set changed during preview/import.");

    foreach (var (name, bytes) in before)
    {
        Require(
            after.TryGetValue(name, out var afterBytes),
            $"v1 source artifact disappeared: {name}");
        Require(
            bytes.AsSpan().SequenceEqual(afterBytes),
            $"v1 source artifact was modified: {name}");
    }
}

static async Task<long> ScalarInt64Async(
    string databasePath,
    string sql)
{
    await using var connection = new SqliteConnection(
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToInt64(await command.ExecuteScalarAsync());
}

static async Task<long> GetOnlyLibraryIdAsync(
    string databasePath)
{
    return await ScalarInt64Async(
        databasePath,
        "SELECT id FROM libraries LIMIT 1;");
}

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    $"lumine-v1-import-smoke-{Guid.NewGuid():N}");
var sourceRoot = Path.Combine(tempRoot, "legacy-library");
var sourcePath = Path.Combine(tempRoot, "legacy", "lumine.db");
var destinationPath = Path.Combine(tempRoot, "v2", "library.db");

Directory.CreateDirectory(tempRoot);

try
{
    await CreateV1FixtureAsync(
        sourcePath,
        sourceRoot);

    var sourceBefore =
        await CaptureSourceArtifactsAsync(sourcePath);

    var importer = new LumineV1Importer(
        new LibraryDatabase(destinationPath));

    var preview = await importer.PreviewAsync(sourcePath);

    Require(
        !File.Exists(destinationPath),
        "Preview/dry-run created or migrated the destination database.");
    Require(preview.CanImport, "Representative v1 preview was unexpectedly blocked.");
    Require(preview.SourceSchemaVersion == 6, "v1 schema version was not detected.");
    Require(preview.Libraries.Count == 1, "Preview library count mismatch.");
    Require(preview.AssetCount == 2 && preview.ImportableAssetCount == 2,
        "Preview asset mapping count mismatch.");
    Require(preview.MeaningfulMetadataAssetCount == 1,
        "Preview meaningful metadata count mismatch.");
    Require(preview.TagAssignmentCount == 2,
        "Preview tag assignment count mismatch.");
    Require(
        preview.Diagnostics.Any(d => d.Code == "unsupported_table"),
        "Unsupported v1 tables were silently omitted from preview.");
    Require(
        preview.Diagnostics.Any(d => d.Code == "folder_exclusion_unsupported"),
        "Folder exclusion loss was not reported.");
    Require(
        preview.Diagnostics.Any(d => d.Code == "tag_color_unsupported"),
        "Tag color loss was not reported.");
    Require(
        preview.Diagnostics.Any(d => d.Code == "library_enabled_state_unsupported"),
        "Disabled-library state loss was not reported.");

    var import = await importer.ImportAsync(
        sourcePath,
        preview.SourceFingerprintSha256);

    Require(!import.AlreadyImported, "First import was incorrectly treated as duplicate.");
    Require(import.LibrariesImported == 1 && import.LibrariesMatched == 0,
        "Library import/match counts are incorrect.");
    Require(import.AssetsImported == 2 && import.AssetsMatched == 0,
        "Asset import/match counts are incorrect.");
    Require(import.MetadataImported == 1 && import.MetadataConflicts == 0,
        "User metadata import counts are incorrect.");
    Require(import.TagAssignmentsImported == 2,
        "Tag assignment import count is incorrect.");

    var sourceAfter =
        await CaptureSourceArtifactsAsync(sourcePath);
    RequireArtifactsEqual(sourceBefore, sourceAfter);

    var destination = new LibraryDatabase(destinationPath);
    await destination.InitializeAsync();
    Require(
        LibraryDatabase.SupportedSchemaVersion == 6,
        "v1 import provenance migration did not advance schema to v6.");

    var repository = new LibraryRepository(destination);
    var libraryId = await GetOnlyLibraryIdAsync(destinationPath);

    Require(await repository.CountAssetsAsync(libraryId) == 2,
        "Imported asset count mismatch.");

    var first = await repository.GetAssetAsync(
        libraryId,
        "nested/a.jpg")
        ?? throw new InvalidOperationException(
            "Imported a.jpg was not found.");
    Require(
        first.Width is null
        && first.Height is null
        && first.Format is null
        && first.SourceIdentity is null,
        "v1 technical/source metadata leaked into v2 instead of remaining rebuildable.");

    var metadata = await repository.GetUserMetadataAsync(
        libraryId,
        first.Id)
        ?? throw new InvalidOperationException(
            "Imported v1 user metadata was not found.");

    Require(
        metadata.Rating == 5
        && metadata.Favorite
        && metadata.StatusLabel == "reviewed"
        && metadata.ColorLabel == "blue"
        && metadata.Notes == "legacy note 猫耳"
        && metadata.Tags.Contains("推し")
        && metadata.Tags.Contains("blue sky"),
        "v1 user metadata did not round-trip into v2.");

    var searched = await repository.GetAssetPageAsync(
        libraryId,
        new AssetQuery(
            SearchText: "猫耳",
            RequiredTags: ["推し"],
            Favorite: true),
        10);
    Require(
        searched.Items.Count == 1
        && searched.Items[0].Id == first.Id,
        "Imported notes/tags were not integrated with v2 local search.");

    Require(
        await ScalarInt64Async(
            destinationPath,
            "SELECT COUNT(*) FROM compatibility_imports;") == 1,
        "Import provenance row was not recorded.");

    var duplicate = await importer.ImportAsync(
        sourcePath,
        preview.SourceFingerprintSha256);
    Require(duplicate.AlreadyImported,
        "Same source fingerprint was not treated as an idempotent import.");
    Require(await repository.CountAssetsAsync(libraryId) == 2,
        "Duplicate import created duplicate assets.");
    Require(
        await ScalarInt64Async(
            destinationPath,
            "SELECT COUNT(*) FROM compatibility_imports;") == 1,
        "Duplicate import created duplicate provenance.");

    // A changed source with the same library/path mapping must preserve
    // already-existing v2 user metadata rather than overwrite it.
    var conflictPath = Path.Combine(tempRoot, "legacy-conflict", "lumine.db");
    await CreateV1FixtureAsync(
        conflictPath,
        sourceRoot,
        rating: 1,
        notes: "changed legacy note",
        colorLabel: "red");

    var conflictImporter = new LumineV1Importer(destination);
    var conflictPreview = await conflictImporter.PreviewAsync(conflictPath);
    var conflict = await conflictImporter.ImportAsync(
        conflictPath,
        conflictPreview.SourceFingerprintSha256);

    Require(conflict.LibrariesMatched == 1,
        "Existing destination library was not matched by root.");
    Require(conflict.AssetsMatched == 2,
        "Existing destination assets were not matched by relative path.");
    Require(conflict.MetadataConflicts == 1,
        "Existing v2 user metadata conflict was not reported.");

    var preserved = await repository.GetUserMetadataAsync(
        libraryId,
        first.Id)
        ?? throw new InvalidOperationException(
            "Existing v2 metadata disappeared during conflict import.");
    Require(
        preserved.Rating == 5
        && preserved.ColorLabel == "blue"
        && preserved.Notes == "legacy note 猫耳",
        "Conflict import overwrote existing v2 user metadata.");

    // Preview fingerprint is an explicit approval boundary.
    var changedPath = Path.Combine(tempRoot, "legacy-changed", "lumine.db");
    await CreateV1FixtureAsync(changedPath, sourceRoot);
    var changedDestinationPath =
        Path.Combine(tempRoot, "changed-v2", "library.db");
    var changedImporter = new LumineV1Importer(
        new LibraryDatabase(changedDestinationPath));
    var changedPreview = await changedImporter.PreviewAsync(changedPath);

    await using (var changedConnection = new SqliteConnection(
                     $"Data Source={changedPath};Pooling=False"))
    {
        await changedConnection.OpenAsync();
        await using var change = changedConnection.CreateCommand();
        change.CommandText =
            "UPDATE assets SET rating = 2 WHERE id = 10;";
        await change.ExecuteNonQueryAsync();
    }

    try
    {
        _ = await changedImporter.ImportAsync(
            changedPath,
            changedPreview.SourceFingerprintSha256);
        throw new InvalidOperationException(
            "Changed v1 source was imported against a stale preview fingerprint.");
    }
    catch (LumineV1SourceChangedException)
    {
    }

    Require(
        !File.Exists(changedDestinationPath),
        "Fingerprint mismatch mutated destination state.");

    // Cancellation after the first imported asset must roll the complete
    // user-data transaction back.
    var rollbackPath = Path.Combine(tempRoot, "rollback-v2", "library.db");
    var rollbackImporter = new LumineV1Importer(
        new LibraryDatabase(rollbackPath));
    var rollbackPreview =
        await rollbackImporter.PreviewAsync(sourcePath);
    using var cancellation = new CancellationTokenSource();
    var progress = new InlineProgress<LumineV1ImportProgress>(
        value =>
        {
            if (value.AssetsProcessed >= 1)
            {
                cancellation.Cancel();
            }
        });

    try
    {
        _ = await rollbackImporter.ImportAsync(
            sourcePath,
            rollbackPreview.SourceFingerprintSha256,
            progress,
            cancellation.Token);
        throw new InvalidOperationException(
            "Cancelled v1 import unexpectedly committed.");
    }
    catch (OperationCanceledException)
    {
    }

    Require(
        await ScalarInt64Async(
            rollbackPath,
            "SELECT COUNT(*) FROM libraries;") == 0,
        "Cancelled v1 import left a partial library.");
    Require(
        await ScalarInt64Async(
            rollbackPath,
            "SELECT COUNT(*) FROM assets;") == 0,
        "Cancelled v1 import left partial assets.");
    Require(
        await ScalarInt64Async(
            rollbackPath,
            "SELECT COUNT(*) FROM compatibility_imports;") == 0,
        "Cancelled v1 import recorded provenance.");

    // Bounds validation must reject, rather than truncate, unsupported user
    // metadata.
    var invalidPath = Path.Combine(tempRoot, "legacy-invalid", "lumine.db");
    await CreateV1FixtureAsync(
        invalidPath,
        sourceRoot,
        colorLabel: new string('x', 65));
    var invalidPreview =
        await importer.PreviewAsync(invalidPath);
    Require(!invalidPreview.CanImport,
        "Out-of-bounds v1 user metadata was silently accepted.");
    Require(
        invalidPreview.Diagnostics.Any(
            d => d.Code == "user_metadata_out_of_bounds"),
        "Out-of-bounds v1 metadata did not produce a blocking diagnostic.");

    Console.WriteLine(
        "Lumine v1 import smoke: preview/source-integrity/import/search/idempotency/conflict/fingerprint/rollback/bounds OK");
}
finally
{
    LibraryDatabase.ClearPools();
    if (Directory.Exists(tempRoot))
    {
        Directory.Delete(tempRoot, recursive: true);
    }
}

internal sealed class InlineProgress<T>(
    Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
