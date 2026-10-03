using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed partial class LibraryRepository
{
    private const int MaxSearchTextLength = 256;
    private const int MaxNotesLength = 16_384;
    private const int MaxTagsPerAsset = 128;

    private const string SearchHaystackSql =
        """
        lower(
            a.file_name || char(31) ||
            a.relative_path || char(31) ||
            COALESCE(um.notes, '') || char(31) ||
            COALESCE((
                SELECT group_concat(name, ' ')
                FROM (
                    SELECT t.name AS name
                    FROM asset_tags AS at
                    INNER JOIN tags AS t ON t.id = at.tag_id
                    WHERE at.asset_id = a.id
                    ORDER BY t.name_key ASC
                )
            ), '')
        )
        """;

    public async Task<AssetUserMetadata?> GetUserMetadataAsync(
        long libraryId,
        long assetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(assetId);

        await using var connection = await _database.OpenConnectionAsync(
            cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                a.id,
                um.rating,
                COALESCE(um.favorite, 0),
                COALESCE(um.notes, ''),
                um.status_label,
                um.color_label,
                COALESCE((
                    SELECT group_concat(name, char(31))
                    FROM (
                        SELECT t.name AS name
                        FROM asset_tags AS at
                        INNER JOIN tags AS t ON t.id = at.tag_id
                        WHERE at.asset_id = a.id
                        ORDER BY t.name_key ASC
                    )
                ), '')
            FROM assets AS a
            LEFT JOIN asset_user_metadata AS um
              ON um.asset_id = a.id
            WHERE a.library_id = $library_id
              AND a.id = $asset_id;
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue("$asset_id", assetId);

        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var tagsText = reader.GetString(6);
        IReadOnlyList<string> tags = tagsText.Length == 0
            ? Array.Empty<string>()
            : tagsText.Split(
                (char)31,
                StringSplitOptions.RemoveEmptyEntries);

        return new AssetUserMetadata(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.GetInt32(2) != 0,
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            tags);
    }

    public async Task<AssetUserMetadata> SetUserMetadataAsync(
        long libraryId,
        long assetId,
        AssetUserMetadataUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(assetId);
        ArgumentNullException.ThrowIfNull(update);

        ValidateUserMetadata(update);
        var tags = NormalizeTags(update.Tags);
        var notes = update.Notes ?? string.Empty;
        var statusLabel = NormalizeOptionalLabel(
            update.StatusLabel,
            nameof(update.StatusLabel));
        var colorLabel = NormalizeOptionalLabel(
            update.ColorLabel,
            nameof(update.ColorLabel));
        var now = DateTimeOffset.UtcNow.UtcDateTime.Ticks;

        await using var connection = await _database.OpenConnectionAsync(
            cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        await using (var exists = connection.CreateCommand())
        {
            exists.Transaction = transaction;
            exists.CommandText =
                """
                SELECT EXISTS(
                    SELECT 1
                    FROM assets
                    WHERE library_id = $library_id
                      AND id = $asset_id
                );
                """;
            exists.Parameters.AddWithValue("$library_id", libraryId);
            exists.Parameters.AddWithValue("$asset_id", assetId);

            if (Convert.ToInt32(
                    await exists.ExecuteScalarAsync(cancellationToken)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture) == 0)
            {
                throw new InvalidOperationException(
                    $"Asset {assetId} does not belong to library {libraryId}.");
            }
        }

        await using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = transaction;
            metadata.CommandText =
                """
                INSERT INTO asset_user_metadata(
                    asset_id, rating, favorite, notes,
                    status_label, color_label, updated_at_utc_ticks)
                VALUES(
                    $asset_id, $rating, $favorite, $notes,
                    $status_label, $color_label, $updated)
                ON CONFLICT(asset_id) DO UPDATE SET
                    rating = excluded.rating,
                    favorite = excluded.favorite,
                    notes = excluded.notes,
                    status_label = excluded.status_label,
                    color_label = excluded.color_label,
                    updated_at_utc_ticks = excluded.updated_at_utc_ticks;
                """;
            metadata.Parameters.AddWithValue("$asset_id", assetId);
            metadata.Parameters.AddWithValue(
                "$rating",
                update.Rating.HasValue
                    ? update.Rating.Value
                    : DBNull.Value);
            metadata.Parameters.AddWithValue(
                "$favorite",
                update.Favorite ? 1 : 0);
            metadata.Parameters.AddWithValue("$notes", notes);
            metadata.Parameters.AddWithValue(
                "$status_label",
                (object?)statusLabel ?? DBNull.Value);
            metadata.Parameters.AddWithValue(
                "$color_label",
                (object?)colorLabel ?? DBNull.Value);
            metadata.Parameters.AddWithValue("$updated", now);
            await metadata.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await using (var clearTags = connection.CreateCommand())
        {
            clearTags.Transaction = transaction;
            clearTags.CommandText =
                """
                DELETE FROM asset_tags
                WHERE asset_id = $asset_id;
                """;
            clearTags.Parameters.AddWithValue("$asset_id", assetId);
            await clearTags.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var tag in tags)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long tagId;
            await using (var upsertTag = connection.CreateCommand())
            {
                upsertTag.Transaction = transaction;
                upsertTag.CommandText =
                    """
                    INSERT INTO tags(
                        library_id, name, name_key, created_at_utc_ticks)
                    VALUES(
                        $library_id, $name, $name_key, $created)
                    ON CONFLICT(library_id, name_key) DO UPDATE SET
                        name = excluded.name
                    RETURNING id;
                    """;
                upsertTag.Parameters.AddWithValue("$library_id", libraryId);
                upsertTag.Parameters.AddWithValue("$name", tag.Name);
                upsertTag.Parameters.AddWithValue("$name_key", tag.Key);
                upsertTag.Parameters.AddWithValue("$created", now);
                tagId = Convert.ToInt64(
                    await upsertTag.ExecuteScalarAsync(cancellationToken)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture);
            }

            await using var assign = connection.CreateCommand();
            assign.Transaction = transaction;
            assign.CommandText =
                """
                INSERT INTO asset_tags(
                    asset_id, tag_id, created_at_utc_ticks)
                VALUES($asset_id, $tag_id, $created);
                """;
            assign.Parameters.AddWithValue("$asset_id", assetId);
            assign.Parameters.AddWithValue("$tag_id", tagId);
            assign.Parameters.AddWithValue("$created", now);
            await assign.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // Keep zero-asset tags. Tags are now a first-class managed library
        // resource and may be created before they are assigned to an image.
        await ReindexAssetOnConnectionAsync(
            connection,
            transaction,
            libraryId,
            assetId,
            cancellationToken).ConfigureAwait(false);

        transaction.Commit();

        return new AssetUserMetadata(
            assetId,
            update.Rating,
            update.Favorite,
            notes,
            statusLabel,
            colorLabel,
            tags.Select(static tag => tag.Name).ToArray());
    }

    public async Task<int> RebuildSearchIndexAsync(
        long libraryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await _database.OpenConnectionAsync(
            cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var count = await CountLibraryAssetsOnConnectionAsync(
            connection,
            transaction,
            libraryId,
            cancellationToken).ConfigureAwait(false);

        await DeleteSearchRowsForLibraryAsync(
            connection,
            transaction,
            libraryId,
            cancellationToken).ConfigureAwait(false);

        await InsertFtsRowsAsync(
            connection,
            transaction,
            libraryId,
            assetId: null,
            dirtyOnly: false,
            cancellationToken).ConfigureAwait(false);

        await InsertCjkBigramsAsync(
            connection,
            transaction,
            libraryId,
            assetId: null,
            dirtyOnly: false,
            cancellationToken).ConfigureAwait(false);

        await using (var clearDirty = connection.CreateCommand())
        {
            clearDirty.Transaction = transaction;
            clearDirty.CommandText =
                """
                DELETE FROM asset_search_dirty
                WHERE library_id = $library_id;
                """;
            clearDirty.Parameters.AddWithValue(
                "$library_id",
                libraryId);
            await clearDirty.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        transaction.Commit();
        return checked((int)count);
    }

    public async Task<int> RefreshSearchIndexAsync(
        long libraryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);

        await using var connection = await _database.OpenConnectionAsync(
            cancellationToken).ConfigureAwait(false);

        return await RefreshDirtySearchIndexAsync(
            connection,
            libraryId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> CountAssetsAsync(
        long libraryId,
        AssetQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(query);

        await using var connection = await _database.OpenConnectionAsync(
            cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            await RefreshDirtySearchIndexAsync(
                connection,
                libraryId,
                cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        var where = BuildQueryPredicate(
            command,
            libraryId,
            query,
            cursor: null);

        command.CommandText =
            $"""
            SELECT COUNT(*)
            FROM assets AS a
            LEFT JOIN asset_technical_metadata AS tm
              ON tm.asset_id = a.id
             AND tm.source_revision = a.source_revision
            LEFT JOIN asset_user_metadata AS um
              ON um.asset_id = a.id
            WHERE {where};
            """;

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<long>> GetOrderedAssetIdsAsync(
        long libraryId,
        AssetQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(query);

        await using var connection =
            await _database.OpenConnectionAsync(
                cancellationToken)
                .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            await RefreshDirtySearchIndexAsync(
                connection,
                libraryId,
                cancellationToken)
                .ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        var where =
            BuildQueryPredicate(
                command,
                libraryId,
                query,
                cursor: null);
        var orderBy =
            GetOrderBy(query.SortOrder);

        command.CommandText =
            $"""
            SELECT a.id
            FROM assets AS a
            LEFT JOIN asset_user_metadata AS um
              ON um.asset_id = a.id
            WHERE {where}
            ORDER BY {orderBy};
            """;

        var ids = new List<long>();
        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken)
                .ConfigureAwait(false);
        while (await reader.ReadAsync(
                   cancellationToken)
                   .ConfigureAwait(false))
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    public async Task<IReadOnlyList<AssetInfo>> GetAssetsByIdsAsync(
        long libraryId,
        IReadOnlyList<long> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentNullException.ThrowIfNull(assetIds);

        if (assetIds.Count == 0)
        {
            return Array.Empty<AssetInfo>();
        }

        if (assetIds.Count > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(assetIds),
                "Asset page may not exceed 1,000 IDs.");
        }

        await using var connection =
            await _database.OpenConnectionAsync(
                cancellationToken)
                .ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var parameters =
            new string[assetIds.Count];
        for (var index = 0; index < assetIds.Count; index++)
        {
            var name = $"$asset_id_{index}";
            parameters[index] = name;
            command.Parameters.AddWithValue(
                name,
                assetIds[index]);
        }

        command.Parameters.AddWithValue(
            "$library_id",
            libraryId);
        command.CommandText =
            $"""
            SELECT
                a.id, a.library_id, a.folder_id,
                a.relative_path, a.file_name, a.extension,
                a.file_size, a.modified_at_utc_ticks,
                a.source_revision,
                a.width, a.height, a.format,
                tm.source_identity,
                tm.raw_width, tm.raw_height, tm.has_alpha,
                a.created_at_utc_ticks,
                um.rating,
                COALESCE(um.favorite, 0),
                um.status_label,
                um.color_label
            FROM assets AS a
            LEFT JOIN asset_technical_metadata AS tm
              ON tm.asset_id = a.id
             AND tm.source_revision = a.source_revision
            LEFT JOIN asset_user_metadata AS um
              ON um.asset_id = a.id
            WHERE a.library_id = $library_id
              AND a.id IN ({string.Join(", ", parameters)});
            """;

        var byId =
            new Dictionary<long, AssetInfo>(
                assetIds.Count);
        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken)
                .ConfigureAwait(false);
        while (await reader.ReadAsync(
                   cancellationToken)
                   .ConfigureAwait(false))
        {
            var asset = ReadBrowseAsset(reader);
            byId[asset.Id] = asset;
        }

        var ordered =
            new List<AssetInfo>(assetIds.Count);
        foreach (var id in assetIds)
        {
            if (!byId.TryGetValue(id, out var asset))
            {
                throw new InvalidOperationException(
                    $"Browse snapshot asset {id} disappeared while paging.");
            }

            ordered.Add(asset);
        }

        return ordered;
    }

    public async Task<AssetQueryPage> GetAssetPageAsync(
        long libraryId,
        AssetQuery query,
        int limit,
        AssetQueryCursor? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentNullException.ThrowIfNull(query);

        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Page size must be between 1 and 1000.");
        }

        ValidateQuery(query);

        if (cursor is not null
            && cursor.SortOrder != query.SortOrder)
        {
            throw new ArgumentException(
                "Query cursor sort order does not match the requested sort.",
                nameof(cursor));
        }

        await using var connection = await _database.OpenConnectionAsync(
            cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            await RefreshDirtySearchIndexAsync(
                connection,
                libraryId,
                cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        var where = BuildQueryPredicate(
            command,
            libraryId,
            query,
            cursor);
        var orderBy = GetOrderBy(query.SortOrder);

        command.CommandText =
            $"""
            SELECT
                a.id, a.library_id, a.folder_id,
                a.relative_path, a.file_name, a.extension,
                a.file_size, a.modified_at_utc_ticks,
                a.source_revision,
                a.width, a.height, a.format,
                tm.source_identity,
                tm.raw_width, tm.raw_height, tm.has_alpha,
                a.created_at_utc_ticks,
                um.rating,
                COALESCE(um.favorite, 0),
                um.status_label,
                um.color_label
            FROM assets AS a
            LEFT JOIN asset_technical_metadata AS tm
              ON tm.asset_id = a.id
             AND tm.source_revision = a.source_revision
            LEFT JOIN asset_user_metadata AS um
              ON um.asset_id = a.id
            WHERE {where}
            ORDER BY {orderBy}
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit + 1);

        var items = new List<AssetInfo>(limit + 1);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(ReadBrowseAsset(reader));
        }

        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        AssetQueryCursor? nextCursor = hasMore && items.Count > 0
            ? CreateQueryCursor(items[^1], query.SortOrder)
            : null;

        return new AssetQueryPage(items, nextCursor);
    }

    private static async Task<long> CountLibraryAssetsOnConnectionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM assets
            WHERE library_id = $library_id;
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task DeleteSearchRowsForLibraryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        CancellationToken cancellationToken)
    {
        await using (var deleteFts = connection.CreateCommand())
        {
            deleteFts.Transaction = transaction;
            deleteFts.CommandText =
                """
                DELETE FROM asset_search_fts
                WHERE rowid IN (
                    SELECT id
                    FROM assets
                    WHERE library_id = $library_id
                );
                """;
            deleteFts.Parameters.AddWithValue("$library_id", libraryId);
            await deleteFts.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await using var deleteBigrams = connection.CreateCommand();
        deleteBigrams.Transaction = transaction;
        deleteBigrams.CommandText =
            """
            DELETE FROM asset_search_cjk_bigrams
            WHERE library_id = $library_id;
            """;
        deleteBigrams.Parameters.AddWithValue("$library_id", libraryId);
        await deleteBigrams.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<int> RefreshDirtySearchIndexAsync(
        SqliteConnection connection,
        long libraryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM asset_search_dirty
                WHERE library_id = $library_id;
                """;
            countCommand.Parameters.AddWithValue("$library_id", libraryId);
            var dirty = Convert.ToInt32(
                await countCommand.ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);

            if (dirty == 0)
            {
                return 0;
            }
        }

        using var transaction = connection.BeginTransaction();

        await using (var deleteFts = connection.CreateCommand())
        {
            deleteFts.Transaction = transaction;
            deleteFts.CommandText =
                """
                DELETE FROM asset_search_fts
                WHERE rowid IN (
                    SELECT asset_id
                    FROM asset_search_dirty
                    WHERE library_id = $library_id
                );
                """;
            deleteFts.Parameters.AddWithValue("$library_id", libraryId);
            await deleteFts.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await using (var deleteBigrams = connection.CreateCommand())
        {
            deleteBigrams.Transaction = transaction;
            deleteBigrams.CommandText =
                """
                DELETE FROM asset_search_cjk_bigrams
                WHERE asset_id IN (
                    SELECT asset_id
                    FROM asset_search_dirty
                    WHERE library_id = $library_id
                );
                """;
            deleteBigrams.Parameters.AddWithValue("$library_id", libraryId);
            await deleteBigrams.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await InsertFtsRowsAsync(
            connection,
            transaction,
            libraryId,
            assetId: null,
            dirtyOnly: true,
            cancellationToken).ConfigureAwait(false);

        await InsertCjkBigramsAsync(
            connection,
            transaction,
            libraryId,
            assetId: null,
            dirtyOnly: true,
            cancellationToken).ConfigureAwait(false);

        int refreshed;
        await using (var clearDirty = connection.CreateCommand())
        {
            clearDirty.Transaction = transaction;
            clearDirty.CommandText =
                """
                DELETE FROM asset_search_dirty
                WHERE library_id = $library_id;
                """;
            clearDirty.Parameters.AddWithValue("$library_id", libraryId);
            refreshed = await clearDirty.ExecuteNonQueryAsync(
                cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        return refreshed;
    }

    private static async Task ReindexAssetOnConnectionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        long assetId,
        CancellationToken cancellationToken)
    {
        await using (var deleteFts = connection.CreateCommand())
        {
            deleteFts.Transaction = transaction;
            deleteFts.CommandText =
                """
                DELETE FROM asset_search_fts
                WHERE rowid = $asset_id;
                """;
            deleteFts.Parameters.AddWithValue("$asset_id", assetId);
            await deleteFts.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await using (var deleteBigrams = connection.CreateCommand())
        {
            deleteBigrams.Transaction = transaction;
            deleteBigrams.CommandText =
                """
                DELETE FROM asset_search_cjk_bigrams
                WHERE asset_id = $asset_id;
                """;
            deleteBigrams.Parameters.AddWithValue("$asset_id", assetId);
            await deleteBigrams.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await InsertFtsRowsAsync(
            connection,
            transaction,
            libraryId,
            assetId,
            dirtyOnly: false,
            cancellationToken).ConfigureAwait(false);

        await InsertCjkBigramsAsync(
            connection,
            transaction,
            libraryId,
            assetId,
            dirtyOnly: false,
            cancellationToken).ConfigureAwait(false);

        await using var clearDirty = connection.CreateCommand();
        clearDirty.Transaction = transaction;
        clearDirty.CommandText =
            """
            DELETE FROM asset_search_dirty
            WHERE asset_id = $asset_id;
            """;
        clearDirty.Parameters.AddWithValue("$asset_id", assetId);
        await clearDirty.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task InsertFtsRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        long? assetId,
        bool dirtyOnly,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO asset_search_fts(
                rowid, file_name, relative_path, notes, tags_text)
            SELECT
                a.id,
                a.file_name,
                a.relative_path,
                COALESCE(um.notes, ''),
                COALESCE((
                    SELECT group_concat(name, ' ')
                    FROM (
                        SELECT t.name AS name
                        FROM asset_tags AS at
                        INNER JOIN tags AS t ON t.id = at.tag_id
                        WHERE at.asset_id = a.id
                        ORDER BY t.name_key ASC
                    )
                ), '')
            FROM assets AS a
            LEFT JOIN asset_user_metadata AS um
              ON um.asset_id = a.id
            WHERE a.library_id = $library_id
              AND ($asset_id IS NULL OR a.id = $asset_id)
              AND (
                  $dirty_only = 0
                  OR EXISTS (
                      SELECT 1
                      FROM asset_search_dirty AS d
                      WHERE d.asset_id = a.id
                        AND d.library_id = a.library_id
                  )
              );
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue(
            "$asset_id",
            assetId.HasValue ? assetId.Value : DBNull.Value);
        command.Parameters.AddWithValue(
            "$dirty_only",
            dirtyOnly ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task InsertCjkBigramsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        long? assetId,
        bool dirtyOnly,
        CancellationToken cancellationToken)
    {
        // Avoid constructing the recursive bigram CTE for libraries that
        // contain only ASCII search text. The previous implementation built
        // the complete search haystack for every asset even when the CTE
        // ultimately produced zero CJK rows, which regressed 100k rebuilds
        // after the browse indexes increased the database working set.
        await using (var detect = connection.CreateCommand())
        {
            detect.Transaction = transaction;
            detect.CommandText =
                """
                SELECT EXISTS(
                    SELECT 1
                    FROM assets AS a
                    LEFT JOIN asset_user_metadata AS um
                      ON um.asset_id = a.id
                    WHERE a.library_id = $library_id
                      AND ($asset_id IS NULL OR a.id = $asset_id)
                      AND (
                          $dirty_only = 0
                          OR EXISTS (
                              SELECT 1
                              FROM asset_search_dirty AS d
                              WHERE d.asset_id = a.id
                                AND d.library_id = a.library_id
                          )
                      )
                      AND (
                          a.file_name GLOB '*[^ -~]*'
                          OR a.relative_path GLOB '*[^ -~]*'
                          OR COALESCE(um.notes, '') GLOB '*[^ -~]*'
                      )
                    LIMIT 1
                )
                OR EXISTS(
                    SELECT 1
                    FROM asset_tags AS at
                    INNER JOIN tags AS t
                      ON t.id = at.tag_id
                    INNER JOIN assets AS a
                      ON a.id = at.asset_id
                    WHERE a.library_id = $library_id
                      AND ($asset_id IS NULL OR a.id = $asset_id)
                      AND (
                          $dirty_only = 0
                          OR EXISTS (
                              SELECT 1
                              FROM asset_search_dirty AS d
                              WHERE d.asset_id = a.id
                                AND d.library_id = a.library_id
                          )
                      )
                      AND t.name GLOB '*[^ -~]*'
                    LIMIT 1
                );
                """;
            detect.Parameters.AddWithValue(
                "$library_id",
                libraryId);
            detect.Parameters.AddWithValue(
                "$asset_id",
                assetId.HasValue
                    ? assetId.Value
                    : DBNull.Value);
            detect.Parameters.AddWithValue(
                "$dirty_only",
                dirtyOnly ? 1 : 0);

            var hasNonAscii =
                Convert.ToInt64(
                    await detect.ExecuteScalarAsync(
                        cancellationToken)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture) != 0;
            if (!hasNonAscii)
            {
                return;
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            WITH RECURSIVE source(
                asset_id,
                library_id,
                search_text
            ) AS (
                SELECT
                    a.id,
                    a.library_id,
                    a.file_name || char(31) ||
                    a.relative_path || char(31) ||
                    COALESCE(um.notes, '') || char(31) ||
                    COALESCE((
                        SELECT group_concat(name, ' ')
                        FROM (
                            SELECT t.name AS name
                            FROM asset_tags AS at
                            INNER JOIN tags AS t ON t.id = at.tag_id
                            WHERE at.asset_id = a.id
                            ORDER BY t.name_key ASC
                        )
                    ), '')
                FROM assets AS a
                LEFT JOIN asset_user_metadata AS um
                  ON um.asset_id = a.id
                WHERE a.library_id = $library_id
                  AND ($asset_id IS NULL OR a.id = $asset_id)
                  AND (
                      $dirty_only = 0
                      OR EXISTS (
                          SELECT 1
                          FROM asset_search_dirty AS d
                          WHERE d.asset_id = a.id
                            AND d.library_id = a.library_id
                      )
                  )
            ),
            positions(
                asset_id,
                library_id,
                search_text,
                position
            ) AS (
                SELECT
                    asset_id,
                    library_id,
                    search_text,
                    1
                FROM source
                WHERE length(search_text) >= 2
                  AND search_text GLOB '*[^ -~]*'

                UNION ALL

                SELECT
                    asset_id,
                    library_id,
                    search_text,
                    position + 1
                FROM positions
                WHERE position + 1 < length(search_text)
            )
            INSERT OR IGNORE INTO asset_search_cjk_bigrams(
                library_id, token, asset_id)
            SELECT
                library_id,
                substr(search_text, position, 2),
                asset_id
            FROM positions
            WHERE
                (
                    unicode(substr(search_text, position, 1))
                        BETWEEN 0x3040 AND 0x30ff
                    OR unicode(substr(search_text, position, 1))
                        BETWEEN 0x3400 AND 0x4dbf
                    OR unicode(substr(search_text, position, 1))
                        BETWEEN 0x4e00 AND 0x9fff
                    OR unicode(substr(search_text, position, 1))
                        BETWEEN 0xff66 AND 0xff9f
                )
                AND
                (
                    unicode(substr(search_text, position + 1, 1))
                        BETWEEN 0x3040 AND 0x30ff
                    OR unicode(substr(search_text, position + 1, 1))
                        BETWEEN 0x3400 AND 0x4dbf
                    OR unicode(substr(search_text, position + 1, 1))
                        BETWEEN 0x4e00 AND 0x9fff
                    OR unicode(substr(search_text, position + 1, 1))
                        BETWEEN 0xff66 AND 0xff9f
                );
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue(
            "$asset_id",
            assetId.HasValue ? assetId.Value : DBNull.Value);
        command.Parameters.AddWithValue(
            "$dirty_only",
            dirtyOnly ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildQueryPredicate(
        SqliteCommand command,
        long libraryId,
        AssetQuery query,
        AssetQueryCursor? cursor)
    {
        var conditions = new List<string>
        {
            "a.library_id = $library_id"
        };
        command.Parameters.AddWithValue("$library_id", libraryId);

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var terms = SplitSearchTerms(query.SearchText!);
            var parameterOrdinal = 0;

            foreach (var term in terms)
            {
                if (IsTwoCharacterCjkTerm(term))
                {
                    var tokenParameter =
                        $"$cjk_{parameterOrdinal}";
                    command.Parameters.AddWithValue(
                        tokenParameter,
                        term);
                    conditions.Add(
                        $"""
                        a.id IN (
                            SELECT cb.asset_id
                            FROM asset_search_cjk_bigrams AS cb
                            WHERE cb.library_id = $library_id
                              AND cb.token = {tokenParameter}
                        )
                        """);
                }
                else
                {
                    var containsParameter =
                        $"$contains_{parameterOrdinal}";
                    command.Parameters.AddWithValue(
                        containsParameter,
                        term);

                    if (term.Length < 3)
                    {
                        // Trigram FTS cannot represent one- or two-character
                        // terms. Preserve product search semantics with a
                        // bounded-to-this-library normalized contains fallback
                        // instead of refusing a legitimate short filename/tag.
                        conditions.Add(
                            $"""
                            instr(
                                {SearchHaystackSql},
                                {containsParameter}
                            ) > 0
                            """);
                    }
                    else
                    {
                        var trigrams = ExtractTrigrams(term);
                        var parameterName =
                            $"$fts_{parameterOrdinal}";
                        command.Parameters.AddWithValue(
                            parameterName,
                            string.Join(
                                " AND ",
                                trigrams.Select(QuoteFtsToken)));

                        conditions.Add(
                            $"""
                            a.id IN (
                                SELECT rowid
                                FROM asset_search_fts
                                WHERE asset_search_fts MATCH {parameterName}
                            )
                            AND instr(
                                {SearchHaystackSql},
                                {containsParameter}
                            ) > 0
                            """);
                    }
                }

                parameterOrdinal++;
            }
        }

        if (!string.IsNullOrWhiteSpace(query.FolderPathPrefix))
        {
            var folderPath =
                query.FolderPathPrefix!
                    .Replace('\\', '/')
                    .Trim('/');

            if (folderPath.Length == 0
                || folderPath
                    .Split(
                        '/',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Any(static segment =>
                        segment is "." or ".."))
            {
                throw new ArgumentException(
                    "Folder scope must be a normalized relative folder path.",
                    nameof(query));
            }

            var folderKey =
                LibraryPaths.FolderPathKey(folderPath);
            var escapedFolderKey =
                folderKey
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("%", "\\%", StringComparison.Ordinal)
                    .Replace("_", "\\_", StringComparison.Ordinal);

            command.Parameters.AddWithValue(
                "$folder_prefix",
                escapedFolderKey + "/%");
            conditions.Add(
                "a.relative_path_key LIKE $folder_prefix ESCAPE '\\'");
        }

        var requiredTags = NormalizeTags(query.RequiredTags);
        if (requiredTags.Length > 0)
        {
            var tagParameters = new string[requiredTags.Length];
            for (var index = 0; index < requiredTags.Length; index++)
            {
                var parameterName = $"$required_tag_{index}";
                tagParameters[index] = parameterName;
                command.Parameters.AddWithValue(
                    parameterName,
                    requiredTags[index].Key);
            }

            conditions.Add(
                $"""
                a.id IN (
                    SELECT at.asset_id
                    FROM asset_tags AS at
                    INNER JOIN tags AS t
                      ON t.id = at.tag_id
                    WHERE t.library_id = $library_id
                      AND t.name_key IN (
                          {string.Join(", ", tagParameters)}
                      )
                    GROUP BY at.asset_id
                    HAVING COUNT(DISTINCT t.name_key) = {requiredTags.Length}
                )
                """);
        }

        if (query.MinRating is { } minRating)
        {
            command.Parameters.AddWithValue("$min_rating", minRating);
            conditions.Add(
                "um.rating IS NOT NULL AND um.rating >= $min_rating");
        }

        if (query.MaxRating is { } maxRating)
        {
            command.Parameters.AddWithValue("$max_rating", maxRating);
            conditions.Add(
                "um.rating IS NOT NULL AND um.rating <= $max_rating");
        }

        if (query.Favorite is { } favorite)
        {
            command.Parameters.AddWithValue(
                "$favorite",
                favorite ? 1 : 0);
            conditions.Add(
                "COALESCE(um.favorite, 0) = $favorite");
        }

        if (!string.IsNullOrWhiteSpace(query.StatusLabel))
        {
            command.Parameters.AddWithValue(
                "$status_label",
                NormalizeOptionalLabel(
                    query.StatusLabel,
                    nameof(query.StatusLabel))!);
            conditions.Add(
                "um.status_label = $status_label COLLATE NOCASE");
        }

        if (!string.IsNullOrWhiteSpace(query.ColorLabel))
        {
            command.Parameters.AddWithValue(
                "$color_label",
                NormalizeOptionalLabel(
                    query.ColorLabel,
                    nameof(query.ColorLabel))!);
            conditions.Add(
                "um.color_label = $color_label COLLATE NOCASE");
        }

        if (cursor is not null)
        {
            command.Parameters.AddWithValue("$cursor_id", cursor.Id);

            switch (query.SortOrder)
            {
                case AssetSortOrder.ModifiedNewest:
                case AssetSortOrder.ModifiedOldest:
                {
                    var value = cursor.ModifiedAtUtcTicks
                        ?? throw new ArgumentException(
                            "Modified sort cursor is missing its timestamp.",
                            nameof(cursor));
                    command.Parameters.AddWithValue(
                        "$cursor_modified",
                        value);
                    var comparison =
                        query.SortOrder == AssetSortOrder.ModifiedNewest
                            ? "<"
                            : ">";
                    conditions.Add(
                        $"(a.modified_at_utc_ticks {comparison} $cursor_modified OR (a.modified_at_utc_ticks = $cursor_modified AND a.id {comparison} $cursor_id))");
                    break;
                }

                case AssetSortOrder.FileNameAscending:
                case AssetSortOrder.FileNameDescending:
                {
                    var value = cursor.FileName
                        ?? throw new ArgumentException(
                            "Filename sort cursor is missing its filename.",
                            nameof(cursor));
                    command.Parameters.AddWithValue(
                        "$cursor_file_name",
                        value);
                    var comparison =
                        query.SortOrder == AssetSortOrder.FileNameAscending
                            ? ">"
                            : "<";
                    conditions.Add(
                        $"(a.file_name COLLATE NOCASE {comparison} $cursor_file_name COLLATE NOCASE OR (a.file_name COLLATE NOCASE = $cursor_file_name COLLATE NOCASE AND a.id {comparison} $cursor_id))");
                    break;
                }

                case AssetSortOrder.CreatedNewest:
                case AssetSortOrder.CreatedOldest:
                {
                    var value = cursor.CreatedAtUtcTicks
                        ?? throw new ArgumentException(
                            "Created sort cursor is missing its timestamp.",
                            nameof(cursor));
                    command.Parameters.AddWithValue(
                        "$cursor_created",
                        value);
                    var comparison =
                        query.SortOrder == AssetSortOrder.CreatedNewest
                            ? "<"
                            : ">";
                    conditions.Add(
                        $"(a.created_at_utc_ticks {comparison} $cursor_created OR (a.created_at_utc_ticks = $cursor_created AND a.id {comparison} $cursor_id))");
                    break;
                }

                case AssetSortOrder.FileSizeLargest:
                case AssetSortOrder.FileSizeSmallest:
                {
                    var value = cursor.FileSize
                        ?? throw new ArgumentException(
                            "File-size sort cursor is missing its size.",
                            nameof(cursor));
                    command.Parameters.AddWithValue(
                        "$cursor_size",
                        value);
                    var comparison =
                        query.SortOrder == AssetSortOrder.FileSizeLargest
                            ? "<"
                            : ">";
                    conditions.Add(
                        $"(a.file_size {comparison} $cursor_size OR (a.file_size = $cursor_size AND a.id {comparison} $cursor_id))");
                    break;
                }

                case AssetSortOrder.RatingHighest:
                case AssetSortOrder.RatingLowest:
                {
                    var value = cursor.Rating
                        ?? throw new ArgumentException(
                            "Rating sort cursor is missing its rating.",
                            nameof(cursor));
                    command.Parameters.AddWithValue(
                        "$cursor_rating",
                        value);
                    var comparison =
                        query.SortOrder == AssetSortOrder.RatingHighest
                            ? "<"
                            : ">";
                    conditions.Add(
                        $"(COALESCE(um.rating, 0) {comparison} $cursor_rating OR (COALESCE(um.rating, 0) = $cursor_rating AND a.id {comparison} $cursor_id))");
                    break;
                }

                case AssetSortOrder.StatusAscending:
                case AssetSortOrder.StatusDescending:
                {
                    var value = cursor.StatusLabel
                        ?? string.Empty;
                    command.Parameters.AddWithValue(
                        "$cursor_status",
                        value);
                    var comparison =
                        query.SortOrder == AssetSortOrder.StatusAscending
                            ? ">"
                            : "<";
                    conditions.Add(
                        $"(COALESCE(um.status_label, '') COLLATE NOCASE {comparison} $cursor_status COLLATE NOCASE OR (COALESCE(um.status_label, '') COLLATE NOCASE = $cursor_status COLLATE NOCASE AND a.id {comparison} $cursor_id))");
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(query));
            }
        }

        return string.Join(
            Environment.NewLine + "AND ",
            conditions.Select(static condition => $"({condition})"));
    }

    private static string GetOrderBy(AssetSortOrder sortOrder) =>
        sortOrder switch
        {
            AssetSortOrder.ModifiedNewest =>
                "a.modified_at_utc_ticks DESC, a.id DESC",
            AssetSortOrder.ModifiedOldest =>
                "a.modified_at_utc_ticks ASC, a.id ASC",
            AssetSortOrder.FileNameAscending =>
                "a.file_name COLLATE NOCASE ASC, a.id ASC",
            AssetSortOrder.FileNameDescending =>
                "a.file_name COLLATE NOCASE DESC, a.id DESC",
            AssetSortOrder.CreatedNewest =>
                "a.created_at_utc_ticks DESC, a.id DESC",
            AssetSortOrder.CreatedOldest =>
                "a.created_at_utc_ticks ASC, a.id ASC",
            AssetSortOrder.FileSizeLargest =>
                "a.file_size DESC, a.id DESC",
            AssetSortOrder.FileSizeSmallest =>
                "a.file_size ASC, a.id ASC",
            AssetSortOrder.RatingHighest =>
                "COALESCE(um.rating, 0) DESC, a.id DESC",
            AssetSortOrder.RatingLowest =>
                "COALESCE(um.rating, 0) ASC, a.id ASC",
            AssetSortOrder.StatusAscending =>
                "COALESCE(um.status_label, '') COLLATE NOCASE ASC, a.id ASC",
            AssetSortOrder.StatusDescending =>
                "COALESCE(um.status_label, '') COLLATE NOCASE DESC, a.id DESC",
            _ => throw new ArgumentOutOfRangeException(
                nameof(sortOrder))
        };

    private static AssetQueryCursor CreateQueryCursor(
        AssetInfo asset,
        AssetSortOrder sortOrder) =>
        sortOrder switch
        {
            AssetSortOrder.ModifiedNewest
                or AssetSortOrder.ModifiedOldest =>
                new AssetQueryCursor(
                    sortOrder,
                    asset.Id,
                    ModifiedAtUtcTicks:
                        asset.ModifiedAtUtc.UtcDateTime.Ticks),
            AssetSortOrder.FileNameAscending
                or AssetSortOrder.FileNameDescending =>
                new AssetQueryCursor(
                    sortOrder,
                    asset.Id,
                    FileName: asset.FileName),
            AssetSortOrder.CreatedNewest
                or AssetSortOrder.CreatedOldest =>
                new AssetQueryCursor(
                    sortOrder,
                    asset.Id,
                    CreatedAtUtcTicks:
                        asset.CreatedAtUtc?.UtcDateTime.Ticks
                        ?? throw new InvalidOperationException(
                            "Browse asset is missing creation time.")),
            AssetSortOrder.FileSizeLargest
                or AssetSortOrder.FileSizeSmallest =>
                new AssetQueryCursor(
                    sortOrder,
                    asset.Id,
                    FileSize: asset.FileSize),
            AssetSortOrder.RatingHighest
                or AssetSortOrder.RatingLowest =>
                new AssetQueryCursor(
                    sortOrder,
                    asset.Id,
                    Rating: asset.Rating ?? 0),
            AssetSortOrder.StatusAscending
                or AssetSortOrder.StatusDescending =>
                new AssetQueryCursor(
                    sortOrder,
                    asset.Id,
                    StatusLabel: asset.StatusLabel ?? string.Empty),
            _ => throw new ArgumentOutOfRangeException(
                nameof(sortOrder))
        };

    private static AssetInfo ReadBrowseAsset(
        SqliteDataReader reader)
    {
        var asset = ReadAsset(reader);
        return asset with
        {
            CreatedAtUtc = FromTicks(reader.GetInt64(16)),
            Rating =
                reader.IsDBNull(17)
                    ? null
                    : reader.GetInt32(17),
            Favorite = reader.GetInt32(18) != 0,
            StatusLabel =
                reader.IsDBNull(19)
                    ? null
                    : reader.GetString(19),
            ColorLabel =
                reader.IsDBNull(20)
                    ? null
                    : reader.GetString(20)
        };
    }

    private static void ValidateUserMetadata(
        AssetUserMetadataUpdate update)
    {
        if (update.Rating is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(update),
                "Rating must be null or between 0 and 5.");
        }

        if ((update.Notes?.Length ?? 0) > MaxNotesLength)
        {
            throw new ArgumentException(
                $"Notes may not exceed {MaxNotesLength:N0} characters.",
                nameof(update));
        }

        _ = NormalizeOptionalLabel(
            update.StatusLabel,
            nameof(update.StatusLabel));
        _ = NormalizeOptionalLabel(
            update.ColorLabel,
            nameof(update.ColorLabel));
        _ = NormalizeTags(update.Tags);
    }

    private static void ValidateQuery(AssetQuery query)
    {
        if ((query.SearchText?.Length ?? 0) > MaxSearchTextLength)
        {
            throw new ArgumentException(
                $"Search text may not exceed {MaxSearchTextLength} characters.",
                nameof(query));
        }

        if (query.MinRating is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                "Minimum rating must be between 0 and 5.");
        }

        if (query.MaxRating is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                "Maximum rating must be between 0 and 5.");
        }

        if (query.MinRating is { } min
            && query.MaxRating is { } max
            && min > max)
        {
            throw new ArgumentException(
                "Minimum rating cannot exceed maximum rating.",
                nameof(query));
        }

        _ = NormalizeOptionalLabel(
            query.StatusLabel,
            nameof(query.StatusLabel));
        _ = NormalizeOptionalLabel(
            query.ColorLabel,
            nameof(query.ColorLabel));
        _ = NormalizeTags(query.RequiredTags);

        if (!string.IsNullOrWhiteSpace(query.FolderPathPrefix))
        {
            var folderPath =
                query.FolderPathPrefix!
                    .Replace('\\', '/')
                    .Trim('/');
            if (folderPath.Length == 0
                || folderPath
                    .Split(
                        '/',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Any(static segment =>
                        segment is "." or ".."))
            {
                throw new ArgumentException(
                    "Folder scope must be a normalized relative folder path.",
                    nameof(query));
            }
        }
    }

    private static string? NormalizeOptionalLabel(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value
            .Trim()
            .Normalize(NormalizationForm.FormKC);
        if (normalized.Length > 64)
        {
            throw new ArgumentException(
                "Label may not exceed 64 characters.",
                parameterName);
        }

        return normalized;
    }

    private static NormalizedTag[] NormalizeTags(
        IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        if (values.Count > MaxTagsPerAsset)
        {
            throw new ArgumentException(
                $"An asset may not have more than {MaxTagsPerAsset} tags.",
                nameof(values));
        }

        var result = new Dictionary<string, NormalizedTag>(
            StringComparer.Ordinal);

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var name = value
                .Trim()
                .Normalize(NormalizationForm.FormKC);
            if (name.Length is < 1 or > 128)
            {
                throw new ArgumentException(
                    "Tags must be between 1 and 128 characters.",
                    nameof(values));
            }

            var key = name.ToUpperInvariant();
            result[key] = new NormalizedTag(name, key);
        }

        if (result.Count > MaxTagsPerAsset)
        {
            throw new ArgumentException(
                $"An asset may not have more than {MaxTagsPerAsset} distinct tags.",
                nameof(values));
        }

        return result.Values
            .OrderBy(static tag => tag.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] SplitSearchTerms(
        string searchText)
    {
        var normalized = searchText
            .Trim()
            .Normalize(NormalizationForm.FormKC)
            .ToLowerInvariant();

        if (normalized.Length == 0)
        {
            return [];
        }

        return normalized.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
    }

    private static bool IsTwoCharacterCjkTerm(
        string value) =>
        value.Length == 2
        && IsCjk(value[0])
        && IsCjk(value[1]);

    private static string[] ExtractTrigrams(
        string value)
    {
        var result = new HashSet<string>(
            StringComparer.Ordinal);

        for (var index = 0; index + 2 < value.Length; index++)
        {
            result.Add(value.Substring(index, 3));
        }

        return result
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsCjk(char value) =>
        value is >= '\u3040' and <= '\u30ff'
        or >= '\u3400' and <= '\u4dbf'
        or >= '\u4e00' and <= '\u9fff'
        or >= '\uff66' and <= '\uff9f';

    private static string QuoteFtsToken(string value) =>
        "\"" + value.Replace(
            "\"",
            "\"\"",
            StringComparison.Ordinal) + "\"";

    private sealed record NormalizedTag(
        string Name,
        string Key);
}
