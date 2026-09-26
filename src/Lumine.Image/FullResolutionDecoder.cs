using NetVips;

namespace Lumine.Image;

public sealed record FullResolutionSource(
    string SourcePath,
    long FileSize,
    long ModifiedAtUtcTicks,
    string? SourceIdentity = null);

public sealed record FullResolutionInfo(
    int Width,
    int Height,
    bool HasAlpha,
    long EstimatedRgbaBytes,
    string? SourceIdentity = null,
    int? RawWidth = null,
    int? RawHeight = null,
    string? Format = null);

public sealed record FullResolutionStripe(
    int Y,
    int Width,
    int Height,
    int RowBytes,
    byte[] RgbaBytes);

public enum FullResolutionAccessPolicy
{
    Adaptive = 0,
    Random = 1,
    Sequential = 2
}

public sealed class FullResolutionPreparedSource : IDisposable
{
    private ImageSourceSnapshot? _snapshot;

    internal FullResolutionPreparedSource(
        FullResolutionSource source,
        ImageSourceSnapshot snapshot,
        FullResolutionInfo info,
        FullResolutionAccessPolicy recommendedAccessPolicy)
    {
        Source = source;
        _snapshot = snapshot;
        Info = info;
        RecommendedAccessPolicy = recommendedAccessPolicy;
    }

    public FullResolutionSource Source { get; }

    public FullResolutionInfo Info { get; }

    public FullResolutionAccessPolicy RecommendedAccessPolicy { get; }

    internal ImageSourceSnapshot Snapshot =>
        _snapshot
        ?? throw new ObjectDisposedException(
            nameof(FullResolutionPreparedSource));

    public void Dispose() =>
        Interlocked.Exchange(ref _snapshot, null)?.Dispose();
}

public sealed class FullResolutionDecoder
{
    public const int DefaultStripeHeight = 64;

    public const FullResolutionAccessPolicy ProductionAccessPolicy =
        FullResolutionAccessPolicy.Adaptive;

    public const long PngSequentialThresholdBytes =
        100L * 1024 * 1024;

    public static Task<FullResolutionPreparedSource> PrepareAsync(
        FullResolutionSource source,
        CancellationToken cancellationToken = default)
    {
        ValidateSource(source);

        return Task.Run(
            () => Prepare(source, cancellationToken),
            cancellationToken);
    }

    public static async Task<FullResolutionInfo> ProbeAsync(
        FullResolutionSource source,
        CancellationToken cancellationToken = default)
    {
        using var prepared = await PrepareAsync(
            source,
            cancellationToken).ConfigureAwait(false);
        return prepared.Info;
    }

    public static Task DecodePreparedAsync(
        FullResolutionPreparedSource prepared,
        long maxDecodedBytes,
        Action<FullResolutionStripe> consume,
        int stripeHeight = DefaultStripeHeight,
        FullResolutionAccessPolicy accessPolicy = ProductionAccessPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ValidateDecodeArguments(
            maxDecodedBytes,
            consume,
            stripeHeight);

        return Task.Run(
            () => DecodeSnapshot(
                prepared.Snapshot,
                prepared.Info,
                maxDecodedBytes,
                consume,
                stripeHeight,
                accessPolicy,
                cancellationToken),
            cancellationToken);
    }

    public static Task DecodeAsync(
        FullResolutionSource source,
        long maxDecodedBytes,
        Action<FullResolutionStripe> consume,
        int stripeHeight = DefaultStripeHeight,
        FullResolutionInfo? expectedInfo = null,
        FullResolutionAccessPolicy accessPolicy = ProductionAccessPolicy,
        CancellationToken cancellationToken = default)
    {
        ValidateSource(source);
        ValidateDecodeArguments(
            maxDecodedBytes,
            consume,
            stripeHeight);

        return Task.Run(
            () => Decode(
                source,
                maxDecodedBytes,
                consume,
                stripeHeight,
                expectedInfo,
                accessPolicy,
                cancellationToken),
            cancellationToken);
    }

