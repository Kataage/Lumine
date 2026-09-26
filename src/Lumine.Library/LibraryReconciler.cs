using Lumine.Core;

namespace Lumine.Library;

public sealed class LibraryReconciler
{
    private const int MaxFailureSamples = 32;

    private readonly LibraryRepository _repository;

    public LibraryReconciler(LibraryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Task<LibraryReconcileResult> ReconcileAsync(
        long libraryId,
        IProgress<LibraryScanProgress>? progress = null,
        int batchSize = 2048,
        CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        return LibraryBackgroundExecution.RunAsync(
            token => ReconcileCoreAsync(
                libraryId,
                progress,
                batchSize,
                token),
            cancellationToken);
    }

    private async Task<LibraryReconcileResult> ReconcileCoreAsync(
        long libraryId,
        IProgress<LibraryScanProgress>? progress,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var library = await _repository.GetLibraryAsync(
            libraryId,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Library {libraryId} does not exist.");

        var generation = await _repository.BeginReconcileGenerationAsync(
            libraryId,
            cancellationToken).ConfigureAwait(false);

        await using var ingest = await _repository.OpenIngestSessionAsync(
            libraryId,
            cancellationToken).ConfigureAwait(false);

        var batch = new List<AssetUpsert>(batchSize);
        var failures = new List<LibraryScanFailure>(MaxFailureSamples);
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(library.RootPath);

        var discovered = 0;
        var persisted = 0;
        var skipped = 0;
        string? current = null;

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pendingDirectories.Pop();

            try
            {
                WindowsFilesystemSemantics.RequireCaseInsensitiveDirectory(directory);

                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch (Exception exception) when (IsFilesystemFailure(exception))
                    {
                        skipped++;
                        AddFailureSample(failures, entry, "attributes", exception);
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if ((attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            pendingDirectories.Push(entry);
                        }

                        continue;
                    }

                    if (!LibraryFileTypes.IsSupportedPath(entry))
                    {
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(entry);
                        current = LibraryPaths.NormalizeRelativePath(
                            Path.GetRelativePath(library.RootPath, entry));

                        batch.Add(
                            new AssetUpsert(
                                current,
                                info.Length,
                                new DateTimeOffset(info.LastWriteTimeUtc),
                                Format: LibraryFileTypes.GetFormat(entry),
                                ObservationGeneration: generation));
                        discovered++;

                        if (batch.Count >= batchSize)
                        {
                            var flush = await FlushBatchAsync(
                                libraryId,
                                library.RootPath,
                                ingest,
                                batch,
                                failures,
                                cancellationToken).ConfigureAwait(false);
                            persisted += flush.Persisted;
                            skipped += flush.Skipped;
                            batch.Clear();

                            progress?.Report(
                                new LibraryScanProgress(
                                    discovered,
                                    persisted,
                                    skipped,
                                    current));
                        }
                    }
                    catch (Exception exception) when (IsFilesystemFailure(exception))
                    {
                        skipped++;
                        AddFailureSample(failures, entry, "metadata", exception);
                    }
                }
            }
            catch (Exception exception) when (IsFilesystemFailure(exception))
            {
                skipped++;
                AddFailureSample(failures, directory, "enumerate", exception);
            }
        }

        if (batch.Count > 0)
        {
            var flush = await FlushBatchAsync(
                libraryId,
                library.RootPath,
                ingest,
                batch,
                failures,
                cancellationToken).ConfigureAwait(false);
            persisted += flush.Persisted;
            skipped += flush.Skipped;
            batch.Clear();
        }

        var finished = DateTimeOffset.UtcNow;
        long deleted = 0;
        var completed = skipped == 0;

        if (completed)
        {
            deleted = await _repository.CompleteReconcileAsync(
                libraryId,
                generation,
                finished,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _repository.MarkReconcileRequiredAsync(
                libraryId,
                "Filesystem enumeration or source-identity validation was incomplete; destructive reconciliation was skipped.",
                cancellationToken).ConfigureAwait(false);
        }

        progress?.Report(
            new LibraryScanProgress(
                discovered,
                persisted,
                skipped,
                current));

        return new LibraryReconcileResult(
            generation,
            discovered,
            persisted,
            skipped,
            deleted,
            completed,
            finished,
            failures);
    }

    private async Task<(int Persisted, int Skipped)> FlushBatchAsync(
        long libraryId,
        string libraryRoot,
        LibraryIngestSession ingest,
        IReadOnlyList<AssetUpsert> batch,
        List<LibraryScanFailure> failures,
        CancellationToken cancellationToken)
    {
        var pathKeys = batch
            .Select(static item =>
                LibraryPaths.RelativePathKey(item.RelativePath))
            .ToArray();

        var tracked = await _repository.LoadTrackedSourceIdentitiesAsync(
            libraryId,
            pathKeys,
            cancellationToken).ConfigureAwait(false);

        if (tracked.Count == 0)
        {
            var written = await ingest.WriteBatchAsync(
                batch,
                cancellationToken).ConfigureAwait(false);
            return (written, 0);
        }

        var prepared = new List<AssetUpsert>(batch.Count);
        var skipped = 0;

        foreach (var item in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pathKey = LibraryPaths.RelativePathKey(
                item.RelativePath);

            if (!tracked.TryGetValue(pathKey, out var previous)
                || previous.FileSize != item.FileSize
                || previous.ModifiedAtUtcTicks
                    != item.ModifiedAtUtc.UtcDateTime.Ticks)
            {
                prepared.Add(item);
                continue;
            }

            var fullPath = Path.GetFullPath(
                Path.Combine(
                    libraryRoot,
                    item.RelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

            try
            {
                var currentIdentity = FileSourceIdentityProbe.Read(
                    fullPath,
                    cancellationToken);
                var info = new FileInfo(fullPath);
                info.Refresh();

                if (!info.Exists)
                {
                    throw new FileNotFoundException(
                        "Reconciliation source disappeared before identity validation.",
                        fullPath);
                }

                var currentModifiedTicks =
                    info.LastWriteTimeUtc.Ticks;
                var sameStat =
                    previous.FileSize == info.Length
                    && previous.ModifiedAtUtcTicks
                        == currentModifiedTicks;

                prepared.Add(
                    item with
                    {
                        FileSize = info.Length,
                        ModifiedAtUtc = new DateTimeOffset(
                            new DateTime(
                                currentModifiedTicks,
                                DateTimeKind.Utc)),
                        ForceSourceRevision =
                            sameStat
                            && !string.Equals(
                                previous.SourceIdentity,
                                currentIdentity.Value,
                                StringComparison.Ordinal)
                    });
            }
            catch (Exception exception)
                when (IsFilesystemFailure(exception))
            {
                skipped++;
                AddFailureSample(
                    failures,
                    fullPath,
                    "source-identity",
                    exception);
            }
        }

        if (prepared.Count == 0)
        {
            return (0, skipped);
        }

        var persisted = await ingest.WriteBatchAsync(
            prepared,
            cancellationToken).ConfigureAwait(false);
        return (persisted, skipped);
    }

    private static bool IsFilesystemFailure(Exception exception) =>
        exception is IOException
        or UnauthorizedAccessException
        or System.Security.SecurityException;

    private static void AddFailureSample(
        List<LibraryScanFailure> failures,
        string path,
        string operation,
        Exception exception)
    {
        if (failures.Count >= MaxFailureSamples)
        {
            return;
        }

        failures.Add(
            new LibraryScanFailure(
                path,
                operation,
                exception.GetType().Name));
    }
}
