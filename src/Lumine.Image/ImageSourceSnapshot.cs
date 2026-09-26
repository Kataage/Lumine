using Lumine.Core;
using NetVips;

namespace Lumine.Image;

public sealed record SourceTechnicalMetadata(
    int Width,
    int Height,
    int RawWidth,
    int RawHeight,
    bool HasAlpha,
    string Format,
    string SourceIdentity,
    bool UsedFullHash,
    long BytesHashed)
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
        string? expectedSourceIdentity = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedFileSize);

        return Task.Run(
            () => Open(
                sourcePath,
                expectedFileSize,
                expectedModifiedAtUtcTicks,
                expectedSourceIdentity,
                cancellationToken),
            cancellationToken);
    }

    internal static ImageSourceSnapshot Open(
        string sourcePath,
        long expectedFileSize,
        long expectedModifiedAtUtcTicks,
        string? expectedSourceIdentity,
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

            var identity = FileSourceIdentityProbe.Read(
                guard,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(expectedSourceIdentity)
                && !string.Equals(
                    expectedSourceIdentity,
                    identity.Value,
                    StringComparison.Ordinal))
            {
                throw new ImageSourceChangedException(
                    fullPath,
                    $"Source identity changed from '{expectedSourceIdentity}' to '{identity.Value}'.");
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
                DetectActualFormat(raw, fullPath),
                identity.Value,
                identity.UsedFullHash,
                identity.BytesHashed);

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

    private static string DetectActualFormat(
        NetVips.Image image,
        string path)
    {
        string? loader = null;

        try
        {
            loader = image.Get("vips-loader")?.ToString();
        }
        catch (VipsException)
        {
        }

        if (!string.IsNullOrWhiteSpace(loader))
        {
            var normalized = loader.ToLowerInvariant();

            if (normalized.Contains("jpeg", StringComparison.Ordinal))
            {
                return "jpeg";
            }

            if (normalized.Contains("png", StringComparison.Ordinal))
            {
                return "png";
            }

            if (normalized.Contains("webp", StringComparison.Ordinal))
            {
                return "webp";
            }

            if (normalized.Contains("heif", StringComparison.Ordinal))
            {
                return string.Equals(
                        Path.GetExtension(path),
                        ".avif",
                        StringComparison.OrdinalIgnoreCase)
                    ? "avif"
                    : "heif";
            }

            if (normalized.Contains("tiff", StringComparison.Ordinal))
            {
                return "tiff";
            }

            if (normalized.Contains("gif", StringComparison.Ordinal))
            {
                return "gif";
            }

            if (normalized.Contains("avif", StringComparison.Ordinal))
            {
                return "avif";
            }

            if (normalized.Contains("bmp", StringComparison.Ordinal))
            {
                return "bmp";
            }
        }

        var extension = Path.GetExtension(path)
            .TrimStart('.')
            .ToLowerInvariant();

        return extension switch
        {
            "jpg" => "jpeg",
            "tif" => "tiff",
            _ => extension
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
