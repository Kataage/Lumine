namespace Lumine.Library;

public sealed partial class LumineV1Importer
{
    public const string SourceKind = "lumine-v1";

    private const int MaxNotesLength = 16_384;
    private const int MaxTagsPerAsset = 128;
    private const int MaxTagNameLength = 128;
    private const int MaxLabelLength = 64;
    private const int LatestKnownV1Migration = 6;

    private readonly LibraryDatabase _destination;

    public LumineV1Importer(LibraryDatabase destination)
    {
        _destination = destination
            ?? throw new ArgumentNullException(nameof(destination));
    }

    public static string GetDefaultSourceDatabasePath()
    {
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "lumine", "lumine.db");
    }

    public async Task<LumineV1ImportPreview> PreviewAsync(
        string sourceDatabasePath,
        CancellationToken cancellationToken = default)
    {
        using var snapshot = await SourceSnapshot.CreateAsync(
            sourceDatabasePath,
            cancellationToken).ConfigureAwait(false);
        await using var source = await OpenSnapshotAsync(
            snapshot.DatabasePath,
            cancellationToken).ConfigureAwait(false);

        return await AnalyzeAsync(
            source,
            snapshot,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LumineV1ImportResult> ImportAsync(
        string sourceDatabasePath,
        string? expectedSourceFingerprintSha256 = null,
        IProgress<LumineV1ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var snapshot = await SourceSnapshot.CreateAsync(
            sourceDatabasePath,
            cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(
                expectedSourceFingerprintSha256)
            && !string.Equals(
                expectedSourceFingerprintSha256,
                snapshot.FingerprintSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new LumineV1SourceChangedException(
                "The Lumine v1 database changed after preview. Run preview again before importing.");
        }

        await using var source = await OpenSnapshotAsync(
            snapshot.DatabasePath,
            cancellationToken).ConfigureAwait(false);

        var preview = await AnalyzeAsync(
            source,
            snapshot,
            cancellationToken).ConfigureAwait(false);

        if (!preview.CanImport)
        {
            throw new LumineV1ImportException(
                "The Lumine v1 database contains blocking migration diagnostics. Review the preview before importing.");
        }

        // Import is the mutating operation. Preview above never opens or
        // migrates the destination database.
        await _destination.InitializeAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var destination =
            await _destination.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
        using var transaction = destination.BeginTransaction();

        if (await HasImportProvenanceAsync(
                destination,
                transaction,
                preview.SourceFingerprintSha256,
                cancellationToken).ConfigureAwait(false))
        {
            transaction.Rollback();
            return new LumineV1ImportResult(
                preview,
                true,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        var sourceLibraries = await LoadSourceLibrariesAsync(
            source,
            cancellationToken).ConfigureAwait(false);
        var tagSummaries = await LoadTagSummariesAsync(
            source,
            cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
        var libraryMap = new Dictionary<long, DestinationLibrary>();
        var folders = new Dictionary<string, long>(
            StringComparer.Ordinal);
        var assetMap = new Dictionary<long, DestinationAsset>();

        var librariesImported = 0;
        var librariesMatched = 0;
        var assetsImported = 0;
        var assetsMatched = 0;
        var metadataImported = 0;
        var metadataConflicts = 0;
        var tagAssignmentsImported = 0;
        var librariesProcessed = 0;
        var assetsProcessed = 0;

        foreach (var legacyLibrary in sourceLibraries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedRoot =
                LibraryPaths.NormalizeRoot(legacyLibrary.RootPath);
            var rootKey = LibraryPaths.RootKey(normalizedRoot);

            var existingLibraryId =
                await FindDestinationLibraryIdAsync(
                    destination,
                    transaction,
                    rootKey,
                    cancellationToken).ConfigureAwait(false);

            long destinationLibraryId;
            if (existingLibraryId is { } matchedId)
            {
                destinationLibraryId = matchedId;
                librariesMatched++;
            }
            else
            {
                destinationLibraryId =
                    await InsertDestinationLibraryAsync(
                        destination,
                        transaction,
                        legacyLibrary.Name,
                        normalizedRoot,
                        rootKey,
                        now,
                        cancellationToken).ConfigureAwait(false);
                librariesImported++;
            }

            libraryMap.Add(
                legacyLibrary.Id,
                new DestinationLibrary(
                    destinationLibraryId,
                    normalizedRoot));

            librariesProcessed++;
            progress?.Report(
                new LumineV1ImportProgress(
                    librariesProcessed,
                    assetsProcessed));
            cancellationToken.ThrowIfCancellationRequested();
        }

        await using (var command = source.CreateCommand())
        {
            command.CommandText = LegacyAssetSelectSql;
            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var legacy = ReadLegacyAsset(reader);
                if (!libraryMap.TryGetValue(
                        legacy.LibraryId,
                        out var destinationLibrary))
                {
                    throw new LumineV1ImportException(
                        $"Source asset {legacy.Id} references missing library {legacy.LibraryId}.");
                }

                if (!TryMapRelativePath(
                        destinationLibrary.RootPath,
                        legacy.FilePath,
                        out var relativePath))
                {
                    throw new LumineV1ImportException(
                        $"Source asset {legacy.Id} is outside its registered library root.");
                }

                var folderPath =
                    LibraryPaths.FolderRelativePath(relativePath);
                long? folderId = null;
                if (folderPath.Length > 0)
                {
                    folderId = await GetOrCreateFolderAsync(
                        destination,
                        transaction,
                        folders,
                        destinationLibrary.Id,
                        folderPath,
                        now,
                        cancellationToken).ConfigureAwait(false);
                }

                var relativePathKey =
                    LibraryPaths.RelativePathKey(relativePath);
                var existingAssetId =
                    await FindDestinationAssetIdAsync(
                        destination,
                        transaction,
                        destinationLibrary.Id,
                        relativePathKey,
                        cancellationToken).ConfigureAwait(false);

                long destinationAssetId;
                if (existingAssetId is { } matchedAssetId)
                {
                    destinationAssetId = matchedAssetId;
                    assetsMatched++;
                }
                else
                {
                    destinationAssetId =
                        await InsertDestinationAssetAsync(
                            destination,
                            transaction,
                            destinationLibrary.Id,
                            folderId,
                            relativePath,
                            relativePathKey,
                            legacy,
                            now,
                            cancellationToken).ConfigureAwait(false);
                    assetsImported++;
                }

                var hasTags =
                    tagSummaries.TryGetValue(
                        legacy.Id,
                        out var tagSummary)
                    && tagSummary.Count > 0;
                var hasMetadata =
                    HasMeaningfulMetadata(legacy, hasTags);
                var allowTagImport = false;

                if (hasMetadata)
                {
                    var destinationHasMetadata =
                        await HasDestinationUserMetadataAsync(
                            destination,
                            transaction,
                            destinationAssetId,
                            cancellationToken).ConfigureAwait(false);

                    if (destinationHasMetadata)
                    {
                        // Presence of a v2 user-metadata row means the user may
                        // already have edited this asset after scanning it into
                        // v2. Preserve v2 rather than silently overwriting it.
                        metadataConflicts++;
                    }
                    else
                    {
                        await InsertDestinationUserMetadataAsync(
                            destination,
                            transaction,
                            destinationAssetId,
                            destinationLibrary.Id,
                            legacy,
                            now,
                            cancellationToken).ConfigureAwait(false);
                        metadataImported++;
                        allowTagImport = hasTags;
                    }
                }

                assetMap.Add(
                    legacy.Id,
                    new DestinationAsset(
                        destinationAssetId,
                        destinationLibrary.Id,
                        allowTagImport));

                assetsProcessed++;
                progress?.Report(
                    new LumineV1ImportProgress(
                        librariesProcessed,
                        assetsProcessed));
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        await using (var tags = source.CreateCommand())
        {
            tags.CommandText =
                """
                SELECT
                    at.asset_id,
                    t.name
                FROM asset_tags AS at
                INNER JOIN tags AS t
                    ON t.id = at.tag_id
                ORDER BY at.asset_id ASC, t.id ASC;
                """;

            await using var reader = await tags.ExecuteReaderAsync(
                cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourceAssetId = reader.GetInt64(0);
                if (!assetMap.TryGetValue(
                        sourceAssetId,
                        out var target)
                    || !target.AllowTagImport)
                {
                    continue;
                }

                var normalized = NormalizeTag(
                    reader.GetString(1));

                var tagId = await GetOrCreateTagAsync(
                    destination,
                    transaction,
                    target.LibraryId,
                    normalized.Name,
                    normalized.Key,
                    now,
                    cancellationToken).ConfigureAwait(false);

                tagAssignmentsImported +=
                    await AssignTagAsync(
                        destination,
                        transaction,
                        target.AssetId,
                        tagId,
                        now,
                        cancellationToken).ConfigureAwait(false);
            }
        }

        await InsertImportProvenanceAsync(
            destination,
            transaction,
            preview,
            librariesImported,
            librariesMatched,
            assetsImported,
            assetsMatched,
            metadataImported,
            metadataConflicts,
            tagAssignmentsImported,
            now,
            cancellationToken).ConfigureAwait(false);

        transaction.Commit();

        return new LumineV1ImportResult(
            preview,
            false,
            librariesImported,
            librariesMatched,
            assetsImported,
            assetsMatched,
            metadataImported,
            metadataConflicts,
            tagAssignmentsImported);
    }

}
