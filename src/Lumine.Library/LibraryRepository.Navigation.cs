using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed partial class LibraryRepository
{
    public async Task<IReadOnlyList<LibraryCatalogItem>> ListLibrariesAsync(
        bool includeDisabled = true,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                l.id,
                l.name,
                l.root_path,
                l.is_enabled,
                l.scan_state,
                (
                    SELECT COUNT(*)
                    FROM assets AS a
                    WHERE a.library_id = l.id
                ) AS asset_count,
                l.last_scan_completed_at_utc_ticks,
                s.last_error
            FROM libraries AS l
            LEFT JOIN library_sync_state AS s
              ON s.library_id = l.id
            WHERE $include_disabled = 1
               OR l.is_enabled = 1
            ORDER BY
                l.is_enabled DESC,
                l.updated_at_utc_ticks DESC,
                l.name COLLATE NOCASE ASC,
                l.id ASC;
            """;
        command.Parameters.AddWithValue(
            "$include_disabled",
            includeDisabled ? 1 : 0);

        var items = new List<LibraryCatalogItem>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            items.Add(
                new LibraryCatalogItem(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3) != 0,
                    (LibraryScanState)reader.GetInt32(4),
                    reader.GetInt64(5),
                    reader.IsDBNull(6)
                        ? null
                        : new DateTimeOffset(
                            new DateTime(
                                reader.GetInt64(6),
                                DateTimeKind.Utc)),
                    reader.IsDBNull(7)
                        ? null
                        : reader.GetString(7)));
        }

        return items;
    }

    public async Task<bool> SetLibraryEnabledAsync(
        long libraryId,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);

        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE libraries
            SET is_enabled = $enabled,
                updated_at_utc_ticks = $updated
            WHERE id = $library_id;
            """;
        command.Parameters.AddWithValue(
            "$enabled",
            isEnabled ? 1 : 0);
        command.Parameters.AddWithValue(
            "$updated",
            DateTimeOffset.UtcNow.UtcDateTime.Ticks);
        command.Parameters.AddWithValue(
            "$library_id",
            libraryId);

        return await command.ExecuteNonQueryAsync(cancellationToken)
                   .ConfigureAwait(false) == 1;
    }

    public async Task<bool> RemoveLibraryRegistrationAsync(
        long libraryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);

        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM libraries
            WHERE id = $library_id;
            """;
        command.Parameters.AddWithValue(
            "$library_id",
            libraryId);

        return await command.ExecuteNonQueryAsync(cancellationToken)
                   .ConfigureAwait(false) == 1;
    }

    public async Task<IReadOnlyList<LibraryFolderInfo>> ListFoldersAsync(
        long libraryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);

        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                f.id,
                f.relative_path,
                COUNT(a.id) AS direct_asset_count
            FROM folders AS f
            LEFT JOIN assets AS a
              ON a.folder_id = f.id
            WHERE f.library_id = $library_id
            GROUP BY f.id, f.relative_path, f.relative_path_key
            ORDER BY f.relative_path_key ASC;
            """;
        command.Parameters.AddWithValue(
            "$library_id",
            libraryId);

        var items = new List<LibraryFolderInfo>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            var path = reader.GetString(1);
            var depth =
                path.Count(static character => character == '/')
                + 1;
            items.Add(
                new LibraryFolderInfo(
                    reader.GetInt64(0),
                    path,
                    depth,
                    reader.GetInt64(2)));
        }

        return items;
    }

    public async Task<IReadOnlyList<LibraryTagInfo>> ListTagsAsync(
        long libraryId,
        string? searchText = null,
        int limit = 512,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 4096);

        var search =
            string.IsNullOrWhiteSpace(searchText)
                ? null
                : searchText.Trim();

        if (search is { Length: > 128 })
        {
            throw new ArgumentException(
                "Tag search text may not exceed 128 characters.",
                nameof(searchText));
        }

        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                t.id,
                t.name,
                t.color,
                COUNT(at.asset_id) AS asset_count
            FROM tags AS t
            LEFT JOIN asset_tags AS at
              ON at.tag_id = t.id
            WHERE t.library_id = $library_id
              AND (
                    $search IS NULL
                    OR instr(
                        lower(t.name),
                        lower($search)
                    ) > 0
                  )
            GROUP BY t.id, t.name, t.color, t.name_key
            ORDER BY
                asset_count DESC,
                t.name_key ASC,
                t.id ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue(
            "$library_id",
            libraryId);
        command.Parameters.AddWithValue(
            "$search",
            (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$limit",
            limit);

        var items = new List<LibraryTagInfo>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            items.Add(
                new LibraryTagInfo(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3)));
        }

        return items;
    }

    public async Task<LibraryTagInfo> CreateTagAsync(
        long libraryId,
        string name,
        string color,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);

        var normalized =
            NormalizeTags([name]);
        if (normalized.Length != 1)
        {
            throw new ArgumentException(
                "Tag name is required.",
                nameof(name));
        }

        var tag = normalized[0];
        var normalizedColor =
            NormalizeTagColor(color);
        var now =
            DateTimeOffset.UtcNow.UtcDateTime.Ticks;

        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
        await using var command =
            connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO tags(
                library_id,
                name,
                name_key,
                color,
                created_at_utc_ticks)
            VALUES(
                $library_id,
                $name,
                $name_key,
                $color,
                $created)
            ON CONFLICT(library_id, name_key) DO UPDATE SET
                name = excluded.name,
                color = excluded.color
            RETURNING id, name, color;
            """;
        command.Parameters.AddWithValue(
            "$library_id",
            libraryId);
        command.Parameters.AddWithValue(
            "$name",
            tag.Name);
        command.Parameters.AddWithValue(
            "$name_key",
            tag.Key);
        command.Parameters.AddWithValue(
            "$color",
            normalizedColor);
        command.Parameters.AddWithValue(
            "$created",
            now);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Tag creation returned no row.");
        }

        return new LibraryTagInfo(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            0);
    }

    public async Task<bool> DeleteTagAsync(
        long libraryId,
        long tagId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tagId);

        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
        using var transaction =
            connection.BeginTransaction();

        var affectedAssetIds =
            new List<long>();
        await using (var affected =
            connection.CreateCommand())
        {
            affected.Transaction = transaction;
            affected.CommandText =
                """
                SELECT at.asset_id
                FROM asset_tags AS at
                INNER JOIN tags AS t
                  ON t.id = at.tag_id
                WHERE t.library_id = $library_id
                  AND t.id = $tag_id
                ORDER BY at.asset_id;
                """;
            affected.Parameters.AddWithValue(
                "$library_id",
                libraryId);
            affected.Parameters.AddWithValue(
                "$tag_id",
                tagId);

            await using var reader =
                await affected.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                affectedAssetIds.Add(
                    reader.GetInt64(0));
            }
        }

        int deleted;
        await using (var command =
            connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                DELETE FROM tags
                WHERE library_id = $library_id
                  AND id = $tag_id;
                """;
            command.Parameters.AddWithValue(
                "$library_id",
                libraryId);
            command.Parameters.AddWithValue(
                "$tag_id",
                tagId);
            deleted =
                await command.ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);
        }

        if (deleted != 0)
        {
            foreach (var assetId in affectedAssetIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ReindexAssetOnConnectionAsync(
                    connection,
                    transaction,
                    libraryId,
                    assetId,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        transaction.Commit();
        return deleted != 0;
    }

    private static string NormalizeTagColor(
        string? color)
    {
        var value =
            string.IsNullOrWhiteSpace(color)
                ? "#6366f1"
                : color.Trim();

        if (value.Length is not (4 or 7 or 9)
            || value[0] != '#'
            || !value
                .AsSpan(1)
                .ToString()
                .All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "Tag color must be #RGB, #RRGGBB, or #RRGGBBAA.",
                nameof(color));
        }

        return value.ToLowerInvariant();
    }

    public async Task<LibraryBrowseFacets> GetBrowseFacetsAsync(
        long libraryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);

        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

        static async Task<IReadOnlyList<string>> ReadValuesAsync(
            SqliteConnection connection,
            long libraryId,
            string column,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                SELECT DISTINCT um.{column}
                FROM asset_user_metadata AS um
                INNER JOIN assets AS a
                  ON a.id = um.asset_id
                WHERE a.library_id = $library_id
                  AND um.{column} IS NOT NULL
                  AND trim(um.{column}) <> ''
                ORDER BY um.{column} COLLATE NOCASE ASC
                LIMIT 256;
                """;
            command.Parameters.AddWithValue(
                "$library_id",
                libraryId);

            var values = new List<string>();
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                values.Add(reader.GetString(0));
            }

            return values;
        }

        var statuses =
            await ReadValuesAsync(
                connection,
                libraryId,
                "status_label",
                cancellationToken)
                .ConfigureAwait(false);
        var colors =
            await ReadValuesAsync(
                connection,
                libraryId,
                "color_label",
                cancellationToken)
                .ConfigureAwait(false);

        return new LibraryBrowseFacets(
            statuses,
            colors);
    }
}
