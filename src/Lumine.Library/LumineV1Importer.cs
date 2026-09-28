using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed class LumineV1Importer
{
    public const string SourceKind = "lumine-v1";

    private const int MaxNotesLength = 16_384;
    private const int MaxTagsPerAsset = 128;
    private const int MaxTagNameLength = 128;
    private const int MaxLabelLength = 64;
    private const int LatestKnownV1Migration = 6;

    private readonly LibraryDatabase _destination;

    public LumineV1Importer(LibraryDatabase destination)
    {
        _destination = destination
            ?? throw new ArgumentNullException(nameof(destination));
    }

    public static string GetDefaultSourceDatabasePath()
    {
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "lumine", "lumine.db");
    }

    public async Task<LumineV1ImportPreview> PreviewAsync(
        string sourceDatabasePath,
        CancellationToken cancellationToken = default)
    {
        using var snapshot = await SourceSnapshot.CreateAsync(
            sourceDatabasePath,
            cancellationToken).ConfigureAwait(false);
        await using var source = await OpenSnapshotAsync(
            snapshot.DatabasePath,
            cancellationToken).ConfigureAwait(false);

        return await AnalyzeAsync(
            source,
            snapshot,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LumineV1ImportResult> ImportAsync(
        string sourceDatabasePath,
        string? expectedSourceFingerprintSha256 = null,
        IProgress<LumineV1ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var snapshot = await SourceSnapshot.CreateAsync(
            sourceDatabasePath,
            cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(
                expectedSourceFingerprintSha256)
            && !string.Equals(
                expectedSourceFingerprintSha256,
                snapshot.FingerprintSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new LumineV1SourceChangedException(
                "The Lumine v1 database changed after preview. Run preview again before importing.");
        }

        await using var source = await OpenSnapshotAsync(
            snapshot.DatabasePath,
            cancellationToken).ConfigureAwait(false);

        var preview = await AnalyzeAsync(
            source,
            snapshot,
            cancellationToken).ConfigureAwait(false);

        if (!preview.CanImport)
        {
            throw new LumineV1ImportException(
                "The Lumine v1 database contains blocking migration diagnostics. Review the preview before importing.");
        }

        // Import is the mutating operation. Preview above never opens or
        // migrates the destination database.
        await _destination.InitializeAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var destination =
            await _destination.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
        using var transaction = destination.BeginTransaction();

        if (await HasImportProvenanceAsync(
                destination,
                transaction,
                preview.SourceFingerprintSha256,
                cancellationToken).ConfigureAwait(false))
        {
            transaction.Rollback();
            return new LumineV1ImportResult(
                preview,
                true,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        var sourceLibraries = await LoadSourceLibrariesAsync(
            source,
            cancellationToken).ConfigureAwait(false);
        var tagSummaries = await LoadTagSummariesAsync(
            source,
            cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
        var libraryMap = new Dictionary<long, DestinationLibrary>();
        var folders = new Dictionary<string, long>(
            StringComparer.Ordinal);
        var assetMap = new Dictionary<long, DestinationAsset>();

        var librariesImported = 0;
        var librariesMatched = 0;
        var assetsImported = 0;
        var assetsMatched = 0;
        var metadataImported = 0;
        var metadataConflicts = 0;
        var tagAssignmentsImported = 0;
        var librariesProcessed = 0;
        var assetsProcessed = 0;

        foreach (var legacyLibrary in sourceLibraries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedRoot =
                LibraryPaths.NormalizeRoot(legacyLibrary.RootPath);
            var rootKey = LibraryPaths.RootKey(normalizedRoot);

            var existingLibraryId =
                await FindDestinationLibraryIdAsync(
                    destination,
                    transaction,
                    rootKey,
                    cancellationToken).ConfigureAwait(false);

            long destinationLibraryId;
            if (existingLibraryId is { } matchedId)
            {
                destinationLibraryId = matchedId;
                librariesMatched++;
            }
            else
            {
                destinationLibraryId =
                    await InsertDestinationLibraryAsync(
                        destination,
                        transaction,
                        legacyLibrary.Name,
                        normalizedRoot,
                        rootKey,
                        now,
                        cancellationToken).ConfigureAwait(false);
                librariesImported++;
            }

            libraryMap.Add(
                legacyLibrary.Id,
                new DestinationLibrary(
                    destinationLibraryId,
                    normalizedRoot));

            librariesProcessed++;
            progress?.Report(
                new LumineV1ImportProgress(
                    librariesProcessed,
                    assetsProcessed));
            cancellationToken.ThrowIfCancellationRequested();
        }

        await using (var command = source.CreateCommand())
        {
            command.CommandText = LegacyAssetSelectSql;
            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var legacy = ReadLegacyAsset(reader);
                if (!libraryMap.TryGetValue(
                        legacy.LibraryId,
                        out var destinationLibrary))
                {
                    throw new LumineV1ImportException(
                        $"Source asset {legacy.Id} references missing library {legacy.LibraryId}.");
                }

                if (!TryMapRelativePath(
                        destinationLibrary.RootPath,
                        legacy.FilePath,
                        out var relativePath))
                {
                    throw new LumineV1ImportException(
                        $"Source asset {legacy.Id} is outside its registered library root.");
                }

                var folderPath =
                    LibraryPaths.FolderRelativePath(relativePath);
                long? folderId = null;
                if (folderPath.Length > 0)
                {
                    folderId = await GetOrCreateFolderAsync(
                        destination,
                        transaction,
                        folders,
                        destinationLibrary.Id,
                        folderPath,
                        now,
                        cancellationToken).ConfigureAwait(false);
                }

                var relativePathKey =
                    LibraryPaths.RelativePathKey(relativePath);
                var existingAssetId =
                    await FindDestinationAssetIdAsync(
                        destination,
                        transaction,
                        destinationLibrary.Id,
                        relativePathKey,
                        cancellationToken).ConfigureAwait(false);

                long destinationAssetId;
                if (existingAssetId is { } matchedAssetId)
                {
                    destinationAssetId = matchedAssetId;
                    assetsMatched++;
                }
                else
                {
                    destinationAssetId =
                        await InsertDestinationAssetAsync(
                            destination,
                            transaction,
                            destinationLibrary.Id,
                            folderId,
                            relativePath,
                            relativePathKey,
                            legacy,
                            now,
                            cancellationToken).ConfigureAwait(false);
                    assetsImported++;
                }

                var hasTags =
                    tagSummaries.TryGetValue(
                        legacy.Id,
                        out var tagSummary)
                    && tagSummary.Count > 0;
                var hasMetadata =
                    HasMeaningfulMetadata(legacy, hasTags);
                var allowTagImport = false;

                if (hasMetadata)
                {
                    var destinationHasMetadata =
                        await HasDestinationUserMetadataAsync(
                            destination,
                            transaction,
                            destinationAssetId,
                            cancellationToken).ConfigureAwait(false);

                    if (destinationHasMetadata)
                    {
                        // Presence of a v2 user-metadata row means the user may
                        // already have edited this asset after scanning it into
                        // v2. Preserve v2 rather than silently overwriting it.
                        metadataConflicts++;
                    }
                    else
                    {
                        await InsertDestinationUserMetadataAsync(
                            destination,
                            transaction,
                            destinationAssetId,
                            destinationLibrary.Id,
                            legacy,
                            now,
                            cancellationToken).ConfigureAwait(false);
                        metadataImported++;
                        allowTagImport = hasTags;
                    }
                }

                assetMap.Add(
                    legacy.Id,
                    new DestinationAsset(
                        destinationAssetId,
                        destinationLibrary.Id,
                        allowTagImport));

                assetsProcessed++;
                progress?.Report(
                    new LumineV1ImportProgress(
                        librariesProcessed,
                        assetsProcessed));
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        await using (var tags = source.CreateCommand())
        {
            tags.CommandText =
                """
                SELECT
                    at.asset_id,
                    t.name
                FROM asset_tags AS at
                INNER JOIN tags AS t
                    ON t.id = at.tag_id
                ORDER BY at.asset_id ASC, t.id ASC;
                """;

            await using var reader = await tags.ExecuteReaderAsync(
                cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourceAssetId = reader.GetInt64(0);
                if (!assetMap.TryGetValue(
                        sourceAssetId,
                        out var target)
                    || !target.AllowTagImport)
                {
                    continue;
                }

                var normalized = NormalizeTag(
                    reader.GetString(1));

                var tagId = await GetOrCreateTagAsync(
                    destination,
                    transaction,
                    target.LibraryId,
                    normalized.Name,
                    normalized.Key,
                    now,
                    cancellationToken).ConfigureAwait(false);

                tagAssignmentsImported +=
                    await AssignTagAsync(
                        destination,
                        transaction,
                        target.AssetId,
                        tagId,
                        now,
                        cancellationToken).ConfigureAwait(false);
            }
        }

        await InsertImportProvenanceAsync(
            destination,
            transaction,
            preview,
            librariesImported,
            librariesMatched,
            assetsImported,
            assetsMatched,
            metadataImported,
            metadataConflicts,
            tagAssignmentsImported,
            now,
            cancellationToken).ConfigureAwait(false);

        transaction.Commit();

        return new LumineV1ImportResult(
            preview,
            false,
            librariesImported,
            librariesMatched,
            assetsImported,
            assetsMatched,
            metadataImported,
            metadataConflicts,
            tagAssignmentsImported);
    }

    private static async Task<LumineV1ImportPreview> AnalyzeAsync(
        SqliteConnection source,
        SourceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var diagnostics =
            new List<LumineV1ImportDiagnostic>();

        var schemaVersion = await ValidateSchemaAsync(
            source,
            diagnostics,
            cancellationToken).ConfigureAwait(false);

        if (diagnostics.Any(static diagnostic =>
                diagnostic.Severity
                    == LumineV1ImportDiagnosticSeverity.Error))
        {
            return new LumineV1ImportPreview(
                snapshot.SourceDatabasePath,
                snapshot.FingerprintSha256,
                snapshot.SourceBytes,
                schemaVersion,
                [],
                0,
                0,
                0,
                0,
                diagnostics);
        }

        var libraries = await LoadSourceLibrariesAsync(
            source,
            cancellationToken).ConfigureAwait(false);
        var tagSummaries = await LoadTagSummariesAsync(
            source,
            cancellationToken).ConfigureAwait(false);

        var assetCounts = libraries.ToDictionary(
            static library => library.Id,
            static _ => 0);
        var importableCounts = libraries.ToDictionary(
            static library => library.Id,
            static _ => 0);

        var assetCount = 0;
        var importableAssetCount = 0;
        var meaningfulMetadataCount = 0;
        var invalidPathCount = 0;
        var invalidMetadataCount = 0;
        var invalidTimestampFallbacks = 0;

        await using (var command = source.CreateCommand())
        {
            command.CommandText = LegacyAssetSelectSql;
            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                var asset = ReadLegacyAsset(reader);
                assetCount++;

                if (!assetCounts.TryGetValue(
                        asset.LibraryId,
                        out var libraryAssetCount))
                {
                    diagnostics.Add(
                        Error(
                            "asset_missing_library",
                            $"Source asset {asset.Id} references missing library {asset.LibraryId}."));
                    continue;
                }

                assetCounts[asset.LibraryId] =
                    libraryAssetCount + 1;

                var library = libraries.First(
                    candidate =>
                        candidate.Id == asset.LibraryId);

                if (!TryMapRelativePath(
                        library.RootPath,
                        asset.FilePath,
                        out var relativePath)
                    || Path.GetExtension(relativePath).Length <= 1)
                {
                    invalidPathCount++;
                    continue;
                }

                var tags = tagSummaries.GetValueOrDefault(
                    asset.Id);

                var validMetadata =
                    ValidateLegacyMetadata(asset, tags);
                if (!validMetadata)
                {
                    invalidMetadataCount++;
                    continue;
                }

                if (asset.UsedTimestampFallback)
                {
                    invalidTimestampFallbacks++;
                }

                importableAssetCount++;
                importableCounts[asset.LibraryId]++;

                if (HasMeaningfulMetadata(
                        asset,
                        tags.Count > 0))
                {
                    meaningfulMetadataCount++;
                }
            }
        }

        if (invalidPathCount > 0)
        {
            diagnostics.Add(
                Error(
                    "asset_path_unmappable",
                    $"{invalidPathCount:N0} source assets are outside their registered library root or have no usable extension. Import refuses to silently drop them."));
        }

        if (invalidMetadataCount > 0)
        {
            diagnostics.Add(
                Error(
                    "user_metadata_out_of_bounds",
                    $"{invalidMetadataCount:N0} source assets contain user metadata that cannot be represented losslessly within v2 bounds."));
        }

        if (invalidTimestampFallbacks > 0)
        {
            diagnostics.Add(
                Warning(
                    "asset_timestamp_fallback",
                    $"{invalidTimestampFallbacks:N0} assets have no parseable modified timestamp; v1 updated_at or Unix epoch will be used only for the source-tracking timestamp."));
        }

        var disabledLibraries =
            libraries.Count(static library =>
                !library.IsEnabled);
        if (disabledLibraries > 0)
        {
            diagnostics.Add(
                Warning(
                    "library_enabled_state_unsupported",
                    $"{disabledLibraries:N0} v1 libraries are disabled. v2 currently has no matching enabled/disabled flag; the libraries will still be registered."));
        }

        var tagColors = tagSummaries.Values.Count(
            static summary => summary.HasColor);
        if (tagColors > 0)
        {
            diagnostics.Add(
                Warning(
                    "tag_color_unsupported",
                    $"{tagColors:N0} tagged assets reference at least one colored v1 tag. Tag names are preserved; v1 tag colors are reported but not imported because v2 has no tag-color field."));
        }

        await AddUnsupportedDataDiagnosticsAsync(
            source,
            diagnostics,
            cancellationToken).ConfigureAwait(false);

        var previews = libraries
            .Select(library =>
                new LumineV1ImportLibraryPreview(
                    library.Id,
                    library.Name,
                    library.RootPath,
                    library.IsEnabled,
                    assetCounts.GetValueOrDefault(
                        library.Id),
                    importableCounts.GetValueOrDefault(
                        library.Id)))
            .ToArray();

        return new LumineV1ImportPreview(
            snapshot.SourceDatabasePath,
            snapshot.FingerprintSha256,
            snapshot.SourceBytes,
            schemaVersion,
            previews,
            assetCount,
            importableAssetCount,
            meaningfulMetadataCount,
            tagSummaries.Values.Sum(
                static summary => summary.Count),
            diagnostics);
    }

    private static async Task<int> ValidateSchemaAsync(
        SqliteConnection source,
        List<LumineV1ImportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!await HasTableAsync(
                source,
                "_migrations",
                cancellationToken).ConfigureAwait(false))
        {
            diagnostics.Add(
                Error(
                    "not_lumine_v1_database",
                    "Source database has no v1 _migrations table."));
            return 0;
        }

        var migrationNames = new List<string>();
        await using (var command = source.CreateCommand())
        {
            command.CommandText =
                """
                SELECT name
                FROM _migrations
                ORDER BY id ASC;
                """;
            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                migrationNames.Add(reader.GetString(0));
            }
        }

        if (!migrationNames.Contains(
                "001_initial_schema.sql",
                StringComparer.Ordinal))
        {
            diagnostics.Add(
                Error(
                    "unsupported_v1_schema",
                    "Source migration history does not contain 001_initial_schema.sql."));
        }

        var schemaVersion = migrationNames
            .Select(ParseMigrationVersion)
            .DefaultIfEmpty(0)
            .Max();

        if (schemaVersion > LatestKnownV1Migration)
        {
            diagnostics.Add(
                Warning(
                    "newer_v1_schema",
                    $"Source schema reports migration {schemaVersion}, newer than the last schema mapped by this importer ({LatestKnownV1Migration}). Known user-data fields will still be validated, and extra non-core tables are reported as unsupported."));
        }

        var required = new Dictionary<string, string[]>(
            StringComparer.Ordinal)
        {
            ["libraries"] =
            [
                "id", "name", "root_path", "is_enabled"
            ],
            ["assets"] =
            [
                "id", "library_id", "file_path", "file_size",
                "modified_at_fs", "width", "height",
                "rating", "status_label", "is_favorite",
                "color_label", "updated_at"
            ],
            ["asset_notes"] =
            [
                "asset_id", "content"
            ],
            ["tags"] =
            [
                "id", "name", "color"
            ],
            ["asset_tags"] =
            [
                "asset_id", "tag_id"
            ]
        };

        foreach (var (table, columns) in required)
        {
            if (!await HasTableAsync(
                    source,
                    table,
                    cancellationToken).ConfigureAwait(false))
            {
                diagnostics.Add(
                    Error(
                        "missing_required_table",
                        $"Source database is missing required v1 table '{table}'."));
                continue;
            }

            var actual = await GetTableColumnsAsync(
                source,
                table,
                cancellationToken).ConfigureAwait(false);
            var missing = columns
                .Where(column =>
                    !actual.Contains(column))
                .ToArray();

            if (missing.Length > 0)
            {
                diagnostics.Add(
                    Error(
                        "missing_required_column",
                        $"Source table '{table}' is missing required columns: {string.Join(", ", missing)}."));
            }
        }

        return schemaVersion;
    }

    private static async Task AddUnsupportedDataDiagnosticsAsync(
        SqliteConnection source,
        List<LumineV1ImportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (await HasTableAsync(
                source,
                "folders",
                cancellationToken).ConfigureAwait(false))
        {
            var columns = await GetTableColumnsAsync(
                source,
                "folders",
                cancellationToken).ConfigureAwait(false);
            if (columns.Contains("is_excluded"))
            {
                var excluded = await CountRowsAsync(
                    source,
                    "folders",
                    "is_excluded <> 0",
                    cancellationToken).ConfigureAwait(false);
                if (excluded > 0)
                {
                    diagnostics.Add(
                        Warning(
                            "folder_exclusion_unsupported",
                            $"{excluded:N0} v1 folder exclusions are not imported because v2 has no equivalent exclusion state."));
                }
            }
        }

        var importedTables = new HashSet<string>(
            StringComparer.Ordinal)
        {
            "_migrations",
            "libraries",
            "folders",
            "assets",
            "asset_notes",
            "tags",
            "asset_tags"
        };

        var unsupported = new List<string>();

        await using (var list = source.CreateCommand())
        {
            list.CommandText =
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%'
                ORDER BY name ASC;
                """;

            await using var reader = await list.ExecuteReaderAsync(
                cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                var name = reader.GetString(0);
                if (!importedTables.Contains(name))
                {
                    unsupported.Add(name);
                }
            }
        }

        foreach (var table in unsupported)
        {
            var count = await CountRowsAsync(
                source,
                table,
                where: null,
                cancellationToken).ConfigureAwait(false);
            if (count > 0)
            {
                diagnostics.Add(
                    Warning(
                        "unsupported_table",
                        $"v1 table '{table}' contains {count:N0} rows and is intentionally not imported. Runtime/job/post/creative/AI state is outside the v2 user-data migration contract."));
            }
        }

        var assetColumns = await GetTableColumnsAsync(
            source,
            "assets",
            cancellationToken).ConfigureAwait(false);
        var importedAssetColumns = new HashSet<string>(
            StringComparer.Ordinal)
        {
            "id",
            "library_id",
            "file_path",
            "file_size",
            "modified_at_fs",
            "width",
            "height",
            "rating",
            "status_label",
            "is_favorite",
            "color_label",
            "updated_at"
        };
        var ignoredColumns = assetColumns
            .Where(column =>
                !importedAssetColumns.Contains(column))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (ignoredColumns.Length > 0)
        {
            diagnostics.Add(
                new LumineV1ImportDiagnostic(
                    LumineV1ImportDiagnosticSeverity.Info,
                    "technical_asset_fields_not_imported",
                    "The following v1 asset columns are intentionally not copied as source-of-truth user metadata: "
                    + string.Join(", ", ignoredColumns)
                    + ". v2 will derive current technical/source metadata independently."));
        }
    }

    private static async Task<IReadOnlyList<LegacyLibrary>>
        LoadSourceLibrariesAsync(
            SqliteConnection source,
            CancellationToken cancellationToken)
    {
        var result = new List<LegacyLibrary>();

        await using var command = source.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, root_path, is_enabled
            FROM libraries
            ORDER BY id ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            result.Add(
                new LegacyLibrary(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3) != 0));
        }

        return result;
    }

    private static async Task<Dictionary<long, TagSummary>>
        LoadTagSummariesAsync(
            SqliteConnection source,
            CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, TagSummary>();

        await using var command = source.CreateCommand();
        command.CommandText =
            """
            SELECT
                at.asset_id,
                t.name,
                t.color
            FROM asset_tags AS at
            INNER JOIN tags AS t
                ON t.id = at.tag_id
            ORDER BY at.asset_id ASC, t.id ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken).ConfigureAwait(false);

        long currentAssetId = -1;
        HashSet<string>? keys = null;
        var rawCount = 0;
        var hasColor = false;
        var invalid = false;

        void Flush()
        {
            if (currentAssetId < 0)
            {
                return;
            }

            result[currentAssetId] = new TagSummary(
                rawCount,
                keys?.Count ?? 0,
                hasColor,
                invalid);
        }

        while (await reader.ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            var assetId = reader.GetInt64(0);
            if (assetId != currentAssetId)
            {
                Flush();
                currentAssetId = assetId;
                keys = new HashSet<string>(
                    StringComparer.Ordinal);
                rawCount = 0;
                hasColor = false;
                invalid = false;
            }

            rawCount++;
            var name = reader.GetString(1);
            if (string.IsNullOrWhiteSpace(name))
            {
                invalid = true;
            }
            else
            {
                var normalized = name
                    .Trim()
                    .Normalize(NormalizationForm.FormKC);
                if (normalized.Length is < 1
                    or > MaxTagNameLength)
                {
                    invalid = true;
                }
                else
                {
                    keys!.Add(
                        normalized.ToUpperInvariant());
                }
            }

            if (!reader.IsDBNull(2)
                && !string.IsNullOrWhiteSpace(
                    reader.GetString(2)))
            {
                hasColor = true;
            }
        }

        Flush();
        return result;
    }

    private static LegacyAsset ReadLegacyAsset(
        SqliteDataReader reader)
    {
        var modified = ReadNullableText(reader, 4);
        var updated = ReadNullableText(reader, 11);
        var modifiedTicks = ParseLegacyTimestamp(
            modified,
            updated,
            out var usedFallback);

        return new LegacyAsset(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetInt64(3),
            modifiedTicks,
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetString(8),
            reader.GetInt64(9) != 0,
            ReadNullableText(reader, 10),
            ReadNullableText(reader, 12) ?? string.Empty,
            usedFallback);
    }

    private static bool ValidateLegacyMetadata(
        LegacyAsset asset,
        TagSummary tags)
    {
        if (asset.FileSize < 0
            || asset.Rating is < 0 or > 5
            || asset.Notes.Length > MaxNotesLength
            || tags.Invalid
            || tags.DistinctCount > MaxTagsPerAsset)
        {
            return false;
        }

        var status = NormalizeOptionalLabel(
            asset.StatusLabel);
        var color = NormalizeOptionalLabel(
            asset.ColorLabel);

        return (status is null
                || status.Length <= MaxLabelLength)
            && (color is null
                || color.Length <= MaxLabelLength);
    }

    private static bool HasMeaningfulMetadata(
        LegacyAsset asset,
        bool hasTags)
    {
        var status = NormalizeOptionalLabel(
            asset.StatusLabel);

        return asset.Rating > 0
            || asset.Favorite
            || asset.Notes.Length > 0
            || (!string.IsNullOrWhiteSpace(status)
                && !string.Equals(
                    status,
                    "unsorted",
                    StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrWhiteSpace(
                NormalizeOptionalLabel(asset.ColorLabel))
            || hasTags;
    }

    private static bool TryMapRelativePath(
        string rootPath,
        string filePath,
        out string relativePath)
    {
        relativePath = string.Empty;

        try
        {
            var root = LibraryPaths.NormalizeRoot(rootPath);
            var fullPath = Path.GetFullPath(filePath);
            var relative = Path.GetRelativePath(
                root,
                fullPath);

            if (Path.IsPathRooted(relative)
                || relative.Equals(
                    "..",
                    StringComparison.Ordinal)
                || relative.StartsWith(
                    "../",
                    StringComparison.Ordinal)
                || relative.StartsWith(
                    "..\\",
                    StringComparison.Ordinal))
            {
                return false;
            }

            relativePath =
                LibraryPaths.NormalizeRelativePath(
                    relative);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    private static async Task<long?> FindDestinationLibraryIdAsync(
        SqliteConnection destination,
        SqliteTransaction transaction,
        string rootKey,
        CancellationToken cancellationToken)
    {
        await using var command =
            destination.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id
            FROM libraries
            WHERE root_path_key = $root_key;
            """;
        command.Parameters.AddWithValue(
            "$root_key",
            rootKey);

        var value = await command.ExecuteScalarAsync(
            cancellationToken).ConfigureAwait(false);
        return value is null || value is DBNull
            ? null
            : Convert.ToInt64(
                value,
                CultureInfo.InvariantCulture);
    }

    private static async Task<long> InsertDestinationLibraryAsync(
        SqliteConnection destination,
        SqliteTransaction transaction,
        string name,
        string rootPath,
        string rootKey,
        long now,
        CancellationToken cancellationToken)
    {
        await using var command =
            destination.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO libraries(
                name,
                root_path,
                root_path_key,
                created_at_utc_ticks,
                updated_at_utc_ticks)
            VALUES(
                $name,
                $root,
                $root_key,
                $created,
                $updated)
            RETURNING id;
            """;
        command.Parameters.AddWithValue(
            "$name",
            string.IsNullOrWhiteSpace(name)
                ? "Imported Lumine v1 library"
                : name.Trim());
        command.Parameters.AddWithValue(
            "$root",
            rootPath);
        command.Parameters.AddWithValue(
            "$root_key",
            rootKey);
        command.Parameters.AddWithValue(
            "$created",
            now);
        command.Parameters.AddWithValue(
            "$updated",
            now);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(
                cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task<long?> FindDestinationAssetIdAsync(
        SqliteConnection destination,
        SqliteTransaction transaction,
        long libraryId,
        string relativePathKey,
        CancellationToken cancellationToken)
    {
        await using var command =
            destination.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id
            FROM assets
            WHERE library_id = $library_id
              AND relative_path_key = $path_key;
            """;
        command.Parameters.AddWithValue(
            "$library_id",
            libraryId);
        command.Parameters.AddWithValue(
            "$path_key",
            relativePathKey);

        var value = await command.ExecuteScalarAsync(
            cancellationToken).ConfigureAwait(false);
        return value is null || value is DBNull
            ? null
            : Convert.ToInt64(
                value,
                CultureInfo.InvariantCulture);
    }

    private static async Task<long> GetOrCreateFolderAsync(
        SqliteConnection destination,
        SqliteTransaction transaction,
        Dictionary<string, long> cache,
        long libraryId,
        string folderPath,
        long now,
        CancellationToken cancellationToken)
    {
        var folderKey =
            LibraryPaths.FolderPathKey(folderPath);
        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{libraryId}:{folderKey}");

        if (cache.TryGetValue(
                cacheKey,
                out var cached))
        {
            return cached;
        }

        await using (var insert =
                     destination.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO folders(
                    library_id,
                    relative_path,
                    relative_path_key,
                    created_at_utc_ticks)
                VALUES(
                    $library_id,
                    $path,
                    $path_key,
                    $created)
                ON CONFLICT(
                    library_id,
                    relative_path_key)
                DO NOTHING;
                """;
            insert.Parameters.AddWithValue(
                "$library_id",
                libraryId);
            insert.Parameters.AddWithValue(
                "$path",
                folderPath);
            insert.Parameters.AddWithValue(
                "$path_key",
                folderKey);
            insert.Parameters.AddWithValue(
                "$created",
                now);
            await insert.ExecuteNonQueryAsync(
                cancellationToken).ConfigureAwait(false);
        }

        await using var select =
            destination.CreateCommand();
        select.Transaction = transaction;
        select.CommandText =
            """
            SELECT id
            FROM folders
            WHERE library_id = $library_id
              AND relative_path_key = $path_key;
            """;
        select.Parameters.AddWithValue(
            "$library_id",
            libraryId);
        select.Parameters.AddWithValue(
            "$path_key",
            folderKey);

        var id = Convert.ToInt64(
            await select.ExecuteScalarAsync(
                cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        cache.Add(cacheKey, id);
        return id;
    }

    private static async Task<long> InsertDestinationAssetAsync(
        SqliteConnection destination,
        SqliteTransaction transaction,
        long libraryId,
        long? folderId,
        string relativePath,
        string relativePathKey,
        LegacyAsset source,
        long now,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(
                relativePath)
            .TrimStart('.')
            .ToLowerInvariant();
        await using var command =
            destination.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO assets(
                library_id,
                folder_id,
                relative_path,
                relative_path_key,
                file_name,
                extension,
                file_size,
                modified_at_utc_ticks,
                source_revision,
                width,
                height,
                format,
                created_at_utc_ticks,
                updated_at_utc_ticks)
            VALUES(
                $library_id,
                $folder_id,
                $relative_path,
                $relative_path_key,
                $file_name,
                $extension,
                $file_size,
                $modified,
                1,
                NULL,
                NULL,
                NULL,
                $created,
                $updated)
            RETURNING id;
            """;
        command.Parameters.AddWithValue(
            "$library_id",
            libraryId);
        command.Parameters.AddWithValue(
            "$folder_id",
            (object?)folderId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$relative_path",
            relativePath);
        command.Parameters.AddWithValue(
            "$relative_path_key",
            relativePathKey);
        command.Parameters.AddWithValue(
            "$file_name",
            Path.GetFileName(relativePath));
        command.Parameters.AddWithValue(
            "$extension",
            extension);
        command.Parameters.AddWithValue(
            "$file_size",
            source.FileSize);
        command.Parameters.AddWithValue(
            "$modified",
            source.ModifiedAtUtcTicks);
        command.Parameters.AddWithValue(
            "$created",
            now);
        command.Parameters.AddWithValue(
            "$updated",
            now);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(
                cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task<bool> HasDestinationUserMetadataAsync(
        SqliteConnection destination,
        SqliteTransaction transaction,
        long assetId,
        CancellationToken cancellationToken)
    {
        await using var command =
            destination.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM asset_user_metadata
                WHERE asset_id = $asset_id
            );
            """;
        command.Parameters.AddWithValue(
            "$asset_id",
            assetId);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(
                cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) != 0;
    }

    private static async Task InsertDestinationUserMetadataAsync(
        SqliteConnection destination,
        SqliteTransaction transaction,
        long assetId,
        long libraryId,
        LegacyAsset source,
        long now,
        CancellationToken cancellationToken)
    {
        var rating = source.Rating == 0
            ? (int?)null
            : source.Rating;
        var status = NormalizeOptionalLabel(
            source.StatusLabel);
        if (string.Equals(
                status,
                "unsorted",
                StringComparison.OrdinalIgnoreCase))
        {
            status = null;
        }

        var color = NormalizeOptionalLabel(
            source.ColorLabel);

        await using (var metadata =
                     destination.CreateCommand())
        {
            metadata.Transaction = transaction;
            metadata.CommandText =
                """
                INSERT INTO asset_user_metadata(
                    asset_id,
                    rating,
                    favorite,
                    notes,
                    status_label,
                    color_label,
                    updated_at_utc_ticks)
                VALUES(
                    $asset_id,
                    $rating,
                    $favorite,
                    $notes,
                    $status,
                    $color,
                    $updated);
                """;
            metadata.Parameters.AddWithValue(
                "$asset_id",
                assetId);
            metadata.Parameters.AddWithValue(
                "$rating",
                (object?)rating ?? DBNull.Value);
            metadata.Parameters.AddWithValue(
                "$favorite",
                source.Favorite ? 1 : 0);
            metadata.Parameters.AddWithValue(
                "$notes",
                source.Notes);
            metadata.Parameters.AddWithValue(
                "$status",
                (object?)status ?? DBNull.Value);
            metadata.Parameters.AddWithValue(
                "$color",
                (object?)color ?? DBNull.Value);
            metadata.Parameters.AddWithValue(
                "$updated",
                now);
            await metadata.ExecuteNonQueryAsync(
                cancellationToken).ConfigureAwait(false);
        }

        await using var dirty =
            destination.CreateCommand();
        dirty.Transaction = transaction;
        dirty.CommandText =
            """
            INSERT INTO asset_search_dirty(
                asset_id,
                library_id)
            VALUES(
                $asset_id,
                $library_id)
            ON CONFLICT(asset_id)
            DO UPDATE SET
                library_id = excluded.library_id;
            """;
        dirty.Parameters.AddWithValue(
            "$asset_id",
            assetId);
        dirty.Parameters.AddWithValue(
            "$library_id",
            libraryId);
        await dirty.ExecuteNonQueryAsync(
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> GetOrCreateTagAsync(
        SqliteConnection destination,
        SqliteTransaction transaction,
        long libraryId,
        string name,
        string key,
        long now,
        CancellationToken cancellationToken)
    {
        await using var command =
            destination.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO tags(
                library_id,
                name,
                name_key,
                created_at_utc_ticks)
            VALUES(
                $library_id,
                $name,
                $name_key,
                $created)
            ON CONFLICT(
                library_id,
                name_key)
            DO UPDATE SET
                name = excluded.name
            RETURNING id;
            """;
        command.Parameters.AddWithValue(
            "$library_id",
            libraryId);
        command.Parameters.AddWithValue(
            "$name",
            name);
        command.Parameters.AddWithValue(
            "$name_key",
            key);
        command.Parameters.AddWithValue(
            "$created",
            now);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(
                cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task<int> AssignTagAsync(
        SqliteConnection destination,
        SqliteTransaction transaction,
        long assetId,
        long tagId,
        long now,
        CancellationToken cancellationToken)
    {
        await using var command =
            destination.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR IGNORE INTO asset_tags(
                asset_id,
                tag_id,
                created_at_utc_ticks)
            VALUES(
                $asset_id,
                $tag_id,
                $created);
            """;
        command.Parameters.AddWithValue(
            "$asset_id",
            assetId);
        command.Parameters.AddWithValue(
            "$tag_id",
            tagId);
        command.Parameters.AddWithValue(
            "$created",
            now);

        return await command.ExecuteNonQueryAsync(
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> HasImportProvenanceAsync(
        SqliteConnection destination,
        SqliteTransaction transaction,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command =
            destination.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM compatibility_imports
                WHERE source_kind = $kind
                  AND source_fingerprint_sha256 = $fingerprint
            );
            """;
        command.Parameters.AddWithValue(
            "$kind",
            SourceKind);
        command.Parameters.AddWithValue(
            "$fingerprint",
            fingerprint);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(
                cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) != 0;
    }

    private static async Task InsertImportProvenanceAsync(
        SqliteConnection destination,
        SqliteTransaction transaction,
        LumineV1ImportPreview preview,
        int librariesImported,
        int librariesMatched,
        int assetsImported,
        int assetsMatched,
        int metadataImported,
        int metadataConflicts,
        int tagAssignmentsImported,
        long now,
        CancellationToken cancellationToken)
    {
        await using var command =
            destination.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO compatibility_imports(
                source_kind,
                source_schema_version,
                source_fingerprint_sha256,
                source_bytes,
                imported_at_utc_ticks,
                libraries_imported,
                libraries_matched,
                assets_imported,
                assets_matched,
                metadata_imported,
                metadata_conflicts,
                tag_assignments_imported,
                diagnostic_count)
            VALUES(
                $kind,
                $schema,
                $fingerprint,
                $bytes,
                $imported,
                $libraries_imported,
                $libraries_matched,
                $assets_imported,
                $assets_matched,
                $metadata_imported,
                $metadata_conflicts,
                $tags,
                $diagnostics);
            """;
        command.Parameters.AddWithValue(
            "$kind",
            SourceKind);
        command.Parameters.AddWithValue(
            "$schema",
            preview.SourceSchemaVersion);
        command.Parameters.AddWithValue(
            "$fingerprint",
            preview.SourceFingerprintSha256);
        command.Parameters.AddWithValue(
            "$bytes",
            preview.SourceBytes);
        command.Parameters.AddWithValue(
            "$imported",
            now);
        command.Parameters.AddWithValue(
            "$libraries_imported",
            librariesImported);
        command.Parameters.AddWithValue(
            "$libraries_matched",
            librariesMatched);
        command.Parameters.AddWithValue(
            "$assets_imported",
            assetsImported);
        command.Parameters.AddWithValue(
            "$assets_matched",
            assetsMatched);
        command.Parameters.AddWithValue(
            "$metadata_imported",
            metadataImported);
        command.Parameters.AddWithValue(
            "$metadata_conflicts",
            metadataConflicts);
        command.Parameters.AddWithValue(
            "$tags",
            tagAssignmentsImported);
        command.Parameters.AddWithValue(
            "$diagnostics",
            preview.Diagnostics.Count);

        await command.ExecuteNonQueryAsync(
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SqliteConnection> OpenSnapshotAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5
        };

        var connection =
            new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command =
            connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA query_only=ON;
            PRAGMA busy_timeout=5000;
            """;
        await command.ExecuteNonQueryAsync(
            cancellationToken).ConfigureAwait(false);

        return connection;
    }

    private static async Task<bool> HasTableAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = $name
            );
            """;
        command.Parameters.AddWithValue(
            "$name",
            tableName);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(
                cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) != 0;
    }

    private static async Task<HashSet<string>>
        GetTableColumnsAsync(
            SqliteConnection connection,
            string tableName,
            CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(
            StringComparer.Ordinal);
        var quoted = QuoteIdentifier(tableName);

        await using var command =
            connection.CreateCommand();
        command.CommandText =
            $"PRAGMA table_info({quoted});";

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            result.Add(reader.GetString(1));
        }

        return result;
    }

    private static async Task<long> CountRowsAsync(
        SqliteConnection connection,
        string tableName,
        string? where,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)}"
            + (string.IsNullOrWhiteSpace(where)
                ? ";"
                : $" WHERE {where};");

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(
                cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static string QuoteIdentifier(
        string value) =>
        """ + value.Replace(
            """,
            """",
            StringComparison.Ordinal) + """;

    private static int ParseMigrationVersion(
        string name)
    {
        var separator = name.IndexOf('_');
        var prefix = separator < 0
            ? name
            : name[..separator];

        return int.TryParse(
            prefix,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var version)
            ? version
            : 0;
    }

    private static string? NormalizeOptionalLabel(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Trim()
            .Normalize(NormalizationForm.FormKC);
    }

    private static NormalizedTag NormalizeTag(
        string value)
    {
        var name = value
            .Trim()
            .Normalize(NormalizationForm.FormKC);
        return new NormalizedTag(
            name,
            name.ToUpperInvariant());
    }

    private static string? ReadNullableText(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Convert.ToString(
                reader.GetValue(ordinal),
                CultureInfo.InvariantCulture);

    private static long ParseLegacyTimestamp(
        string? primary,
        string? fallback,
        out bool usedFallback)
    {
        if (TryParseLegacyTimestamp(
                primary,
                out var primaryTicks))
        {
            usedFallback = false;
            return primaryTicks;
        }

        if (TryParseLegacyTimestamp(
                fallback,
                out var fallbackTicks))
        {
            usedFallback = true;
            return fallbackTicks;
        }

        usedFallback = true;
        return DateTimeOffset.UnixEpoch.UtcDateTime.Ticks;
    }

    private static bool TryParseLegacyTimestamp(
        string? value,
        out long ticks)
    {
        ticks = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal
                    | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return false;
        }

        ticks = parsed.UtcDateTime.Ticks;
        return true;
    }

    private static LumineV1ImportDiagnostic Error(
        string code,
        string message) =>
        new(
            LumineV1ImportDiagnosticSeverity.Error,
            code,
            message);

    private static LumineV1ImportDiagnostic Warning(
        string code,
        string message) =>
        new(
            LumineV1ImportDiagnosticSeverity.Warning,
            code,
            message);

    private const string LegacyAssetSelectSql =
        """
        SELECT
            a.id,
            a.library_id,
            a.file_path,
            a.file_size,
            a.modified_at_fs,
            a.width,
            a.height,
            a.rating,
            a.status_label,
            a.is_favorite,
            a.color_label,
            a.updated_at,
            n.content
        FROM assets AS a
        LEFT JOIN asset_notes AS n
            ON n.asset_id = a.id
        ORDER BY a.library_id ASC, a.id ASC;
        """;

    private sealed record LegacyLibrary(
        long Id,
        string Name,
        string RootPath,
        bool IsEnabled);

    private sealed record LegacyAsset(
        long Id,
        long LibraryId,
        string FilePath,
        long FileSize,
        long ModifiedAtUtcTicks,
        int Width,
        int Height,
        int Rating,
        string StatusLabel,
        bool Favorite,
        string? ColorLabel,
        string Notes,
        bool UsedTimestampFallback);

    private readonly record struct TagSummary(
        int Count,
        int DistinctCount,
        bool HasColor,
        bool Invalid);

    private readonly record struct DestinationLibrary(
        long Id,
        string RootPath);

    private readonly record struct DestinationAsset(
        long AssetId,
        long LibraryId,
        bool AllowTagImport);

    private readonly record struct NormalizedTag(
        string Name,
        string Key);

    private sealed class SourceSnapshot : IDisposable
    {
        private SourceSnapshot(
            string sourceDatabasePath,
            string rootPath,
            string databasePath,
            string fingerprintSha256,
            long sourceBytes)
        {
            SourceDatabasePath = sourceDatabasePath;
            RootPath = rootPath;
            DatabasePath = databasePath;
            FingerprintSha256 =
                fingerprintSha256;
            SourceBytes = sourceBytes;
        }

        public string SourceDatabasePath { get; }

        public string RootPath { get; }

        public string DatabasePath { get; }

        public string FingerprintSha256 { get; }

        public long SourceBytes { get; }

        public static async Task<SourceSnapshot> CreateAsync(
            string sourceDatabasePath,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                sourceDatabasePath);

            var sourcePath =
                Path.GetFullPath(sourceDatabasePath);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    "Lumine v1 database was not found.",
                    sourcePath);
            }

            var before = await CaptureFingerprintAsync(
                sourcePath,
                cancellationToken).ConfigureAwait(false);

            var root = Path.Combine(
                Path.GetTempPath(),
                $"lumine-v1-import-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            try
            {
                var snapshotPath = Path.Combine(
                    root,
                    Path.GetFileName(sourcePath));

                await CopyFileAsync(
                    sourcePath,
                    snapshotPath,
                    cancellationToken).ConfigureAwait(false);

                var sourceWal = sourcePath + "-wal";
                if (File.Exists(sourceWal))
                {
                    await CopyFileAsync(
                        sourceWal,
                        snapshotPath + "-wal",
                        cancellationToken).ConfigureAwait(false);
                }

                var after = await CaptureFingerprintAsync(
                    sourcePath,
                    cancellationToken).ConfigureAwait(false);

                if (!string.Equals(
                        before.FingerprintSha256,
                        after.FingerprintSha256,
                        StringComparison.Ordinal)
                    || before.TotalBytes != after.TotalBytes)
                {
                    throw new LumineV1SourceChangedException(
                        "The Lumine v1 database changed while its read-only migration snapshot was being created. Close Lumine v1 and retry.");
                }

                return new SourceSnapshot(
                    sourcePath,
                    root,
                    snapshotPath,
                    before.FingerprintSha256,
                    before.TotalBytes);
            }
            catch
            {
                DeleteDirectoryBestEffort(root);
                throw;
            }
        }

        public void Dispose() =>
            DeleteDirectoryBestEffort(RootPath);

        private static async Task<SourceFingerprint>
            CaptureFingerprintAsync(
                string databasePath,
                CancellationToken cancellationToken)
        {
            using var hash =
                IncrementalHash.CreateHash(
                    HashAlgorithmName.SHA256);
            long totalBytes = 0;

            foreach (var (path, label) in new[]
                     {
                         (databasePath, "database"),
                         (databasePath + "-wal", "wal")
                     })
            {
                if (!File.Exists(path))
                {
                    hash.AppendData(
                        Encoding.UTF8.GetBytes(
                            label + ":absent\n"));
                    continue;
                }

                var info = new FileInfo(path);
                info.Refresh();
                totalBytes = checked(
                    totalBytes + info.Length);

                hash.AppendData(
                    Encoding.UTF8.GetBytes(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{label}:{info.Length}\n")));

                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    64 * 1024,
                    FileOptions.Asynchronous
                        | FileOptions.SequentialScan);

                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = await stream.ReadAsync(
                        buffer,
                        cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    hash.AppendData(
                        buffer.AsSpan(0, read));
                }
            }

            return new SourceFingerprint(
                Convert.ToHexString(
                        hash.GetHashAndReset())
                    .ToLowerInvariant(),
                totalBytes);
        }

        private static async Task CopyFileAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.Asynchronous
                    | FileOptions.SequentialScan);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous
                    | FileOptions.SequentialScan);

            await source.CopyToAsync(
                destination,
                64 * 1024,
                cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(
                cancellationToken).ConfigureAwait(false);
        }

        private static void DeleteDirectoryBestEffort(
            string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(
                        path,
                        recursive: true);
                }
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException)
            {
            }
        }
    }

    private readonly record struct SourceFingerprint(
        string FingerprintSha256,
        long TotalBytes);
}
