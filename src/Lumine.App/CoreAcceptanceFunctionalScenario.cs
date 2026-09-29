using System.Globalization;
using Lumine.Diagnostics;
using Lumine.Library;

namespace Lumine.App;

internal static class CoreAcceptanceFunctionalScenario
{
    public static async Task RunAsync(
        BenchmarkRecorder recorder,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(metadata);

        var root = Path.Combine(
            Path.GetTempPath(),
            $"lumine-core-acceptance-{Guid.NewGuid():N}");
        var libraryRoot =
            Path.Combine(root, "library");
        var databasePath =
            Path.Combine(root, "data", "library.db");

        Directory.CreateDirectory(
            libraryRoot);

        var initialPath =
            Path.Combine(
                libraryRoot,
                "initial.jpg");
        await File.WriteAllBytesAsync(
            initialPath,
            [1, 2, 3],
            cancellationToken);

        try
        {
            var service =
                new LibraryService(
                    databasePath);
            await service.InitializeAsync(
                cancellationToken);

            var library =
                await service.RegisterLibraryAsync(
                    "Core acceptance scratch",
                    libraryRoot,
                    cancellationToken);

            using (recorder.Measure(
                       "acceptance.functional.live_changes"))
            {
                await using var sync =
                    await service.StartWindowsSyncAsync(
                        library.Id,
                        cancellationToken);

                _ = await WaitForAssetAsync(
                    service,
                    library.Id,
                    "initial.jpg",
                    static asset => asset.FileSize == 3,
                    "Scratch bootstrap did not index the initial file.",
                    cancellationToken);

                var livePath =
                    Path.Combine(
                        libraryRoot,
                        "live.jpg");
                await File.WriteAllBytesAsync(
                    livePath,
                    [10, 20, 30, 40],
                    cancellationToken);

                var created =
                    await WaitForAssetAsync(
                        service,
                        library.Id,
                        "live.jpg",
                        static asset => asset.FileSize == 4,
                        "Scratch watcher did not apply create.",
                        cancellationToken);

                await Task.Delay(
                    75,
                    cancellationToken);

                await File.WriteAllBytesAsync(
                    livePath,
                    [1, 2, 3, 4, 5, 6],
                    cancellationToken);
                File.SetLastWriteTimeUtc(
                    livePath,
                    DateTime.UtcNow.AddSeconds(1));

                var modified =
                    await WaitForAssetAsync(
                        service,
                        library.Id,
                        "live.jpg",
                        asset =>
                            asset.SourceRevision
                                > created.SourceRevision
                            && asset.FileSize == 6,
                        "Scratch watcher did not apply modify.",
                        cancellationToken);

                var renamedPath =
                    Path.Combine(
                        libraryRoot,
                        "renamed.jpg");
                File.Move(
                    livePath,
                    renamedPath);

                var renamed =
                    await WaitForAssetAsync(
                        service,
                        library.Id,
                        "renamed.jpg",
                        asset =>
                            asset.Id == modified.Id,
                        "Scratch watcher did not preserve identity across rename.",
                        cancellationToken);

                if (renamed.Id != modified.Id)
                {
                    throw new InvalidOperationException(
                        "Scratch rename changed stable asset identity.");
                }

                File.Delete(
                    renamedPath);

                await WaitUntilAsync(
                    async () =>
                        await service.GetAssetAsync(
                            library.Id,
                            "renamed.jpg",
                            cancellationToken)
                        is null,
                    "Scratch watcher did not apply delete.",
                    cancellationToken);

                await Task.Delay(
                    250,
                    cancellationToken);
                var reconciliationsBefore =
                    sync.Diagnostics.Reconciliations;
                await Task.Delay(
                    500,
                    cancellationToken);
                var reconciliationsAfter =
                    sync.Diagnostics.Reconciliations;

                if (reconciliationsBefore
                    != reconciliationsAfter)
                {
                    throw new InvalidOperationException(
                        "Idle scratch watcher performed periodic reconciliation.");
                }

                metadata[
                    "functional.live_events_observed"] =
                    sync.Diagnostics.EventsObserved.ToString(
                        CultureInfo.InvariantCulture);
                metadata[
                    "functional.live_max_apply_latency_ms"] =
                    sync.Diagnostics.MaxApplyLatencyMs.ToString(
                        "F3",
                        CultureInfo.InvariantCulture);
            }

            var offlinePath =
                Path.Combine(
                    libraryRoot,
                    "offline-日本語.jpg");
            await File.WriteAllBytesAsync(
                offlinePath,
                [5, 4, 3, 2, 1],
                cancellationToken);

            LibrarySyncBootstrapMode restartMode;

            using (recorder.Measure(
                       "acceptance.functional.offline_recovery"))
            {
                await using var restarted =
                    await service.StartWindowsSyncAsync(
                        library.Id,
                        cancellationToken);

                restartMode =
                    restarted.BootstrapMode;

                _ = await WaitForAssetAsync(
                    service,
                    library.Id,
                    "offline-日本語.jpg",
                    static asset => asset.FileSize == 5,
                    "Scratch restart did not recover an offline file.",
                    cancellationToken);
            }

            metadata[
                "functional.restart_bootstrap_mode"] =
                restartMode.ToString();

            var offline =
                await service.GetAssetAsync(
                    library.Id,
                    "offline-日本語.jpg",
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Scratch offline asset disappeared before metadata acceptance.");

            using (recorder.Measure(
                       "acceptance.functional.metadata_edit"))
            {
                var written =
                    await service.SetUserMetadataAsync(
                        library.Id,
                        offline.Id,
                        new AssetUserMetadataUpdate(
                            Rating: 5,
                            Favorite: true,
                            Notes: "猫耳 acceptance reference",
                            StatusLabel: "accepted",
                            ColorLabel: "blue",
                            Tags:
                            [
                                "推し",
                                "acceptance"
                            ]),
                        cancellationToken);

                if (written.Rating != 5
                    || !written.Favorite
                    || !written.Tags.Contains(
                        "推し"))
                {
                    throw new InvalidOperationException(
                        "Scratch metadata edit did not round-trip.");
                }
            }

            using (recorder.Measure(
                       CoreMetricNames.LibrarySearchJapaneseShort))
            {
                var japanese =
                    await service.GetAssetPageAsync(
                        library.Id,
                        new AssetQuery(
                            SearchText: "猫耳"),
                        10,
                        cancellationToken:
                            cancellationToken);

                if (japanese.Items.Count != 1
                    || japanese.Items[0].Id
                        != offline.Id)
                {
                    throw new InvalidOperationException(
                        "Scratch Japanese short search failed.");
                }
            }

            using (recorder.Measure(
                       CoreMetricNames.LibrarySearchAscii))
            {
                var filename =
                    await service.GetAssetPageAsync(
                        library.Id,
                        new AssetQuery(
                            SearchText: "offline"),
                        10,
                        cancellationToken:
                            cancellationToken);

                if (filename.Items.Count != 1
                    || filename.Items[0].Id
                        != offline.Id)
                {
                    throw new InvalidOperationException(
                        "Scratch filename search failed.");
                }
            }

            using (recorder.Measure(
                       CoreMetricNames.LibrarySearchTagFilter))
            {
                var composed =
                    await service.GetAssetPageAsync(
                        library.Id,
                        new AssetQuery(
                            SearchText: "reference",
                            RequiredTags: ["推し"],
                            Favorite: true,
                            MinRating: 5,
                            StatusLabel: "accepted",
                            ColorLabel: "blue"),
                        10,
                        cancellationToken:
                            cancellationToken);

                if (composed.Items.Count != 1
                    || composed.Items[0].Id
                        != offline.Id)
                {
                    throw new InvalidOperationException(
                        "Scratch composed metadata/tag filter failed.");
                }
            }

            var sorted =
                await service.GetAssetPageAsync(
                    library.Id,
                    new AssetQuery(
                        SortOrder:
                            AssetSortOrder.FileNameAscending),
                    10,
                    cancellationToken:
                        cancellationToken);

            var names =
                sorted.Items
                    .Select(
                        static asset =>
                            asset.FileName)
                    .ToArray();

            if (!names.SequenceEqual(
                    names.Order(
                        StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Scratch filename sort was not stable.");
            }

            metadata[
                "functional.search_metadata"] =
                "pass";
            metadata[
                "functional.live_changes"] =
                "pass";
            metadata[
                "functional.offline_recovery"] =
                "pass";

            await service.CheckpointAsync(
                cancellationToken);
        }
        finally
        {
            LibraryDatabase.ClearPools();

            if (Directory.Exists(root))
            {
                Directory.Delete(
                    root,
                    recursive: true);
            }
        }
    }

    private static async Task<AssetInfo> WaitForAssetAsync(
        LibraryService service,
        long libraryId,
        string relativePath,
        Func<AssetInfo, bool> predicate,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        AssetInfo? found = null;

        await WaitUntilAsync(
            async () =>
            {
                found =
                    await service.GetAssetAsync(
                        libraryId,
                        relativePath,
                        cancellationToken);

                return found is not null
                    && predicate(found);
            },
            failureMessage,
            cancellationToken);

        return found!;
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> predicate,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTime.UtcNow
            + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await predicate())
            {
                return;
            }

            await Task.Delay(
                25,
                cancellationToken);
        }

        throw new TimeoutException(
            failureMessage);
    }
}
