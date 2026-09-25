using System.Security.Cryptography;
using NetVips;

namespace Lumine.Image;

public sealed record SourceTechnicalMetadata(
    int Width,
    int Height,
    int RawWidth,
    int RawHeight,
    bool HasAlpha,
    string Format,
    string ContentSha256)
{
    public long EstimatedRgbaBytes => checked((long)Width * Height * 4L);
}

public sealed class ImageSourceSnapshot : IDisposable
{
    private readonly FileStream _guard;

    private ImageSourceSnapshot(
        string sourcePath,
        FileStream guard,
        long fileSize,
        long modifiedAtUtcTicks,
        SourceTechnicalMetadata metadata)
    {
        SourcePath = sourcePath;
        _guard = guard;
        FileSize = fileSize;
        ModifiedAtUtcTicks = modifiedAtUtcTicks;
        Metadata = metadata;
    }

    public string SourcePath { get; }

    public long FileSize { get; }

    public long ModifiedAtUtcTicks { get; }

    public SourceTechnicalMetadata Metadata { get; }

    public static Task<ImageSourceSnapshot> OpenAsync(
        string sourcePath,
        long expectedFileSize,
        long expectedModifiedAtUtcTicks,
        string? expectedContentSha256 = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedFileSize);

        return Task.Run(
            () => Open(
                sourcePath,
                expectedFileSize,
                expectedModifiedAtUtcTicks,
                expectedContentSha256,
                cancellationToken),
            cancellationToken);
    }

    internal static ImageSourceSnapshot Open(
        string sourcePath,
        long expectedFileSize,
        long expectedModifiedAtUtcTicks,
        string? expectedContentSha256,
        CancellationToken cancellationToken)
    {
        VipsRuntimePolicy.EnsureConfigured();
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(sourcePath);
        var guard = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);

        try
        {
            var info = new FileInfo(fullPath);
            info.Refresh();
            ValidateStat(
                fullPath,
                info,
                expectedFileSize,
                expectedModifiedAtUtcTicks);

            using var incremental = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = guard.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                incremental.AppendData(buffer, 0, read);
            }

            var contentSha256 = Convert.ToHexString(
                    incremental.GetHashAndReset())
                .ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(expectedContentSha256)
                && !string.Equals(
                    expectedContentSha256,
                    contentSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ImageSourceChangedException(
                    fullPath,
                    "Content SHA-256 no longer matches the persisted source identity.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var raw = NetVips.Image.NewFromFile(
                fullPath,
                access: Enums.Access.Sequential,
                failOn: Enums.FailOn.Error);
            using var oriented = raw.Autorot();

            cancellationToken.ThrowIfCancellationRequested();

            info.Refresh();
            ValidateStat(
                fullPath,
                info,
                expectedFileSize,
                expectedModifiedAtUtcTicks);

            var metadata = new SourceTechnicalMetadata(
                oriented.Width,
                oriented.Height,
                raw.Width,
                raw.Height,
                oriented.HasAlpha(),
                NormalizeFormat(fullPath),
                contentSha256);

            return new ImageSourceSnapshot(
                fullPath,
                guard,
                info.Length,
                info.LastWriteTimeUtc.Ticks,
                metadata);
        }
        catch
        {
            guard.Dispose();
            throw;
        }
    }

    public void Dispose() => _guard.Dispose();

    private static void ValidateStat(
        string sourcePath,
        FileInfo info,
        long expectedFileSize,
        long expectedModifiedAtUtcTicks)
    {
        if (!info.Exists)
        {
            throw new FileNotFoundException(
                "Image source no longer exists.",
                sourcePath);
        }

        if (info.Length != expectedFileSize
            || info.LastWriteTimeUtc.Ticks != expectedModifiedAtUtcTicks)
        {
            throw new ImageSourceChangedException(
                sourcePath,
                $"Expected size={expectedFileSize:N0}, modified={expectedModifiedAtUtcTicks}, actual size={info.Length:N0}, modified={info.LastWriteTimeUtc.Ticks}.");
        }
    }

    private static string NormalizeFormat(string path)
    {
        var format = Path.GetExtension(path)
            .TrimStart('.')
            .ToLowerInvariant();

        return format switch
        {
            "jpg" => "jpeg",
            "tif" => "tiff",
            _ => format
        };
    }
}

public sealed class ImageSourceChangedException : IOException
{
    public ImageSourceChangedException(
        string sourcePath,
        string detail)
        : base($"Image source changed while preparing a stable snapshot: {detail}")
    {
        SourcePath = sourcePath;
    }

    public string SourcePath { get; }
}
