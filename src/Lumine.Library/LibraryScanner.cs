namespace Lumine.Library;

public sealed class LibraryScanner
{
    private const int MaxFailureSamples = 32;

    private static readonly HashSet<string> SupportedExtensions = new(
        [
            ".jpg", ".jpeg", ".png", ".webp", ".gif",
            ".avif", ".heif", ".heic", ".bmp", ".tif", ".tiff"
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly LibraryRepository _repository;

    public LibraryScanner(LibraryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Task<LibraryScanResult> ScanAsync(
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
            token => ScanCoreAsync(libraryId, progress, batchSize, token),
            cancellationToken);
    }

    private async Task<LibraryScanResult> ScanCoreAsync(
        long libraryId,
        IProgress<LibraryScanProgress>? progress,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var library = await _repository.GetLibraryAsync(libraryId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Library {libraryId} does not exist.");

        await _repository.MarkScanStartedAsync(
            libraryId,
            DateTimeOffset.UtcNow,
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

                    if (!SupportedExtensions.Contains(Path.GetExtension(entry)))
                    {
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(entry);
                        current = LibraryPaths.NormalizeRelativePath(
                            Path.GetRelativePath(library.RootPath, entry));
                        var extension = Path.GetExtension(entry).TrimStart('.').ToLowerInvariant();

                        batch.Add(new AssetUpsert(
                            current,
                            info.Length,
                            new DateTimeOffset(info.LastWriteTimeUtc),
                            Format: extension));

                        discovered++;

                        if (batch.Count >= batchSize)
                        {
                            persisted += await ingest.WriteBatchAsync(
                                batch,
                                cancellationToken).ConfigureAwait(false);
                            batch.Clear();
                            progress?.Report(
                                new LibraryScanProgress(discovered, persisted, skipped, current));
                        }
                    }
                    catch (Exception exception) when (IsFilesystemFailure(exception))
                    {
                        skipped++;
                        AddFailureSample(failures, entry, "metadata", exception);
                        progress?.Report(
                            new LibraryScanProgress(discovered, persisted, skipped, current));
                    }
                }
            }
            catch (Exception exception) when (IsFilesystemFailure(exception))
            {
                skipped++;
                AddFailureSample(failures, directory, "enumerate", exception);
                progress?.Report(
                    new LibraryScanProgress(discovered, persisted, skipped, current));
            }
        }

        if (batch.Count > 0)
        {
            persisted += await ingest.WriteBatchAsync(
                batch,
                cancellationToken).ConfigureAwait(false);
        }

        var finished = DateTimeOffset.UtcNow;
        var completed = skipped == 0;

        await _repository.MarkScanFinishedAsync(
            libraryId,
            finished,
            completed,
            cancellationToken).ConfigureAwait(false);

        progress?.Report(new LibraryScanProgress(discovered, persisted, skipped, current));
        return new LibraryScanResult(
            discovered,
            persisted,
            skipped,
            completed,
            finished,
            failures);
    }

    private static bool IsFilesystemFailure(Exception exception) =>
        exception is IOException
        or UnauthorizedAccessException
        or System.Security.SecurityException;

    private static void AddFailureSample(
        ICollection<LibraryScanFailure> failures,
        string path,
        string operation,
        Exception exception)
    {
        if (failures.Count >= MaxFailureSamples)
        {
            return;
        }

        failures.Add(new LibraryScanFailure(
            path,
            operation,
            exception.GetType().Name));
    }
}
