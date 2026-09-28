using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed partial class LumineV1Importer
{
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
}
