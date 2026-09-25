using NetVips;

namespace Lumine.Image;

public sealed record FullResolutionSource(
    string SourcePath,
    long FileSize,
    long ModifiedAtUtcTicks,
    string? ContentSha256 = null);

public sealed record FullResolutionInfo(
    int Width,
    int Height,
    bool HasAlpha,
    long EstimatedRgbaBytes,
    string? ContentSha256 = null,
    int? RawWidth = null,
    int? RawHeight = null,
    string? Format = null);

public sealed record FullResolutionStripe(
    int Y,
    int Width,
    int Height,
    int RowBytes,
    byte[] RgbaBytes);

public sealed class FullResolutionDecoder
{
    public const int DefaultStripeHeight = 64;

    public static Task<FullResolutionInfo> ProbeAsync(
        FullResolutionSource source,
        CancellationToken cancellationToken = default)
    {
        ValidateSource(source);

        return Task.Run(
            () => Probe(source, cancellationToken),
            cancellationToken);
    }

    public static Task DecodeAsync(
        FullResolutionSource source,
        long maxDecodedBytes,
        Action<FullResolutionStripe> consume,
        int stripeHeight = DefaultStripeHeight,
        FullResolutionInfo? expectedInfo = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSource(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDecodedBytes);
        ArgumentNullException.ThrowIfNull(consume);

        if (stripeHeight is < 8 or > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(stripeHeight));
        }

