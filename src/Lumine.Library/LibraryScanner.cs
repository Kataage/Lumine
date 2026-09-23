namespace Lumine.Library;

public sealed class LibraryScanner
{
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

    public async Task<LibraryScanResult> ScanAsync(
        long libraryId,
        IProgress<LibraryScanProgress>? progress = null,
        int batchSize = 512,
        CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        var library = await _repository.GetLibraryAsync(libraryId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Library {libraryId} does not exist.");

        var batch = new List<AssetUpsert>(batchSize);
        var discovered = 0;
        var persisted = 0;
        var skipped = 0;
        string? current = null;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var filePath in Directory.EnumerateFiles(library.RootPath, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!SupportedExtensions.Contains(Path.GetExtension(filePath)))
            {
                continue;
            }

            try
            {
                var info = new FileInfo(filePath);
                current = LibraryPaths.NormalizeRelativePath(Path.GetRelativePath(library.RootPath, filePath));
                var extension = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();

                batch.Add(new AssetUpsert(
                    current,
                    info.Length,
                    new DateTimeOffset(info.LastWriteTimeUtc),
                    Format: extension));

                discovered++;

                if (batch.Count >= batchSize)
                {
                    persisted += await _repository.UpsertAssetsAsync(
                        libraryId,
                        batch,
                        cancellationToken).ConfigureAwait(false);
                    batch.Clear();
                    progress?.Report(new LibraryScanProgress(discovered, persisted, skipped, current));
                }
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException
                or FileNotFoundException
                or DirectoryNotFoundException)
            {
                skipped++;
                progress?.Report(new LibraryScanProgress(discovered, persisted, skipped, current));
            }
        }

        if (batch.Count > 0)
        {
            persisted += await _repository.UpsertAssetsAsync(
                libraryId,
                batch,
                cancellationToken).ConfigureAwait(false);
        }

        var finished = DateTimeOffset.UtcNow;
        var completed = skipped == 0;
        if (completed)
        {
            await _repository.MarkScanCompletedAsync(libraryId, finished, cancellationToken).ConfigureAwait(false);
        }

        progress?.Report(new LibraryScanProgress(discovered, persisted, skipped, current));
        return new LibraryScanResult(discovered, persisted, skipped, completed, finished);
    }
}
