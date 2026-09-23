using System.Globalization;
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
                last_scan_completed_at_utc_ticks;
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
                last_scan_completed_at_utc_ticks
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
                id, asset_key, library_id, folder_id,
                relative_path, file_name, extension,
                file_size, modified_at_utc_ticks,
                width, height, format
            FROM assets
            WHERE library_id = $library_id
              AND relative_path_key = $path_key;
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue("$path_key", pathKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadAsset(reader)
            : null;
    }

    public async Task<int> UpsertAssetsAsync(
        long libraryId,
        IReadOnlyList<AssetUpsert> assets,
        CancellationToken cancellationToken = default)
    {
        await using var session = await OpenIngestSessionAsync(libraryId, cancellationToken).ConfigureAwait(false);
        return await session.WriteBatchAsync(assets, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LibraryIngestSession> OpenIngestSessionAsync(
        long libraryId,
        CancellationToken cancellationToken = default)
    {
        var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return new LibraryIngestSession(this, connection, libraryId);
    }

    internal async Task<int> UpsertAssetsOnConnectionAsync(
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

        var folderIds = await LoadExistingFolderIdsAsync(
            connection,
            transaction,
            libraryId,
            prepared,
            cancellationToken).ConfigureAwait(false);

        if (folderIds.Count < prepared.Count)
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

    private static async Task EnsureMissingFoldersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        IReadOnlyList<PreparedAsset> prepared,
        IReadOnlyDictionary<string, long> existingFolderIds,
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

    private static async Task UpsertPreparedAssetsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        IReadOnlyList<PreparedAsset> prepared,
        IReadOnlyDictionary<string, long> folderIds,
        CancellationToken cancellationToken)
    {
        // 64 rows * 15 bound values = 960 variables, which stays below
        // SQLite's historical 999-variable floor while reducing managed/native
        // command crossings by roughly two orders of magnitude.
        const int rowsPerCommand = 64;

        for (var offset = 0; offset < prepared.Count; offset += rowsPerCommand)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(rowsPerCommand, prepared.Count - offset);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;

            var rows = new string[count];
            var nowTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks;

            for (var index = 0; index < count; index++)
            {
                var item = prepared[offset + index];
                var suffix = index.ToString(CultureInfo.InvariantCulture);
                var assetKey = "$asset_key_" + suffix;
                var folderId = "$folder_id_" + suffix;
                var relativePath = "$relative_path_" + suffix;
                var relativePathKey = "$relative_path_key_" + suffix;
                var fileName = "$file_name_" + suffix;
                var extension = "$extension_" + suffix;
                var fileSize = "$file_size_" + suffix;
                var modifiedAt = "$modified_at_" + suffix;
                var width = "$width_" + suffix;
                var height = "$height_" + suffix;
                var format = "$format_" + suffix;
                var createdAt = "$created_at_" + suffix;
                var updatedAt = "$updated_at_" + suffix;

                rows[index] =
                    $"({assetKey}, $library_id, {folderId}, {relativePath}, {relativePathKey}, " +
                    $"{fileName}, {extension}, {fileSize}, {modifiedAt}, {width}, {height}, {format}, " +
                    $"{createdAt}, {updatedAt})";

                command.Parameters.AddWithValue(assetKey, Guid.CreateVersion7().ToString("N"));
                command.Parameters.AddWithValue(
                    folderId,
                    item.FolderKey is null ? DBNull.Value : folderIds[item.FolderKey]);
                command.Parameters.AddWithValue(relativePath, item.RelativePath);
                command.Parameters.AddWithValue(relativePathKey, item.RelativePathKey);
                command.Parameters.AddWithValue(fileName, item.FileName);
                command.Parameters.AddWithValue(extension, item.Extension);
                command.Parameters.AddWithValue(fileSize, item.Source.FileSize);
                command.Parameters.AddWithValue(modifiedAt, item.Source.ModifiedAtUtc.UtcDateTime.Ticks);
                command.Parameters.AddWithValue(width, item.Source.Width.HasValue ? item.Source.Width.Value : DBNull.Value);
                command.Parameters.AddWithValue(height, item.Source.Height.HasValue ? item.Source.Height.Value : DBNull.Value);
                command.Parameters.AddWithValue(
                    format,
                    string.IsNullOrWhiteSpace(item.Source.Format)
                        ? item.Extension
                        : item.Source.Format.Trim());
                command.Parameters.AddWithValue(createdAt, nowTicks);
                command.Parameters.AddWithValue(updatedAt, nowTicks);
            }

            command.Parameters.AddWithValue("$library_id", libraryId);
            command.CommandText =
                $"""
                INSERT INTO assets(
                    asset_key, library_id, folder_id,
                    relative_path, relative_path_key,
                    file_name, extension,
                    file_size, modified_at_utc_ticks,
                    width, height, format,
                    created_at_utc_ticks, updated_at_utc_ticks)
                VALUES {string.Join(", ", rows)}
                ON CONFLICT(library_id, relative_path_key) DO UPDATE SET
                    folder_id = excluded.folder_id,
                    relative_path = excluded.relative_path,
                    file_name = excluded.file_name,
                    extension = excluded.extension,
                    file_size = excluded.file_size,
                    modified_at_utc_ticks = excluded.modified_at_utc_ticks,
                    width = COALESCE(excluded.width, assets.width),
                    height = COALESCE(excluded.height, assets.height),
                    format = COALESCE(excluded.format, assets.format),
                    updated_at_utc_ticks = excluded.updated_at_utc_ticks;
                """;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
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
                  id, asset_key, library_id, folder_id,
                  relative_path, file_name, extension,
                  file_size, modified_at_utc_ticks,
                  width, height, format
              FROM assets
              WHERE library_id = $library_id
              ORDER BY modified_at_utc_ticks DESC, id DESC
              LIMIT $limit;
              """
            : """
              SELECT
                  id, asset_key, library_id, folder_id,
                  relative_path, file_name, extension,
                  file_size, modified_at_utc_ticks,
                  width, height, format
              FROM assets
              WHERE library_id = $library_id
                AND (
                    modified_at_utc_ticks < $cursor_modified
                    OR (
                        modified_at_utc_ticks = $cursor_modified
                        AND id < $cursor_id
                    )
                )
              ORDER BY modified_at_utc_ticks DESC, id DESC
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

    public async Task MarkScanCompletedAsync(
        long libraryId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE libraries
            SET last_scan_completed_at_utc_ticks = $completed,
                updated_at_utc_ticks = $completed
            WHERE id = $library_id;
            """;
        command.Parameters.AddWithValue("$completed", completedAtUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$library_id", libraryId);

        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException($"Library {libraryId} does not exist.");
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

    private sealed record PreparedAsset(
        AssetUpsert Source,
        string RelativePath,
        string RelativePathKey,
        string FileName,
        string Extension,
        string FolderPath,
        string? FolderKey);

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

    private static LibraryInfo ReadLibrary(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            FromTicks(reader.GetInt64(3)),
            FromTicks(reader.GetInt64(4)),
            reader.IsDBNull(5) ? null : FromTicks(reader.GetInt64(5)));

    private static AssetInfo ReadAsset(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt64(7),
            FromTicks(reader.GetInt64(8)),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetString(11));

    private static DateTimeOffset FromTicks(long ticks) =>
        new(new DateTime(ticks, DateTimeKind.Utc));
}
