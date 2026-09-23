using System.Security.Cryptography;
using System.Text;
using NetVips;

namespace Lumine.Image;

public sealed class ThumbnailCache
{
    public const int GeneratorVersion = 1;

    private readonly string _rootPath;

    public ThumbnailCache(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        VipsRuntimePolicy.EnsureConfigured();

        _rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);
        CleanupInterruptedWrites();
    }

    public string RootPath => _rootPath;

    public static string GetCacheKey(ThumbnailSource source, ThumbnailProfile profile)
    {
        ValidateSource(source);
        ValidateProfile(profile);

        var payload = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{GeneratorVersion}|{source.AssetId}|{source.SourceRevision}|{source.FileSize}|{source.ModifiedAtUtcTicks}|{profile.Id}|{profile.Version}|{profile.MaxWidth}|{profile.MaxHeight}|{profile.Quality}");

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public string GetCachePath(string cacheKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);

        if (cacheKey.Length != 64 || cacheKey.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Thumbnail cache key must be a SHA-256 hex digest.", nameof(cacheKey));
        }

        return Path.Combine(
            _rootPath,
            cacheKey[..2],
            cacheKey.Substring(2, 2),
            cacheKey + ".webp");
    }

    internal ThumbnailResult? TryOpenValid(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetCachePath(cacheKey);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var image = NetVips.Image.NewFromFile(
                path,
                access: Enums.Access.Sequential,
                failOn: Enums.FailOn.Error);

            if (image.Width <= 0 || image.Height <= 0)
            {
                DeleteBestEffort(path);
                return null;
            }

            var width = image.Width;
            var height = image.Height;
            image.Invalidate();

            var length = new FileInfo(path).Length;
            if (length <= 0)
            {
                DeleteBestEffort(path);
                return null;
            }

            return new ThumbnailResult(
                cacheKey,
                path,
                true,
                width,
                height,
                length);
        }
        catch (Exception exception) when (
            exception is VipsException
            or IOException
            or UnauthorizedAccessException)
        {
            DeleteBestEffort(path);
            return null;
        }
    }

    internal string CreateTemporaryPath(string cacheKey)
    {
        var target = GetCachePath(cacheKey);
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);

        return Path.Combine(
            directory,
            $"{cacheKey}.{Guid.NewGuid():N}.tmp.webp");
    }

    internal string CommitTemporaryFile(string cacheKey, string temporaryPath)
    {
        var target = GetCachePath(cacheKey);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        try
        {
            File.Move(temporaryPath, target, overwrite: false);
        }
        catch (IOException) when (File.Exists(target))
        {
            DeleteBestEffort(temporaryPath);
        }

        return target;
    }

    public Task<ThumbnailCacheStats> GetStatsAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => GetStats(cancellationToken), cancellationToken);

    public Task<ThumbnailPruneResult> PruneAsync(
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        return Task.Run(
            () => Prune(maxBytes, cancellationToken),
            cancellationToken);
    }

    private ThumbnailCacheStats GetStats(CancellationToken cancellationToken)
    {
        long count = 0;
        long bytes = 0;
        long interrupted = 0;

        foreach (var path in Directory.EnumerateFiles(
                     _rootPath,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (path.EndsWith(".tmp.webp", StringComparison.OrdinalIgnoreCase))
            {
                interrupted++;
                continue;
            }

            if (!path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var info = new FileInfo(path);
                count++;
                bytes += info.Length;
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException)
            {
            }
        }

        return new ThumbnailCacheStats(count, bytes, interrupted);
    }

    private ThumbnailPruneResult Prune(long maxBytes, CancellationToken cancellationToken)
    {
        var files = new List<FileInfo>();

        foreach (var path in Directory.EnumerateFiles(
                     _rootPath,
                     "*.webp",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (path.EndsWith(".tmp.webp", StringComparison.OrdinalIgnoreCase))
            {
                DeleteBestEffort(path);
                continue;
            }

            try
            {
                files.Add(new FileInfo(path));
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException)
            {
            }
        }

        var bytesBefore = files.Sum(static file => file.Length);
        var filesBefore = files.Count;
        var currentBytes = bytesBefore;
        long filesDeleted = 0;
        long bytesDeleted = 0;

        foreach (var file in files.OrderBy(static file => file.LastWriteTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (currentBytes <= maxBytes)
            {
                break;
            }

            try
            {
                var length = file.Length;
                file.Delete();
                currentBytes -= length;
                filesDeleted++;
                bytesDeleted += length;
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException)
            {
            }
        }

        return new ThumbnailPruneResult(
            filesBefore,
            bytesBefore,
            filesDeleted,
            bytesDeleted,
            filesBefore - filesDeleted,
            currentBytes);
    }

    private void CleanupInterruptedWrites()
    {
        foreach (var path in Directory.EnumerateFiles(
                     _rootPath,
                     "*.tmp.webp",
                     SearchOption.AllDirectories))
        {
            DeleteBestEffort(path);
        }
    }

    private static void DeleteBestEffort(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException)
        {
        }
    }

    internal static void ValidateSource(ThumbnailSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourcePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(source.AssetId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(source.SourceRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(source.FileSize);
    }

    internal static void ValidateProfile(ThumbnailProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(profile.MaxWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(profile.MaxHeight);

        if (profile.Quality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "Thumbnail quality must be between 1 and 100.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(profile.Version);
    }
}
