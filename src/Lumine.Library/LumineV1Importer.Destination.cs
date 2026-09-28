using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed partial class LumineV1Importer
{
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

}