    private static FullResolutionPreparedSource Prepare(
        FullResolutionSource source,
        CancellationToken cancellationToken)
    {
        var snapshot = OpenStableSnapshot(
            source,
            source.SourceIdentity,
            cancellationToken);

        try
        {
            var metadata = snapshot.Metadata;
            var info = new FullResolutionInfo(
                metadata.Width,
                metadata.Height,
                metadata.HasAlpha,
                metadata.EstimatedRgbaBytes,
                metadata.SourceIdentity,
                metadata.RawWidth,
                metadata.RawHeight,
                metadata.Format);

            return new FullResolutionPreparedSource(
                source,
                snapshot,
                info,
                RecommendAccessPolicy(snapshot));
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    private static void Decode(
        FullResolutionSource source,
        long maxDecodedBytes,
        Action<FullResolutionStripe> consume,
        int stripeHeight,
        FullResolutionInfo? expectedInfo,
        FullResolutionAccessPolicy accessPolicy,
        CancellationToken cancellationToken)
    {
        var expectedIdentity =
            source.SourceIdentity
            ?? expectedInfo?.SourceIdentity;

        if (!string.IsNullOrWhiteSpace(source.SourceIdentity)
            && !string.IsNullOrWhiteSpace(expectedInfo?.SourceIdentity)
            && !string.Equals(
                source.SourceIdentity,
                expectedInfo.SourceIdentity,
                StringComparison.Ordinal))
        {
            throw new FullResolutionSourceChangedException(
                source.SourcePath,
                "The requested source identity and probed source identity disagree.");
        }

        using var snapshot = OpenStableSnapshot(
            source,
            expectedIdentity,
            cancellationToken);
        var snapshotInfo = CreateInfo(snapshot.Metadata);

        if (expectedInfo is not null)
        {
            EnsureExpectedInfo(
                source.SourcePath,
                expectedInfo,
                snapshotInfo);
        }

        DecodeSnapshot(
            snapshot,
            snapshotInfo,
            maxDecodedBytes,
            consume,
            stripeHeight,
            accessPolicy,
            cancellationToken);
    }

    private static void DecodeSnapshot(
        ImageSourceSnapshot snapshot,
        FullResolutionInfo info,
        long maxDecodedBytes,
        Action<FullResolutionStripe> consume,
        int stripeHeight,
        FullResolutionAccessPolicy accessPolicy,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (info.EstimatedRgbaBytes > maxDecodedBytes)
        {
            throw new FullResolutionBudgetExceededException(
                info.Width,
                info.Height,
                info.EstimatedRgbaBytes,
                maxDecodedBytes);
        }

        var resolvedAccessPolicy =
            ResolveAccessPolicy(
                snapshot,
                accessPolicy,
                stripeHeight);

        using var input = NetVips.Image.NewFromFile(
            snapshot.SourcePath,
            access: resolvedAccessPolicy switch
            {
                FullResolutionAccessPolicy.Random => Enums.Access.Random,
                FullResolutionAccessPolicy.Sequential => Enums.Access.Sequential,
                _ => throw new InvalidOperationException(
                    $"Adaptive full-resolution access policy resolved to unsupported value '{resolvedAccessPolicy}'.")
            },
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

            if (pixels.Width != info.Width
                || pixels.Height != info.Height
                || estimatedBytes != info.EstimatedRgbaBytes)
            {
                throw new FullResolutionSourceChangedException(
                    snapshot.SourcePath,
                    $"Prepared snapshot metadata is {info.Width}x{info.Height} ({info.EstimatedRgbaBytes:N0} RGBA bytes) but decode produced {pixels.Width}x{pixels.Height} ({estimatedBytes:N0} RGBA bytes).");
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

    internal static FullResolutionAccessPolicy ResolveAccessPolicy(
        ImageSourceSnapshot snapshot,
        FullResolutionAccessPolicy requestedPolicy,
        int stripeHeight) =>
        requestedPolicy switch
        {
            FullResolutionAccessPolicy.Adaptive =>
                stripeHeight == DefaultStripeHeight
                    ? RecommendAccessPolicy(snapshot)
                    : FullResolutionAccessPolicy.Random,
            FullResolutionAccessPolicy.Random =>
                FullResolutionAccessPolicy.Random,
            FullResolutionAccessPolicy.Sequential =>
                FullResolutionAccessPolicy.Sequential,
            _ => throw new ArgumentOutOfRangeException(
                nameof(requestedPolicy),
                requestedPolicy,
                "Unknown full-resolution access policy.")
        };

    private static FullResolutionAccessPolicy RecommendAccessPolicy(
        ImageSourceSnapshot snapshot)
    {
        if (snapshot.Orientation != 1
            || snapshot.HasEmbeddedIcc)
        {
            return FullResolutionAccessPolicy.Random;
        }

        return string.Equals(
                   snapshot.Metadata.Format,
                   "png",
                   StringComparison.OrdinalIgnoreCase)
               && snapshot.DecodedSourceBytes
                   >= PngSequentialThresholdBytes
            ? FullResolutionAccessPolicy.Sequential
            : FullResolutionAccessPolicy.Random;
    }

    private static FullResolutionInfo CreateInfo(
        SourceTechnicalMetadata metadata) =>
        new(
            metadata.Width,
            metadata.Height,
            metadata.HasAlpha,
            metadata.EstimatedRgbaBytes,
            metadata.SourceIdentity,
            metadata.RawWidth,
            metadata.RawHeight,
            metadata.Format);

    private static void EnsureExpectedInfo(
        string sourcePath,
        FullResolutionInfo expected,
        FullResolutionInfo actual)
    {
        if (actual.Width != expected.Width
            || actual.Height != expected.Height
            || actual.EstimatedRgbaBytes != expected.EstimatedRgbaBytes
            || (!string.IsNullOrWhiteSpace(expected.SourceIdentity)
                && !string.Equals(
                    expected.SourceIdentity,
                    actual.SourceIdentity,
                    StringComparison.Ordinal)))
        {
            throw new FullResolutionSourceChangedException(
                sourcePath,
                $"Expected {expected.Width}x{expected.Height} ({expected.EstimatedRgbaBytes:N0} RGBA bytes, identity={expected.SourceIdentity ?? "<none>"}) but stable decode snapshot is {actual.Width}x{actual.Height} ({actual.EstimatedRgbaBytes:N0} RGBA bytes, identity={actual.SourceIdentity ?? "<none>"}).");
        }
    }

    private static ImageSourceSnapshot OpenStableSnapshot(
        FullResolutionSource source,
        string? expectedSourceIdentity,
        CancellationToken cancellationToken)
    {
        try
        {
            return ImageSourceSnapshot.Open(
                source.SourcePath,
                source.FileSize,
                source.ModifiedAtUtcTicks,
                expectedSourceIdentity,
                cancellationToken);
        }
        catch (ImageSourceChangedException exception)
        {
            throw new FullResolutionSourceChangedException(
                source.SourcePath,
                exception.Message);
        }
    }

    private static void ValidateDecodeArguments(
        long maxDecodedBytes,
        Action<FullResolutionStripe> consume,
        int stripeHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDecodedBytes);
        ArgumentNullException.ThrowIfNull(consume);

        if (stripeHeight is < 8 or > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(stripeHeight));
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