        return Task.Run(
            () => Decode(
                source,
                maxDecodedBytes,
                consume,
                stripeHeight,
                expectedInfo,
                cancellationToken),
            cancellationToken);
    }

    private static FullResolutionInfo Probe(
        FullResolutionSource source,
        CancellationToken cancellationToken)
    {
        using var snapshot = OpenStableSnapshot(
            source,
            source.ContentSha256,
            cancellationToken);
        var metadata = snapshot.Metadata;

        return new FullResolutionInfo(
            metadata.Width,
            metadata.Height,
            metadata.HasAlpha,
            metadata.EstimatedRgbaBytes,
            metadata.ContentSha256,
            metadata.RawWidth,
            metadata.RawHeight,
            metadata.Format);
    }

    private static void Decode(
        FullResolutionSource source,
        long maxDecodedBytes,
        Action<FullResolutionStripe> consume,
        int stripeHeight,
        FullResolutionInfo? expectedInfo,
        CancellationToken cancellationToken)
    {
        var expectedFingerprint =
            source.ContentSha256
            ?? expectedInfo?.ContentSha256;

        if (!string.IsNullOrWhiteSpace(source.ContentSha256)
            && !string.IsNullOrWhiteSpace(expectedInfo?.ContentSha256)
            && !string.Equals(
                source.ContentSha256,
                expectedInfo.ContentSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ImageSourceChangedException(
                source.SourcePath,
                "The requested source identity and probed source identity disagree.");
        }

        using var snapshot = OpenStableSnapshot(
            source,
            expectedFingerprint,
            cancellationToken);
        var snapshotMetadata = snapshot.Metadata;

        if (expectedInfo is not null
            && (snapshotMetadata.Width != expectedInfo.Width
                || snapshotMetadata.Height != expectedInfo.Height
                || snapshotMetadata.EstimatedRgbaBytes
                    != expectedInfo.EstimatedRgbaBytes))
        {
            throw new FullResolutionSourceChangedException(
                source.SourcePath,
                $"Probed {expectedInfo.Width}x{expectedInfo.Height} ({expectedInfo.EstimatedRgbaBytes:N0} RGBA bytes) but decode snapshot is {snapshotMetadata.Width}x{snapshotMetadata.Height} ({snapshotMetadata.EstimatedRgbaBytes:N0} RGBA bytes).");
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var input = NetVips.Image.NewFromFile(
            snapshot.SourcePath,
            access: Enums.Access.Random,
            failOn: Enums.FailOn.Error);
        using var oriented = input.Autorot();

        NetVips.Image? colorManaged = null;
        NetVips.Image? withAlpha = null;
        NetVips.Image? pixels = null;

        try
        {
            colorManaged = oriented.Contains("icc-profile-data")
                ? oriented.IccTransform("srgb")
                : oriented.Interpretation == Enums.Interpretation.Srgb
                    ? oriented.Copy()
                    : oriented.Colourspace(Enums.Interpretation.Srgb);

            withAlpha = colorManaged.HasAlpha()
                ? colorManaged.Copy()
                : colorManaged.AddAlpha();

            pixels = withAlpha.Format == Enums.BandFormat.Uchar
                ? withAlpha.Copy()
                : withAlpha.Cast(Enums.BandFormat.Uchar);

            if (pixels.Bands != 4)
            {
                throw new InvalidOperationException(
                    $"Full-resolution decode expected RGBA output but produced {pixels.Bands} bands.");
            }

            var estimatedBytes = checked(
                (long)pixels.Width
                * pixels.Height
                * 4L);

            if (estimatedBytes > maxDecodedBytes)
            {
                throw new FullResolutionBudgetExceededException(
                    pixels.Width,
                    pixels.Height,
                    estimatedBytes,
                    maxDecodedBytes);
            }

            if (pixels.Width != snapshotMetadata.Width
                || pixels.Height != snapshotMetadata.Height
                || estimatedBytes
                    != snapshotMetadata.EstimatedRgbaBytes)
            {
                throw new FullResolutionSourceChangedException(
                    source.SourcePath,
                    $"Stable snapshot metadata is {snapshotMetadata.Width}x{snapshotMetadata.Height} ({snapshotMetadata.EstimatedRgbaBytes:N0} RGBA bytes) but decode produced {pixels.Width}x{pixels.Height} ({estimatedBytes:N0} RGBA bytes).");
            }

            for (var y = 0; y < pixels.Height; y += stripeHeight)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var height = Math.Min(
                    stripeHeight,
                    pixels.Height - y);

                using var stripe = pixels.Crop(
                    0,
                    y,
                    pixels.Width,
                    height);
                using var nativeCancellation = cancellationToken.Register(
                    static state => ((NetVips.Image)state!).SetKill(true),
                    stripe);

                byte[] bytes;
                try
                {
                    bytes = stripe.WriteToMemory<byte>();
                }
                catch (VipsException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                var expected = checked(
                    pixels.Width
                    * height
                    * 4);

                if (bytes.Length != expected)
                {
                    throw new InvalidOperationException(
                        $"Full-resolution stripe returned {bytes.Length:N0} bytes; expected {expected:N0}.");
                }

                consume(
                    new FullResolutionStripe(
                        y,
                        pixels.Width,
                        height,
                        checked(pixels.Width * 4),
                        bytes));
            }
        }
        finally
        {
            pixels?.Dispose();
            withAlpha?.Dispose();
            colorManaged?.Dispose();
            oriented.Invalidate();
            input.Invalidate();
        }
    }

    private static ImageSourceSnapshot OpenStableSnapshot(
        FullResolutionSource source,
        string? expectedContentSha256,
        CancellationToken cancellationToken)
    {
        try
        {
            return ImageSourceSnapshot.Open(
                source.SourcePath,
                source.FileSize,
                source.ModifiedAtUtcTicks,
                expectedContentSha256,
                cancellationToken);
        }
        catch (ImageSourceChangedException exception)
        {
            throw new FullResolutionSourceChangedException(
                source.SourcePath,
                exception.Message);
        }
    }

    private static void ValidateSource(FullResolutionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourcePath);
        ArgumentOutOfRangeException.ThrowIfNegative(source.FileSize);

        if (!File.Exists(source.SourcePath))
        {
            throw new FileNotFoundException(
                "Full-resolution source no longer exists.",
                source.SourcePath);
        }
    }
}

public sealed class FullResolutionSourceChangedException : IOException
{
    public FullResolutionSourceChangedException(
        string sourcePath,
        string detail)
        : base($"Full-resolution source changed while preparing the Detail image: {detail}")
    {
        SourcePath = sourcePath;
    }

    public string SourcePath { get; }
}

public sealed class FullResolutionBudgetExceededException : InvalidOperationException
{
    public FullResolutionBudgetExceededException(
        int width,
        int height,
        long requiredBytes,
        long budgetBytes)
        : base(
            $"Full-resolution image {width}x{height} requires {requiredBytes:N0} decoded bytes, above the {budgetBytes:N0}-byte detail budget.")
    {
        Width = width;
        Height = height;
        RequiredBytes = requiredBytes;
        BudgetBytes = budgetBytes;
    }

    public int Width { get; }

    public int Height { get; }

    public long RequiredBytes { get; }

    public long BudgetBytes { get; }
}
