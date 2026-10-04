using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed partial class LibraryRepository
{
    public const int PublicationPageSize = 100;
    public const int InspectorPublicationLimit = 24;
    public const int PublicationSummaryAssetLimit = 12;
    public async Task<WorkInfo> CreateWorkAsync(
        long libraryId,
        WorkCreate create,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentNullException.ThrowIfNull(create);
        var title = RequireText(create.Title, nameof(create.Title), 256);
        var assetIds = ValidateOrderedAssetIds(create.AssetIds, nameof(create.AssetIds));
        var now = DateTimeOffset.UtcNow.UtcDateTime.Ticks;

        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        await EnsureAssetsBelongToLibraryAsync(
            connection, transaction, libraryId, assetIds, cancellationToken).ConfigureAwait(false);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO works(
                library_id, title, description, cover_asset_id,
                created_at_utc_ticks, updated_at_utc_ticks)
            VALUES(
                $library_id, $title, $description, $cover_asset_id,
                $created, $updated)
            RETURNING id;
            """;
        insert.Parameters.AddWithValue("$library_id", libraryId);
        insert.Parameters.AddWithValue("$title", title);
        insert.Parameters.AddWithValue("$description", create.Description ?? string.Empty);
        insert.Parameters.AddWithValue(
            "$cover_asset_id",
            assetIds.Length > 0 ? assetIds[0] : DBNull.Value);
        insert.Parameters.AddWithValue("$created", now);
        insert.Parameters.AddWithValue("$updated", now);
        var workId = Convert.ToInt64(
            await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);

        await InsertOrderedMembershipAsync(
            connection,
            transaction,
            "work_assets",
            "work_id",
            workId,
            assetIds,
            includePrimary: false,
            cancellationToken).ConfigureAwait(false);

        transaction.Commit();
        return await GetWorkAsync(
            libraryId, workId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Created work could not be reloaded.");
    }

    public async Task<GenerationGroupInfo> CreateGenerationGroupAsync(
        long libraryId,
        GenerationGroupCreate create,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentNullException.ThrowIfNull(create);
        var name = RequireText(create.Name, nameof(create.Name), 256);
        var assetIds = ValidateOrderedAssetIds(create.AssetIds, nameof(create.AssetIds));
        if (create.Steps < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(create), "Steps must be non-negative.");
        }

        if (create.CfgScale < 0
            || double.IsNaN(create.CfgScale)
            || double.IsInfinity(create.CfgScale))
        {
            throw new ArgumentOutOfRangeException(nameof(create), "CFG must be a finite non-negative value.");
        }

        var now = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        await EnsureAssetsBelongToLibraryAsync(
            connection, transaction, libraryId, assetIds, cancellationToken).ConfigureAwait(false);
        await EnsureWorkBelongsToLibraryAsync(
            connection, transaction, libraryId, create.WorkId, cancellationToken).ConfigureAwait(false);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO generation_groups(
                library_id, work_id, name,
                prompt, negative_prompt, model_name,
                sampler, scheduler, steps, cfg_scale,
                workflow_json, notes,
                created_at_utc_ticks, updated_at_utc_ticks)
            VALUES(
                $library_id, $work_id, $name,
                $prompt, $negative_prompt, $model_name,
                $sampler, $scheduler, $steps, $cfg,
                $workflow, $notes,
                $created, $updated)
            RETURNING id;
            """;
        insert.Parameters.AddWithValue("$library_id", libraryId);
        insert.Parameters.AddWithValue("$work_id", (object?)create.WorkId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$name", name);
        insert.Parameters.AddWithValue("$prompt", create.Prompt ?? string.Empty);
        insert.Parameters.AddWithValue("$negative_prompt", create.NegativePrompt ?? string.Empty);
        insert.Parameters.AddWithValue("$model_name", create.ModelName ?? string.Empty);
        insert.Parameters.AddWithValue("$sampler", create.Sampler ?? string.Empty);
        insert.Parameters.AddWithValue("$scheduler", create.Scheduler ?? string.Empty);
        insert.Parameters.AddWithValue("$steps", create.Steps);
        insert.Parameters.AddWithValue("$cfg", create.CfgScale);
        insert.Parameters.AddWithValue("$workflow", create.WorkflowJson ?? string.Empty);
        insert.Parameters.AddWithValue("$notes", create.Notes ?? string.Empty);
        insert.Parameters.AddWithValue("$created", now);
        insert.Parameters.AddWithValue("$updated", now);
        var groupId = Convert.ToInt64(
            await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);

        await InsertOrderedMembershipAsync(
            connection,
            transaction,
            "generation_group_assets",
            "generation_group_id",
            groupId,
            assetIds,
            includePrimary: true,
            cancellationToken).ConfigureAwait(false);

        transaction.Commit();
        return await GetGenerationGroupAsync(
            libraryId, groupId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Created generation group could not be reloaded.");
    }

    public async Task<AssetRelationInfo> CreateAssetRelationAsync(
        long libraryId,
        AssetRelationCreate create,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(create.ParentAssetId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(create.ChildAssetId);
        if (create.ParentAssetId == create.ChildAssetId)
        {
            throw new ArgumentException("Asset lineage cannot link an asset to itself.", nameof(create));
        }

        var relationType =
            RequireText(create.RelationType, nameof(create.RelationType), 64)
                .ToLowerInvariant();
        var now = DateTimeOffset.UtcNow.UtcDateTime.Ticks;

        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        await EnsureAssetsBelongToLibraryAsync(
            connection,
            transaction,
            libraryId,
            [create.ParentAssetId, create.ChildAssetId],
            cancellationToken).ConfigureAwait(false);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO asset_relations(
                library_id, parent_asset_id, child_asset_id,
                relation_type, note, created_at_utc_ticks)
            VALUES(
                $library_id, $parent, $child,
                $type, $note, $created)
            RETURNING id;
            """;
        insert.Parameters.AddWithValue("$library_id", libraryId);
        insert.Parameters.AddWithValue("$parent", create.ParentAssetId);
        insert.Parameters.AddWithValue("$child", create.ChildAssetId);
        insert.Parameters.AddWithValue("$type", relationType);
        insert.Parameters.AddWithValue("$note", create.Note ?? string.Empty);
        insert.Parameters.AddWithValue("$created", now);
        var relationId = Convert.ToInt64(
            await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);

        transaction.Commit();
        return await GetAssetRelationAsync(
            libraryId, relationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Created asset relation could not be reloaded.");
    }

    public async Task<PublicationInfo> CreatePublicationAsync(
        long libraryId,
        PublicationCreate create,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentNullException.ThrowIfNull(create);
        var destination = RequireText(create.Destination, nameof(create.Destination), 128);
        var assetIds = ValidateOrderedAssetIds(create.AssetIds, nameof(create.AssetIds));
        if (assetIds.Length == 0)
        {
            throw new ArgumentException("Publication requires at least one ordered asset.", nameof(create));
        }

        ValidateJsonObject(create.PlatformMetadataJson, nameof(create.PlatformMetadataJson));

        var now = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        await EnsureAssetsBelongToLibraryAsync(
            connection, transaction, libraryId, assetIds, cancellationToken).ConfigureAwait(false);
        await EnsureWorkBelongsToLibraryAsync(
            connection, transaction, libraryId, create.WorkId, cancellationToken).ConfigureAwait(false);
        var snapshots =
            await LoadAssetRefsAsync(
                connection, transaction, libraryId, assetIds, cancellationToken).ConfigureAwait(false);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO publications(
                library_id, work_id,
                title, body, tags_snapshot,
                destination, account, published_at_utc_ticks,
                external_id, external_url, platform_metadata_json,
                created_at_utc_ticks, updated_at_utc_ticks)
            VALUES(
                $library_id, $work_id,
                $title, $body, $tags,
                $destination, $account, $published,
                $external_id, $external_url, $metadata,
                $created, $updated)
            RETURNING id;
            """;
        insert.Parameters.AddWithValue("$library_id", libraryId);
        insert.Parameters.AddWithValue("$work_id", (object?)create.WorkId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$title", create.Title ?? string.Empty);
        insert.Parameters.AddWithValue("$body", create.Body ?? string.Empty);
        insert.Parameters.AddWithValue("$tags", create.TagsSnapshot ?? string.Empty);
        insert.Parameters.AddWithValue("$destination", destination);
        insert.Parameters.AddWithValue("$account", create.Account ?? string.Empty);
        insert.Parameters.AddWithValue(
            "$published",
            create.PublishedAtUtc.UtcDateTime.Ticks);
        insert.Parameters.AddWithValue("$external_id", create.ExternalId ?? string.Empty);
        insert.Parameters.AddWithValue("$external_url", create.ExternalUrl ?? string.Empty);
        insert.Parameters.AddWithValue(
            "$metadata",
            string.IsNullOrWhiteSpace(create.PlatformMetadataJson)
                ? "{}"
                : create.PlatformMetadataJson.Trim());
        insert.Parameters.AddWithValue("$created", now);
        insert.Parameters.AddWithValue("$updated", now);
        var publicationId = Convert.ToInt64(
            await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);

        await using var addAsset = connection.CreateCommand();
        addAsset.Transaction = transaction;
        addAsset.CommandText =
            """
            INSERT INTO publication_assets(
                publication_id, asset_id, sort_order,
                file_name_snapshot, relative_path_snapshot)
            VALUES(
                $publication_id, $asset_id, $sort_order,
                $file_name, $relative_path);
            """;
        addAsset.Parameters.AddWithValue("$publication_id", publicationId);
        var assetParameter =
            addAsset.Parameters.Add("$asset_id", SqliteType.Integer);
        var orderParameter =
            addAsset.Parameters.Add("$sort_order", SqliteType.Integer);
        var nameParameter =
            addAsset.Parameters.Add("$file_name", SqliteType.Text);
        var pathParameter =
            addAsset.Parameters.Add("$relative_path", SqliteType.Text);
        addAsset.Prepare();

        for (var index = 0; index < snapshots.Count; index++)
        {
            assetParameter.Value = snapshots[index].Id;
            orderParameter.Value = index;
            nameParameter.Value = snapshots[index].FileName;
            pathParameter.Value = snapshots[index].RelativePath;
            await addAsset.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        return await GetPublicationAsync(
            libraryId, publicationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Created publication could not be reloaded.");
    }

    public async Task<IReadOnlyList<WorkInfo>> ListWorksAsync(
        long libraryId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateListLimit(limit);
        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var ids = new List<long>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT id
                FROM works
                WHERE library_id = $library_id
                ORDER BY updated_at_utc_ticks DESC, id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$library_id", libraryId);
            command.Parameters.AddWithValue("$limit", limit);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ids.Add(reader.GetInt64(0));
            }
        }

        var result = new List<WorkInfo>(ids.Count);
        foreach (var id in ids)
        {
            var item = await GetWorkOnConnectionAsync(
                connection, null, libraryId, id, cancellationToken).ConfigureAwait(false);
            if (item is not null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<PublicationInfo>> ListPublicationsAsync(
        long libraryId,
        int limit = PublicationPageSize,
        CancellationToken cancellationToken = default) =>
        (await ListPublicationsPageAsync(
            libraryId,
            limit,
            cursor: null,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false)).Items;

    public async Task<PublicationPage> ListPublicationsPageAsync(
        long libraryId,
        int limit = PublicationPageSize,
        PublicationCursor? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ValidateListLimit(limit);

        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

        long totalCount;
        await using (var count = connection.CreateCommand())
        {
            count.CommandText =
                """
                SELECT COUNT(*)
                FROM publications
                WHERE library_id = $library_id;
                """;
            count.Parameters.AddWithValue(
                "$library_id",
                libraryId);
            totalCount = Convert.ToInt64(
                await count.ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        var ids =
            new List<long>(
                Math.Min(
                    limit + 1,
                    501));
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT p.id
                FROM publications AS p
                WHERE p.library_id = $library_id
                  AND (
                        $cursor_ticks IS NULL
                        OR p.published_at_utc_ticks < $cursor_ticks
                        OR (
                            p.published_at_utc_ticks = $cursor_ticks
                            AND p.id < $cursor_id
                        )
                      )
                ORDER BY
                    p.published_at_utc_ticks DESC,
                    p.id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue(
                "$library_id",
                libraryId);
            command.Parameters.AddWithValue(
                "$cursor_ticks",
                cursor is null
                    ? DBNull.Value
                    : cursor.PublishedAtUtc.UtcDateTime.Ticks);
            command.Parameters.AddWithValue(
                "$cursor_id",
                cursor?.Id ?? long.MaxValue);
            command.Parameters.AddWithValue(
                "$limit",
                limit + 1);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                ids.Add(
                    reader.GetInt64(0));
            }
        }

        var hasMore =
            ids.Count > limit;
        if (hasMore)
        {
            ids.RemoveAt(ids.Count - 1);
        }

        var items =
            await LoadPublicationsByIdsAsync(
                connection,
                transaction: null,
                libraryId: libraryId,
                publicationIds: ids,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        PublicationCursor? nextCursor = null;
        if (hasMore
            && items.Count > 0)
        {
            var last =
                items[^1];
            nextCursor =
                new PublicationCursor(
                    last.PublishedAtUtc,
                    last.Id);
        }

        return new PublicationPage(
            items,
            nextCursor,
            totalCount);
    }

    public async Task<AssetCreativeContext> GetAssetCreativeContextAsync(
        long libraryId,
        long assetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(assetId);
        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        await EnsureAssetsBelongToLibraryAsync(
            connection, transaction, libraryId, [assetId], cancellationToken).ConfigureAwait(false);

        var workIds = await ReadIdListAsync(
            connection,
            transaction,
            """
            SELECT w.id
            FROM work_assets AS wa
            INNER JOIN works AS w ON w.id = wa.work_id
            WHERE wa.asset_id = $asset_id
              AND w.library_id = $library_id
            ORDER BY w.updated_at_utc_ticks DESC, w.id DESC;
            """,
            libraryId,
            assetId,
            cancellationToken).ConfigureAwait(false);

        var groupIds = await ReadIdListAsync(
            connection,
            transaction,
            """
            SELECT g.id
            FROM generation_group_assets AS ga
            INNER JOIN generation_groups AS g
              ON g.id = ga.generation_group_id
            WHERE ga.asset_id = $asset_id
              AND g.library_id = $library_id
            ORDER BY g.updated_at_utc_ticks DESC, g.id DESC;
            """,
            libraryId,
            assetId,
            cancellationToken).ConfigureAwait(false);

        long publicationCount;
        await using (var publicationCountCommand =
                     connection.CreateCommand())
        {
            publicationCountCommand.Transaction =
                transaction;
            publicationCountCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM publication_assets AS pa
                INNER JOIN publications AS p
                  ON p.id = pa.publication_id
                WHERE pa.asset_id = $asset_id
                  AND p.library_id = $library_id;
                """;
            publicationCountCommand.Parameters.AddWithValue(
                "$library_id",
                libraryId);
            publicationCountCommand.Parameters.AddWithValue(
                "$asset_id",
                assetId);
            publicationCount = Convert.ToInt64(
                await publicationCountCommand
                    .ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        var publicationIds = await ReadIdListAsync(
            connection,
            transaction,
            $"""
            SELECT p.id
            FROM publication_assets AS pa
            INNER JOIN publications AS p ON p.id = pa.publication_id
            WHERE pa.asset_id = $asset_id
              AND p.library_id = $library_id
            ORDER BY p.published_at_utc_ticks DESC, p.id DESC
            LIMIT {InspectorPublicationLimit};
            """,
            libraryId,
            assetId,
            cancellationToken).ConfigureAwait(false);

        var works = new List<WorkInfo>(workIds.Count);
        foreach (var id in workIds)
        {
            var item = await GetWorkOnConnectionAsync(
                connection, transaction, libraryId, id, cancellationToken).ConfigureAwait(false);
            if (item is not null) works.Add(item);
        }

        var groups = new List<GenerationGroupInfo>(groupIds.Count);
        foreach (var id in groupIds)
        {
            var item = await GetGenerationGroupOnConnectionAsync(
                connection, transaction, libraryId, id, cancellationToken).ConfigureAwait(false);
            if (item is not null) groups.Add(item);
        }

        var relations = new List<AssetRelationInfo>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT
                    r.id, r.library_id,
                    r.parent_asset_id, parent.file_name, parent.relative_path,
                    r.child_asset_id, child.file_name, child.relative_path,
                    r.relation_type, r.note, r.created_at_utc_ticks
                FROM asset_relations AS r
                INNER JOIN assets AS parent ON parent.id = r.parent_asset_id
                INNER JOIN assets AS child ON child.id = r.child_asset_id
                WHERE r.library_id = $library_id
                  AND (r.parent_asset_id = $asset_id OR r.child_asset_id = $asset_id)
                ORDER BY r.created_at_utc_ticks DESC, r.id DESC;
                """;
            command.Parameters.AddWithValue("$library_id", libraryId);
            command.Parameters.AddWithValue("$asset_id", assetId);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                relations.Add(ReadRelation(reader));
            }
        }

        var publications =
            await LoadPublicationsByIdsAsync(
                connection,
                transaction,
                libraryId,
                publicationIds,
                cancellationToken).ConfigureAwait(false);

        transaction.Commit();
        return new AssetCreativeContext(
            works,
            groups,
            relations,
            publications,
            publicationCount);
    }

    public Task<WorkInfo?> GetWorkAsync(
        long libraryId,
        long workId,
        CancellationToken cancellationToken = default) =>
        ReadWithConnectionAsync(
            (connection, token) =>
                GetWorkOnConnectionAsync(
                    connection, null, libraryId, workId, token),
            cancellationToken);

    public Task<GenerationGroupInfo?> GetGenerationGroupAsync(
        long libraryId,
        long groupId,
        CancellationToken cancellationToken = default) =>
        ReadWithConnectionAsync(
            (connection, token) =>
                GetGenerationGroupOnConnectionAsync(
                    connection, null, libraryId, groupId, token),
            cancellationToken);

    public Task<AssetRelationInfo?> GetAssetRelationAsync(
        long libraryId,
        long relationId,
        CancellationToken cancellationToken = default) =>
        ReadWithConnectionAsync(
            async (connection, token) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT
                        r.id, r.library_id,
                        r.parent_asset_id, parent.file_name, parent.relative_path,
                        r.child_asset_id, child.file_name, child.relative_path,
                        r.relation_type, r.note, r.created_at_utc_ticks
                    FROM asset_relations AS r
                    INNER JOIN assets AS parent ON parent.id = r.parent_asset_id
                    INNER JOIN assets AS child ON child.id = r.child_asset_id
                    WHERE r.library_id = $library_id
                      AND r.id = $id;
                    """;
                command.Parameters.AddWithValue("$library_id", libraryId);
                command.Parameters.AddWithValue("$id", relationId);
                await using var reader =
                    await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                return await reader.ReadAsync(token).ConfigureAwait(false)
                    ? ReadRelation(reader)
                    : null;
            },
            cancellationToken);

    public Task<PublicationInfo?> GetPublicationAsync(
        long libraryId,
        long publicationId,
        CancellationToken cancellationToken = default) =>
        ReadWithConnectionAsync(
            (connection, token) =>
                GetPublicationOnConnectionAsync(
                    connection, null, libraryId, publicationId, token),
            cancellationToken);

    private async Task<T> ReadWithConnectionAsync<T>(
        Func<SqliteConnection, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await read(connection, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WorkInfo?> GetWorkOnConnectionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long libraryId,
        long workId,
        CancellationToken cancellationToken)
    {
        long id;
        string title;
        string description;
        long? cover;
        long created;
        long updated;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT
                    id, title, description, cover_asset_id,
                    created_at_utc_ticks, updated_at_utc_ticks
                FROM works
                WHERE library_id = $library_id
                  AND id = $id;
                """;
            command.Parameters.AddWithValue("$library_id", libraryId);
            command.Parameters.AddWithValue("$id", workId);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            id = reader.GetInt64(0);
            title = reader.GetString(1);
            description = reader.GetString(2);
            cover = reader.IsDBNull(3) ? null : reader.GetInt64(3);
            created = reader.GetInt64(4);
            updated = reader.GetInt64(5);
        }

        var assets = await LoadMembershipRefsAsync(
            connection,
            transaction,
            """
            SELECT a.id, a.file_name, a.relative_path
            FROM work_assets AS wa
            INNER JOIN assets AS a ON a.id = wa.asset_id
            WHERE wa.work_id = $owner_id
            ORDER BY wa.sort_order ASC;
            """,
            id,
            cancellationToken).ConfigureAwait(false);

        return new WorkInfo(
            id, libraryId, title, description, cover, assets,
            FromTicks(created), FromTicks(updated));
    }

    private static async Task<GenerationGroupInfo?> GetGenerationGroupOnConnectionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long libraryId,
        long groupId,
        CancellationToken cancellationToken)
    {
        long id;
        long? workId;
        string name;
        string prompt;
        string negative;
        string model;
        string sampler;
        string scheduler;
        int steps;
        double cfg;
        string workflow;
        string notes;
        long created;
        long updated;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT
                    id, work_id, name,
                    prompt, negative_prompt, model_name,
                    sampler, scheduler, steps, cfg_scale,
                    workflow_json, notes,
                    created_at_utc_ticks, updated_at_utc_ticks
                FROM generation_groups
                WHERE library_id = $library_id
                  AND id = $id;
                """;
            command.Parameters.AddWithValue("$library_id", libraryId);
            command.Parameters.AddWithValue("$id", groupId);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            id = reader.GetInt64(0);
            workId = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            name = reader.GetString(2);
            prompt = reader.GetString(3);
            negative = reader.GetString(4);
            model = reader.GetString(5);
            sampler = reader.GetString(6);
            scheduler = reader.GetString(7);
            steps = reader.GetInt32(8);
            cfg = reader.GetDouble(9);
            workflow = reader.GetString(10);
            notes = reader.GetString(11);
            created = reader.GetInt64(12);
            updated = reader.GetInt64(13);
        }

        var assets = await LoadMembershipRefsAsync(
            connection,
            transaction,
            """
            SELECT a.id, a.file_name, a.relative_path
            FROM generation_group_assets AS ga
            INNER JOIN assets AS a ON a.id = ga.asset_id
            WHERE ga.generation_group_id = $owner_id
            ORDER BY ga.sort_order ASC;
            """,
            id,
            cancellationToken).ConfigureAwait(false);

        return new GenerationGroupInfo(
            id, libraryId, workId, name,
            prompt, negative, model, sampler, scheduler,
            steps, cfg, workflow, notes, assets,
            FromTicks(created), FromTicks(updated));
    }

    private static async Task<IReadOnlyList<PublicationInfo>>
        LoadPublicationsByIdsAsync(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            long libraryId,
            IReadOnlyList<long> publicationIds,
            CancellationToken cancellationToken)
    {
        if (publicationIds.Count == 0)
        {
            return Array.Empty<PublicationInfo>();
        }

        var parameterNames =
            new string[publicationIds.Count];
        await using var publicationsCommand =
            connection.CreateCommand();
        publicationsCommand.Transaction =
            transaction;
        publicationsCommand.Parameters.AddWithValue(
            "$library_id",
            libraryId);

        for (var index = 0;
             index < publicationIds.Count;
             index++)
        {
            var parameterName =
                $"$publication_id_{index}";
            parameterNames[index] =
                parameterName;
            publicationsCommand.Parameters.AddWithValue(
                parameterName,
                publicationIds[index]);
        }

        publicationsCommand.CommandText =
            $"""
            SELECT
                id, work_id,
                title, body, tags_snapshot,
                destination, account, published_at_utc_ticks,
                external_id, external_url, platform_metadata_json,
                created_at_utc_ticks, updated_at_utc_ticks
            FROM publications
            WHERE library_id = $library_id
              AND id IN ({string.Join(", ", parameterNames)});
            """;

        var publications =
            new Dictionary<long, PublicationInfo>();
        await using (var reader =
                     await publicationsCommand
                         .ExecuteReaderAsync(cancellationToken)
                         .ConfigureAwait(false))
        {
            while (await reader
                       .ReadAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                var id =
                    reader.GetInt64(0);
                publications[id] =
                    new PublicationInfo(
                        id,
                        libraryId,
                        reader.IsDBNull(1)
                            ? null
                            : reader.GetInt64(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetString(5),
                        reader.GetString(6),
                        FromTicks(
                            reader.GetInt64(7)),
                        reader.GetString(8),
                        reader.GetString(9),
                        reader.GetString(10),
                        Array.Empty<PublicationAssetSnapshot>(),
                        FromTicks(
                            reader.GetInt64(11)),
                        FromTicks(
                            reader.GetInt64(12)));
            }
        }

        var assets =
            publicationIds.ToDictionary(
                static id => id,
                static _ =>
                    new List<PublicationAssetSnapshot>());
        var assetCounts =
            publicationIds.ToDictionary(
                static id => id,
                static _ => 0L);

        await using var assetsCommand =
            connection.CreateCommand();
        assetsCommand.Transaction =
            transaction;
        for (var index = 0;
             index < publicationIds.Count;
             index++)
        {
            assetsCommand.Parameters.AddWithValue(
                parameterNames[index],
                publicationIds[index]);
        }
        assetsCommand.Parameters.AddWithValue(
            "$asset_limit",
            PublicationSummaryAssetLimit);

        assetsCommand.CommandText =
            $"""
            SELECT
                publication_id,
                asset_id,
                file_name_snapshot,
                relative_path_snapshot,
                sort_order,
                asset_count
            FROM (
                SELECT
                    publication_id,
                    asset_id,
                    file_name_snapshot,
                    relative_path_snapshot,
                    sort_order,
                    COUNT(*) OVER (
                        PARTITION BY publication_id
                    ) AS asset_count,
                    ROW_NUMBER() OVER (
                        PARTITION BY publication_id
                        ORDER BY sort_order ASC
                    ) AS row_number
                FROM publication_assets
                WHERE publication_id IN (
                    {string.Join(", ", parameterNames)}
                )
            )
            WHERE row_number <= $asset_limit
            ORDER BY
                publication_id ASC,
                sort_order ASC;
            """;

        await using (var reader =
                     await assetsCommand
                         .ExecuteReaderAsync(cancellationToken)
                         .ConfigureAwait(false))
        {
            while (await reader
                       .ReadAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                var publicationId =
                    reader.GetInt64(0);
                if (!assets.TryGetValue(
                        publicationId,
                        out var snapshots))
                {
                    continue;
                }

                assetCounts[publicationId] =
                    reader.GetInt64(5);
                snapshots.Add(
                    new PublicationAssetSnapshot(
                        reader.IsDBNull(1)
                            ? null
                            : reader.GetInt64(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetInt32(4)));
            }
        }

        var result =
            new List<PublicationInfo>(
                publicationIds.Count);
        foreach (var publicationId in
                 publicationIds)
        {
            if (!publications.TryGetValue(
                    publicationId,
                    out var publication))
            {
                continue;
            }

            result.Add(
                publication with
                {
                    Assets =
                        assets[publicationId]
                            .ToArray(),
                    AssetCount =
                        assetCounts[publicationId]
                });
        }

        return result;
    }

    private static async Task<PublicationInfo?> GetPublicationOnConnectionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long libraryId,
        long publicationId,
        CancellationToken cancellationToken)
    {
        long id;
        long? workId;
        string title;
        string body;
        string tags;
        string destination;
        string account;
        long published;
        string externalId;
        string externalUrl;
        string metadata;
        long created;
        long updated;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT
                    id, work_id,
                    title, body, tags_snapshot,
                    destination, account, published_at_utc_ticks,
                    external_id, external_url, platform_metadata_json,
                    created_at_utc_ticks, updated_at_utc_ticks
                FROM publications
                WHERE library_id = $library_id
                  AND id = $id;
                """;
            command.Parameters.AddWithValue("$library_id", libraryId);
            command.Parameters.AddWithValue("$id", publicationId);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            id = reader.GetInt64(0);
            workId = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            title = reader.GetString(2);
            body = reader.GetString(3);
            tags = reader.GetString(4);
            destination = reader.GetString(5);
            account = reader.GetString(6);
            published = reader.GetInt64(7);
            externalId = reader.GetString(8);
            externalUrl = reader.GetString(9);
            metadata = reader.GetString(10);
            created = reader.GetInt64(11);
            updated = reader.GetInt64(12);
        }

        var assets = new List<PublicationAssetSnapshot>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT
                    asset_id,
                    file_name_snapshot,
                    relative_path_snapshot,
                    sort_order
                FROM publication_assets
                WHERE publication_id = $id
                ORDER BY sort_order ASC;
                """;
            command.Parameters.AddWithValue("$id", id);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                assets.Add(
                    new PublicationAssetSnapshot(
                        reader.IsDBNull(0) ? null : reader.GetInt64(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetInt32(3)));
            }
        }

        return new PublicationInfo(
            id, libraryId, workId,
            title, body, tags,
            destination, account, FromTicks(published),
            externalId, externalUrl, metadata,
            assets, FromTicks(created), FromTicks(updated),
            assets.Count);
    }

    private static AssetRelationInfo ReadRelation(
        SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetInt64(1),
            new CreativeAssetRef(
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4)),
            new CreativeAssetRef(
                reader.GetInt64(5),
                reader.GetString(6),
                reader.GetString(7)),
            reader.GetString(8),
            reader.GetString(9),
            FromTicks(reader.GetInt64(10)));

    private static async Task<List<long>> ReadIdListAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        long libraryId,
        long assetId,
        CancellationToken cancellationToken)
    {
        var result = new List<long>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue("$asset_id", assetId);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(reader.GetInt64(0));
        }

        return result;
    }

    private static async Task<IReadOnlyList<CreativeAssetRef>> LoadMembershipRefsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        long ownerId,
        CancellationToken cancellationToken)
    {
        var result = new List<CreativeAssetRef>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$owner_id", ownerId);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(
                new CreativeAssetRef(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<CreativeAssetRef>> LoadAssetRefsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        long[] orderedAssetIds,
        CancellationToken cancellationToken)
    {
        var result = new List<CreativeAssetRef>(orderedAssetIds.Length);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, file_name, relative_path
            FROM assets
            WHERE library_id = $library_id
              AND id = $asset_id;
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);
        var assetParameter =
            command.Parameters.Add("$asset_id", SqliteType.Integer);
        command.Prepare();

        foreach (var assetId in orderedAssetIds)
        {
            assetParameter.Value = assetId;
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"Asset {assetId} does not belong to library {libraryId}.");
            }

            result.Add(
                new CreativeAssetRef(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2)));
        }

        return result;
    }

    private static async Task EnsureAssetsBelongToLibraryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        long[] assetIds,
        CancellationToken cancellationToken)
    {
        if (assetIds.Length > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(assetIds),
                "Creative archive operations are limited to 10,000 assets.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM assets
                WHERE library_id = $library_id
                  AND id = $asset_id);
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);
        var assetParameter =
            command.Parameters.Add("$asset_id", SqliteType.Integer);
        command.Prepare();

        foreach (var assetId in assetIds)
        {
            assetParameter.Value = assetId;
            var exists = Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (exists != 1)
            {
                throw new InvalidOperationException(
                    $"Asset {assetId} does not belong to library {libraryId}.");
            }
        }
    }

    private static async Task EnsureWorkBelongsToLibraryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long libraryId,
        long? workId,
        CancellationToken cancellationToken)
    {
        if (workId is null)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM works
                WHERE library_id = $library_id
                  AND id = $work_id);
            """;
        command.Parameters.AddWithValue("$library_id", libraryId);
        command.Parameters.AddWithValue("$work_id", workId.Value);
        var exists = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (exists != 1)
        {
            throw new InvalidOperationException(
                $"Work {workId.Value} does not belong to library {libraryId}.");
        }
    }

    private static async Task InsertOrderedMembershipAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string ownerColumn,
        long ownerId,
        IReadOnlyList<long> assetIds,
        bool includePrimary,
        CancellationToken cancellationToken)
    {
        if (assetIds.Count == 0)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = includePrimary
            ? $"""
               INSERT INTO {table}(
                   {ownerColumn}, asset_id, sort_order, is_primary)
               VALUES(
                   $owner_id, $asset_id, $sort_order, $is_primary);
               """
            : $"""
               INSERT INTO {table}(
                   {ownerColumn}, asset_id, sort_order, role)
               VALUES(
                   $owner_id, $asset_id, $sort_order, 'member');
               """;
        command.Parameters.AddWithValue("$owner_id", ownerId);
        var assetParameter =
            command.Parameters.Add("$asset_id", SqliteType.Integer);
        var orderParameter =
            command.Parameters.Add("$sort_order", SqliteType.Integer);
        SqliteParameter? primaryParameter = null;
        if (includePrimary)
        {
            primaryParameter =
                command.Parameters.Add("$is_primary", SqliteType.Integer);
        }

        command.Prepare();
        for (var index = 0; index < assetIds.Count; index++)
        {
            assetParameter.Value = assetIds[index];
            orderParameter.Value = index;
            if (primaryParameter is not null)
            {
                primaryParameter.Value = index == 0 ? 1 : 0;
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static long[] ValidateOrderedAssetIds(
        IReadOnlyList<long> assetIds,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        if (assetIds.Count > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Creative archive operations are limited to 10,000 assets.");
        }

        var seen = new HashSet<long>();
        var result = new long[assetIds.Count];
        for (var index = 0; index < assetIds.Count; index++)
        {
            var id = assetIds[index];
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id, parameterName);
            if (!seen.Add(id))
            {
                throw new ArgumentException(
                    $"Duplicate asset id {id} would make ordered membership ambiguous.",
                    parameterName);
            }

            result[index] = id;
        }

        return result;
    }

    private static string RequireText(
        string? value,
        string parameterName,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Value must be at most {maxLength} characters.");
        }

        return trimmed;
    }

    private static void ValidateJsonObject(
        string? value,
        string parameterName)
    {
        var json =
            string.IsNullOrWhiteSpace(value)
                ? "{}"
                : value.Trim();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    "Platform metadata must be a JSON object.",
                    parameterName);
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "Platform metadata must be valid JSON.",
                parameterName,
                exception);
        }
    }

    private static void ValidateListLimit(int limit)
    {
        if (limit is <= 0 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "List limit must be between 1 and 500.");
        }
    }

}
