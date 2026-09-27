using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed partial class LibraryRepository
{
    private const int MaxSearchTextLength = 256;
    private const int MaxNotesLength = 100_000;
    private const int MaxTagsPerAsset = 128;

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
                update.Rating.HasValue ? update.Rating.Value : DBNull.Value);
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
                INSERT INTO asset_tags(asset_id, tag_id, created_at_utc_ticks)
                VALUES($asset_id, $tag_id, $created);
                """;
            assign.Parameters.AddWithValue("$asset_id", assetId);
            assign.Parameters.AddWithValue("$tag_id", tagId);
            assign.Parameters.AddWithValue("$created", now);
            await assign.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var tagsText = string.Join(' ', tags.Select(static tag => tag.Name));

        await using (var searchDocument = connection.CreateCommand())
        {
            searchDocument.Transaction = transaction;
            searchDocument.CommandText =
                """
                UPDATE asset_search_documents
                SET notes = $notes,
                    tags_text = $tags_text,
                    normalized_text = lower(
                        file_name || char(31) ||
                        relative_path || char(31) ||
                        $notes || char(31) ||
                        $tags_text)
                WHERE asset_id = $asset_id
                  AND library_id = $library_id;
                """;
            searchDocument.Parameters.AddWithValue("$notes", notes);
            searchDocument.Parameters.AddWithValue("$tags_text", tagsText);
            searchDocument.Parameters.AddWithValue("$asset_id", assetId);
            searchDocument.Parameters.AddWithValue("$library_id", libraryId);

            if (await searchDocument.ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false) != 1)
            {
                transaction.Rollback();
                throw new InvalidOperationException(
                    $"Search document for asset {assetId} is missing.");
            }
        }

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
            INNER JOIN asset_search_documents AS sd
              ON sd.asset_id = a.id
            WHERE {where};
            """;

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
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
                tm.raw_width, tm.raw_height, tm.has_alpha
            FROM assets AS a
            LEFT JOIN asset_technical_metadata AS tm
              ON tm.asset_id = a.id
             AND tm.source_revision = a.source_revision
            LEFT JOIN asset_user_metadata AS um
              ON um.asset_id = a.id
            INNER JOIN asset_search_documents AS sd
              ON sd.asset_id = a.id
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
            items.Add(ReadAsset(reader));
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
                if (ContainsCjk(term))
                {
                    var bigrams = ExtractCjkBigrams(term);
                    if (bigrams.Count == 0)
                    {
                        throw new ArgumentException(
                            "Japanese/CJK search terms must contain at least two adjacent CJK characters.",
                            nameof(query));
                    }

                    var tokenParameters = new string[bigrams.Count];
                    for (var index = 0; index < bigrams.Count; index++)
                    {
                        var parameterName =
                            $"$cjk_{parameterOrdinal}_{index}";
                        tokenParameters[index] = parameterName;
                        command.Parameters.AddWithValue(
                            parameterName,
                            bigrams[index]);
                    }

                    var containsParameter =
                        $"$contains_{parameterOrdinal}";
                    command.Parameters.AddWithValue(
                        containsParameter,
                        term);

                    conditions.Add(
                        $"""
                        a.id IN (
                            SELECT cb.asset_id
                            FROM asset_search_cjk_bigrams AS cb
                            WHERE cb.library_id = $library_id
                              AND cb.token IN (
                                  {string.Join(", ", tokenParameters)}
                              )
                            GROUP BY cb.asset_id
                            HAVING COUNT(DISTINCT cb.token) = {bigrams.Count}
                        )
                        AND instr(sd.normalized_text, {containsParameter}) > 0
                        """);
                }
                else
                {
                    if (term.Length < 3)
                    {
                        throw new ArgumentException(
                            "Non-CJK partial search terms must be at least three characters. Use an exact tag filter for shorter labels.",
                            nameof(query));
                    }

                    var trigrams = ExtractTrigrams(term);
                    var parameterName =
                        $"$fts_{parameterOrdinal}";
                    command.Parameters.AddWithValue(
                        parameterName,
                        string.Join(
                            " AND ",
                            trigrams.Select(QuoteFtsPhrase)));

                    var containsParameter =
                        $"$contains_{parameterOrdinal}";
                    command.Parameters.AddWithValue(
                        containsParameter,
                        term);

                    conditions.Add(
                        $"""
                        a.id IN (
                            SELECT rowid
                            FROM asset_search_fts
                            WHERE asset_search_fts MATCH {parameterName}
                        )
                        AND instr(sd.normalized_text, {containsParameter}) > 0
                        """);
                }

                parameterOrdinal++;
            }
        }

        var requiredTags = NormalizeTags(query.RequiredTags);
        if (requiredTags.Count > 0)
        {
            var tagParameters = new string[requiredTags.Count];
            for (var index = 0; index < requiredTags.Count; index++)
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
                    HAVING COUNT(DISTINCT t.name_key) = {requiredTags.Count}
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
                query.StatusLabel.Trim());
            conditions.Add(
                "um.status_label = $status_label COLLATE NOCASE");
        }

        if (!string.IsNullOrWhiteSpace(query.ColorLabel))
        {
            command.Parameters.AddWithValue(
                "$color_label",
                query.ColorLabel.Trim());
            conditions.Add(
                "um.color_label = $color_label COLLATE NOCASE");
        }

        if (cursor is not null)
        {
            command.Parameters.AddWithValue("$cursor_id", cursor.Id);

            switch (query.SortOrder)
            {
                case AssetSortOrder.ModifiedNewest:
                    var newestTicks = cursor.ModifiedAtUtcTicks
                        ?? throw new ArgumentException(
                            "Modified sort cursor is missing its timestamp.",
                            nameof(cursor));
                    command.Parameters.AddWithValue(
                        "$cursor_modified",
                        newestTicks);
                    conditions.Add(
                        """
                        (
                            a.modified_at_utc_ticks < $cursor_modified
                            OR (
                                a.modified_at_utc_ticks = $cursor_modified
                                AND a.id < $cursor_id
                            )
                        )
                        """);
                    break;

                case AssetSortOrder.ModifiedOldest:
                    var oldestTicks = cursor.ModifiedAtUtcTicks
                        ?? throw new ArgumentException(
                            "Modified sort cursor is missing its timestamp.",
                            nameof(cursor));
                    command.Parameters.AddWithValue(
                        "$cursor_modified",
                        oldestTicks);
                    conditions.Add(
                        """
                        (
                            a.modified_at_utc_ticks > $cursor_modified
                            OR (
                                a.modified_at_utc_ticks = $cursor_modified
                                AND a.id > $cursor_id
                            )
                        )
                        """);
                    break;

                case AssetSortOrder.FileNameAscending:
                    var ascendingName = cursor.FileName
                        ?? throw new ArgumentException(
                            "Filename sort cursor is missing its filename.",
                            nameof(cursor));
                    command.Parameters.AddWithValue(
                        "$cursor_file_name",
                        ascendingName);
                    conditions.Add(
                        """
                        (
                            a.file_name COLLATE NOCASE > $cursor_file_name COLLATE NOCASE
                            OR (
                                a.file_name COLLATE NOCASE = $cursor_file_name COLLATE NOCASE
                                AND a.id > $cursor_id
                            )
                        )
                        """);
                    break;

                case AssetSortOrder.FileNameDescending:
                    var descendingName = cursor.FileName
                        ?? throw new ArgumentException(
                            "Filename sort cursor is missing its filename.",
                            nameof(cursor));
                    command.Parameters.AddWithValue(
                        "$cursor_file_name",
                        descendingName);
                    conditions.Add(
                        """
                        (
                            a.file_name COLLATE NOCASE < $cursor_file_name COLLATE NOCASE
                            OR (
                                a.file_name COLLATE NOCASE = $cursor_file_name COLLATE NOCASE
                                AND a.id < $cursor_id
                            )
                        )
                        """);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(query.SortOrder));
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
                    asset.ModifiedAtUtc.UtcDateTime.Ticks),
            AssetSortOrder.FileNameAscending
                or AssetSortOrder.FileNameDescending =>
                new AssetQueryCursor(
                    sortOrder,
                    asset.Id,
                    FileName: asset.FileName),
            _ => throw new ArgumentOutOfRangeException(
                nameof(sortOrder))
        };

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
    }

    private static string? NormalizeOptionalLabel(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > 64)
        {
            throw new ArgumentException(
                "Label may not exceed 64 characters.",
                parameterName);
        }

        return normalized;
    }

    private static IReadOnlyList<NormalizedTag> NormalizeTags(
        IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<NormalizedTag>();
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

            var name = value.Trim().Normalize(
                NormalizationForm.FormKC);
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

    private static IReadOnlyList<string> SplitSearchTerms(
        string searchText)
    {
        var normalized = searchText
            .Trim()
            .ToLowerInvariant();

        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        return normalized.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
    }

    private static bool ContainsCjk(string value)
    {
        foreach (var character in value)
        {
            if (IsCjk(character))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> ExtractTrigrams(
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

    private static IReadOnlyList<string> ExtractCjkBigrams(
        string value)
    {
        var result = new HashSet<string>(
            StringComparer.Ordinal);

        for (var index = 0; index + 1 < value.Length; index++)
        {
            if (IsCjk(value[index])
                && IsCjk(value[index + 1]))
            {
                result.Add(value.Substring(index, 2));
            }
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

    private static string QuoteFtsPhrase(string value) =>
        "\"" + value.Replace(
            "\"",
            "\"\"",
            StringComparison.Ordinal) + "\"";

    private sealed record NormalizedTag(
        string Name,
        string Key);
}
