using System.Buffers.Binary;
using System.Security.Cryptography;
using Lumine.Image;
using NetVips;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task<string> DecodeDigestAsync(
    string path,
    FullResolutionAccessPolicy accessPolicy,
    FullResolutionAccessPolicy expectedRecommendation,
    string label,
    int stripeHeight = FullResolutionDecoder.DefaultStripeHeight)
{
    var file = new FileInfo(path);
    using var prepared =
        await FullResolutionDecoder.PrepareAsync(
            new FullResolutionSource(
                path,
                file.Length,
                file.LastWriteTimeUtc.Ticks));

    Require(
        prepared.RecommendedAccessPolicy
            == expectedRecommendation,
        $"NativeAOT {label} recommendation was {prepared.RecommendedAccessPolicy}; expected {expectedRecommendation}.");

    using var digest = IncrementalHash.CreateHash(
        HashAlgorithmName.SHA256);
    var rows = 0;

    var budgetBytes = checked(
        prepared.Info.EstimatedRgbaBytes
        + 16L * 1024 * 1024);

    await FullResolutionDecoder.DecodePreparedAsync(
        prepared,
        budgetBytes,
        stripe =>
        {
            rows += stripe.Height;
            digest.AppendData(stripe.RgbaBytes);
        },
        stripeHeight: stripeHeight,
        accessPolicy: accessPolicy);

    Require(
        rows == prepared.Info.Height,
        $"NativeAOT {label} decode returned {rows} rows; expected {prepared.Info.Height}.");

    return Convert.ToHexString(
            digest.GetHashAndReset())
        .ToLowerInvariant();
}

static void WriteBmp24(
    string path,
    int width,
    int height)
{
    var rowStride =
        checked(((width * 3 + 3) / 4) * 4);
    var pixelOffset = 54;
    var fileSize = checked(
        pixelOffset + (rowStride * height));
    var bytes = new byte[fileSize];

    bytes[0] = (byte)'B';
    bytes[1] = (byte)'M';
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(2, 4),
        checked((uint)fileSize));
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(10, 4),
        checked((uint)pixelOffset));
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(14, 4),
        40);
    BinaryPrimitives.WriteInt32LittleEndian(
        bytes.AsSpan(18, 4),
        width);
    BinaryPrimitives.WriteInt32LittleEndian(
        bytes.AsSpan(22, 4),
        height);
    BinaryPrimitives.WriteUInt16LittleEndian(
        bytes.AsSpan(26, 2),
        1);
    BinaryPrimitives.WriteUInt16LittleEndian(
        bytes.AsSpan(28, 2),
        24);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(34, 4),
        checked((uint)(rowStride * height)));

    for (var y = 0; y < height; y++)
    {
        var storedY = height - 1 - y;
        var row =
            pixelOffset + (storedY * rowStride);

        for (var x = 0; x < width; x++)
        {
            var offset = row + (x * 3);
            bytes[offset] =
                (byte)(40 + x + y);
            bytes[offset + 1] =
                (byte)(70 + y);
            bytes[offset + 2] =
                (byte)(100 + x);
        }
    }

    File.WriteAllBytes(path, bytes);
}

static void WritePlainPng(
    string path,
    int width,
    int height)
{
    using var blank = NetVips.Image.Black(
        width,
        height,
        bands: 4);
    using var values = blank.NewFromImage(
        [32, 220, 64, 180]);
    using var rgba = values.Copy(
        interpretation: Enums.Interpretation.Srgb);

    rgba.Pngsave(
        path,
        keep: Enums.ForeignKeep.None);
}

static void WriteIccPng(string path)
{
    using var blank = NetVips.Image.Black(
        640,
        480,
        bands: 4);
    using var values = blank.NewFromImage(
        [32, 220, 64, 180]);
    using var srgb = values.Copy(
        interpretation: Enums.Interpretation.Srgb);
    using var p3 = srgb.IccTransform(
        "p3",
        inputProfile: "srgb");

    p3.Pngsave(
        path,
        keep: Enums.ForeignKeep.Icc);
}

static async Task VerifyAdaptiveMatchesAsync(
    string path,
    FullResolutionAccessPolicy expectedRecommendation,
    string label,
    int stripeHeight = FullResolutionDecoder.DefaultStripeHeight)
{
    var adaptiveDigest = await DecodeDigestAsync(
        path,
        FullResolutionAccessPolicy.Adaptive,
        expectedRecommendation,
        label,
        stripeHeight);
    var explicitDigest = await DecodeDigestAsync(
        path,
        expectedRecommendation,
        expectedRecommendation,
        label,
        stripeHeight);

    Require(
        adaptiveDigest.Length == 64,
        $"NativeAOT {label} Adaptive decode did not produce a SHA-256 digest.");
    Require(
        string.Equals(
            adaptiveDigest,
            explicitDigest,
            StringComparison.Ordinal),
        $"NativeAOT {label} Adaptive output differs from explicit {expectedRecommendation}: adaptive={adaptiveDigest}, explicit={explicitDigest}.");

    Console.WriteLine(
        $"NativeAOT {label}: Adaptive->{expectedRecommendation}, digest={adaptiveDigest}");
}

