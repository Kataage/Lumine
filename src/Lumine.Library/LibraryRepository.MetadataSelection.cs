using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed partial class LibraryRepository
{
    public async Task<AssetUserMetadataSelectionSummary>
        GetUserMetadataSelectionSummaryAsync(
            long libraryId,
            IReadOnlyList<long> assetIds,
            CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentNullException.ThrowIfNull(assetIds);

        var ids =
            assetIds
                .Distinct()
                .ToArray();

        if (ids.Length == 0)
        {
            throw new ArgumentException(
                "At least one asset is required.",
                nameof(assetIds));
        }

        if (ids.Length > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(assetIds),
                "Metadata summaries are limited to 10,000 selected assets.");
        }

        foreach (var id in ids)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        }

        await using var connection =
            await _database.OpenConnectionAsync(
                cancellationToken).ConfigureAwait(false);
        using var transaction =
            connection.BeginTransaction();

        await using (var create =
            connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText =
                """
                CREATE TEMP TABLE IF NOT EXISTS
                    lumine_metadata_selection(
                        asset_id INTEGER PRIMARY KEY
                    );
                DELETE FROM lumine_metadata_selection;
                """;
            await create.ExecuteNonQueryAsync(
                cancellationToken).ConfigureAwait(false);
        }

        await using (var insert =
            connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO lumine_metadata_selection(asset_id)
                VALUES($asset_id);
                """;
            var parameter =
                insert.Parameters.Add(
                    "$asset_id",
                    SqliteType.Integer);
            insert.Prepare();

            foreach (var id in ids)
            {
                parameter.Value = id;
                await insert.ExecuteNonQueryAsync(
                    cancellationToken).ConfigureAwait(false);
            }
        }

        await using (var membership =
            connection.CreateCommand())
        {
            membership.Transaction = transaction;
            membership.CommandText =
                """
                SELECT COUNT(*)
                FROM lumine_metadata_selection AS s
                INNER JOIN assets AS a
                  ON a.id = s.asset_id
                 AND a.library_id = $library_id;
                """;
            membership.Parameters.AddWithValue(
                "$library_id",
                libraryId);

            var owned =
                Convert.ToInt32(
                    await membership.ExecuteScalarAsync(
                        cancellationToken)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture);
            if (owned != ids.Length)
            {
                throw new InvalidOperationException(
                    "One or more selected assets do not belong to the requested library.");
            }
        }

        int ratingDistinct;
        int? rating;
        int favoriteDistinct;
        bool favorite;
        int statusDistinct;
        string? status;
        int colorDistinct;
        string? color;
        int notesDistinct;
        int tagsDistinct;

        await using (var aggregate =
            connection.CreateCommand())
        {
            aggregate.Transaction = transaction;
            aggregate.CommandText =
                """
                SELECT
                    COUNT(DISTINCT COALESCE(
                        CAST(um.rating AS TEXT),
                        char(30))),
                    MIN(um.rating),
                    COUNT(DISTINCT CAST(
                        COALESCE(um.favorite, 0)
                        AS TEXT)),
                    MIN(COALESCE(um.favorite, 0)),
                    COUNT(DISTINCT COALESCE(
                        um.status_label,
                        char(30))),
                    MIN(um.status_label),
                    COUNT(DISTINCT COALESCE(
                        um.color_label,
                        char(30))),
                    MIN(um.color_label),
                    COUNT(DISTINCT COALESCE(
                        um.notes,
                        '')),
                    COUNT(DISTINCT COALESCE((
                        SELECT group_concat(name_key, char(31))
                        FROM (
                            SELECT t.name_key AS name_key
                            FROM asset_tags AS at
                            INNER JOIN tags AS t
                              ON t.id = at.tag_id
                            WHERE at.asset_id = a.id
                            ORDER BY t.name_key
                        )
                    ), ''))
                FROM lumine_metadata_selection AS s
                INNER JOIN assets AS a
                  ON a.id = s.asset_id
                 AND a.library_id = $library_id
                LEFT JOIN asset_user_metadata AS um
                  ON um.asset_id = a.id;
                """;
            aggregate.Parameters.AddWithValue(
                "$library_id",
                libraryId);

            await using var reader =
                await aggregate.ExecuteReaderAsync(
                    cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(
                    cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "Selected metadata summary did not return a row.");
            }

            ratingDistinct = reader.GetInt32(0);
            rating =
                reader.IsDBNull(1)
                    ? null
                    : reader.GetInt32(1);
            favoriteDistinct = reader.GetInt32(2);
            favorite = reader.GetInt32(3) != 0;
            statusDistinct = reader.GetInt32(4);
            status =
                reader.IsDBNull(5)
                    ? null
                    : reader.GetString(5);
            colorDistinct = reader.GetInt32(6);
            color =
                reader.IsDBNull(7)
                    ? null
                    : reader.GetString(7);
            notesDistinct = reader.GetInt32(8);
            tagsDistinct = reader.GetInt32(9);
        }

        var commonTags =
            new List<string>();
        await using (var tags =
            connection.CreateCommand())
        {
            tags.Transaction = transaction;
            tags.CommandText =
                """
                SELECT
                    t.name
                FROM lumine_metadata_selection AS s
                INNER JOIN asset_tags AS at
                  ON at.asset_id = s.asset_id
                INNER JOIN tags AS t
                  ON t.id = at.tag_id
                 AND t.library_id = $library_id
                GROUP BY t.id, t.name, t.name_key
                HAVING COUNT(DISTINCT s.asset_id) = $selected_count
                ORDER BY t.name_key;
                """;
            tags.Parameters.AddWithValue(
                "$library_id",
                libraryId);
            tags.Parameters.AddWithValue(
                "$selected_count",
                ids.Length);

            await using var reader =
                await tags.ExecuteReaderAsync(
                    cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(
                       cancellationToken).ConfigureAwait(false))
            {
                commonTags.Add(reader.GetString(0));
            }
        }

        transaction.Commit();

        return new AssetUserMetadataSelectionSummary(
            ids.Length,
            ratingDistinct > 1,
            ratingDistinct == 1
                ? rating
                : null,
            favoriteDistinct > 1,
            favorite,
            statusDistinct > 1,
            statusDistinct == 1
                ? status
                : null,
            colorDistinct > 1,
            colorDistinct == 1
                ? color
                : null,
            notesDistinct > 1,
            tagsDistinct > 1,
            commonTags);
    }
}
