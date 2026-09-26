using System.Globalization;
using Lumine.Core;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed class LibraryRepository
{
    private readonly LibraryDatabase _database;

    public LibraryRepository(LibraryDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<LibraryInfo> RegisterLibraryAsync(
        string name,
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalizedRoot = LibraryPaths.NormalizeRoot(rootPath);
        if (!Directory.Exists(normalizedRoot))
        {
            throw new DirectoryNotFoundException($"Library root does not exist: {normalizedRoot}");
        }

        var rootKey = LibraryPaths.RootKey(normalizedRoot);
        var nowTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks;

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO libraries(
                name, root_path, root_path_key,
                created_at_utc_ticks, updated_at_utc_ticks)
            VALUES($name, $root, $root_key, $created, $updated)
            ON CONFLICT(root_path_key) DO UPDATE SET
                name = excluded.name,
                root_path = excluded.root_path,
                updated_at_utc_ticks = excluded.updated_at_utc_ticks
            RETURNING
                id, name, root_path,
                created_at_utc_ticks, updated_at_utc_ticks,
                last_scan_completed_at_utc_ticks,
                last_scan_attempted_at_utc_ticks,
                scan_state;
            """;
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$root", normalizedRoot);
        command.Parameters.AddWithValue("$root_key", rootKey);
        command.Parameters.AddWithValue("$created", nowTicks);
        command.Parameters.AddWithValue("$updated", nowTicks);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Library registration returned no row.");
        }

        return ReadLibrary(reader);
    }

    public async Task<LibraryInfo?> GetLibraryAsync(
        long libraryId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id, name, root_path,
                created_at_utc_ticks, updated_at_utc_ticks,
                last_scan_completed_at_utc_ticks,
                last_scan_attempted_at_utc_ticks,
                scan_state
            FROM libraries
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", libraryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadLibrary(reader)
            : null;
    }

    public async Task<long> CountAssetsAsync(
        long libraryId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM assets WHERE library_id = $library_id;";
        command.Parameters.AddWithValue("$library_id", libraryId);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    public async Task<AssetInfo?> GetAssetAsync(
        long libraryId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var pathKey = LibraryPaths.RelativePathKey(relativePath);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                a.id, a.library_id, a.folder_id,
                a.relative_path, a.file_name, a.extension,
                a.file_size, a.modified_at_utc_ticks,
                a.source_revision,
                a.width, a.height, a.format,
                tm.source_identity,
                tm.raw_width, tm.raw_height, tm.has_alpha,
                a.observed_generation
            FROM assets AS a
            LEFT JOIN asset_technical_metadata AS tm
              ON tm.asset_id = a.id
             AND tm.source_revision = a.source_revision
            WHERE a.library_id = $library_id
              AND a.relative_path_key = $path_key;
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue("$path_key", pathKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadAsset(reader)
            : null;
    }

    public async Task<bool> UpdateTechnicalMetadataAsync(
        long libraryId,
        long assetId,
        long expectedSourceRevision,
        long expectedFileSize,
        long expectedModifiedAtUtcTicks,
        AssetTechnicalMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(assetId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedSourceRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedFileSize);

        if (metadata.Width <= 0
            || metadata.Height <= 0
            || metadata.RawWidth <= 0
            || metadata.RawHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metadata),
                "Technical image dimensions must be positive.");
        }

        if (string.IsNullOrWhiteSpace(metadata.Format)
            || metadata.Format.Length > 64)
        {
            throw new ArgumentException(
                "Technical image format must be present and at most 64 characters.",
                nameof(metadata));
        }

        if (!FileSourceIdentityProbe.IsValid(metadata.SourceIdentity))
        {
            throw new ArgumentException(
                "Source identity must be a valid NTFS-USN or SHA-256 identity.",
                nameof(metadata));
        }

        await using var connection = await _database.OpenConnectionAsync(
            cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        await using var updateAsset = connection.CreateCommand();
        updateAsset.Transaction = transaction;
        updateAsset.CommandText =
            """
            UPDATE assets
            SET width = $width,
                height = $height,
                format = $format,
                updated_at_utc_ticks = $updated
            WHERE library_id = $library_id
              AND id = $asset_id
              AND source_revision = $source_revision
              AND file_size = $file_size
              AND modified_at_utc_ticks = $modified
              AND NOT EXISTS (
                  SELECT 1
                  FROM asset_technical_metadata AS tm
                  WHERE tm.asset_id = assets.id
                    AND tm.source_revision = assets.source_revision
                    AND lower(tm.source_identity) <> lower($source_identity)
              );
            """;
        updateAsset.Parameters.AddWithValue("$width", metadata.Width);
        updateAsset.Parameters.AddWithValue("$height", metadata.Height);
        updateAsset.Parameters.AddWithValue("$format", metadata.Format.Trim());
        updateAsset.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.UtcDateTime.Ticks);
        updateAsset.Parameters.AddWithValue("$library_id", libraryId);
        updateAsset.Parameters.AddWithValue("$asset_id", assetId);
        updateAsset.Parameters.AddWithValue("$source_revision", expectedSourceRevision);
        updateAsset.Parameters.AddWithValue("$file_size", expectedFileSize);
        updateAsset.Parameters.AddWithValue("$modified", expectedModifiedAtUtcTicks);
        updateAsset.Parameters.AddWithValue(
            "$source_identity",
            metadata.SourceIdentity);

        if (await updateAsset.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            transaction.Rollback();
            return false;
        }

        await using var upsertMetadata = connection.CreateCommand();
        upsertMetadata.Transaction = transaction;
        upsertMetadata.CommandText =
            """
            INSERT INTO asset_technical_metadata(
                asset_id, source_revision,
                source_identity,
                raw_width, raw_height, has_alpha,
                updated_at_utc_ticks)
            VALUES(
                $asset_id, $source_revision,
                $source_identity,
                $raw_width, $raw_height, $has_alpha,
                $updated)
            ON CONFLICT(asset_id) DO UPDATE SET
                source_revision = excluded.source_revision,
                source_identity = excluded.source_identity,
                raw_width = excluded.raw_width,
                raw_height = excluded.raw_height,
                has_alpha = excluded.has_alpha,
                updated_at_utc_ticks = excluded.updated_at_utc_ticks;
            """;
        upsertMetadata.Parameters.AddWithValue("$asset_id", assetId);
        upsertMetadata.Parameters.AddWithValue("$source_revision", expectedSourceRevision);
        upsertMetadata.Parameters.AddWithValue(
            "$source_identity",
            metadata.SourceIdentity);
        upsertMetadata.Parameters.AddWithValue("$raw_width", metadata.RawWidth);
        upsertMetadata.Parameters.AddWithValue("$raw_height", metadata.RawHeight);
        upsertMetadata.Parameters.AddWithValue("$has_alpha", metadata.HasAlpha ? 1 : 0);
        upsertMetadata.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.UtcDateTime.Ticks);
        await upsertMetadata.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        transaction.Commit();
        return true;
    }

    internal async Task<IReadOnlyDictionary<string, TrackedSourceIdentity>>
        LoadTrackedSourceIdentitiesAsync(
            long libraryId,
            IReadOnlyList<string> relativePathKeys,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relativePathKeys);

        if (relativePathKeys.Count == 0)
        {
            return new Dictionary<string, TrackedSourceIdentity>(
                StringComparer.Ordinal);
        }

        var keys = relativePathKeys
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var result = new Dictionary<string, TrackedSourceIdentity>(
            keys.Length,
            StringComparer.Ordinal);

        await using var connection = await _database.OpenConnectionAsync(
            cancellationToken).ConfigureAwait(false);

        const int chunkSize = 400;
        for (var offset = 0; offset < keys.Length; offset += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(
                chunkSize,
                keys.Length - offset);

            await using var command = connection.CreateCommand();
            command.Parameters.AddWithValue("$library_id", libraryId);

            var parameterNames = new string[count];
            for (var index = 0; index < count; index++)
            {
                var parameterName = $"$path_key_{index}";
                parameterNames[index] = parameterName;
                command.Parameters.AddWithValue(
                    parameterName,
                    keys[offset + index]);
            }

            command.CommandText =
                $"""
                SELECT
                    a.id,
                    a.source_revision,
                    a.relative_path_key,
                    a.file_size,
                    a.modified_at_utc_ticks,
                    tm.source_identity
                FROM assets AS a
                INNER JOIN asset_technical_metadata AS tm
                  ON tm.asset_id = a.id
                 AND tm.source_revision = a.source_revision
                WHERE a.library_id = $library_id
                  AND a.relative_path_key IN (
                      {string.Join(", ", parameterNames)}
                  );
                """;

            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var tracked = new TrackedSourceIdentity(
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetString(5));
                result[tracked.RelativePathKey] = tracked;
            }
        }

        return result;
    }

    public async Task<int> UpsertAssetsAsync(
        long libraryId,
        IReadOnlyList<AssetUpsert> assets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);

        await using var session = await OpenIngestSessionAsync(libraryId, cancellationToken).ConfigureAwait(false);
        return await session.WriteBatchAsync(assets, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LibraryIngestSession> OpenIngestSessionAsync(
        long libraryId,
        CancellationToken cancellationToken = default)
    {
        var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LibraryIngestSession.CreateAsync(
                this,
                connection,
                libraryId,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<int> UpsertAssetsOnConnectionAsync(
        SqliteConnection connection,
        long libraryId,
        IReadOnlyList<AssetUpsert> assets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assets);

        if (assets.Count == 0)
        {
            return 0;
        }

        var prepared = new List<PreparedAsset>(assets.Count);
        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateAsset(asset);

            var relativePath = LibraryPaths.NormalizeRelativePath(asset.RelativePath);
            var extension = Path.GetExtension(relativePath).TrimStart('.').ToLowerInvariant();
            if (extension.Length == 0)
            {
                throw new ArgumentException($"Asset has no file extension: {relativePath}", nameof(assets));
            }

            var folderPath = LibraryPaths.FolderRelativePath(relativePath);
            prepared.Add(new PreparedAsset(
                asset,
                relativePath,
                LibraryPaths.RelativePathKey(relativePath),
                Path.GetFileName(relativePath),
                extension,
                folderPath,
                folderPath.Length == 0 ? null : LibraryPaths.FolderPathKey(folderPath)));
        }

        using var transaction = connection.BeginTransaction();

        await ApplyForcedSourceRevisionHintsAsync(
            connection,
            transaction,
            libraryId,
            prepared,
            cancellationToken).ConfigureAwait(false);

        var folderIds = await LoadExistingFolderIdsAsync(
            connection,
            transaction,
            libraryId,
            prepared,
            cancellationToken).ConfigureAwait(false);

        var requiredFolderCount = prepared
            .Where(static item => item.FolderKey is not null)
            .Select(static item => item.FolderKey!)
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (folderIds.Count < requiredFolderCount)
        {
            await EnsureMissingFoldersAsync(
                connection,
                transaction,
                libraryId,
                prepared,
                folderIds,
                cancellationToken).ConfigureAwait(false);

            folderIds = await LoadExistingFolderIdsAsync(
                connection,
                transaction,
                libraryId,
                prepared,
                cancellationToken).ConfigureAwait(false);
        }

        await UpsertPreparedAssetsAsync(
            connection,
            transaction,
            libraryId,
            prepared,
            folderIds,
            cancellationToken).ConfigureAwait(false);

        transaction.Commit();
        return assets.Count;
    }

    public async Task<int> RemoveAssetsAsync(
        long libraryId,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);

        if (relativePaths.Count == 0)
        {
            return 0;
        }

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM assets
            WHERE library_id = $library_id
              AND relative_path_key = $path_key;
            """;

        var libraryParameter = command.Parameters.Add("$library_id", SqliteType.Integer);
        var pathParameter = command.Parameters.Add("$path_key", SqliteType.Text);
        libraryParameter.Value = libraryId;
        command.Prepare();

        var removed = 0;

        foreach (var relativePath in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pathParameter.Value = LibraryPaths.RelativePathKey(relativePath);
            removed += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        return removed;
    }

    public async Task<bool> RemoveAssetAsync(
        long libraryId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var pathKey = LibraryPaths.RelativePathKey(relativePath);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM assets
            WHERE library_id = $library_id
              AND relative_path_key = $path_key;
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue("$path_key", pathKey);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<AssetPage> GetAssetPageAsync(
        long libraryId,
        int limit,
        AssetCursor? cursor = null,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Page size must be between 1 and 1000.");
        }

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = cursor is null
            ? """
              SELECT
                  a.id, a.library_id, a.folder_id,
                  a.relative_path, a.file_name, a.extension,
                  a.file_size, a.modified_at_utc_ticks,
                  a.source_revision,
                  a.width, a.height, a.format,
                  tm.source_identity,
                  tm.raw_width, tm.raw_height, tm.has_alpha
              FROM assets AS a
              LEFT JOIN asset_technical_metadata AS tm
                ON tm.asset_id = a.id
               AND tm.source_revision = a.source_revision
              WHERE a.library_id = $library_id
              ORDER BY a.modified_at_utc_ticks DESC, a.id DESC
              LIMIT $limit;
              """
            : """
              SELECT
                  a.id, a.library_id, a.folder_id,
                  a.relative_path, a.file_name, a.extension,
                  a.file_size, a.modified_at_utc_ticks,
                  a.source_revision,
                  a.width, a.height, a.format,
                  tm.source_identity,
                  tm.raw_width, tm.raw_height, tm.has_alpha
              FROM assets AS a
              LEFT JOIN asset_technical_metadata AS tm
                ON tm.asset_id = a.id
               AND tm.source_revision = a.source_revision
              WHERE a.library_id = $library_id
                AND (
                    a.modified_at_utc_ticks < $cursor_modified
                    OR (
                        a.modified_at_utc_ticks = $cursor_modified
                        AND a.id < $cursor_id
                    )
                )
              ORDER BY a.modified_at_utc_ticks DESC, a.id DESC
              LIMIT $limit;
              """;

        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue("$limit", limit + 1);

        if (cursor is { } value)
        {
            command.Parameters.AddWithValue("$cursor_modified", value.ModifiedAtUtcTicks);
            command.Parameters.AddWithValue("$cursor_id", value.Id);
        }

        var items = new List<AssetInfo>(limit + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(ReadAsset(reader));
        }

        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        AssetCursor? nextCursor = hasMore && items.Count > 0
            ? AssetCursor.From(items[^1])
            : null;

        return new AssetPage(items, nextCursor);
    }

    public async Task MarkScanStartedAsync(
        long libraryId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE libraries
            SET scan_state = $state,
                last_scan_attempted_at_utc_ticks = $attempted,
                updated_at_utc_ticks = $attempted
            WHERE id = $library_id;
            """;
        command.Parameters.AddWithValue("$state", (int)LibraryScanState.InProgress);
        command.Parameters.AddWithValue("$attempted", startedAtUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$library_id", libraryId);

        EnsureSingleLibraryUpdated(
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false),
            libraryId);
    }

    public async Task MarkScanFinishedAsync(
        long libraryId,
        DateTimeOffset finishedAtUtc,
        bool completed,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE libraries
            SET scan_state = $state,
                last_scan_attempted_at_utc_ticks = $attempted,
                last_scan_completed_at_utc_ticks =
                    CASE WHEN $completed = 1 THEN $attempted
                         ELSE last_scan_completed_at_utc_ticks
                    END,
                updated_at_utc_ticks = $attempted
            WHERE id = $library_id;
            """;
        command.Parameters.AddWithValue(
            "$state",
            completed ? (int)LibraryScanState.Complete : (int)LibraryScanState.Partial);
        command.Parameters.AddWithValue("$completed", completed ? 1 : 0);
        command.Parameters.AddWithValue("$attempted", finishedAtUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$library_id", libraryId);

        EnsureSingleLibraryUpdated(
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false),
            libraryId);
    }

    public async Task<LibrarySyncState> GetOrCreateSyncStateAsync(
        long libraryId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (var ensure = connection.CreateCommand())
        {
            ensure.CommandText =
                """
                INSERT INTO library_sync_state(library_id)
                VALUES ($library_id)
                ON CONFLICT(library_id) DO NOTHING;
                """;
            ensure.Parameters.AddWithValue("$library_id", libraryId);
            await ensure.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                library_id,
                reconcile_generation,
                reconcile_required,
                usn_journal_id,
                next_usn,
                watcher_stopped_at_utc_ticks,
                last_reconciled_at_utc_ticks,
                last_error
            FROM library_sync_state
            WHERE library_id = $library_id;
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Library {libraryId} sync state could not be created.");
        }

        return ReadSyncState(reader);
    }

    public async Task<long> BeginReconcileGenerationAsync(
        long libraryId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO library_sync_state(
                library_id,
                reconcile_generation,
                reconcile_required)
            VALUES ($library_id, 1, 1)
            ON CONFLICT(library_id) DO UPDATE SET
                reconcile_generation = library_sync_state.reconcile_generation + 1,
                reconcile_required = 1,
                last_error = NULL
            RETURNING reconcile_generation;
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    public async Task<long> CompleteReconcileAsync(
        long libraryId,
        long generation,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        long deleted;
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText =
                """
                DELETE FROM assets
                WHERE library_id = $library_id
                  AND observed_generation <> $generation;
                """;
            delete.Parameters.AddWithValue("$library_id", libraryId);
            delete.Parameters.AddWithValue("$generation", generation);
            deleted = await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var cleanupFolders = connection.CreateCommand())
        {
            cleanupFolders.Transaction = transaction;
            cleanupFolders.CommandText =
                """
                DELETE FROM folders
                WHERE library_id = $library_id
                  AND NOT EXISTS(
                      SELECT 1
                      FROM assets
                      WHERE assets.folder_id = folders.id
                  );
                """;
            cleanupFolders.Parameters.AddWithValue("$library_id", libraryId);
            await cleanupFolders.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText =
                """
                UPDATE library_sync_state
                SET reconcile_required = 0,
                    last_reconciled_at_utc_ticks = $completed,
                    last_error = NULL
                WHERE library_id = $library_id
                  AND reconcile_generation = $generation;
                """;
            state.Parameters.AddWithValue("$completed", completedAtUtc.UtcDateTime.Ticks);
            state.Parameters.AddWithValue("$library_id", libraryId);
            state.Parameters.AddWithValue("$generation", generation);

            if (await state.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException(
                    $"Library {libraryId} reconcile generation changed while reconciliation was running.");
            }
        }

        transaction.Commit();
        return deleted;
    }

    public async Task MarkReconcileRequiredAsync(
        long libraryId,
        string? error = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO library_sync_state(
                library_id,
                reconcile_required,
                last_error)
            VALUES ($library_id, 1, $error)
            ON CONFLICT(library_id) DO UPDATE SET
                reconcile_required = 1,
                last_error = excluded.last_error;
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateUsnCheckpointAsync(
        long libraryId,
        string? journalId,
        long? nextUsn,
        bool reconcileRequired,
        DateTimeOffset? watcherStoppedAtUtc,
        string? error,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO library_sync_state(
                library_id,
                reconcile_required,
                usn_journal_id,
                next_usn,
                watcher_stopped_at_utc_ticks,
                last_error)
            VALUES (
                $library_id,
                $reconcile_required,
                $journal_id,
                $next_usn,
                $stopped,
                $error)
            ON CONFLICT(library_id) DO UPDATE SET
                reconcile_required = excluded.reconcile_required,
                usn_journal_id = excluded.usn_journal_id,
                next_usn = excluded.next_usn,
                watcher_stopped_at_utc_ticks = excluded.watcher_stopped_at_utc_ticks,
                last_error = excluded.last_error;
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue("$reconcile_required", reconcileRequired ? 1 : 0);
        command.Parameters.AddWithValue("$journal_id", (object?)journalId ?? DBNull.Value);
        command.Parameters.AddWithValue("$next_usn", (object?)nextUsn ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$stopped",
            watcherStoppedAtUtc is { } stopped
                ? stopped.UtcDateTime.Ticks
                : DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RenameAssetAsync(
        long libraryId,
        string oldRelativePath,
        AssetUpsert replacement,
        CancellationToken cancellationToken = default)
    {
        ValidateAsset(replacement);

        var oldKey = LibraryPaths.RelativePathKey(oldRelativePath);
        var newPath = LibraryPaths.NormalizeRelativePath(replacement.RelativePath);
        var newKey = LibraryPaths.RelativePathKey(newPath);
        var folderPath = LibraryPaths.FolderRelativePath(newPath);
        var folderKey = folderPath.Length == 0
            ? null
            : LibraryPaths.FolderPathKey(folderPath);
        var extension = LibraryFileTypes.GetFormat(newPath);
        var now = DateTimeOffset.UtcNow.UtcDateTime.Ticks;

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        if (!string.Equals(oldKey, newKey, StringComparison.Ordinal))
        {
            await using var removeDestination = connection.CreateCommand();
            removeDestination.Transaction = transaction;
            removeDestination.CommandText =
                """
                DELETE FROM assets
                WHERE library_id = $library_id
                  AND relative_path_key = $new_key
                  AND relative_path_key <> $old_key;
                """;
            removeDestination.Parameters.AddWithValue("$library_id", libraryId);
            removeDestination.Parameters.AddWithValue("$new_key", newKey);
            removeDestination.Parameters.AddWithValue("$old_key", oldKey);
            await removeDestination.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        long? folderId = null;
        if (folderKey is not null)
        {
            await using var folder = connection.CreateCommand();
            folder.Transaction = transaction;
            folder.CommandText =
                """
                INSERT INTO folders(
                    library_id,
                    relative_path,
                    relative_path_key,
                    created_at_utc_ticks)
                VALUES ($library_id, $path, $key, $created)
                ON CONFLICT(library_id, relative_path_key) DO UPDATE SET
                    relative_path = excluded.relative_path
                RETURNING id;
                """;
            folder.Parameters.AddWithValue("$library_id", libraryId);
            folder.Parameters.AddWithValue("$path", folderPath);
            folder.Parameters.AddWithValue("$key", folderKey);
            folder.Parameters.AddWithValue("$created", now);
            folderId = Convert.ToInt64(
                await folder.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE assets
            SET folder_id = $folder_id,
                relative_path = $new_path,
                relative_path_key = $new_key,
                file_name = $file_name,
                extension = $extension,
                source_revision =
                    CASE WHEN file_size <> $file_size
                               OR modified_at_utc_ticks <> $modified
                         THEN source_revision + 1
                         ELSE source_revision
                    END,
                width =
                    CASE WHEN file_size <> $file_size
                               OR modified_at_utc_ticks <> $modified
                         THEN $width
                         ELSE COALESCE($width, width)
                    END,
                height =
                    CASE WHEN file_size <> $file_size
                               OR modified_at_utc_ticks <> $modified
                         THEN $height
                         ELSE COALESCE($height, height)
                    END,
                format =
                    CASE WHEN file_size <> $file_size
                               OR modified_at_utc_ticks <> $modified
                         THEN $format
                         ELSE COALESCE($format, format)
                    END,
                file_size = $file_size,
                modified_at_utc_ticks = $modified,
                observed_generation =
                    CASE WHEN $observed_generation > 0
                         THEN $observed_generation
                         ELSE observed_generation
                    END,
                updated_at_utc_ticks = $updated
            WHERE library_id = $library_id
              AND relative_path_key = $old_key;
            """;
        command.Parameters.AddWithValue("$folder_id", (object?)folderId ?? DBNull.Value);
        command.Parameters.AddWithValue("$new_path", newPath);
        command.Parameters.AddWithValue("$new_key", newKey);
        command.Parameters.AddWithValue("$file_name", Path.GetFileName(newPath));
        command.Parameters.AddWithValue("$extension", extension);
        command.Parameters.AddWithValue("$file_size", replacement.FileSize);
        command.Parameters.AddWithValue("$modified", replacement.ModifiedAtUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$width", (object?)replacement.Width ?? DBNull.Value);
        command.Parameters.AddWithValue("$height", (object?)replacement.Height ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$format",
            string.IsNullOrWhiteSpace(replacement.Format)
                ? extension
                : replacement.Format.Trim());
        command.Parameters.AddWithValue(
            "$observed_generation",
            replacement.ObservationGeneration);
        command.Parameters.AddWithValue("$updated", now);
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue("$old_key", oldKey);

        var updated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        transaction.Commit();
        return updated == 1;
    }

    public async Task<bool> HasTrackedFolderAtOrBelowAsync(
        long libraryId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var pathKey = LibraryPaths.FolderPathKey(relativePath);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM folders
                WHERE library_id = $library_id
                  AND (
                      relative_path_key = $path_key
                      OR relative_path_key LIKE $path_prefix ESCAPE '\'
                  )
            );
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue("$path_key", pathKey);
        command.Parameters.AddWithValue(
            "$path_prefix",
            EscapeLike(pathKey) + "/%");

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) != 0;
    }

    private static async Task EnsureMissingFoldersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        IReadOnlyList<PreparedAsset> prepared,
        Dictionary<string, long> existingFolderIds,
        CancellationToken cancellationToken)
    {
        var missing = prepared
            .Where(static item => item.FolderKey is not null)
            .GroupBy(static item => item.FolderKey!, StringComparer.Ordinal)
            .Where(group => !existingFolderIds.ContainsKey(group.Key))
            .Select(static group => group.First())
            .ToArray();

        const int rowsPerCommand = 400;
        for (var offset = 0; offset < missing.Length; offset += rowsPerCommand)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(rowsPerCommand, missing.Length - offset);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;

            var rows = new string[count];
            command.Parameters.AddWithValue("$library_id", libraryId);
            command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.UtcDateTime.Ticks);

            for (var index = 0; index < count; index++)
            {
                var item = missing[offset + index];
                var pathName = $"$folder_path_{index}";
                var keyName = $"$folder_key_{index}";
                rows[index] = $"($library_id, {pathName}, {keyName}, $created)";
                command.Parameters.AddWithValue(pathName, item.FolderPath);
                command.Parameters.AddWithValue(keyName, item.FolderKey!);
            }

            command.CommandText =
                $"""
                INSERT INTO folders(
                    library_id, relative_path, relative_path_key, created_at_utc_ticks)
                VALUES {string.Join(", ", rows)}
                ON CONFLICT(library_id, relative_path_key) DO UPDATE SET
                    relative_path = excluded.relative_path;
                """;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ApplyForcedSourceRevisionHintsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        IReadOnlyList<PreparedAsset> prepared,
        CancellationToken cancellationToken)
    {
        Dictionary<string, PreparedAsset>? forced = null;

        foreach (var item in prepared)
        {
            if (!item.Source.ForceSourceRevision)
            {
                continue;
            }

            forced ??= new Dictionary<string, PreparedAsset>(
                StringComparer.Ordinal);
            forced[item.RelativePathKey] = item;
        }

        if (forced is null)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE assets
            SET source_revision = source_revision + 1,
                width = NULL,
                height = NULL,
                format = NULL,
                updated_at_utc_ticks = $updated
            WHERE library_id = $library_id
              AND relative_path_key = $path_key
              AND file_size = $file_size
              AND modified_at_utc_ticks = $modified;
            """;

        var updated = command.Parameters.Add("$updated", SqliteType.Integer);
        var library = command.Parameters.Add("$library_id", SqliteType.Integer);
        var path = command.Parameters.Add("$path_key", SqliteType.Text);
        var size = command.Parameters.Add("$file_size", SqliteType.Integer);
        var modified = command.Parameters.Add("$modified", SqliteType.Integer);
        library.Value = libraryId;
        command.Prepare();

        foreach (var item in forced.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            updated.Value = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
            path.Value = item.RelativePathKey;
            size.Value = item.Source.FileSize;
            modified.Value = item.Source.ModifiedAtUtc.UtcDateTime.Ticks;
            await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task UpsertPreparedAssetsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        IReadOnlyList<PreparedAsset> prepared,
        Dictionary<string, long> folderIds,
        CancellationToken cancellationToken)
    {
        const int rowsPerCommand = 64;
        var fullRows = prepared.Count / rowsPerCommand * rowsPerCommand;

        if (fullRows > 0)
        {
            await using var command = CreateAssetUpsertCommand(
                connection,
                transaction,
                libraryId,
                rowsPerCommand,
                out var bindings);

            for (var offset = 0; offset < fullRows; offset += rowsPerCommand)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BindAssetUpsertCommand(
                    command,
                    bindings,
                    prepared,
                    offset,
                    rowsPerCommand,
                    folderIds);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var remaining = prepared.Count - fullRows;
        if (remaining > 0)
        {
            await using var command = CreateAssetUpsertCommand(
                connection,
                transaction,
                libraryId,
                remaining,
                out var bindings);

            BindAssetUpsertCommand(
                command,
                bindings,
                prepared,
                fullRows,
                remaining,
                folderIds);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static SqliteCommand CreateAssetUpsertCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        int rowCount,
        out AssetCommandBinding[] bindings)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.Add("$now", SqliteType.Integer);

        bindings = new AssetCommandBinding[rowCount];
        var rows = new string[rowCount];

        for (var index = 0; index < rowCount; index++)
        {
            var suffix = index.ToString(CultureInfo.InvariantCulture);
            var folderId = command.Parameters.Add("$folder_id_" + suffix, SqliteType.Integer);
            var relativePath = command.Parameters.Add("$relative_path_" + suffix, SqliteType.Text);
            var relativePathKey = command.Parameters.Add("$relative_path_key_" + suffix, SqliteType.Text);
            var fileName = command.Parameters.Add("$file_name_" + suffix, SqliteType.Text);
            var extension = command.Parameters.Add("$extension_" + suffix, SqliteType.Text);
            var fileSize = command.Parameters.Add("$file_size_" + suffix, SqliteType.Integer);
            var modifiedAt = command.Parameters.Add("$modified_at_" + suffix, SqliteType.Integer);
            var width = command.Parameters.Add("$width_" + suffix, SqliteType.Integer);
            var height = command.Parameters.Add("$height_" + suffix, SqliteType.Integer);
            var format = command.Parameters.Add("$format_" + suffix, SqliteType.Text);
            var observationGeneration = command.Parameters.Add(
                "$observed_generation_" + suffix,
                SqliteType.Integer);

            bindings[index] = new AssetCommandBinding(
                folderId,
                relativePath,
                relativePathKey,
                fileName,
                extension,
                fileSize,
                modifiedAt,
                width,
                height,
                format,
                observationGeneration);

            rows[index] =
                $"($library_id, {folderId.ParameterName}, {relativePath.ParameterName}, " +
                $"{relativePathKey.ParameterName}, {fileName.ParameterName}, {extension.ParameterName}, " +
                $"{fileSize.ParameterName}, {modifiedAt.ParameterName}, 1, " +
                $"{width.ParameterName}, {height.ParameterName}, {format.ParameterName}, " +
                $"{observationGeneration.ParameterName}, $now, $now)";
        }

        command.CommandText =
            $"""
            INSERT INTO assets(
                library_id, folder_id,
                relative_path, relative_path_key,
                file_name, extension,
                file_size, modified_at_utc_ticks,
                source_revision,
                width, height, format,
                observed_generation,
                created_at_utc_ticks, updated_at_utc_ticks)
            VALUES {string.Join(", ", rows)}
            ON CONFLICT(library_id, relative_path_key) DO UPDATE SET
                folder_id = excluded.folder_id,
                relative_path = excluded.relative_path,
                file_name = excluded.file_name,
                extension = excluded.extension,
                source_revision =
                    CASE WHEN assets.file_size <> excluded.file_size
                               OR assets.modified_at_utc_ticks <> excluded.modified_at_utc_ticks
                         THEN assets.source_revision + 1
                         ELSE assets.source_revision
                    END,
                width =
                    CASE WHEN assets.file_size <> excluded.file_size
                               OR assets.modified_at_utc_ticks <> excluded.modified_at_utc_ticks
                         THEN excluded.width
                         ELSE COALESCE(excluded.width, assets.width)
                    END,
                height =
                    CASE WHEN assets.file_size <> excluded.file_size
                               OR assets.modified_at_utc_ticks <> excluded.modified_at_utc_ticks
                         THEN excluded.height
                         ELSE COALESCE(excluded.height, assets.height)
                    END,
                format =
                    CASE WHEN assets.file_size <> excluded.file_size
                               OR assets.modified_at_utc_ticks <> excluded.modified_at_utc_ticks
                         THEN excluded.format
                         ELSE COALESCE(excluded.format, assets.format)
                    END,
                file_size = excluded.file_size,
                modified_at_utc_ticks = excluded.modified_at_utc_ticks,
                observed_generation =
                    CASE WHEN excluded.observed_generation > 0
                         THEN excluded.observed_generation
                         ELSE assets.observed_generation
                    END,
                updated_at_utc_ticks = excluded.updated_at_utc_ticks;
            """;

        command.Prepare();
        return command;
    }

    private static void BindAssetUpsertCommand(
        SqliteCommand command,
        IReadOnlyList<AssetCommandBinding> bindings,
        IReadOnlyList<PreparedAsset> prepared,
        int offset,
        int count,
        Dictionary<string, long> folderIds)
    {
        command.Parameters["$now"].Value = DateTimeOffset.UtcNow.UtcDateTime.Ticks;

        for (var index = 0; index < count; index++)
        {
            var item = prepared[offset + index];
            var binding = bindings[index];

            binding.FolderId.Value =
                item.FolderKey is null ? DBNull.Value : folderIds[item.FolderKey];
            binding.RelativePath.Value = item.RelativePath;
            binding.RelativePathKey.Value = item.RelativePathKey;
            binding.FileName.Value = item.FileName;
            binding.Extension.Value = item.Extension;
            binding.FileSize.Value = item.Source.FileSize;
            binding.ModifiedAt.Value = item.Source.ModifiedAtUtc.UtcDateTime.Ticks;
            binding.Width.Value =
                item.Source.Width.HasValue ? item.Source.Width.Value : DBNull.Value;
            binding.Height.Value =
                item.Source.Height.HasValue ? item.Source.Height.Value : DBNull.Value;
            binding.Format.Value =
                string.IsNullOrWhiteSpace(item.Source.Format)
                    ? item.Extension
                    : item.Source.Format.Trim();
            binding.ObservationGeneration.Value = item.Source.ObservationGeneration;
        }
    }

    private static async Task<Dictionary<string, long>> LoadExistingFolderIdsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        IReadOnlyList<PreparedAsset> prepared,
        CancellationToken cancellationToken)
    {
        var keys = prepared
            .Where(static item => item.FolderKey is not null)
            .Select(static item => item.FolderKey!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var result = new Dictionary<string, long>(keys.Length, StringComparer.Ordinal);
        const int chunkSize = 500;

        for (var offset = 0; offset < keys.Length; offset += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(chunkSize, keys.Length - offset);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;

            var parameterNames = new string[count];
            for (var index = 0; index < count; index++)
            {
                var parameterName = $"$key{index}";
                parameterNames[index] = parameterName;
                command.Parameters.AddWithValue(parameterName, keys[offset + index]);
            }

            command.Parameters.AddWithValue("$library_id", libraryId);
            command.CommandText =
                $"SELECT relative_path_key, id FROM folders WHERE library_id = $library_id AND relative_path_key IN ({string.Join(", ", parameterNames)});";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result[reader.GetString(0)] = reader.GetInt64(1);
            }
        }

        return result;
    }

    private sealed record AssetCommandBinding(
        SqliteParameter FolderId,
        SqliteParameter RelativePath,
        SqliteParameter RelativePathKey,
        SqliteParameter FileName,
        SqliteParameter Extension,
        SqliteParameter FileSize,
        SqliteParameter ModifiedAt,
        SqliteParameter Width,
        SqliteParameter Height,
        SqliteParameter Format,
        SqliteParameter ObservationGeneration);

    private sealed record PreparedAsset(
        AssetUpsert Source,
        string RelativePath,
        string RelativePathKey,
        string FileName,
        string Extension,
        string FolderPath,
        string? FolderKey);

    private static string EscapeLike(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static void ValidateAsset(AssetUpsert asset)
    {
        if (asset.FileSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(asset), "Asset file size cannot be negative.");
        }

        if (asset.Width is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(asset), "Asset width must be positive when present.");
        }

        if (asset.Height is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(asset), "Asset height must be positive when present.");
        }
    }

    private static void EnsureSingleLibraryUpdated(int affectedRows, long libraryId)
    {
        if (affectedRows != 1)
        {
            throw new InvalidOperationException($"Library {libraryId} does not exist.");
        }
    }

    private static LibraryInfo ReadLibrary(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            FromTicks(reader.GetInt64(3)),
            FromTicks(reader.GetInt64(4)),
            reader.IsDBNull(5) ? null : FromTicks(reader.GetInt64(5)),
            reader.IsDBNull(6) ? null : FromTicks(reader.GetInt64(6)),
            (LibraryScanState)reader.GetInt32(7));

    private static LibrarySyncState ReadSyncState(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt32(2) != 0,
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.IsDBNull(5) ? null : FromTicks(reader.GetInt64(5)),
            reader.IsDBNull(6) ? null : FromTicks(reader.GetInt64(6)),
            reader.IsDBNull(7) ? null : reader.GetString(7));

    private static AssetInfo ReadAsset(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt64(6),
            FromTicks(reader.GetInt64(7)),
            reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetInt32(13),
            reader.IsDBNull(14) ? null : reader.GetInt32(14),
            reader.IsDBNull(15) ? null : reader.GetInt32(15) != 0);

    private static DateTimeOffset FromTicks(long ticks) =>
        new(new DateTime(ticks, DateTimeKind.Utc));
}
