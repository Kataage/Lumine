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
    FullResolutionAccessPolicy accessPolicy)
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
            == FullResolutionAccessPolicy.Sequential,
        $"NativeAOT PNG recommendation was {prepared.RecommendedAccessPolicy}; expected Sequential.");

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
        $"NativeAOT decode returned {rows} rows; expected {prepared.Info.Height}.");

    return Convert.ToHexString(
            digest.GetHashAndReset())
        .ToLowerInvariant();
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
    var path = Path.Combine(root, "icc-alpha.png");

    using (var blank = NetVips.Image.Black(
               640,
               480,
               bands: 4))
    using (var values = blank.NewFromImage(
               [32, 220, 64, 180]))
    using (var srgb = values.Copy(
               interpretation: Enums.Interpretation.Srgb))
    using (var p3 = srgb.IccTransform(
               "p3",
               inputProfile: "srgb"))
    {
        p3.Pngsave(
            path,
            keep: Enums.ForeignKeep.Icc);
    }

    var adaptiveDigest = await DecodeDigestAsync(
        path,
        FullResolutionAccessPolicy.Adaptive);
    var randomDigest = await DecodeDigestAsync(
        path,
        FullResolutionAccessPolicy.Random);

    Require(
        adaptiveDigest.Length == 64,
        "NativeAOT Adaptive decode did not produce a SHA-256 digest.");
    Require(
        string.Equals(
            adaptiveDigest,
            randomDigest,
            StringComparison.Ordinal),
        $"NativeAOT Adaptive PNG output differs from Random: adaptive={adaptiveDigest}, random={randomDigest}.");

    Console.WriteLine(
        $"NativeAOT Image smoke passed: policy=Adaptive->Sequential, digest={adaptiveDigest}");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}
