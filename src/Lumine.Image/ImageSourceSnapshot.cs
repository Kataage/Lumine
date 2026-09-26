using System.Globalization;
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
        int orientation,
        bool hasEmbeddedIcc,
        long decodedSourceBytes,
        SourceTechnicalMetadata metadata)
    {
        SourcePath = sourcePath;
        _guard = guard;
        FileSize = fileSize;
        ModifiedAtUtcTicks = modifiedAtUtcTicks;
        Orientation = orientation;
        HasEmbeddedIcc = hasEmbeddedIcc;
        DecodedSourceBytes = decodedSourceBytes;
        Metadata = metadata;
    }

    public string SourcePath { get; }

    public long FileSize { get; }

    public long ModifiedAtUtcTicks { get; }

    public int Orientation { get; }

    public bool HasEmbeddedIcc { get; }

    public long DecodedSourceBytes { get; }

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
            var orientation = ReadOrientation(raw);
            var hasEmbeddedIcc = raw.Contains("icc-profile-data");
            var decodedSourceBytes = EstimateDecodedSourceBytes(raw);
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
                orientation,
                hasEmbeddedIcc,
                decodedSourceBytes,
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

    private static long EstimateDecodedSourceBytes(
        NetVips.Image image)
    {
        var bytesPerSample = image.Format switch
        {
            Enums.BandFormat.Uchar => 1,
            Enums.BandFormat.Char => 1,
            Enums.BandFormat.Ushort => 2,
            Enums.BandFormat.Short => 2,
            Enums.BandFormat.Uint => 4,
            Enums.BandFormat.Int => 4,
            Enums.BandFormat.Float => 4,
            Enums.BandFormat.Complex => 8,
            Enums.BandFormat.Double => 8,
            Enums.BandFormat.Dpcomplex => 16,
            _ => 1
        };

        return checked(
            (long)image.Width
            * image.Height
            * image.Bands
            * bytesPerSample);
    }

    private static int ReadOrientation(
        NetVips.Image image)
    {
        try
        {
            if (!image.Contains("orientation"))
            {
                return 1;
            }

            var value = Convert.ToInt32(
                image.Get("orientation"),
                CultureInfo.InvariantCulture);

            return value is >= 1 and <= 8
                ? value
                : 0;
        }
        catch (Exception exception)
            when (exception is VipsException
                  or InvalidCastException
                  or FormatException
                  or OverflowException)
        {
            // 0 means the source advertised orientation metadata but it could
            // not be trusted. Adaptive full-resolution policy treats every
            // non-1 value, including unknown, as Random.
            return 0;
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
