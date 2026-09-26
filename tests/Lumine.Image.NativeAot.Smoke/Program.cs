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

    WritePlainPng(
        plainPath,
        5120,
        5120);
    WriteIccPng(iccPath);

    await VerifyAdaptiveMatchesAsync(
        plainPath,
        FullResolutionAccessPolicy.Sequential,
        "large plain PNG");
    await VerifyNonDefaultStripeFallsBackToRandomAsync(
        plainPath);
    await VerifyAdaptiveMatchesAsync(
        iccPath,
        FullResolutionAccessPolicy.Random,
        "ICC PNG");

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