static async Task VerifyNonDefaultStripeFallsBackToRandomAsync(
    string path)
{
    const int nonDefaultStripeHeight = 23;

    var adaptiveDigest = await DecodeDigestAsync(
        path,
        FullResolutionAccessPolicy.Adaptive,
        FullResolutionAccessPolicy.Sequential,
        "large plain PNG non-default stripe",
        nonDefaultStripeHeight);
    var randomDigest = await DecodeDigestAsync(
        path,
        FullResolutionAccessPolicy.Random,
        FullResolutionAccessPolicy.Sequential,
        "large plain PNG non-default stripe",
        nonDefaultStripeHeight);

    Require(
        string.Equals(
            adaptiveDigest,
            randomDigest,
            StringComparison.Ordinal),
        $"NativeAOT non-default Adaptive output differs from Random fallback: adaptive={adaptiveDigest}, random={randomDigest}.");

    Console.WriteLine(
        $"NativeAOT large plain PNG non-default stripe: Adaptive->Random fallback, digest={adaptiveDigest}");
}

Require(
    FullResolutionDecoder.ProductionAccessPolicy
        == FullResolutionAccessPolicy.Adaptive,
    "NativeAOT smoke expected Adaptive production access policy.");

const int plainWidth = 4608;
const int plainHeight = 4608;
const long plainDecodedBytes =
    (long)plainWidth * plainHeight * 4L;

var expectedThresholdText =
    Environment.GetEnvironmentVariable(
        "LUMINE_EXPECT_DISC_THRESHOLD_BYTES")
    ?? throw new InvalidOperationException(
        "LUMINE_EXPECT_DISC_THRESHOLD_BYTES is required.");

Require(
    long.TryParse(
        expectedThresholdText,
        out var expectedThresholdBytes)
    && expectedThresholdBytes > 0,
    $"Invalid expected disc threshold '{expectedThresholdText}'.");

Require(
    FullResolutionDecoder.PngSequentialThresholdBytes
        == expectedThresholdBytes,
    $"NativeAOT libvips disc threshold was {FullResolutionDecoder.PngSequentialThresholdBytes}; expected {expectedThresholdBytes}.");

var expectedPlainPolicy =
    plainDecodedBytes > expectedThresholdBytes
        ? FullResolutionAccessPolicy.Sequential
        : FullResolutionAccessPolicy.Random;

var capabilities = VipsCapabilities.Probe();
Require(
    capabilities.HeifLoadOperation,
    "NativeAOT bundled libvips has no heifload operation.");

var externalHeicFixturePath = Path.Combine(
    AppContext.BaseDirectory,
    "fixtures",
    "heif",
    "libheif-example.heic");
Require(
    File.Exists(externalHeicFixturePath)
    && new FileInfo(externalHeicFixturePath).Length == 718_114,
    "NativeAOT pinned official libheif HEIC fixture is missing or changed.");

