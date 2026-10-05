using System.Globalization;
using NetVips;

namespace Lumine.Image;

public sealed record ImageExifMetadata(
    string? CameraModel,
    string? LensModel,
    string? FocalLength,
    string? Aperture,
    string? ShutterSpeed,
    int? Iso,
    string? CapturedAt,
    string? GpsLatitude,
    string? GpsLongitude)
{
    public bool HasValues =>
        !string.IsNullOrWhiteSpace(CameraModel)
        || !string.IsNullOrWhiteSpace(LensModel)
        || !string.IsNullOrWhiteSpace(FocalLength)
        || !string.IsNullOrWhiteSpace(Aperture)
        || !string.IsNullOrWhiteSpace(ShutterSpeed)
        || Iso.HasValue
        || !string.IsNullOrWhiteSpace(CapturedAt)
        || !string.IsNullOrWhiteSpace(GpsLatitude)
        || !string.IsNullOrWhiteSpace(GpsLongitude);
}

public static class ImageExifMetadataProbe
{
    private const int MaxConcurrentProbes = 2;
    private static readonly SemaphoreSlim ProbeGate =
        new(MaxConcurrentProbes, MaxConcurrentProbes);

    public static async Task<ImageExifMetadata> ProbeAsync(
        string sourcePath,
        long expectedFileSize,
        long expectedModifiedAtUtcTicks,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedFileSize);

        await ProbeGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => ProbeCore(
                    sourcePath,
                    expectedFileSize,
                    expectedModifiedAtUtcTicks,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ProbeGate.Release();
        }
    }

    private static ImageExifMetadata ProbeCore(
        string sourcePath,
        long expectedFileSize,
        long expectedModifiedAtUtcTicks,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(sourcePath);
        var info = new FileInfo(fullPath);
        info.Refresh();
        ValidateSource(
            fullPath,
            info,
            expectedFileSize,
            expectedModifiedAtUtcTicks);

        using var image = NetVips.Image.NewFromFile(
            fullPath,
            access: Enums.Access.Sequential,
            failOn: Enums.FailOn.Error);

        cancellationToken.ThrowIfCancellationRequested();

        var fields = image.GetFields();
        var cameraModel =
            Primary(
                ReadField(
                    image,
                    fields,
                    "exif-ifd0-Model"));
        var lensModel =
            Primary(
                ReadField(
                    image,
                    fields,
                    "exif-ifd2-LensModel"));
        var focalLength =
            ExifDisplay(
                ReadField(
                    image,
                    fields,
                    "exif-ifd2-FocalLength"));
        var aperture =
            ExifDisplay(
                ReadField(
                    image,
                    fields,
                    "exif-ifd2-FNumber"));
        var shutterSpeed =
            ExifDisplay(
                ReadField(
                    image,
                    fields,
                    "exif-ifd2-ExposureTime"));
        var iso =
            ParseIso(
                ReadField(
                    image,
                    fields,
                    "exif-ifd2-ISOSpeedRatings",
                    "exif-ifd2-PhotographicSensitivity"));
        var capturedAt =
            Primary(
                ReadField(
                    image,
                    fields,
                    "exif-ifd2-DateTimeOriginal",
                    "exif-ifd0-DateTime"));
        var latitude =
            FormatGps(
                ReadFieldBySuffix(
                    image,
                    fields,
                    "-GPSLatitude"),
                ReadFieldBySuffix(
                    image,
                    fields,
                    "-GPSLatitudeRef"));
        var longitude =
            FormatGps(
                ReadFieldBySuffix(
                    image,
                    fields,
                    "-GPSLongitude"),
                ReadFieldBySuffix(
                    image,
                    fields,
                    "-GPSLongitudeRef"));

        cancellationToken.ThrowIfCancellationRequested();

        info.Refresh();
        ValidateSource(
            fullPath,
            info,
            expectedFileSize,
            expectedModifiedAtUtcTicks);

        return new ImageExifMetadata(
            cameraModel,
            lensModel,
            focalLength,
            aperture,
            shutterSpeed,
            iso,
            capturedAt,
            latitude,
            longitude);
    }

    private static void ValidateSource(
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
            || info.LastWriteTimeUtc.Ticks
                != expectedModifiedAtUtcTicks)
        {
            throw new ImageSourceChangedException(
                sourcePath,
                $"Expected size={expectedFileSize:N0}, modified={expectedModifiedAtUtcTicks}; actual size={info.Length:N0}, modified={info.LastWriteTimeUtc.Ticks}.");
        }
    }

    private static string? ReadField(
        NetVips.Image image,
        IReadOnlyList<string> fields,
        params string[] names)
    {
        foreach (var name in names)
        {
            var field =
                fields.FirstOrDefault(
                    candidate =>
                        string.Equals(
                            candidate,
                            name,
                            StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                continue;
            }

            var value = TryGet(image, field);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? ReadFieldBySuffix(
        NetVips.Image image,
        IReadOnlyList<string> fields,
        string suffix)
    {
        var field =
            fields.FirstOrDefault(
                candidate =>
                    candidate.StartsWith(
                        "exif-",
                        StringComparison.OrdinalIgnoreCase)
                    && candidate.EndsWith(
                        suffix,
                        StringComparison.OrdinalIgnoreCase));

        return field is null
            ? null
            : TryGet(image, field);
    }

    private static string? TryGet(
        NetVips.Image image,
        string field)
    {
        try
        {
            var value = image.Get(field);
            return value?.ToString();
        }
        catch (Exception exception)
            when (exception is VipsException
                  or InvalidCastException
                  or FormatException
                  or OverflowException)
        {
            return null;
        }
    }

    private static string? Primary(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed =
            raw.Trim()
                .TrimEnd('\0');
        var metadataStart =
            trimmed.IndexOf(
                " (",
                StringComparison.Ordinal);
        if (metadataStart > 0)
        {
            trimmed =
                trimmed[..metadataStart]
                    .Trim();
        }

        return string.IsNullOrWhiteSpace(trimmed)
            ? null
            : trimmed;
    }

    private static string? ExifDisplay(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        var open = trimmed.IndexOf('(');
        if (open >= 0)
        {
            var comma =
                trimmed.IndexOf(
                    ',',
                    open + 1);
            if (comma > open + 1)
            {
                var display =
                    trimmed[(open + 1)..comma]
                        .Trim();
                if (!string.IsNullOrWhiteSpace(display))
                {
                    return display;
                }
            }
        }

        return Primary(trimmed);
    }

    private static int? ParseIso(
        string? raw)
    {
        var primary = Primary(raw);
        if (string.IsNullOrWhiteSpace(primary))
        {
            return null;
        }

        var token =
            primary
                .Split(
                    [' ', ',', ';'],
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

        return int.TryParse(
                   token,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out var value)
               && value is >= 0 and <= 10_000_000
            ? value
            : null;
    }

    private static string? FormatGps(
        string? rawValue,
        string? rawReference)
    {
        var value = Primary(rawValue);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var reference = Primary(rawReference);
        if (string.IsNullOrWhiteSpace(reference))
        {
            return value;
        }

        return $"{reference} {value}";
    }

    internal static string? PrimaryForSmoke(
        string? raw) =>
        Primary(raw);

    internal static string? ExifDisplayForSmoke(
        string? raw) =>
        ExifDisplay(raw);

    internal static int? ParseIsoForSmoke(
        string? raw) =>
        ParseIso(raw);

    internal static int MaxConcurrentProbesForSmoke =>
        MaxConcurrentProbes;
}
