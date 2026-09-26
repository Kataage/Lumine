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
    string label)
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

    await FullResolutionDecoder.DecodePreparedAsync(
        prepared,
        32L * 1024 * 1024,
        stripe =>
        {
            rows += stripe.Height;
            digest.AppendData(stripe.RgbaBytes);
        },
        stripeHeight: 23,
        accessPolicy: accessPolicy);

    Require(
        rows == prepared.Info.Height,
        $"NativeAOT {label} decode returned {rows} rows; expected {prepared.Info.Height}.");

    return Convert.ToHexString(
            digest.GetHashAndReset())
        .ToLowerInvariant();
}

static void WritePlainPng(string path)
{
    using var blank = NetVips.Image.Black(
        640,
        480,
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
    string label)
{
    var adaptiveDigest = await DecodeDigestAsync(
        path,
        FullResolutionAccessPolicy.Adaptive,
        expectedRecommendation,
        label);
    var explicitDigest = await DecodeDigestAsync(
        path,
        expectedRecommendation,
        expectedRecommendation,
        label);

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

    WritePlainPng(plainPath);
    WriteIccPng(iccPath);

    await VerifyAdaptiveMatchesAsync(
        plainPath,
        FullResolutionAccessPolicy.Sequential,
        "plain PNG");
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
