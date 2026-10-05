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
            (5, 'user-metadata-and-local-search', 5),
            (6, 'product-navigation-library-state', 6),
            (7, 'creative-archive-domain', 7),
            (8, 'tag-color-and-management-parity', 8),
            (9, 'publication-destination-and-account-profiles', 9),
            (10, 'future-schema', 10);
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
var secondaryRoot = Path.Combine(tempRoot, "secondary-library");

Directory.CreateDirectory(Path.Combine(libraryRoot, "nested"));
Directory.CreateDirectory(secondaryRoot);
await File.WriteAllBytesAsync(Path.Combine(libraryRoot, "a.jpg"), [1, 2, 3]);
await File.WriteAllBytesAsync(Path.Combine(libraryRoot, "b.png"), [4, 5]);
await File.WriteAllBytesAsync(Path.Combine(libraryRoot, "nested", "c.webp"), [6]);
await File.WriteAllBytesAsync(Path.Combine(libraryRoot, "custom.jfif"), [7, 8]);
await File.WriteAllBytesAsync(Path.Combine(libraryRoot, "nested", "ignored.txt"), [9]);

try
{
    var database = new LibraryDatabase(databasePath);
    await database.InitializeAsync();
    Require(LibraryDatabase.SupportedSchemaVersion == 9, "Unexpected Library schema version.");

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
    var defaultPublicationDestinations =
        await repository.ListPublicationDestinationsAsync(
            library.Id);
    Require(
        new[] { "Pixiv", "X", "Misskey", "Bluesky", "その他" }
            .All(
                name =>
                    defaultPublicationDestinations.Any(
                        destination =>
                            string.Equals(
                                destination.Name,
                                name,
                                StringComparison.Ordinal))),
        "Publication profile migration/default trigger did not seed the expected local destinations.");
    var scanFileTypes =
        new LibraryFileTypePolicy(
            [".JPG", "webp"]);
    Require(
        scanFileTypes.Extensions.SequenceEqual(
            new[] { ".jpg", ".webp" },
            StringComparer.Ordinal),
        "Scan extension policy did not normalize case/leading dots deterministically.");
    var scanner =
        new LibraryScanner(
            repository,
            scanFileTypes);
    var filteredScan =
        await scanner.ScanAsync(
            library.Id,
            batchSize: 2);

    Require(filteredScan.Completed, "Filtered initial scan did not complete.");
    Require(
        filteredScan.Discovered == 2
        && await repository.CountAssetsAsync(library.Id) == 2
        && await repository.GetAssetAsync(
            library.Id,
            "b.png") is null,
        "Scanner did not honor the configured extension policy.");

    Require(
        await repository.GetAssetAsync(
            library.Id,
            "custom.jfif") is null,
        "Scanner indexed a custom extension before it was explicitly enabled.");

    scanFileTypes.Update(
        [".jpg", ".webp", "JFIF"]);
    var customScan =
        await scanner.ScanAsync(
            library.Id,
            batchSize: 2);
    Require(
        customScan.Completed
        && await repository.CountAssetsAsync(library.Id) == 3
        && await repository.GetAssetAsync(
            library.Id,
            "custom.jfif") is not null,
        "Scanner did not honor an explicitly enabled normalized custom extension.");

    var customReconciler =
        new LibraryReconciler(
            repository,
            scanFileTypes);
    scanFileTypes.Update(
        [".jpg", ".webp"]);
    var customRemoval =
        await customReconciler.ReconcileAsync(
            library.Id);
    Require(
        customRemoval.Completed
        && await repository.GetAssetAsync(
            library.Id,
            "custom.jfif") is null
        && File.Exists(
            Path.Combine(
                libraryRoot,
                "custom.jfif")),
        "Reconcile did not remove the custom-extension asset from the index while preserving its source file.");

    scanFileTypes.Update(
        LibraryFileTypes.DefaultExtensions);
    var scan = await scanner.ScanAsync(library.Id, batchSize: 2);

    Require(scan.Completed, "Initial scan did not complete.");
    Require(scan.Skipped == 0 && scan.FailureSamples.Count == 0, "Clean scan reported failures.");
    Require(scan.Discovered == 3, $"Expected 3 image assets, got {scan.Discovered}.");
    Require(await repository.CountAssetsAsync(library.Id) == 3, "Initial asset count mismatch.");

    var policyReconciler =
        new LibraryReconciler(
            repository,
            scanFileTypes);
    scanFileTypes.Update(
        [".jpg", ".webp"]);
    var filteredReconcile =
        await policyReconciler.ReconcileAsync(
            library.Id);
    Require(
        filteredReconcile.Completed
        && await repository.GetAssetAsync(
            library.Id,
            "b.png") is null
        && await repository.GetAssetAsync(
            library.Id,
            "a.jpg") is not null
        && await repository.GetAssetAsync(
            library.Id,
            "nested/c.webp") is not null,
        "Reconcile did not remove an asset excluded by the live extension policy.");

    scanFileTypes.Update(
        LibraryFileTypes.DefaultExtensions);
    var restoredReconcile =
        await policyReconciler.ReconcileAsync(
            library.Id);
    Require(
        restoredReconcile.Completed
        && await repository.GetAssetAsync(
            library.Id,
            "b.png") is not null,
        "Reconcile did not restore a re-enabled extension.");

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


    var userMetadata = await repository.SetUserMetadataAsync(
        library.Id,
        technical.Id,
        new AssetUserMetadataUpdate(
            Rating: 4,
            Favorite: true,
            Notes: "猫耳 メイド reference note",
            StatusLabel: "reviewed",
            ColorLabel: "blue",
            Tags: ["推し", "blue sky"]));

    Require(
        userMetadata.Rating == 4
        && userMetadata.Favorite
        && userMetadata.Notes.Contains("猫耳", StringComparison.Ordinal)
        && userMetadata.StatusLabel == "reviewed"
        && userMetadata.ColorLabel == "blue"
        && userMetadata.Tags.Count == 2,
        "User metadata write result was incomplete.");

    var persistedUserMetadata =
        await repository.GetUserMetadataAsync(
            library.Id,
            technical.Id)
        ?? throw new InvalidOperationException(
            "User metadata did not persist.");

    Require(
        persistedUserMetadata.Rating == 4
        && persistedUserMetadata.Favorite
        && persistedUserMetadata.Tags.Contains("推し")
        && persistedUserMetadata.Tags.Contains("blue sky"),
        "User metadata did not round-trip.");


    var catalog =
        await repository.ListLibrariesAsync();
    Require(
        catalog.Count == 1
        && catalog[0].Id == library.Id
        && catalog[0].IsEnabled
        && catalog[0].AssetCount == 3,
        "Library catalog did not expose the registered library and asset count.");

    var folders =
        await repository.ListFoldersAsync(
            library.Id);
    Require(
        folders.Count == 1
        && string.Equals(
            folders[0].RelativePath,
            "nested",
            StringComparison.Ordinal)
        && folders[0].Depth == 1
        && folders[0].DirectAssetCount == 1,
        "Folder navigation index did not expose the scanned hierarchy.");

    var nestedScope =
        await repository.GetAssetPageAsync(
            library.Id,
            new AssetQuery(
                FolderPathPrefix: "nested"),
            10);
    Require(
        nestedScope.Items.Count == 1
        && string.Equals(
            nestedScope.Items[0].RelativePath,
            "nested/c.webp",
            StringComparison.Ordinal),
        "Folder scope query did not restrict assets to the selected hierarchy.");

    var standaloneTag =
        await repository.CreateTagAsync(
            library.Id,
            "後で使う",
            "#22aa88");
    var disposableTag =
        await repository.CreateTagAsync(
            library.Id,
            "削除確認",
            "#f04f5f");

    Require(
        standaloneTag.AssetCount == 0
        && standaloneTag.Color == "#22aa88"
        && disposableTag.AssetCount == 0,
        "Explicit tag creation did not preserve color or zero-asset state.");

    var tags =
        await repository.ListTagsAsync(
            library.Id);
    Require(
        tags.Count == 4
        && tags.Any(tag =>
            tag.Name == "推し"
            && tag.AssetCount == 1
            && tag.Color == "#6366f1")
        && tags.Any(tag =>
            tag.Name == "後で使う"
            && tag.AssetCount == 0
            && tag.Color == "#22aa88"),
        "Tag navigation index did not expose managed tags, colors and counts.");

    try
    {
        _ = await repository.CreateTagAsync(
            library.Id,
            " 推し ",
            "#000000");
        throw new InvalidOperationException(
            "Duplicate explicit tag creation unexpectedly succeeded.");
    }
    catch (InvalidOperationException exception)
        when (exception.Message.Contains(
            "already exists",
            StringComparison.Ordinal))
    {
    }

    var afterDuplicateCreate =
        await repository.ListTagsAsync(
            library.Id);
    Require(
        afterDuplicateCreate.Count == 4
        && afterDuplicateCreate.Any(tag =>
            tag.Name == "推し"
            && tag.Color == "#6366f1"
            && tag.AssetCount == 1),
        "Duplicate create mutated the existing tag instead of failing explicitly.");

    Require(
        await repository.DeleteTagAsync(
            library.Id,
            disposableTag.Id),
        "Explicit tag deletion reported no change.");
    Require(
        (await repository.ListTagsAsync(
            library.Id))
            .All(tag =>
                tag.Id != disposableTag.Id)
        && (await repository.ListTagsAsync(
            library.Id))
            .Any(tag =>
                tag.Id == standaloneTag.Id
                && tag.AssetCount == 0),
        "Deleting one tag removed or corrupted another standalone tag.");

    var searchedTags =
        await repository.ListTagsAsync(
            library.Id,
            "推");
    Require(
        searchedTags.Count == 1
        && searchedTags[0].Name == "推し",
        "Tag navigation search did not filter locally persisted tags.");

    var tagOnlySaved =
        await repository.SetAssetTagsAsync(
            library.Id,
            technical.Id,
            ["推し", "後で使う"]);
    var afterTagOnlySave =
        await repository.GetUserMetadataAsync(
            library.Id,
            technical.Id)
        ?? throw new InvalidOperationException(
            "Tag-only assignment lost user metadata.");
    Require(
        tagOnlySaved.Count == 2
        && tagOnlySaved.Contains(
            "推し",
            StringComparer.Ordinal)
        && tagOnlySaved.Contains(
            "後で使う",
            StringComparer.Ordinal),
        "Tag-only assignment did not persist the requested tag set.");
    Require(
        afterTagOnlySave.Rating == 4
        && afterTagOnlySave.Favorite
        && afterTagOnlySave.Notes
            == "猫耳 メイド reference note"
        && afterTagOnlySave.StatusLabel
            == "reviewed"
        && afterTagOnlySave.ColorLabel
            == "blue",
        "Tag-only assignment overwrote unrelated user metadata.");

    var multiTagScope =
        await repository.GetAssetPageAsync(
            library.Id,
            new AssetQuery(
                RequiredTags:
                    ["推し", "後で使う"]),
            10);
    Require(
        multiTagScope.Items.Count == 1
        && multiTagScope.Items[0].Id
            == technical.Id,
        "Multi-tag AND filtering did not preserve v1 tag semantics.");

    var updatedStandaloneTag =
        await repository.UpdateTagAsync(
            library.Id,
            standaloneTag.Id,
            "  あとで使う  ",
            "#ABCDEF80");
    Require(
        updatedStandaloneTag.Id == standaloneTag.Id
        && updatedStandaloneTag.Name == "あとで使う"
        && updatedStandaloneTag.Color == "#abcdef80"
        && updatedStandaloneTag.AssetCount == 1,
        "Explicit tag update did not preserve id/count or normalize name/color.");

    var metadataAfterTagRename =
        await repository.GetUserMetadataAsync(
            library.Id,
            technical.Id)
        ?? throw new InvalidOperationException(
            "Renamed tag asset lost metadata.");
    Require(
        metadataAfterTagRename.Tags.Contains(
            "あとで使う",
            StringComparer.Ordinal)
        && !metadataAfterTagRename.Tags.Contains(
            "後で使う",
            StringComparer.Ordinal),
        "Tag rename did not preserve asset membership under the new name.");

    var renamedTagScope =
        await repository.GetAssetPageAsync(
            library.Id,
            new AssetQuery(
                RequiredTags:
                    ["あとで使う"]),
            10);
    var renamedTagSearch =
        await repository.GetAssetPageAsync(
            library.Id,
            new AssetQuery(
                SearchText:
                    "あとで使う"),
            10);
    Require(
        renamedTagScope.Items.Count == 1
        && renamedTagScope.Items[0].Id
            == technical.Id
        && renamedTagSearch.Items.Any(
            item =>
                item.Id == technical.Id),
        "Tag rename did not refresh filter/search behavior for assigned assets.");

    try
    {
        _ = await repository.UpdateTagAsync(
            library.Id,
            standaloneTag.Id,
            "推し",
            "#111111");
        throw new InvalidOperationException(
            "Colliding tag rename unexpectedly succeeded.");
    }
    catch (InvalidOperationException exception)
        when (exception.Message.Contains(
            "already exists",
            StringComparison.Ordinal))
    {
    }

    var afterRenameCollision =
        await repository.ListTagsAsync(
            library.Id);
    Require(
        afterRenameCollision.Any(tag =>
            tag.Id == standaloneTag.Id
            && tag.Name == "あとで使う"
            && tag.Color == "#abcdef80"
            && tag.AssetCount == 1)
        && afterRenameCollision.Any(tag =>
            tag.Name == "推し"
            && tag.Color == "#6366f1"
            && tag.AssetCount == 1),
        "Collision rollback changed tag identity, color, or membership.");

    _ = await repository.SetAssetTagsAsync(
        library.Id,
        technical.Id,
        ["推し", "blue sky"]);

    var browseFacets =
        await repository.GetBrowseFacetsAsync(
            library.Id);
    Require(
        browseFacets.StatusLabels.SequenceEqual(
            ["reviewed"])
        && browseFacets.ColorLabels.SequenceEqual(
            ["blue"]),
        "Browse status/color facets did not reflect persisted user metadata.");


    Require(
        await repository.SetLibraryEnabledAsync(
            library.Id,
            false),
        "Library disable operation did not update the catalog.");
    var disabledCatalog =
        await repository.ListLibrariesAsync();
    Require(
        disabledCatalog.Count == 1
        && !disabledCatalog[0].IsEnabled,
        "Disabled library state did not persist.");

    library =
        await repository.RegisterLibraryAsync(
            "Smoke",
            libraryRoot);
    Require(
        (await repository.ListLibrariesAsync())[0].IsEnabled,
        "Opening a disabled library did not reactivate its registration.");

    var secondary =
        await repository.RegisterLibraryAsync(
            "Secondary",
            secondaryRoot);
    Require(
        await repository.RemoveLibraryRegistrationAsync(
            secondary.Id)
        && Directory.Exists(secondaryRoot)
        && (await repository.GetLibraryAsync(
            secondary.Id)) is null,
        "Library registration removal touched originals or left database state behind.");

    var japaneseShort = await repository.GetAssetPageAsync(
        library.Id,
        new AssetQuery(SearchText: "猫耳"),
        10);
    Require(
        japaneseShort.Items.Count == 1
        && japaneseShort.Items[0].Id == technical.Id,
        "Two-character Japanese search did not use the CJK bigram index correctly.");

    var japaneseSingle = await repository.GetAssetPageAsync(
        library.Id,
        new AssetQuery(SearchText: "猫"),
        10);
    Require(
        japaneseSingle.Items.Count == 1
        && japaneseSingle.Items[0].Id == technical.Id,
        "Single-character Japanese search fallback failed.");

    var asciiTwoCharacter = await repository.GetAssetPageAsync(
        library.Id,
        new AssetQuery(SearchText: "pn"),
        10);
    Require(
        asciiTwoCharacter.Items.Any(
            asset => asset.Id == technical.Id),
        "Two-character ASCII search fallback failed.");

    var asciiPartial = await repository.GetAssetPageAsync(
        library.Id,
        new AssetQuery(SearchText: "png"),
        10);
    Require(
        asciiPartial.Items.Any(asset => asset.Id == technical.Id),
        "ASCII filename partial search failed.");

    var pathPartial = await repository.GetAssetPageAsync(
        library.Id,
        new AssetQuery(SearchText: "nested"),
        10);
    Require(
        pathPartial.Items.Count == 1
        && string.Equals(
            pathPartial.Items[0].RelativePath,
            "nested/c.webp",
            StringComparison.Ordinal),
        "Relative-path search failed.");

    var mixedSearch = await repository.GetAssetPageAsync(
        library.Id,
        new AssetQuery(SearchText: "猫耳 blue"),
        10);
    Require(
        mixedSearch.Items.Count == 1
        && mixedSearch.Items[0].Id == technical.Id,
        "Mixed Japanese/English search failed.");

    var exactTag = await repository.GetAssetPageAsync(
        library.Id,
        new AssetQuery(RequiredTags: ["推し"]),
        10);
    Require(
        exactTag.Items.Count == 1
        && exactTag.Items[0].Id == technical.Id,
        "Exact tag filter failed.");

    var multiTag = await repository.GetAssetPageAsync(
        library.Id,
        new AssetQuery(
            RequiredTags:
                ["推し", "blue sky"]),
        10);
    Require(
        multiTag.Items.Count == 1
        && multiTag.Items[0].Id == technical.Id,
        "Multi-tag AND filter failed.");

    var impossibleMultiTag = await repository.GetAssetPageAsync(
        library.Id,
        new AssetQuery(
            RequiredTags:
                ["推し", "後で使う"]),
        10);
    Require(
        impossibleMultiTag.Items.Count == 0,
        "Multi-tag AND filter ignored an unassigned required tag.");

    var composedFilter = await repository.GetAssetPageAsync(
        library.Id,
        new AssetQuery(
            SearchText: "reference",
            RequiredTags: ["blue sky"],
            MinRating: 4,
            Favorite: true,
            StatusLabel: "reviewed",
            ColorLabel: "blue"),
        10);
    Require(
        composedFilter.Items.Count == 1
        && composedFilter.Items[0].Id == technical.Id,
        "Composed search/filter query failed.");

    Require(
        await repository.CountAssetsAsync(
            library.Id,
            new AssetQuery(
                SearchText: "猫耳",
                Favorite: true)) == 1,
        "Filtered search count disagreed with the page result.");

    var fileNameSortFirst = await repository.GetAssetPageAsync(
        library.Id,
        new AssetQuery(
            SortOrder: AssetSortOrder.FileNameAscending),
        2);
    Require(
        fileNameSortFirst.Items.Count == 2
        && fileNameSortFirst.NextCursor is not null,
        "Filename keyset sort did not produce a continuation cursor.");

    var fileNameSortSecond = await repository.GetAssetPageAsync(
        library.Id,
        new AssetQuery(
            SortOrder: AssetSortOrder.FileNameAscending),
        2,
        fileNameSortFirst.NextCursor);
    var sortedNames = fileNameSortFirst.Items
        .Concat(fileNameSortSecond.Items)
        .Select(static asset => asset.FileName)
        .ToArray();
    Require(
        sortedNames.SequenceEqual(
            sortedNames.Order(
                StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase),
        "Filename keyset paging was not stably sorted.");


    var rebuiltSearchRows =
        await repository.RebuildSearchIndexAsync(library.Id);
    Require(
        rebuiltSearchRows == 3,
        $"Search index rebuild covered {rebuiltSearchRows} assets; expected 3.");

    var rebuiltJapanese = await repository.GetAssetPageAsync(
        library.Id,
        new AssetQuery(
            SearchText: "猫耳",
            RequiredTags: ["推し"]),
        10);
    Require(
        rebuiltJapanese.Items.Count == 1
        && rebuiltJapanese.Items[0].Id == technical.Id,
        "Search index rebuild did not restore notes/tag search state.");

    using (var cancelledSearch = new CancellationTokenSource())
    {
        cancelledSearch.Cancel();
        try
        {
            _ = await repository.GetAssetPageAsync(
                library.Id,
                new AssetQuery(SearchText: "reference"),
                10,
                cancellationToken: cancelledSearch.Token);
            throw new InvalidOperationException(
                "Cancelled search query unexpectedly completed.");
        }
        catch (OperationCanceledException)
        {
        }
    }

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

    var userMetadataAfterSourceRevision =
        await repository.GetUserMetadataAsync(
            library.Id,
            forcedRevision.Id)
        ?? throw new InvalidOperationException(
            "User metadata was incorrectly removed by source revision change.");
    Require(
        userMetadataAfterSourceRevision.Favorite
        && userMetadataAfterSourceRevision.Tags.Contains("推し"),
        "User metadata changed when only source technical revision advanced.");
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

    var firstMetadata =
        await repository.SetUserMetadataAsync(
            library.Id,
            first.Id,
            new AssetUserMetadataUpdate(
                Rating: 2,
                Favorite: false,
                Notes: "preserve-me",
                StatusLabel: "unsorted",
                ColorLabel: "red",
                Tags: ["existing"]));

    Require(
        await repository.PatchUserMetadataAsync(
            library.Id,
            [first.Id, technical.Id],
            new AssetUserMetadataPatch(
                SetRating: true,
                Rating: 5,
                SetFavorite: true,
                Favorite: true,
                SetStatusLabel: true,
                StatusLabel: "candidate",
                AddTags: ["bulk-tag"])) == 2,
        "Bulk metadata patch did not report both selected assets.");

    var patchedFirst =
        await repository.GetUserMetadataAsync(
            library.Id,
            first.Id)
        ?? throw new InvalidOperationException(
            "Bulk-patched first asset metadata disappeared.");
    var patchedTechnical =
        await repository.GetUserMetadataAsync(
            library.Id,
            technical.Id)
        ?? throw new InvalidOperationException(
            "Bulk-patched technical asset metadata disappeared.");

    Require(
        patchedFirst.Rating == 5
        && patchedFirst.Favorite
        && patchedFirst.StatusLabel == "candidate"
        && patchedFirst.ColorLabel == "red"
        && patchedFirst.Notes == "preserve-me"
        && patchedFirst.Tags.Contains("existing")
        && patchedFirst.Tags.Contains("bulk-tag"),
        "Bulk patch overwrote unspecified metadata on the first asset.");

    Require(
        patchedTechnical.Rating == 5
        && patchedTechnical.Favorite
        && patchedTechnical.StatusLabel == "candidate"
        && patchedTechnical.ColorLabel == "blue"
        && patchedTechnical.Notes.Contains("猫耳", StringComparison.Ordinal)
        && patchedTechnical.Tags.Contains("推し")
        && patchedTechnical.Tags.Contains("bulk-tag"),
        "Bulk patch overwrote unspecified metadata on the existing tagged asset.");

    var smokeDestination =
        await repository.CreatePublicationDestinationAsync(
            library.Id,
            new PublicationDestinationCreate(
                "Smoke Social",
                "other"));
    var smokeAccount =
        await repository.CreatePublicationAccountAsync(
            library.Id,
            new PublicationAccountCreate(
                smokeDestination.Id,
                "Smoke Creator",
                "@smoke-profile"));
    var profilePublication =
        await repository.CreatePublicationAsync(
            library.Id,
            new PublicationCreate(
                [first.Id],
                smokeDestination.Name,
                new DateTimeOffset(
                    2026, 10, 4, 0, 0, 0, TimeSpan.Zero),
                Title: "Profile Snapshot",
                Account:
                    $"{smokeAccount.DisplayName} {smokeAccount.AccountIdentifier}"));
    var renamedDestination =
        await repository.UpdatePublicationDestinationAsync(
            library.Id,
            smokeDestination.Id,
            new PublicationDestinationCreate(
                "Renamed Social",
                "other"));
    var renamedAccount =
        await repository.UpdatePublicationAccountAsync(
            library.Id,
            smokeAccount.Id,
            new PublicationAccountCreate(
                smokeDestination.Id,
                "Renamed Creator",
                "@renamed"));
    Require(
        renamedDestination?.Name == "Renamed Social"
        && renamedAccount?.DisplayName == "Renamed Creator",
        "Publication destination/account update did not round-trip.");
    var immutableProfilePublication =
        await repository.GetPublicationAsync(
            library.Id,
            profilePublication.Id)
        ?? throw new InvalidOperationException(
            "Publication profile snapshot disappeared after profile rename.");
    Require(
        immutableProfilePublication.Destination == "Smoke Social"
        && immutableProfilePublication.Account
            == "Smoke Creator @smoke-profile",
        "Publication snapshot was rewritten when its reusable profile changed.");
    Require(
        await repository.DeletePublicationAccountAsync(
            library.Id,
            smokeAccount.Id)
        && await repository.DeletePublicationDestinationAsync(
            library.Id,
            smokeDestination.Id),
        "Reusable publication profile deletion failed.");
    var afterProfileDelete =
        await repository.GetPublicationAsync(
            library.Id,
            profilePublication.Id)
        ?? throw new InvalidOperationException(
            "Publication snapshot was cascaded by profile deletion.");
    Require(
        afterProfileDelete.Destination == "Smoke Social"
        && afterProfileDelete.Account
            == "Smoke Creator @smoke-profile",
        "Deleting a reusable profile modified the immutable Publication snapshot.");
    var firstBeforePublicationDelete =
        await repository.GetUserMetadataAsync(
            library.Id,
            first.Id);
    Require(
        await repository.DeletePublicationAsync(
            library.Id,
            profilePublication.Id),
        "Publication history deletion did not remove the selected snapshot.");
    var firstAfterPublicationDelete =
        await repository.GetUserMetadataAsync(
            library.Id,
            first.Id);
    Require(
        await repository.GetPublicationAsync(
            library.Id,
            profilePublication.Id)
            is null
        && await repository.GetAssetAsync(
            library.Id,
            first.RelativePath)
            is not null
        && firstBeforePublicationDelete is not null
        && firstAfterPublicationDelete is not null
        && firstBeforePublicationDelete.Rating
            == firstAfterPublicationDelete.Rating
        && firstBeforePublicationDelete.Favorite
            == firstAfterPublicationDelete.Favorite
        && firstBeforePublicationDelete.StatusLabel
            == firstAfterPublicationDelete.StatusLabel
        && firstBeforePublicationDelete.ColorLabel
            == firstAfterPublicationDelete.ColorLabel
        && firstBeforePublicationDelete.Notes
            == firstAfterPublicationDelete.Notes
        && firstBeforePublicationDelete.Tags.SequenceEqual(
            firstAfterPublicationDelete.Tags,
            StringComparer.Ordinal),
        "Deleting Publication history touched source assets or user metadata.");
    await using (var publicationDeleteConnection =
                 new SqliteConnection(
                     $"Data Source={databasePath};Pooling=False"))
    {
        await publicationDeleteConnection.OpenAsync();
        await using var deletedMembership =
            publicationDeleteConnection.CreateCommand();
        deletedMembership.CommandText =
            """
            SELECT COUNT(*)
            FROM publication_assets
            WHERE publication_id = $publication_id;
            """;
        deletedMembership.Parameters.AddWithValue(
            "$publication_id",
            profilePublication.Id);
        Require(
            Convert.ToInt32(
                await deletedMembership.ExecuteScalarAsync(),
                CultureInfo.InvariantCulture) == 0,
            "Deleting Publication history left orphaned publication_assets rows.");
    }

    var metadataSummary =
        await repository.GetUserMetadataSelectionSummaryAsync(
            library.Id,
            [first.Id, technical.Id]);

    Require(
        metadataSummary.SelectionCount == 2
        && !metadataSummary.RatingMixed
        && metadataSummary.Rating == 5
        && !metadataSummary.FavoriteMixed
        && metadataSummary.Favorite
        && !metadataSummary.StatusLabelMixed
        && metadataSummary.StatusLabel == "candidate"
        && metadataSummary.ColorLabelMixed
        && metadataSummary.ColorLabel is null
        && metadataSummary.NotesMixed
        && metadataSummary.TagsMixed
        && metadataSummary.CommonTags.SequenceEqual(["bulk-tag"]),
        "Mixed metadata selection summary did not preserve exact common/mixed semantics.");

    var creativeWork =
        await repository.CreateWorkAsync(
            library.Id,
            new WorkCreate(
                "Smoke Work",
                "human-facing creative unit",
                [first.Id, technical.Id]));
    Require(
        creativeWork.Assets.Select(static asset => asset.Id)
            .SequenceEqual([first.Id, technical.Id]),
        "Work did not preserve ordered asset membership.");

    var creativeThird =
        await repository.GetAssetAsync(
            library.Id,
            "nested/c.webp")
        ?? throw new InvalidOperationException(
            "Creative membership fixture disappeared.");
    var expandedWork =
        await repository.AddAssetsToWorkAsync(
            library.Id,
            creativeWork.Id,
            [first.Id, creativeThird.Id]);
    Require(
        expandedWork.Assets
            .Select(static asset => asset.Id)
            .SequenceEqual(
                [first.Id, technical.Id, creativeThird.Id]),
        "Adding assets to an existing Work did not preserve existing order and append only missing assets.");
    var idempotentWork =
        await repository.AddAssetsToWorkAsync(
            library.Id,
            creativeWork.Id,
            [creativeThird.Id, first.Id]);
    Require(
        idempotentWork.Assets
            .Select(static asset => asset.Id)
            .SequenceEqual(
                [first.Id, technical.Id, creativeThird.Id]),
        "Duplicate Work membership changed order or created duplicate rows.");

    var creativeGroup =
        await repository.CreateGenerationGroupAsync(
            library.Id,
            new GenerationGroupCreate(
                "Smoke Generation",
                [technical.Id, first.Id],
                WorkId: creativeWork.Id,
                Prompt: "blue archive style",
                NegativePrompt: "low quality",
                ModelName: "smoke-model",
                Sampler: "euler",
                Scheduler: "normal",
                Steps: 28,
                CfgScale: 5.5,
                WorkflowJson: "{\"node\":1}",
                Notes: "manual group"));
    Require(
        creativeGroup.WorkId == creativeWork.Id
        && creativeGroup.Assets.Select(static asset => asset.Id)
            .SequenceEqual([technical.Id, first.Id])
        && creativeGroup.Prompt == "blue archive style"
        && creativeGroup.Steps == 28
        && Math.Abs(creativeGroup.CfgScale - 5.5) < 0.001,
        "Generation Group lost ordered membership or generation context.");

    var expandedGroup =
        await repository.AddAssetsToGenerationGroupAsync(
            library.Id,
            creativeGroup.Id,
            [technical.Id, creativeThird.Id]);
    Require(
        expandedGroup.Assets
            .Select(static asset => asset.Id)
            .SequenceEqual(
                [technical.Id, first.Id, creativeThird.Id]),
        "Adding assets to an existing Generation Group did not preserve existing order and append only missing assets.");
    var listedGroups =
        await repository.ListGenerationGroupsAsync(
            library.Id);
    Require(
        listedGroups.Any(
            group =>
                group.Id == creativeGroup.Id
                && group.Assets.Select(static asset => asset.Id)
                    .SequenceEqual(
                        [technical.Id, first.Id, creativeThird.Id])),
        "Generation Group listing did not expose updated ordered membership.");

    var relation =
        await repository.CreateAssetRelationAsync(
            library.Id,
            new AssetRelationCreate(
                first.Id,
                technical.Id,
                "img2img",
                "manual lineage"));
    Require(
        relation.Parent.Id == first.Id
        && relation.Child.Id == technical.Id
        && relation.RelationType == "img2img",
        "Directed asset lineage did not preserve direction/type.");

    try
    {
        await repository.CreateAssetRelationAsync(
            library.Id,
            new AssetRelationCreate(
                first.Id,
                first.Id,
                "edit"));
        throw new InvalidOperationException(
            "Self-link lineage unexpectedly succeeded.");
    }
    catch (ArgumentException)
    {
    }

    var publication =
        await repository.CreatePublicationAsync(
            library.Id,
            new PublicationCreate(
                [technical.Id, first.Id],
                "Pixiv",
                new DateTimeOffset(
                    2026, 10, 2, 6, 0, 0, TimeSpan.Zero),
                WorkId: creativeWork.Id,
                Title: "Published Smoke",
                Body: "snapshot body",
                TagsSnapshot: "smoke #archive",
                Account: "@smoke",
                ExternalId: "pixiv-123",
                ExternalUrl: "https://example.invalid/p/123",
                PlatformMetadataJson:
                    "{\"ageRestriction\":\"r18\",\"aiGenerated\":true}"));
    Require(
        publication.Assets.Count == 2
        && publication.Assets[0].AssetId == technical.Id
        && publication.Assets[1].AssetId == first.Id
        && publication.TagsSnapshot == "smoke #archive"
        && publication.Destination == "Pixiv",
        "Publication did not persist its ordered publication snapshot.");

    var creativeContext =
        await repository.GetAssetCreativeContextAsync(
            library.Id,
            technical.Id);
    Require(
        creativeContext.Works.Any(
            work => work.Id == creativeWork.Id)
        && creativeContext.GenerationGroups.Any(
            group => group.Id == creativeGroup.Id)
        && creativeContext.Relations.Any(
            item =>
                item.Id == relation.Id
                && item.Parent.Id == first.Id
                && item.Child.Id == technical.Id)
        && creativeContext.Publications.Any(
            item => item.Id == publication.Id),
        "Asset creative context did not expose Work/Group/lineage/Publication human context.");

    var foreignCreativeLibrary =
        await repository.RegisterLibraryAsync(
            "Creative Foreign",
            secondaryRoot);
    try
    {
        await repository.AddAssetsToWorkAsync(
            foreignCreativeLibrary.Id,
            creativeWork.Id,
            Array.Empty<long>());
        throw new InvalidOperationException(
            "Cross-library Work maintenance unexpectedly succeeded.");
    }
    catch (InvalidOperationException exception)
        when (exception.Message.Contains(
            "does not belong",
            StringComparison.Ordinal))
    {
    }

    try
    {
        await repository.AddAssetsToGenerationGroupAsync(
            foreignCreativeLibrary.Id,
            creativeGroup.Id,
            Array.Empty<long>());
        throw new InvalidOperationException(
            "Cross-library Generation Group maintenance unexpectedly succeeded.");
    }
    catch (InvalidOperationException exception)
        when (exception.Message.Contains(
            "does not belong",
            StringComparison.Ordinal))
    {
    }

    Require(
        !await repository.DeleteAssetRelationAsync(
            foreignCreativeLibrary.Id,
            relation.Id),
        "Lineage deletion crossed library ownership.");
    Require(
        await repository.DeleteAssetRelationAsync(
            library.Id,
            relation.Id),
        "Lineage deletion did not remove the owned relation.");
    var creativeContextAfterRelationDelete =
        await repository.GetAssetCreativeContextAsync(
            library.Id,
            technical.Id);
    Require(
        creativeContextAfterRelationDelete.Relations.All(
            item => item.Id != relation.Id),
        "Deleted lineage remained visible in creative context.");
    Require(
        await repository.RemoveLibraryRegistrationAsync(
            foreignCreativeLibrary.Id),
        "Creative foreign-library fixture could not be removed.");

    var publicationScaleCreated =
        DateTimeOffset.UtcNow.UtcDateTime.Ticks;
    var publicationScaleBase =
        new DateTimeOffset(
            2027,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero)
            .UtcDateTime.Ticks;

    await using (var publicationScaleConnection =
                 new SqliteConnection(
                     $"Data Source={databasePath};Pooling=False"))
    {
        await publicationScaleConnection.OpenAsync();
        using var publicationScaleTransaction =
            publicationScaleConnection.BeginTransaction();

        await using (var insertPublications =
                     publicationScaleConnection.CreateCommand())
        {
            insertPublications.Transaction =
                publicationScaleTransaction;
            insertPublications.CommandText =
                """
                WITH RECURSIVE sequence(value) AS (
                    SELECT 0
                    UNION ALL
                    SELECT value + 1
                    FROM sequence
                    WHERE value < 129
                )
                INSERT INTO publications(
                    library_id,
                    work_id,
                    title,
                    body,
                    tags_snapshot,
                    destination,
                    account,
                    published_at_utc_ticks,
                    external_id,
                    external_url,
                    platform_metadata_json,
                    created_at_utc_ticks,
                    updated_at_utc_ticks)
                SELECT
                    $library_id,
                    NULL,
                    printf('Scale Publication %03d', value),
                    'scale body',
                    'scale',
                    'Pixiv',
                    '@scale',
                    $published_base - value,
                    '',
                    '',
                    '{}',
                    $created,
                    $created
                FROM sequence;
                """;
            insertPublications.Parameters.AddWithValue(
                "$library_id",
                library.Id);
            insertPublications.Parameters.AddWithValue(
                "$published_base",
                publicationScaleBase);
            insertPublications.Parameters.AddWithValue(
                "$created",
                publicationScaleCreated);
            Require(
                await insertPublications
                    .ExecuteNonQueryAsync() == 130,
                "Publication scale fixture did not create 130 history rows.");
        }

        await using (var insertPublicationAssets =
                     publicationScaleConnection.CreateCommand())
        {
            insertPublicationAssets.Transaction =
                publicationScaleTransaction;
            insertPublicationAssets.CommandText =
                """
                INSERT INTO publication_assets(
                    publication_id,
                    asset_id,
                    sort_order,
                    file_name_snapshot,
                    relative_path_snapshot)
                SELECT
                    p.id,
                    $asset_id,
                    0,
                    $file_name,
                    $relative_path
                FROM publications AS p
                WHERE p.library_id = $library_id
                  AND p.created_at_utc_ticks = $created
                  AND p.title LIKE 'Scale Publication %';
                """;
            insertPublicationAssets.Parameters.AddWithValue(
                "$library_id",
                library.Id);
            insertPublicationAssets.Parameters.AddWithValue(
                "$created",
                publicationScaleCreated);
            insertPublicationAssets.Parameters.AddWithValue(
                "$asset_id",
                technical.Id);
            insertPublicationAssets.Parameters.AddWithValue(
                "$file_name",
                technical.FileName);
            insertPublicationAssets.Parameters.AddWithValue(
                "$relative_path",
                technical.RelativePath);
            Require(
                await insertPublicationAssets
                    .ExecuteNonQueryAsync() == 130,
                "Publication scale fixture did not snapshot its asset membership.");
        }

        publicationScaleTransaction.Commit();
    }

    var firstPublicationPage =
        await repository.ListPublicationsPageAsync(
            library.Id);
    Require(
        firstPublicationPage.Items.Count
            == LibraryRepository.PublicationPageSize
        && firstPublicationPage.NextCursor is not null
        && firstPublicationPage.TotalCount >= 131
        && firstPublicationPage.Items[0].Title
            == "Scale Publication 000",
        "Publication history did not expose a bounded first page across the former 100-item cutoff.");

    var secondPublicationPage =
        await repository.ListPublicationsPageAsync(
            library.Id,
            cursor:
                firstPublicationPage.NextCursor);
    Require(
        secondPublicationPage.Items.Count > 0
        && secondPublicationPage.Items.All(
            item =>
                firstPublicationPage.Items.All(
                    first =>
                        first.Id != item.Id))
        && secondPublicationPage.TotalCount
            == firstPublicationPage.TotalCount
        && secondPublicationPage.NextCursor is null,
        "Publication keyset paging did not expose the older history without overlap.");

    var scaleCreativeContext =
        await repository.GetAssetCreativeContextAsync(
            library.Id,
            technical.Id);
    Require(
        scaleCreativeContext.PublicationCount >= 131
        && scaleCreativeContext.Publications.Count
            == LibraryRepository.InspectorPublicationLimit
        && scaleCreativeContext.Publications[0].Title
            == "Scale Publication 000",
        "Inspector creative context did not bound publication work while preserving the full history count.");

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

    var publicationAfterSourceReplacement =
        await repository.GetPublicationAsync(
            library.Id,
            publication.Id)
        ?? throw new InvalidOperationException(
            "Publication snapshot disappeared after source asset replacement.");
    Require(
        publicationAfterSourceReplacement.Assets.Count == 2
        && publicationAfterSourceReplacement.Assets[1].AssetId is null
        && publicationAfterSourceReplacement.Assets[1].FileName
            == first.FileName
        && publicationAfterSourceReplacement.Assets[1].RelativePath
            == first.RelativePath,
        "Publication history did not preserve file/path snapshot after the original asset row was deleted.");

    Require(
        LibraryRepository.MaxTagListLimit == 10_000,
        "Product tag-list limit drifted from the 10k navigation acceptance contract.");

    await using (var scaleTagConnection =
                 new SqliteConnection(
                     $"Data Source={databasePath};Pooling=False"))
    {
        await scaleTagConnection.OpenAsync();
        await using var scaleTagInsert =
            scaleTagConnection.CreateCommand();
        scaleTagInsert.CommandText =
            """
            WITH RECURSIVE hundred(value) AS (
                SELECT 0
                UNION ALL
                SELECT value + 1
                FROM hundred
                WHERE value < 99
            ),
            sequence(value) AS (
                SELECT high.value * 100 + low.value
                FROM hundred AS high
                CROSS JOIN hundred AS low
                WHERE high.value * 100 + low.value < 9990
            )
            INSERT INTO tags(
                library_id,
                name,
                name_key,
                color,
                created_at_utc_ticks)
            SELECT
                $library_id,
                printf('scale-tag-%04d', value),
                printf('SCALE-TAG-%04d', value),
                '#123456',
                $created
            FROM sequence;
            """;
        scaleTagInsert.Parameters.AddWithValue(
            "$library_id",
            library.Id);
        scaleTagInsert.Parameters.AddWithValue(
            "$created",
            DateTimeOffset.UtcNow.UtcDateTime.Ticks);
        Require(
            await scaleTagInsert.ExecuteNonQueryAsync() == 9_990,
            "High-count tag fixture insert did not create the near-10k production fixture.");
    }

    var productionScaleTags =
        await repository.ListTagsAsync(
            library.Id);
    Require(
        productionScaleTags.Count > 512
        && productionScaleTags.Any(
            static tag =>
                tag.Name == "scale-tag-9989"
                && tag.Color == "#123456"),
        "Default production tag listing did not expose the near-10k production fixture.");

    var tailTagSearch =
        await repository.ListTagsAsync(
            library.Id,
            "scale-tag-9989");
    Require(
        tailTagSearch.Count == 1
        && tailTagSearch[0].Name == "scale-tag-9989",
        "DB-backed tag search could not discover a tag beyond the former 512-tag window.");

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

    var publicationSummaryAssets =
        (await repository.GetAssetPageAsync(
            library.Id,
            20))
            .Items;
    Require(
        publicationSummaryAssets.Count == 20,
        "Publication summary fixture could not resolve 20 assets.");

    var largePublication =
        await repository.CreatePublicationAsync(
            library.Id,
            new PublicationCreate(
                publicationSummaryAssets
                    .Select(
                        static asset =>
                            asset.Id)
                    .ToArray(),
                "Pixiv",
                new DateTimeOffset(
                    2028,
                    1,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero),
                Title:
                    "Large Publication Summary"));

    var largePublicationPage =
        await repository.ListPublicationsPageAsync(
            library.Id);
    var largePublicationSummary =
        largePublicationPage.Items.Single(
            item =>
                item.Id
                    == largePublication.Id);
    Require(
        largePublicationSummary.AssetCount == 20
        && largePublicationSummary.Assets.Count
            == LibraryRepository.PublicationSummaryAssetLimit,
        "Publication history summary did not bound snapshot materialization while preserving the full asset count.");

    var largePublicationDetail =
        await repository.GetPublicationAsync(
            library.Id,
            largePublication.Id)
        ?? throw new InvalidOperationException(
            "Large Publication detail could not be reloaded.");
    Require(
        largePublicationDetail.AssetCount == 20
        && largePublicationDetail.Assets.Count == 20,
        "Publication detail lost the immutable full asset snapshot after summary bounding.");

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
    var servicePublicationDetail =
        await service.GetPublicationAsync(
            library.Id,
            largePublication.Id)
        ?? throw new InvalidOperationException(
            "LibraryService could not load a full Publication detail.");
    Require(
        servicePublicationDetail.Assets.Count == 20
        && servicePublicationDetail.Assets
            .Select(
                static asset =>
                    asset.SortOrder)
            .SequenceEqual(
                Enumerable.Range(0, 20)),
        "LibraryService full Publication detail did not preserve all ordered snapshot assets.");

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

    var reopenedMetadata =
        await reopenedRepository.GetUserMetadataAsync(
            library.Id,
            technical.Id)
        ?? throw new InvalidOperationException(
            "User metadata disappeared after database reopen.");
    Require(
        reopenedMetadata.Rating == 5
        && reopenedMetadata.Favorite
        && reopenedMetadata.StatusLabel == "candidate"
        && reopenedMetadata.ColorLabel == "blue"
        && reopenedMetadata.Notes.Contains("猫耳", StringComparison.Ordinal)
        && reopenedMetadata.Tags.Contains("推し")
        && reopenedMetadata.Tags.Contains("bulk-tag"),
        "User-owned metadata did not survive database reopen.");

    var reopenedPublication =
        await reopenedRepository.GetPublicationAsync(
            library.Id,
            publication.Id)
        ?? throw new InvalidOperationException(
            "Publication disappeared after database reopen.");
    var reopenedCreativeContext =
        await reopenedRepository.GetAssetCreativeContextAsync(
            library.Id,
            technical.Id);
    Require(
        reopenedPublication.Title == "Published Smoke"
        && reopenedPublication.Assets.Count == 2,
        "Publication detail did not survive database reopen.");
    Require(
        reopenedCreativeContext.Works.Any(
            work => work.Id == creativeWork.Id)
        && reopenedCreativeContext.GenerationGroups.Any(
            group => group.Id == creativeGroup.Id),
        "Work or Generation Group context did not survive database reopen.");
    Require(
        reopenedCreativeContext.PublicationCount >= 131
        && reopenedCreativeContext.Publications.Count
            == LibraryRepository.InspectorPublicationLimit,
        "Bounded Inspector Publication context did not survive database reopen.");

    var legacyPath = Path.Combine(tempRoot, "legacy-v1.db");
    await CreateLegacyV1DatabaseAsync(legacyPath);
    var legacyDatabase = new LibraryDatabase(legacyPath);
    await legacyDatabase.InitializeAsync();

    await using (var legacyConnection = new SqliteConnection($"Data Source={legacyPath};Pooling=False"))
    {
        await legacyConnection.OpenAsync();
        await using var migration = legacyConnection.CreateCommand();
        migration.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        Require(Convert.ToInt32(await migration.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 9, "v1 database did not migrate to v9.");

        await using var asset = legacyConnection.CreateCommand();
        asset.CommandText = "SELECT id, source_revision, width, height, observed_generation FROM assets WHERE relative_path = 'legacy.jpg';";
        await using var reader = await asset.ExecuteReaderAsync();
        Require(await reader.ReadAsync(), "Legacy asset disappeared during migration.");
        Require(reader.GetInt64(0) == 42, "Migration changed existing stable asset id.");
        Require(reader.GetInt64(1) == 1, "Migrated asset source revision was not initialized.");
        Require(reader.GetInt32(2) == 640 && reader.GetInt32(3) == 480, "Migration lost technical metadata.");
        Require(reader.GetInt64(4) == 0, "Migrated legacy asset should start outside any reconciliation generation.");

        await using var enabled = legacyConnection.CreateCommand();
        enabled.CommandText = "SELECT is_enabled FROM libraries WHERE id = 1;";
        Require(
            Convert.ToInt32(
                await enabled.ExecuteScalarAsync(),
                CultureInfo.InvariantCulture) == 1,
            "Navigation migration did not enable an existing library by default.");

        await using var publicationDefaults =
            legacyConnection.CreateCommand();
        publicationDefaults.CommandText =
            "SELECT COUNT(*) FROM publication_destinations WHERE library_id = 1;";
        Require(
            Convert.ToInt32(
                await publicationDefaults.ExecuteScalarAsync(),
                CultureInfo.InvariantCulture) == 5,
            "Publication profile migration did not seed existing libraries.");
    }

    var v8CompatPath =
        Path.Combine(
            tempRoot,
            "publication-v8-compat.db");
    var v8CompatRoot =
        Path.Combine(
            tempRoot,
            "publication-v8-library");
    Directory.CreateDirectory(v8CompatRoot);
    var v8CompatDatabase =
        new LibraryDatabase(
            v8CompatPath);
    await v8CompatDatabase.InitializeAsync();
    var v8CompatRepository =
        new LibraryRepository(
            v8CompatDatabase);
    var v8CompatLibrary =
        await v8CompatRepository.RegisterLibraryAsync(
            "Publication v8",
            v8CompatRoot);
    long v8PublicationId;
    await using (var v8Connection =
                 new SqliteConnection(
                     $"Data Source={v8CompatPath};Pooling=False"))
    {
        await v8Connection.OpenAsync();
        await using var insert =
            v8Connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO publications(
                library_id, work_id,
                title, body, tags_snapshot,
                destination, account,
                published_at_utc_ticks,
                external_id, external_url,
                platform_metadata_json,
                created_at_utc_ticks,
                updated_at_utc_ticks)
            VALUES(
                $library_id, NULL,
                'Legacy v8 Publication',
                'legacy body',
                'legacy-tags',
                'Pixiv',
                'legacy-account',
                638950000000000000,
                'legacy-external',
                'https://example.invalid/legacy-v8',
                '{"legacy":true}',
                638950000000000000,
                638950000000000000)
            RETURNING id;
            """;
        insert.Parameters.AddWithValue(
            "$library_id",
            v8CompatLibrary.Id);
        v8PublicationId =
            Convert.ToInt64(
                await insert.ExecuteScalarAsync(),
                CultureInfo.InvariantCulture);

        await using var downgrade =
            v8Connection.CreateCommand();
        downgrade.CommandText =
            """
            DROP TRIGGER IF EXISTS libraries_ai_publication_defaults;
            DROP TABLE publication_accounts;
            DROP TABLE publication_destinations;
            DELETE FROM schema_migrations
            WHERE version = 9;
            """;
        await downgrade.ExecuteNonQueryAsync();
    }

    var migratedV8Database =
        new LibraryDatabase(
            v8CompatPath);
    await migratedV8Database.InitializeAsync();
    var migratedV8Repository =
        new LibraryRepository(
            migratedV8Database);
    var migratedV8Publication =
        await migratedV8Repository.GetPublicationAsync(
            v8CompatLibrary.Id,
            v8PublicationId)
        ?? throw new InvalidOperationException(
            "Existing v8 Publication disappeared during the v9 profile migration.");
    Require(
        migratedV8Publication.Title
            == "Legacy v8 Publication"
        && migratedV8Publication.Destination
            == "Pixiv"
        && migratedV8Publication.Account
            == "legacy-account"
        && (await migratedV8Repository
                .ListPublicationDestinationsAsync(
                    v8CompatLibrary.Id))
            .Count == 5,
        "v9 migration changed an existing Publication snapshot or failed to seed profiles.");

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
        $"Library/search smoke: {expectedCount:N0} assets, metadata/FTS/CJK-bigram/filter/keyset/migration/source-revision OK");
}
finally
{
    LibraryDatabase.ClearPools();
    if (Directory.Exists(tempRoot))
    {
        Directory.Delete(tempRoot, recursive: true);
    }
}
