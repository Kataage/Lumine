using System.Globalization;
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
            GROUP BY t.id, t.name, t.name_key
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
                    reader.GetInt64(2)));
        }

        return items;
    }
}