var root = Path.Combine(
    Path.GetTempPath(),
    $"lumine-native-aot-image-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);

try
{
    var plainPath = Path.Combine(
        root,
        "plain-alpha.png");
    var iccPath = Path.Combine(
        root,
        "icc-alpha.png");
    var bmpPath = Path.Combine(
        root,
        "fallback.bmp");
    var bmpCacheRoot = Path.Combine(
        root,
        "bmp-cache");
    var heifCacheRoot = Path.Combine(
        root,
        "heif-cache");
    var externalHeicPath = Path.Combine(
        root,
        "external-real.heic");
    var externalHeifPath = Path.Combine(
        root,
        "external-real.heif");

    File.Copy(
        externalHeicFixturePath,
        externalHeicPath);
    File.Copy(
        externalHeicFixturePath,
        externalHeifPath);

    WritePlainPng(
        plainPath,
        plainWidth,
        plainHeight);
    WriteIccPng(iccPath);
    WriteBmp24(
        bmpPath,
        17,
        9);

    await VerifyAdaptiveMatchesAsync(
        plainPath,
        expectedPlainPolicy,
        "threshold PNG");

    if (expectedPlainPolicy
        == FullResolutionAccessPolicy.Sequential)
    {
        await VerifyNonDefaultStripeFallsBackToRandomAsync(
            plainPath);
    }

    Console.WriteLine(
        $"NativeAOT threshold contract: threshold={expectedThresholdBytes}, decoded={plainDecodedBytes}, policy={expectedPlainPolicy}");
    await VerifyAdaptiveMatchesAsync(
        iccPath,
        FullResolutionAccessPolicy.Random,
        "ICC PNG");

    foreach (var external in new[]
             {
                 (Id: 10L, Path: externalHeicPath, Label: ".heic"),
                 (Id: 11L, Path: externalHeifPath, Label: ".heif")
             })
    {
        var file = new FileInfo(external.Path);
        using var snapshot = await ImageSourceSnapshot.OpenAsync(
            external.Path,
            file.Length,
            file.LastWriteTimeUtc.Ticks);

        Require(
            string.Equals(
                snapshot.Metadata.Format,
                "heif",
                StringComparison.Ordinal)
            && snapshot.Metadata.Width > 0
            && snapshot.Metadata.Height > 0,
            $"NativeAOT external HEIF {external.Label} metadata probe contract failed.");

        using var prepared =
            await FullResolutionDecoder.PrepareAsync(
                new FullResolutionSource(
                    external.Path,
                    file.Length,
                    file.LastWriteTimeUtc.Ticks));

        var rows = 0;
        try
        {
            await FullResolutionDecoder.DecodePreparedAsync(
                prepared,
                checked(
                    prepared.Info.EstimatedRgbaBytes
                    + 16L * 1024 * 1024),
                stripe => rows += stripe.Height,
                accessPolicy: FullResolutionAccessPolicy.Random);
            throw new InvalidOperationException(
                $"NativeAOT external HEIF {external.Label} unexpectedly decoded without an HEVC decoder.");
        }
        catch (VipsException)
        {
            Require(
                rows == 0,
                $"NativeAOT external HEIF {external.Label} emitted rows before rejection.");
        }
    }

    await using (var heifPipeline =
                 new ThumbnailPipeline(
                     new ThumbnailCache(
                         heifCacheRoot),
                     new ThumbnailPipelineOptions
                     {
                         WorkerCount = 1,
                         QueueCapacity = 2
                     }))
    {
        foreach (var external in new[]
                 {
                     (Id: 10L, Path: externalHeicPath, Label: ".heic"),
                     (Id: 11L, Path: externalHeifPath, Label: ".heif")
                 })
        {
            var file = new FileInfo(external.Path);
            try
            {
                _ = await heifPipeline.RequestAsync(
                    new ThumbnailSource(
                        external.Id,
                        1,
                        external.Path,
                        file.Length,
                        file.LastWriteTimeUtc.Ticks),
                    ThumbnailProfiles.GridSmall);
                throw new InvalidOperationException(
                    $"NativeAOT external HEIF {external.Label} unexpectedly generated a thumbnail.");
            }
            catch (VipsException)
            {
            }
        }
    }

    Console.WriteLine(
        "NativeAOT external HEIC/HEIF rejection contract passed.");

    var bmpFile = new FileInfo(bmpPath);
    using (var bmpPrepared =
           await FullResolutionDecoder.PrepareAsync(
               new FullResolutionSource(
                   bmpPath,
                   bmpFile.Length,
                   bmpFile.LastWriteTimeUtc.Ticks)))
    {
        Require(
            bmpPrepared.Info.Width == 17
            && bmpPrepared.Info.Height == 9
            && string.Equals(
                bmpPrepared.Info.Format,
                "bmp",
                StringComparison.Ordinal)
            && bmpPrepared.RecommendedAccessPolicy
                == FullResolutionAccessPolicy.Random,
            "NativeAOT BMP fallback probe contract failed.");
    }

    await VerifyAdaptiveMatchesAsync(
        bmpPath,
        FullResolutionAccessPolicy.Random,
        "BMP fallback");

    await using (var bmpPipeline =
                 new ThumbnailPipeline(
                     new ThumbnailCache(
                         bmpCacheRoot),
                     new ThumbnailPipelineOptions
                     {
                         WorkerCount = 1,
                         QueueCapacity = 2
                     }))
    {
        var bmpThumbnail =
            await bmpPipeline.RequestAsync(
                new ThumbnailSource(
                    1,
                    1,
                    bmpPath,
                    bmpFile.Length,
                    bmpFile.LastWriteTimeUtc.Ticks),
                ThumbnailProfiles.GridSmall);

        Require(
            File.Exists(
                bmpThumbnail.CachePath)
            && bmpThumbnail.Width == 17
            && bmpThumbnail.Height == 9
            && string.Equals(
                bmpThumbnail.SourceMetadata?.Format,
                "bmp",
                StringComparison.Ordinal),
            "NativeAOT BMP thumbnail fallback failed.");
    }

    Console.WriteLine(
        "NativeAOT Image smoke passed.");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(
            root,
            recursive: true);
    }
}
