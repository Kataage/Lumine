using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed partial class LibraryRepository
{
    public async Task<int> PatchUserMetadataAsync(
        long libraryId,
        IReadOnlyList<long> assetIds,
        AssetUserMetadataPatch patch,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentNullException.ThrowIfNull(assetIds);
        ArgumentNullException.ThrowIfNull(patch);

        if (assetIds.Count == 0)
        {
            return 0;
        }

        if (assetIds.Count > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(assetIds),
                "Bulk metadata updates are limited to 10,000 assets per operation.");
        }

        if (patch.SetRating
            && patch.Rating is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(patch),
                "Rating must be null or between 0 and 5.");
        }

        var status =
            patch.SetStatusLabel
                ? NormalizeOptionalLabel(
                    patch.StatusLabel,
                    nameof(patch.StatusLabel))
                : null;
        var color =
            patch.SetColorLabel
                ? NormalizeOptionalLabel(
                    patch.ColorLabel,
                    nameof(patch.ColorLabel))
                : null;
        var addTags =
            NormalizeTags(patch.AddTags);
        var ids =
            assetIds
                .Distinct()
                .ToArray();

        var changesMetadata =
            patch.SetRating
            || patch.SetFavorite
            || patch.SetStatusLabel
            || patch.SetColorLabel
            || patch.ClearTags
            || addTags.Length > 0;

        if (!changesMetadata)
        {
            return 0;
        }

        var now =
            DateTimeOffset.UtcNow.UtcDateTime.Ticks;

        await using var connection =
            await _database.OpenConnectionAsync(
                cancellationToken).ConfigureAwait(false);
        using var transaction =
            connection.BeginTransaction();

        await using var exists =
            connection.CreateCommand();
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
        exists.Parameters.AddWithValue(
            "$library_id",
            libraryId);
        var existsAssetId =
            exists.Parameters.Add(
                "$asset_id",
                SqliteType.Integer);
        exists.Prepare();

        await using var ensureMetadata =
            connection.CreateCommand();
        ensureMetadata.Transaction = transaction;
        ensureMetadata.CommandText =
            """
            INSERT INTO asset_user_metadata(
                asset_id, rating, favorite, notes,
                status_label, color_label, updated_at_utc_ticks)
            VALUES(
                $asset_id, NULL, 0, '',
                NULL, NULL, $updated)
            ON CONFLICT(asset_id) DO NOTHING;
            """;
        var ensureAssetId =
            ensureMetadata.Parameters.Add(
                "$asset_id",
                SqliteType.Integer);
        ensureMetadata.Parameters.AddWithValue(
            "$updated",
            now);
        ensureMetadata.Prepare();

        await using var updateMetadata =
            connection.CreateCommand();
        updateMetadata.Transaction = transaction;
        updateMetadata.CommandText =
            """
            UPDATE asset_user_metadata
            SET rating =
                    CASE
                        WHEN $set_rating = 1
                            THEN $rating
                        ELSE rating
                    END,
                favorite =
                    CASE
                        WHEN $set_favorite = 1
                            THEN $favorite
                        ELSE favorite
                    END,
                status_label =
                    CASE
                        WHEN $set_status = 1
                            THEN $status
                        ELSE status_label
                    END,
                color_label =
                    CASE
                        WHEN $set_color = 1
                            THEN $color
                        ELSE color_label
                    END,
                updated_at_utc_ticks = $updated
            WHERE asset_id = $asset_id;
            """;
        var updateAssetId =
            updateMetadata.Parameters.Add(
                "$asset_id",
                SqliteType.Integer);
        updateMetadata.Parameters.AddWithValue(
            "$set_rating",
            patch.SetRating ? 1 : 0);
        updateMetadata.Parameters.AddWithValue(
            "$rating",
            patch.Rating.HasValue
                ? patch.Rating.Value
                : DBNull.Value);
        updateMetadata.Parameters.AddWithValue(
            "$set_favorite",
            patch.SetFavorite ? 1 : 0);
        updateMetadata.Parameters.AddWithValue(
            "$favorite",
            patch.Favorite ? 1 : 0);
        updateMetadata.Parameters.AddWithValue(
            "$set_status",
            patch.SetStatusLabel ? 1 : 0);
        updateMetadata.Parameters.AddWithValue(
            "$status",
            (object?)status ?? DBNull.Value);
        updateMetadata.Parameters.AddWithValue(
            "$set_color",
            patch.SetColorLabel ? 1 : 0);
        updateMetadata.Parameters.AddWithValue(
            "$color",
            (object?)color ?? DBNull.Value);
        updateMetadata.Parameters.AddWithValue(
            "$updated",
            now);
        updateMetadata.Prepare();

        await using var clearTags =
            connection.CreateCommand();
        clearTags.Transaction = transaction;
        clearTags.CommandText =
            """
            DELETE FROM asset_tags
            WHERE asset_id = $asset_id;
            """;
        var clearAssetId =
            clearTags.Parameters.Add(
                "$asset_id",
                SqliteType.Integer);
        clearTags.Prepare();

        await using var markDirty =
            connection.CreateCommand();
        markDirty.Transaction = transaction;
        markDirty.CommandText =
            """
            INSERT INTO asset_search_dirty(
                asset_id,
                library_id)
            VALUES(
                $asset_id,
                $library_id)
            ON CONFLICT(asset_id) DO UPDATE SET
                library_id = excluded.library_id;
            """;
        var dirtyAssetId =
            markDirty.Parameters.Add(
                "$asset_id",
                SqliteType.Integer);
        markDirty.Parameters.AddWithValue(
            "$library_id",
            libraryId);
        markDirty.Prepare();

        var tagIds =
            new List<long>(addTags.Length);
        foreach (var tag in addTags)
        {
            await using var upsertTag =
                connection.CreateCommand();
            upsertTag.Transaction = transaction;
            upsertTag.CommandText =
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
                ON CONFLICT(library_id, name_key)
                DO UPDATE SET
                    name = excluded.name
                RETURNING id;
                """;
            upsertTag.Parameters.AddWithValue(
                "$library_id",
                libraryId);
            upsertTag.Parameters.AddWithValue(
                "$name",
                tag.Name);
            upsertTag.Parameters.AddWithValue(
                "$name_key",
                tag.Key);
            upsertTag.Parameters.AddWithValue(
                "$created",
                now);
            tagIds.Add(
                Convert.ToInt64(
                    await upsertTag.ExecuteScalarAsync(
                        cancellationToken)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture));
        }

        var updated = 0;
        foreach (var assetId in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
                assetId);

            existsAssetId.Value = assetId;
            if (Convert.ToInt32(
                    await exists.ExecuteScalarAsync(
                        cancellationToken)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture) == 0)
            {
                throw new InvalidOperationException(
                    $"Asset {assetId} does not belong to library {libraryId}.");
            }

            ensureAssetId.Value = assetId;
            await ensureMetadata.ExecuteNonQueryAsync(
                cancellationToken).ConfigureAwait(false);

            updateAssetId.Value = assetId;
            await updateMetadata.ExecuteNonQueryAsync(
                cancellationToken).ConfigureAwait(false);

            if (patch.ClearTags)
            {
                clearAssetId.Value = assetId;
                await clearTags.ExecuteNonQueryAsync(
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (var tagId in tagIds)
            {
                await using var assign =
                    connection.CreateCommand();
                assign.Transaction = transaction;
                assign.CommandText =
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
                assign.Parameters.AddWithValue(
                    "$asset_id",
                    assetId);
                assign.Parameters.AddWithValue(
                    "$tag_id",
                    tagId);
                assign.Parameters.AddWithValue(
                    "$created",
                    now);
                await assign.ExecuteNonQueryAsync(
                    cancellationToken).ConfigureAwait(false);
            }

            dirtyAssetId.Value = assetId;
            await markDirty.ExecuteNonQueryAsync(
                cancellationToken).ConfigureAwait(false);
            updated++;
        }

        if (patch.ClearTags)
        {
            await using var removeOrphans =
                connection.CreateCommand();
            removeOrphans.Transaction = transaction;
            removeOrphans.CommandText =
                """
                DELETE FROM tags
                WHERE library_id = $library_id
                  AND NOT EXISTS(
                      SELECT 1
                      FROM asset_tags AS at
                      WHERE at.tag_id = tags.id
                  );
                """;
            removeOrphans.Parameters.AddWithValue(
                "$library_id",
                libraryId);
            await removeOrphans.ExecuteNonQueryAsync(
                cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        return updated;
    }
}
