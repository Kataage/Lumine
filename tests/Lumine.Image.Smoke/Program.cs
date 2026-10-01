using System.Buffers.Binary;
using System.Reflection;
using Lumine.Core;
using Lumine.Image;
using NetVips;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void VerifyUnreadableOrientationIsUnsafe()
{
    using var blank = NetVips.Image.Black(
        8,
        8,
        bands: 3);
    using var invalid = blank.Mutate(
        image => image.Set(
            GValue.GIntType,
            "orientation",
            9));

    var method = typeof(ImageSourceSnapshot).GetMethod(
        "ReadOrientation",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "ImageSourceSnapshot.ReadOrientation was not found.");

    var value = method.Invoke(
        null,
        [invalid]);

    Require(
        value is int orientation
        && orientation == 0,
        $"Invalid orientation metadata was trusted as '{value}' instead of unknown/unsafe.");
}

static byte[] CreateHeifFixture(Enums.ForeignHeifCompression compression)
{
    using var blank = NetVips.Image.Black(8, 6, bands: 3);
    using var image = blank.Copy(interpretation: Enums.Interpretation.Srgb);

    return image.HeifsaveBuffer(
        q: 80,
        compression: compression,
        effort: 0,
        keep: Enums.ForeignKeep.None);
}

static Task WriteAnimatedGifAsync(string path) =>
    File.WriteAllBytesAsync(
        path,
        Convert.FromBase64String(
            "R0lGODlhMAAgAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACgAAACwAAAAAMAAgAAAIQQABCBxIsKDBgwgTKlzIsKHDhxAjSpxIsaLFixgzatzIsaPHjyBDihxJsqTJkyhTqlzJsqXLlzBjypxJs6bNmQEBACH5BAEKAAEALAAAAAAwACAAgQAA/wAAAAAAAAAAAAhBAAEIHEiwoMGDCBMqXMiwocOHECNKnEixosWLGDNq3Mixo8ePIEOKHEmypMmTKFOqXMmypcuXMGPKnEmzps2ZAQEAOw=="));

static void WriteP3ProfileJpeg(string path)
{
    using var blank = NetVips.Image.Black(96, 64, bands: 3);
    using var greenValues = blank.NewFromImage([0, 240, 0]);
    using var green = greenValues.Copy(interpretation: Enums.Interpretation.Srgb);
    using var p3 = green.IccTransform("p3", inputProfile: "srgb");

    p3.Jpegsave(
        path,
        q: 95,
        keep: Enums.ForeignKeep.Icc);
}

static void WriteP3ProfilePng(string path)
{
    using var blank = NetVips.Image.Black(256, 192, bands: 4);
    using var values = blank.NewFromImage([32, 220, 64, 180]);
    using var srgb = values.Copy(interpretation: Enums.Interpretation.Srgb);
    using var p3 = srgb.IccTransform("p3", inputProfile: "srgb");

    p3.Pngsave(
        path,
        keep: Enums.ForeignKeep.Icc);
}

static ThumbnailSource SourceFor(long assetId, long revision, string path)
{
    var info = new FileInfo(path);
    return new ThumbnailSource(
        assetId,
        revision,
        path,
        info.Length,
        info.LastWriteTimeUtc.Ticks);
}

static void CreateSizedCacheFile(
    string path,
    long length,
    DateTime lastWriteUtc)
{
    Directory.CreateDirectory(
        Path.GetDirectoryName(path)!);

    using (var stream = new FileStream(
               path,
               FileMode.Create,
               FileAccess.Write,
               FileShare.Read))
    {
        stream.SetLength(length);
    }

    File.SetLastWriteTimeUtc(
        path,
        lastWriteUtc);
}

static async Task WaitUntilAsync(
    Func<Task<bool>> predicate,
    TimeSpan timeout,
    string failureMessage)
{
    var started =
        System.Diagnostics.Stopwatch.StartNew();

    while (started.Elapsed < timeout)
    {
        if (await predicate())
        {
            return;
        }

        await Task.Delay(25);
    }

    throw new InvalidOperationException(
        failureMessage);
}

static void WriteBmp24(
    string path,
    int width,
    int height,
    bool topDown)
{
    var rowStride = checked(((width * 3 + 3) / 4) * 4);
    var pixelOffset = 54;
    var fileSize = checked(pixelOffset + (rowStride * height));
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
        topDown ? -height : height);
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
        var storedY = topDown
            ? y
            : height - 1 - y;
        var row = pixelOffset + (storedY * rowStride);

        for (var x = 0; x < width; x++)
        {
            var offset = row + (x * 3);
            var red = (byte)(20 + (x * 17));
            var green = (byte)(40 + (y * 23));
            var blue = (byte)(60 + x + y);
            bytes[offset] = blue;
            bytes[offset + 1] = green;
            bytes[offset + 2] = red;
        }
    }

    File.WriteAllBytes(path, bytes);
}

static void WriteBmp32Rgb(
    string path,
    int width,
    int height)
{
    var rowStride = checked(width * 4);
    var pixelOffset = 54;
    var fileSize = checked(pixelOffset + (rowStride * height));
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
        32);

    for (var y = 0; y < height; y++)
    {
        var storedY = height - 1 - y;
        var row = pixelOffset + (storedY * rowStride);

        for (var x = 0; x < width; x++)
        {
            var offset = row + (x * 4);
            bytes[offset] = (byte)(30 + x);
            bytes[offset + 1] = (byte)(50 + y);
            bytes[offset + 2] = (byte)(70 + x + y);
            bytes[offset + 3] = 0;
        }
    }

    File.WriteAllBytes(path, bytes);
}

static void WriteBmp32BitfieldsAlpha(
    string path,
    int width,
    int height,
    uint compression = 3)
{
    const int dibSize = 56;
    var rowStride = checked(width * 4);
    var pixelOffset = 14 + dibSize;
    var fileSize = checked(pixelOffset + (rowStride * height));
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
        dibSize);
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
        32);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(30, 4),
        compression);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(54, 4),
        0x00ff0000);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(58, 4),
        0x0000ff00);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(62, 4),
        0x000000ff);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(66, 4),
        0xff000000);

    for (var y = 0; y < height; y++)
    {
        var storedY = height - 1 - y;
        var row = pixelOffset + (storedY * rowStride);

        for (var x = 0; x < width; x++)
        {
            var red = (byte)(90 + x);
            var green = (byte)(110 + y);
            var blue = (byte)(130 + x + y);
            var alpha = (byte)(128 + y);
            var value =
                ((uint)alpha << 24)
                | ((uint)red << 16)
                | ((uint)green << 8)
                | blue;

            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(
                    row + (x * 4),
                    4),
                value);
        }
    }

    File.WriteAllBytes(path, bytes);
}

static void WriteUnsupportedProfileBmp(
    string path,
    uint colorSpaceType = 0x4d424544)
{
    const int width = 2;
    const int height = 2;
    const int dibSize = 108;
    const int rowStride = width * 4;
    const int pixelOffset = 14 + dibSize;
    var bytes = new byte[
        pixelOffset + (rowStride * height)];

    bytes[0] = (byte)'B';
    bytes[1] = (byte)'M';
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(2, 4),
        checked((uint)bytes.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(10, 4),
        pixelOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(14, 4),
        dibSize);
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
        32);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(30, 4),
        3);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(54, 4),
        0x00ff0000);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(58, 4),
        0x0000ff00);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(62, 4),
        0x000000ff);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(66, 4),
        0xff000000);

    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(70, 4),
        colorSpaceType);

    File.WriteAllBytes(path, bytes);
}

static void WriteUnsupportedDib64Bmp(
    string path)
{
    const int dibSize = 64;
    const int pixelOffset = 14 + dibSize;
    var bytes = new byte[
        pixelOffset + 8];

    bytes[0] = (byte)'B';
    bytes[1] = (byte)'M';
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(2, 4),
        checked((uint)bytes.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(10, 4),
        pixelOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(14, 4),
        dibSize);
    BinaryPrimitives.WriteInt32LittleEndian(
        bytes.AsSpan(18, 4),
        2);
    BinaryPrimitives.WriteInt32LittleEndian(
        bytes.AsSpan(22, 4),
        1);
    BinaryPrimitives.WriteUInt16LittleEndian(
        bytes.AsSpan(26, 2),
        1);
    BinaryPrimitives.WriteUInt16LittleEndian(
        bytes.AsSpan(28, 2),
        24);

    File.WriteAllBytes(path, bytes);
}

static void WriteUnsupportedBmp8(string path)
{
    const int width = 2;
    const int height = 2;
    const int rowStride = 4;
    const int pixelOffset = 54;
    var bytes = new byte[
        pixelOffset + (rowStride * height)];

    bytes[0] = (byte)'B';
    bytes[1] = (byte)'M';
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(2, 4),
        checked((uint)bytes.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(10, 4),
        pixelOffset);
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
        8);

    File.WriteAllBytes(path, bytes);
}

static void WriteRgb(string path, int width = 320, int height = 200)
{
    using var black = NetVips.Image.Black(width, height, bands: 3);
    using var rgb = black.Copy(interpretation: Enums.Interpretation.Srgb);
    rgb.WriteToFile(path);
}

static void WriteRgbaPng(string path)
{
    using var blank = NetVips.Image.Black(240, 160, bands: 4);
    using var rgbaValues = blank.NewFromImage(SmokeConstants.RgbaPixel);
    using var rgba = rgbaValues.Copy(interpretation: Enums.Interpretation.Srgb);
    rgba.WriteToFile(path);
}

static void WriteSolidJpeg(string path, int value)
{
    using var blank = NetVips.Image.Black(320, 200, bands: 3);
    using var values = blank.NewFromImage([value, value, value]);
    using var image = values.Copy(interpretation: Enums.Interpretation.Srgb);
    image.Jpegsave(path, q: 90);
}

static void PadToLength(string path, long length)
{
    using var stream = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Write,
        FileShare.None);
    if (stream.Length > length)
    {
        throw new InvalidOperationException(
            "Cannot pad a file to a smaller length.");
    }

    stream.SetLength(length);
}

static async Task VerifyRecommendedAccessPolicyAsync(
    string path,
    FullResolutionAccessPolicy expected,
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
        prepared.RecommendedAccessPolicy == expected,
        $"{label} adaptive access policy was {prepared.RecommendedAccessPolicy}; expected {expected}.");
}

static async Task VerifyFullResolutionAsync(
    string path,
    int expectedWidth,
    int expectedHeight,
    string label)
{
    var file = new FileInfo(path);
    var source = new FullResolutionSource(
        path,
        file.Length,
        file.LastWriteTimeUtc.Ticks);
    var info = await FullResolutionDecoder.ProbeAsync(source);

    Require(
        info.Width == expectedWidth && info.Height == expectedHeight,
        $"{label} full-resolution probe mismatch: {info.Width}x{info.Height}.");

    var rows = 0;
    await FullResolutionDecoder.DecodeAsync(
        source,
        32L * 1024 * 1024,
        stripe => rows += stripe.Height,
        stripeHeight: 29,
        expectedInfo: info);

    Require(
        rows == expectedHeight,
        $"{label} full-resolution decode returned {rows} rows; expected {expectedHeight}.");
}

static async Task VerifyExternalHeicRejectedAsync(
    string path,
    string label)
{
    var file = new FileInfo(path);
    var source = new FullResolutionSource(
        path,
        file.Length,
        file.LastWriteTimeUtc.Ticks);

    using var snapshot = await ImageSourceSnapshot.OpenAsync(
        path,
        file.Length,
        file.LastWriteTimeUtc.Ticks);

    Require(
        string.Equals(
            snapshot.Metadata.Format,
            "heif",
            StringComparison.Ordinal)
        && snapshot.Metadata.Width > 0
        && snapshot.Metadata.Height > 0,
        $"{label} external fixture was not recognized as HEIF metadata.");

    using var prepared =
        await FullResolutionDecoder.PrepareAsync(source);
    var rows = 0;

    try
    {
        await FullResolutionDecoder.DecodePreparedAsync(
            prepared,
            checked(
                prepared.Info.EstimatedRgbaBytes
                + 16L * 1024 * 1024),
            stripe => rows += stripe.Height,
            stripeHeight: 29);

        throw new InvalidOperationException(
            $"{label} unexpectedly decoded HEVC although the pinned Windows runtime has no HEVC decoder.");
    }
    catch (VipsException exception)
    {
        Require(
            rows == 0,
            $"{label} emitted {rows} rows before the unsupported HEVC decoder failure.");
        Console.WriteLine(
            $"{label}: HEIF container recognized ({snapshot.Metadata.Width}x{snapshot.Metadata.Height}) but HEVC decode is unavailable as expected: {exception.Message.Split(Environment.NewLine)[0]}");
    }
}
var capabilities = VipsCapabilities.Probe();
Require(capabilities.JpegLoad, "Bundled libvips has no JPEG loader.");
Require(capabilities.PngLoad, "Bundled libvips has no PNG loader.");
Require(capabilities.WebpLoad, "Bundled libvips has no WebP loader.");
Require(capabilities.WebpSave, "Bundled libvips has no WebP saver.");
Require(capabilities.GifLoad, "Bundled libvips has no GIF loader.");
Require(
    capabilities.BmpFallbackLoad,
    "Lumine BMP fallback capability is unavailable.");
Require(
    capabilities.HeifLoadOperation,
    "Bundled libvips unexpectedly lost the HEIF container loader used by the #312 negative contract.");

var externalHeicFixturePath = Path.Combine(
    AppContext.BaseDirectory,
    "fixtures",
    "heif",
    "libheif-example.heic");
Require(
    File.Exists(externalHeicFixturePath),
    $"Pinned external HEIC fixture was not copied to the test output: {externalHeicFixturePath}");
Require(
    new FileInfo(externalHeicFixturePath).Length == 718_114,
    "Pinned official libheif HEIC fixture size changed; update provenance before accepting a new fixture.");

var root = Path.Combine(Path.GetTempPath(), $"lumine-image-smoke-{Guid.NewGuid():N}");
var sourceRoot = Path.Combine(root, "sources");
var cacheRoot = Path.Combine(root, "cache");
Directory.CreateDirectory(sourceRoot);

try
{
    var jpgPath = Path.Combine(sourceRoot, "sample.jpg");
    var pngPath = Path.Combine(sourceRoot, "alpha.png");
    var disguisedPngPath = Path.Combine(sourceRoot, "png-with-jpg-extension.jpg");
    var webpPath = Path.Combine(sourceRoot, "sample.webp");
    var gifPath = Path.Combine(sourceRoot, "sample.gif");
    var tiffPath = Path.Combine(sourceRoot, "sample.tiff");
    var orientedPath = Path.Combine(sourceRoot, "oriented.jpg");
    var corruptSourcePath = Path.Combine(sourceRoot, "corrupt-source.jpg");
    var changedPath = Path.Combine(sourceRoot, "changed.jpg");
    var identityPath = Path.Combine(sourceRoot, "identity.jpg");
    var identityReplacementPath = Path.Combine(sourceRoot, "identity-replacement.jpg");
    var concurrentPath = Path.Combine(sourceRoot, "concurrent.jpg");
    var p3Path = Path.Combine(sourceRoot, "profile-p3.jpg");
    var p3PngPath = Path.Combine(sourceRoot, "profile-p3.png");
    var cancellationPath = Path.Combine(sourceRoot, "cancellation.png");
    var detailCachePath = Path.Combine(sourceRoot, "detail-cache.jpg");
    var sourceChangePath = Path.Combine(sourceRoot, "source-change.jpg");
    var bmp24BottomUpPath = Path.Combine(sourceRoot, "bmp-24-bottom-up.bmp");
    var bmp24TopDownPath = Path.Combine(sourceRoot, "bmp-24-top-down.bmp");
    var bmp32RgbPath = Path.Combine(sourceRoot, "bmp-32-rgb.bmp");
    var bmp32AlphaPath = Path.Combine(sourceRoot, "bmp-32-alpha.bmp");
    var bmp32AlphaFieldsPath = Path.Combine(sourceRoot, "bmp-32-alpha-fields.bmp");
    var bmpUnsupportedProfilePath = Path.Combine(sourceRoot, "bmp-profile.bmp");
    var bmpCalibratedProfilePath = Path.Combine(sourceRoot, "bmp-calibrated.bmp");
    var bmpUnsupportedDib64Path = Path.Combine(sourceRoot, "bmp-dib64.bmp");
    var bmpDisguisedPath = Path.Combine(sourceRoot, "bmp-disguised.jpg");
    var bmpUnsupportedPath = Path.Combine(sourceRoot, "bmp-unsupported.bmp");
    var externalHeicPath = Path.Combine(sourceRoot, "external-real.heic");
    var externalHeifPath = Path.Combine(sourceRoot, "external-real.heif");

    File.Copy(externalHeicFixturePath, externalHeicPath);
    File.Copy(externalHeicFixturePath, externalHeifPath);

    WriteRgb(jpgPath);
    WriteRgbaPng(pngPath);
    File.Copy(pngPath, disguisedPngPath);
    WriteRgb(webpPath);
    await WriteAnimatedGifAsync(gifPath);
    WriteRgb(tiffPath);
    WriteRgb(corruptSourcePath);
    WriteRgb(changedPath, 800, 600);
    WriteRgb(concurrentPath, 1200, 800);
    WriteP3ProfileJpeg(p3Path);
    WriteP3ProfilePng(p3PngPath);
    WriteRgb(cancellationPath, 6000, 6000);
    WriteRgb(detailCachePath, 2200, 1400);
    WriteRgb(sourceChangePath, 320, 200);
    WriteBmp24(bmp24BottomUpPath, 5, 3, topDown: false);
    WriteBmp24(bmp24TopDownPath, 5, 3, topDown: true);
    WriteBmp32Rgb(bmp32RgbPath, 4, 3);
    WriteBmp32BitfieldsAlpha(bmp32AlphaPath, 4, 3);
    WriteBmp32BitfieldsAlpha(
        bmp32AlphaFieldsPath,
        4,
        3,
        compression: 6);
    WriteUnsupportedProfileBmp(
        bmpUnsupportedProfilePath);
    WriteUnsupportedProfileBmp(
        bmpCalibratedProfilePath,
        colorSpaceType: 0);
    WriteUnsupportedDib64Bmp(
        bmpUnsupportedDib64Path);
    File.Copy(bmp24BottomUpPath, bmpDisguisedPath);
    WriteUnsupportedBmp8(bmpUnsupportedPath);

    Require(
        FullResolutionDecoder.ProductionAccessPolicy
            == FullResolutionAccessPolicy.Adaptive,
        "Production full-resolution access policy is not Adaptive.");
    VerifyUnreadableOrientationIsUnsafe();
    await VerifyRecommendedAccessPolicyAsync(
        jpgPath,
        FullResolutionAccessPolicy.Random,
        "JPEG");
    await VerifyRecommendedAccessPolicyAsync(
        pngPath,
        FullResolutionAccessPolicy.Random,
        "small PNG");
    await VerifyRecommendedAccessPolicyAsync(
        webpPath,
        FullResolutionAccessPolicy.Random,
        "WebP");
    await VerifyRecommendedAccessPolicyAsync(
        tiffPath,
        FullResolutionAccessPolicy.Random,
        "TIFF");
    await VerifyRecommendedAccessPolicyAsync(
        p3Path,
        FullResolutionAccessPolicy.Random,
        "JPEG ICC");
    await VerifyRecommendedAccessPolicyAsync(
        p3PngPath,
        FullResolutionAccessPolicy.Random,
        "PNG ICC");

    using (var orientationBlank = NetVips.Image.Black(120, 60, bands: 3))
    using (var baseImage = orientationBlank.Copy(interpretation: Enums.Interpretation.Srgb))
    using (var oriented = baseImage.Mutate(
               image => image.Set(GValue.GIntType, "orientation", 6)))
    {
        oriented.WriteToFile(orientedPath);
    }

    await VerifyRecommendedAccessPolicyAsync(
        orientedPath,
        FullResolutionAccessPolicy.Random,
        "EXIF-oriented JPEG");

    var disguisedInfo = new FileInfo(disguisedPngPath);
    using (var disguisedSnapshot = await ImageSourceSnapshot.OpenAsync(
               disguisedPngPath,
               disguisedInfo.Length,
               disguisedInfo.LastWriteTimeUtc.Ticks))
    {
        Require(
            string.Equals(
                disguisedSnapshot.Metadata.Format,
                "png",
                StringComparison.Ordinal),
            $"Actual loader format was not detected for extension-mismatched PNG: {disguisedSnapshot.Metadata.Format}.");
    }

    foreach (var bmpPath in new[]
             {
                 bmp24BottomUpPath,
                 bmp24TopDownPath,
                 bmp32RgbPath,
                 bmp32AlphaPath,
                 bmp32AlphaFieldsPath,
                 bmpDisguisedPath
             })
    {
        var bmpFile = new FileInfo(bmpPath);
        using var snapshot = await ImageSourceSnapshot.OpenAsync(
            bmpPath,
            bmpFile.Length,
            bmpFile.LastWriteTimeUtc.Ticks);

        Require(
            snapshot.Metadata.Width is 4 or 5
            && snapshot.Metadata.Height == 3,
            $"BMP metadata dimensions were invalid for {Path.GetFileName(bmpPath)}.");
        Require(
            string.Equals(
                snapshot.Metadata.Format,
                "bmp",
                StringComparison.Ordinal),
            $"BMP signature was not recognized for {Path.GetFileName(bmpPath)}.");
        Require(
            snapshot.Orientation == 1
            && !snapshot.HasEmbeddedIcc,
            "BMP fallback exposed invalid orientation/ICC state.");
    }

    var bmp32AlphaFile = new FileInfo(
        bmp32AlphaPath);
    using (var alphaSnapshot =
           await ImageSourceSnapshot.OpenAsync(
               bmp32AlphaPath,
               bmp32AlphaFile.Length,
               bmp32AlphaFile.LastWriteTimeUtc.Ticks))
    {
        Require(
            alphaSnapshot.Metadata.HasAlpha,
            "32-bit BITFIELDS BMP lost its explicit alpha mask.");
    }

    var unsupportedProfileFile =
        new FileInfo(bmpUnsupportedProfilePath);
    try
    {
        using var _ =
            await ImageSourceSnapshot.OpenAsync(
                bmpUnsupportedProfilePath,
                unsupportedProfileFile.Length,
                unsupportedProfileFile.LastWriteTimeUtc.Ticks);
        throw new InvalidOperationException(
            "BMP with embedded-profile declaration was accepted.");
    }
    catch (InvalidDataException)
    {
    }

    var calibratedProfileFile =
        new FileInfo(bmpCalibratedProfilePath);
    try
    {
        using var _ =
            await ImageSourceSnapshot.OpenAsync(
                bmpCalibratedProfilePath,
                calibratedProfileFile.Length,
                calibratedProfileFile.LastWriteTimeUtc.Ticks);
        throw new InvalidOperationException(
            "Calibrated-RGB BMP was accepted without color conversion support.");
    }
    catch (InvalidDataException)
    {
    }

    var unsupportedDib64File =
        new FileInfo(bmpUnsupportedDib64Path);
    try
    {
        using var _ =
            await ImageSourceSnapshot.OpenAsync(
                bmpUnsupportedDib64Path,
                unsupportedDib64File.Length,
                unsupportedDib64File.LastWriteTimeUtc.Ticks);
        throw new InvalidOperationException(
            "Unsupported 64-byte/OS2-style BMP DIB header was accepted.");
    }
    catch (InvalidDataException)
    {
    }

    var unsupportedBmpFile =
        new FileInfo(bmpUnsupportedPath);
    try
    {
        using var _ =
            await ImageSourceSnapshot.OpenAsync(
                bmpUnsupportedPath,
                unsupportedBmpFile.Length,
                unsupportedBmpFile.LastWriteTimeUtc.Ticks);
        throw new InvalidOperationException(
            "Unsupported 8-bit BMP was accepted.");
    }
    catch (InvalidDataException)
    {
    }

    var imagePolicy = CoreResourcePolicy.Default;
    var cache = new ThumbnailCache(
        cacheRoot,
        imagePolicy);
    Require(
        cache.ConfiguredByteLimit
            == imagePolicy.ThumbnailCacheByteLimit,
        "Thumbnail cache did not retain the effective Core disk budget.");
    Require(
        !ThumbnailProfiles.GridSmall.LinearLight
        && !ThumbnailProfiles.GridMedium.LinearLight
        && ThumbnailProfiles.DetailPreview.LinearLight,
        "Thumbnail quality profiles lost the grid shrink-on-load / Detail linear-light contract.");

    var cacheModeSource = SourceFor(900, 1, jpgPath);
    var nonLinearCacheKey = ThumbnailCache.GetCacheKey(
        cacheModeSource,
        new ThumbnailProfile(
            "cache-mode",
            256,
            256,
            80,
            1,
            LinearLight: false));
    var linearCacheKey = ThumbnailCache.GetCacheKey(
        cacheModeSource,
        new ThumbnailProfile(
            "cache-mode",
            256,
            256,
            80,
            1,
            LinearLight: true));
    Require(
        !string.Equals(
            nonLinearCacheKey,
            linearCacheKey,
            StringComparison.Ordinal),
        "Thumbnail cache key did not distinguish linear-light processing mode.");

    Require(NetVips.Cache.Max == 0, "libvips operation cache was not disabled.");
    Require(NetVips.Cache.MaxFiles == 0, "libvips file operation cache was not disabled.");
    Require(
        NetVips.NetVips.Concurrency
            == imagePolicy.VipsConcurrency
        && NetVips.NetVips.Concurrency
            == VipsRuntimePolicy.ThumbnailVipsConcurrency,
        "libvips concurrency does not match Image Core resource policy.");
    Require(
        NetVips.Cache.MaxMem
            == checked((ulong)imagePolicy.VipsTrackedMemoryLimitBytes)
        && NetVips.Cache.MaxMem
            == VipsRuntimePolicy.ThumbnailTrackedMemoryLimitBytes,
        "libvips tracked-memory cache does not match Image Core policy.");

    var pipelineOptions =
        ThumbnailPipelineOptions.FromResourcePolicy(imagePolicy);
    await using var pipeline = new ThumbnailPipeline(
        cache,
        pipelineOptions);

    Require(
        pipeline.WorkerCount == imagePolicy.ThumbnailWorkerCount,
        "Worker bound was not mapped from the Core policy.");
    Require(
        pipeline.QueueCapacity == imagePolicy.ThumbnailQueueCapacity,
        "Queue bound was not mapped from the Core policy.");
    Require(
        pipeline.MaxForegroundBurst
            == imagePolicy.ThumbnailForegroundBurst,
        "Foreground fairness bound was not mapped from the Core policy.");

    var conflictingVipsPolicy = CoreResourcePolicy.Resolve(
        new ResourcePolicySettings
        {
            VipsTrackedMemoryLimitBytes =
                imagePolicy.VipsTrackedMemoryLimitBytes
                + (4L * 1024 * 1024)
        },
        processorCount: imagePolicy.ProcessorCount);
    var conflictingPolicyRejected = false;
    try
    {
        VipsRuntimePolicy.EnsureConfigured(
            conflictingVipsPolicy);
    }
    catch (InvalidOperationException)
    {
        conflictingPolicyRejected = true;
    }

    Require(
        conflictingPolicyRejected,
        "Conflicting libvips process-global policy was silently accepted.");

    await VerifyExternalHeicRejectedAsync(
        externalHeicPath,
        "external .heic");
    await VerifyExternalHeicRejectedAsync(
        externalHeifPath,
        "external .heif alias");

    foreach (var external in new[]
             {
                 (Id: 100L, Path: externalHeicPath, Label: ".heic"),
                 (Id: 101L, Path: externalHeifPath, Label: ".heif")
             })
    {
        var failuresBefore =
            pipeline.Diagnostics.Failed;

        try
        {
            _ = await pipeline.RequestAsync(
                SourceFor(external.Id, 1, external.Path),
                ThumbnailProfiles.GridSmall);
            throw new InvalidOperationException(
                $"External HEIF {external.Label} unexpectedly generated a thumbnail without an HEVC decoder.");
        }
        catch (VipsException)
        {
        }

        Require(
            pipeline.Diagnostics.Failed == failuresBefore + 1,
            $"Real thumbnail generation failure for external HEIF {external.Label} was not counted exactly once.");
    }

    long assetId = 1;
    foreach (var sourcePath in new[]
             {
                 jpgPath,
                 pngPath,
                 webpPath,
                 gifPath,
                 tiffPath,
                 bmp24BottomUpPath,
                 bmp24TopDownPath,
                 bmp32RgbPath,
                 bmp32AlphaPath,
                 bmp32AlphaFieldsPath,
                 bmpDisguisedPath
             })
    {
        var source = SourceFor(assetId++, 1, sourcePath);
        var result = await pipeline.RequestAsync(source, ThumbnailProfiles.GridSmall);

        Require(!result.CacheHit, $"First {Path.GetExtension(sourcePath)} request unexpectedly hit cache.");
        Require(File.Exists(result.CachePath), "Generated thumbnail was not persisted.");
        Require(result.Width <= ThumbnailProfiles.GridSmall.MaxWidth, "Thumbnail width exceeded profile.");
        Require(result.Height <= ThumbnailProfiles.GridSmall.MaxHeight, "Thumbnail height exceeded profile.");

        using var cachedImage = NetVips.Image.NewFromFile(result.CachePath);
        Require(
            cachedImage.Interpretation == Enums.Interpretation.Srgb,
            "Cached thumbnail is not normalized to sRGB.");
        cachedImage.Invalidate();
    }

    var gifStaticSource = SourceFor(9, 1, gifPath);
    var gifStaticResult = await pipeline.RequestAsync(
        gifStaticSource,
        ThumbnailProfiles.GridSmall);
    Require(
        gifStaticResult.Width == 48 && gifStaticResult.Height == 32,
        $"Animated GIF preview must use only the first frame; got {gifStaticResult.Width}x{gifStaticResult.Height}.");

    var p3Source = SourceFor(11, 1, p3Path);
    var p3Result = await pipeline.RequestAsync(
        p3Source,
        ThumbnailProfiles.GridSmall);
    using (var p3Cached = NetVips.Image.NewFromFile(p3Result.CachePath))
    using (var redBand = p3Cached.ExtractBand(0))
    using (var greenBand = p3Cached.ExtractBand(1))
    using (var blueBand = p3Cached.ExtractBand(2))
    {
        var redMean = redBand.Avg();
        var greenMean = greenBand.Avg();
        var blueMean = blueBand.Avg();

        Require(
            redMean < 40 && greenMean > 180 && blueMean < 40,
            $"Embedded P3 profile was not normalized to sRGB pixels: R={redMean:F1}, G={greenMean:F1}, B={blueMean:F1}.");
        p3Cached.Invalidate();
    }

    var alphaSource = SourceFor(2, 1, pngPath);
    var alphaResult = await pipeline.RequestAsync(alphaSource, ThumbnailProfiles.GridSmall);
    using (var alphaCached = NetVips.Image.NewFromFile(alphaResult.CachePath))
    {
        Require(alphaCached.Bands >= 4, "PNG alpha channel was not preserved in WebP cache.");
        alphaCached.Invalidate();
    }

    var orientationSource = SourceFor(10, 1, orientedPath);
    var orientationResult = await pipeline.RequestAsync(
        orientationSource,
        new ThumbnailProfile("orientation", 100, 100, 82, 1));
    Require(
        orientationResult.Height > orientationResult.Width,
        "EXIF orientation was not applied before thumbnail sizing.");

    var persistentSource = SourceFor(20, 1, jpgPath);
    var persistentFirst = await pipeline.RequestAsync(
        persistentSource,
        ThumbnailProfiles.GridMedium);
    Require(!persistentFirst.CacheHit, "Persistent cache first request unexpectedly hit.");
    Require(
        persistentFirst.SourceMetadata is not null,
        "Persistent cache generation did not return source technical metadata.");
    persistentSource = persistentSource.WithMetadata(
        persistentFirst.SourceMetadata!);

    var diagnosticsBeforeHit = pipeline.Diagnostics;
    File.Delete(jpgPath);

    var persistentHit = await pipeline.RequestAsync(
        persistentSource,
        ThumbnailProfiles.GridMedium);
    Require(persistentHit.CacheHit, "Warm cache request missed after original deletion.");
    Require(
        pipeline.Diagnostics.SourceOpens == diagnosticsBeforeHit.SourceOpens
        && pipeline.Diagnostics.MetadataProbes == diagnosticsBeforeHit.MetadataProbes,
        "Warm cache hit touched or reprobed the original source.");

    await using (var restartedPipeline = new ThumbnailPipeline(
                     new ThumbnailCache(cacheRoot),
                     new ThumbnailPipelineOptions
                     {
                         WorkerCount = 1,
                         QueueCapacity = 2
                     }))
    {
        var restartedHit = await restartedPipeline.RequestAsync(
            persistentSource,
            ThumbnailProfiles.GridMedium);
        Require(restartedHit.CacheHit, "Fresh pipeline/cache instance did not reuse persistent thumbnail.");
        Require(
            restartedPipeline.Diagnostics.SourceOpens == 0
            && restartedPipeline.Diagnostics.MetadataProbes == 0,
            "Fresh pipeline persistent hit touched the deleted original source.");
    }

    var detailPersistentSource = SourceFor(21, 1, detailCachePath);
    var detailPersistentFirst = await pipeline.RequestAsync(
        detailPersistentSource,
        ThumbnailProfiles.DetailPreview);
    Require(
        !detailPersistentFirst.CacheHit,
        "Detail preview first request unexpectedly hit cache.");
    Require(
        detailPersistentFirst.SourceMetadata is not null,
        "Detail preview generation did not return source metadata.");
    detailPersistentSource = detailPersistentSource.WithMetadata(
        detailPersistentFirst.SourceMetadata!);

    var detailDiagnosticsBeforeHit = pipeline.Diagnostics;
    File.Delete(detailCachePath);

    var detailPersistentHit = await pipeline.RequestAsync(
        detailPersistentSource,
        ThumbnailProfiles.DetailPreview);
    Require(
        detailPersistentHit.CacheHit,
        "Detail preview did not prefer persistent cache after original disappeared.");
    Require(
        pipeline.Diagnostics.SourceOpens == detailDiagnosticsBeforeHit.SourceOpens
        && pipeline.Diagnostics.MetadataProbes == detailDiagnosticsBeforeHit.MetadataProbes,
        "Warm Detail preview cache hit touched or reprobed the original source.");

    await using (var restartedDetailPipeline = new ThumbnailPipeline(
                     new ThumbnailCache(cacheRoot),
                     new ThumbnailPipelineOptions
                     {
                         WorkerCount = 1,
                         QueueCapacity = 2
                     }))
    {
        var restartedDetailHit = await restartedDetailPipeline.RequestAsync(
            detailPersistentSource,
            ThumbnailProfiles.DetailPreview);
        Require(
            restartedDetailHit.CacheHit,
            "Restarted pipeline did not reuse persistent Detail preview.");
        Require(
            restartedDetailPipeline.Diagnostics.SourceOpens == 0
            && restartedDetailPipeline.Diagnostics.MetadataProbes == 0,
            "Restarted Detail preview cache hit touched the missing original.");
    }

    var corruptSource = SourceFor(30, 1, corruptSourcePath);
    var corruptFirst = await pipeline.RequestAsync(
        corruptSource,
        ThumbnailProfiles.GridSmall);
    await File.WriteAllBytesAsync(corruptFirst.CachePath, [1, 2, 3, 4, 5]);

    var corruptRecovered = await pipeline.RequestAsync(
        corruptSource,
        ThumbnailProfiles.GridSmall);
    Require(!corruptRecovered.CacheHit, "Corrupt cache entry was accepted as a hit.");
    Require(corruptRecovered.CacheFileBytes > 5, "Corrupt cache entry was not regenerated.");

    var changedV1 = SourceFor(40, 1, changedPath);
    var changedFirst = await pipeline.RequestAsync(
        changedV1,
        ThumbnailProfiles.GridSmall);

    await Task.Delay(20);
    WriteRgb(changedPath, 640, 480);
    File.SetLastWriteTimeUtc(changedPath, DateTime.UtcNow.AddSeconds(1));
    var changedV2 = SourceFor(40, 2, changedPath);
    var changedSecond = await pipeline.RequestAsync(
        changedV2,
        ThumbnailProfiles.GridSmall);

    Require(!changedSecond.CacheHit, "Changed source reused stale thumbnail.");
    Require(
        !string.Equals(changedFirst.CacheKey, changedSecond.CacheKey, StringComparison.Ordinal),
        "Source revision did not change thumbnail cache key.");
    Require(
        !string.Equals(changedFirst.CachePath, changedSecond.CachePath, StringComparison.Ordinal),
        "Source revision did not move to a new cache path.");

    WriteSolidJpeg(identityPath, 24);
    WriteSolidJpeg(identityReplacementPath, 220);
    var identityLength = Math.Max(
        new FileInfo(identityPath).Length,
        new FileInfo(identityReplacementPath).Length);
    PadToLength(identityPath, identityLength);
    PadToLength(identityReplacementPath, identityLength);
    var identityTimestamp = DateTime.UtcNow.AddMinutes(-5);
    File.SetLastWriteTimeUtc(identityPath, identityTimestamp);
    File.SetLastWriteTimeUtc(identityReplacementPath, identityTimestamp);

    var identityStat = new FileInfo(identityPath);
    using var identitySnapshot = await ImageSourceSnapshot.OpenAsync(
        identityPath,
        identityStat.Length,
        identityStat.LastWriteTimeUtc.Ticks);
    var identityMetadata = identitySnapshot.Metadata;
    identitySnapshot.Dispose();

    FileSourceIdentity? fileIdIdentityBefore = null;
    if (OperatingSystem.IsWindows())
    {
        using var identityStream = new FileStream(
            identityPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        fileIdIdentityBefore =
            FileSourceIdentityProbe.ReadWindowsFileIdIdentity(
                identityStream);
        Require(
            fileIdIdentityBefore is not null
            && fileIdIdentityBefore.Kind
                == FileSourceIdentityKind.WindowsFileId
            && !fileIdIdentityBefore.UsedFullHash
            && fileIdIdentityBefore.BytesHashed == 0
            && fileIdIdentityBefore.Value.Length <= 80
            && FileSourceIdentityProbe.IsValid(
                fileIdIdentityBefore.Value),
            "Windows FILE_ID_INFO identity fallback was unavailable, invalid, too large for persisted schema, or hashed source bytes.");
    }

    var replacementBytes = await File.ReadAllBytesAsync(
        identityReplacementPath);
    await File.WriteAllBytesAsync(
        identityPath,
        replacementBytes);
    File.SetLastWriteTimeUtc(
        identityPath,
        identityTimestamp);

    var replacedStat = new FileInfo(identityPath);
    Require(
        replacedStat.Length == identityStat.Length
        && replacedStat.LastWriteTimeUtc.Ticks
            == identityStat.LastWriteTimeUtc.Ticks,
        "Same-stat replacement fixture did not preserve size/mtime.");

    if (OperatingSystem.IsWindows())
    {
        using var replacedStream = new FileStream(
            identityPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var fileIdIdentityAfter =
            FileSourceIdentityProbe.ReadWindowsFileIdIdentity(
                replacedStream);
        Require(
            fileIdIdentityBefore is not null
            && fileIdIdentityAfter is not null
            && !string.Equals(
                fileIdIdentityBefore.Value,
                fileIdIdentityAfter.Value,
                StringComparison.Ordinal),
            "Windows file-id fallback did not detect same-size/mtime source replacement.");
    }

    try
    {
        await FullResolutionDecoder.DecodeAsync(
            new FullResolutionSource(
                identityPath,
                identityStat.Length,
                identityStat.LastWriteTimeUtc.Ticks,
                identityMetadata.SourceIdentity),
            32L * 1024 * 1024,
            _ => { });
        throw new InvalidOperationException(
            "Same-size/mtime source replacement was not rejected by content identity.");
    }
    catch (FullResolutionSourceChangedException)
    {
    }

    var concurrentSource = SourceFor(45, 1, concurrentPath);
    var concurrentResults = await Task.WhenAll(
        Enumerable.Range(0, 8)
            .Select(_ => pipeline.RequestAsync(
                concurrentSource,
                ThumbnailProfiles.GridMedium)));

    Require(
        concurrentResults.All(result =>
            string.Equals(
                result.CachePath,
                concurrentResults[0].CachePath,
                StringComparison.Ordinal)),
        "Concurrent requests did not converge on one persistent cache path.");
    Require(
        !Directory.EnumerateFiles(cacheRoot, "*.tmp.webp", SearchOption.AllDirectories).Any(),
        "Concurrent generation left interrupted temporary files.");

    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        try
        {
            _ = await pipeline.RequestAsync(
                changedV2,
                ThumbnailProfiles.GridSmall,
                cancellationToken: cancelled.Token);
            throw new InvalidOperationException("Pre-cancelled thumbnail request unexpectedly succeeded.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    using (var nativeCancelled = new CancellationTokenSource())
    {
        var opensBeforeCancellation = pipeline.Diagnostics.SourceOpens;
        var sourceOpenCancellationsBefore =
            pipeline.Diagnostics.SourceOpenCancellations;
        var failuresBeforeCancellation = pipeline.Diagnostics.Failed;
        var cancellationSource = SourceFor(46, 1, cancellationPath);
        var cancellationTask = pipeline.RequestAsync(
            cancellationSource,
            ThumbnailProfiles.DetailPreview,
            cancellationToken: nativeCancelled.Token);

        for (var attempt = 0;
             attempt < 2000 && pipeline.Diagnostics.SourceOpens == opensBeforeCancellation;
             attempt++)
        {
            await Task.Delay(1);
        }

        Require(
            pipeline.Diagnostics.SourceOpens > opensBeforeCancellation,
            "Cancellation smoke never reached native source evaluation.");

        nativeCancelled.Cancel();

        try
        {
            _ = await cancellationTask;
            throw new InvalidOperationException("In-flight native thumbnail cancellation unexpectedly completed.");
        }
        catch (OperationCanceledException)
        {
        }

        for (var attempt = 0;
             attempt < 2000
             && pipeline.Diagnostics.SourceOpenCancellations
                == sourceOpenCancellationsBefore;
             attempt++)
        {
            await Task.Delay(1);
        }

        Require(
            pipeline.Diagnostics.Failed == failuresBeforeCancellation,
            "Expected in-flight cancellation was incorrectly counted as a thumbnail generation failure.");
        Require(
            pipeline.Diagnostics.SourceOpenCancellations
                == sourceOpenCancellationsBefore + 1,
            "In-flight cancellation after source evaluation was not accounted separately.");

        Require(
            !Directory.EnumerateFiles(cacheRoot, "*.tmp.webp", SearchOption.AllDirectories).Any(),
            "Native cancellation left a temporary cache file behind.");
    }

    var shutdownCacheRoot = Path.Combine(root, "shutdown-cache");
    var shutdownPipeline = new ThumbnailPipeline(
        new ThumbnailCache(shutdownCacheRoot),
        new ThumbnailPipelineOptions
        {
            WorkerCount = 1,
            QueueCapacity = 2,
            MaxForegroundBurst = 2
        });
    var shutdownSource = SourceFor(47, 1, cancellationPath);
    var shutdownOpensBefore =
        shutdownPipeline.Diagnostics.SourceOpens;
    var shutdownRequest = shutdownPipeline.RequestAsync(
        shutdownSource,
        ThumbnailProfiles.DetailPreview);

    for (var attempt = 0;
         attempt < 2000
         && shutdownPipeline.Diagnostics.SourceOpens
             == shutdownOpensBefore;
         attempt++)
    {
        await Task.Delay(1);
    }

    Require(
        shutdownPipeline.Diagnostics.SourceOpens
            > shutdownOpensBefore,
        "Pipeline shutdown smoke never reached active source evaluation.");

    var firstShutdown =
        shutdownPipeline.DisposeAsync().AsTask();
    var secondShutdown =
        shutdownPipeline.DisposeAsync().AsTask();

    await Task.WhenAll(firstShutdown, secondShutdown);

    try
    {
        _ = await shutdownRequest;
        throw new InvalidOperationException(
            "Active thumbnail request completed after pipeline shutdown.");
    }
    catch (ObjectDisposedException)
    {
    }

    Require(
        firstShutdown.IsCompletedSuccessfully
        && secondShutdown.IsCompletedSuccessfully,
        "Concurrent ThumbnailPipeline DisposeAsync callers did not observe one completed shutdown.");

    try
    {
        _ = await shutdownPipeline.RequestAsync(
            shutdownSource,
            ThumbnailProfiles.GridSmall);
        throw new InvalidOperationException(
            "Disposed ThumbnailPipeline accepted new work.");
    }
    catch (ObjectDisposedException)
    {
    }

    Require(
        !Directory.EnumerateFiles(
                shutdownCacheRoot,
                "*.tmp.webp",
                SearchOption.AllDirectories)
            .Any(),
        "Pipeline shutdown left an interrupted temporary thumbnail behind.");

    var maintenanceRoot =
        Path.Combine(
            root,
            "maintenance-cache");
    var maintenancePolicy =
        CoreResourcePolicy.Resolve(
            new ResourcePolicySettings
            {
                ThumbnailCacheByteLimit =
                    64L * 1024 * 1024
            },
            processorCount:
                imagePolicy.ProcessorCount);
    var maintenanceCache =
        new ThumbnailCache(
            maintenanceRoot,
            maintenancePolicy);
    var oldCacheTime =
        DateTime.UtcNow
        - TimeSpan.FromDays(7);

    for (var index = 0; index < 4; index++)
    {
        CreateSizedCacheFile(
            Path.Combine(
                maintenanceRoot,
                $"seed-{index:D2}.webp"),
            20L * 1024 * 1024,
            oldCacheTime.AddMinutes(index));
    }

    var maintenancePipeline =
        new ThumbnailPipeline(
            maintenanceCache,
            new ThumbnailPipelineOptions
            {
                WorkerCount = 1,
                QueueCapacity = 4,
                MaxForegroundBurst = 2,
                CacheMaintenanceQuietPeriod =
                    TimeSpan.FromMilliseconds(500)
            });

    try
    {
        await Task.Delay(650);

        var startupMaintenanceStats =
            await maintenanceCache
                .GetStatsAsync();
        Require(
            startupMaintenanceStats.TotalBytes
                > maintenanceCache.ConfiguredByteLimit,
            "Pipeline construction unexpectedly pruned the persistent cache.");
        Require(
            maintenancePipeline
                .MaintenanceDiagnostics
                .RunsScheduled == 0,
            "Pipeline construction scheduled an unconditional warm-start cache scan.");

        var maintenanceSource =
            SourceFor(
                4700,
                1,
                p3PngPath);
        var firstMaintenanceRequest =
            await maintenancePipeline.RequestAsync(
                maintenanceSource,
                ThumbnailProfiles.GridSmall);

        Require(
            firstMaintenanceRequest.SourceMetadata
                is not null,
            "Maintenance smoke did not receive source metadata.");

        await Task.Delay(50);

        var beforePreemption =
            maintenancePipeline
                .MaintenanceDiagnostics;
        var preemptionStarted =
            System.Diagnostics.Stopwatch.StartNew();

        _ = await maintenancePipeline.RequestAsync(
            maintenanceSource.WithMetadata(
                firstMaintenanceRequest.SourceMetadata!),
            ThumbnailProfiles.GridSmall);

        preemptionStarted.Stop();

        var afterPreemption =
            maintenancePipeline
                .MaintenanceDiagnostics;

        Require(
            afterPreemption.ForegroundPreemptions
                > beforePreemption.ForegroundPreemptions,
            "Foreground request did not preempt pending cache maintenance.");
        Require(
            afterPreemption.RunsCancelled
                > beforePreemption.RunsCancelled,
            "Preempted cache maintenance was not observed as cancelled.");
        Require(
            preemptionStarted.Elapsed
                < TimeSpan.FromSeconds(2),
            "Foreground thumbnail request waited too long for cache maintenance to yield.");

        await WaitUntilAsync(
            async () =>
            {
                var diagnostics =
                    maintenancePipeline
                        .MaintenanceDiagnostics;
                if (diagnostics.RunsCompleted
                    == 0)
                {
                    return false;
                }

                var stats =
                    await maintenanceCache
                        .GetStatsAsync();

                return stats.TotalBytes
                    <= maintenanceCache
                        .ConfiguredByteLimit;
            },
            TimeSpan.FromSeconds(8),
            "Background cache maintenance did not converge below the configured disk budget.");

        var backgroundDiagnostics =
            maintenancePipeline
                .MaintenanceDiagnostics;
        Require(
            backgroundDiagnostics.RunsCompleted
                > 0
            && backgroundDiagnostics.FilesDeleted
                > 0
            && backgroundDiagnostics.BytesDeleted
                > 0
            && backgroundDiagnostics.RunsFailed
                == 0,
            "Background cache maintenance diagnostics did not report a clean prune.");

        CreateSizedCacheFile(
            Path.Combine(
                maintenanceRoot,
                "shutdown-overflow.webp"),
            20L * 1024 * 1024,
            oldCacheTime.AddDays(-1));

        var beforeShutdownStats =
            await maintenanceCache
                .GetStatsAsync();
        Require(
            beforeShutdownStats.TotalBytes
                > maintenanceCache
                    .ConfiguredByteLimit,
            "Shutdown cache fixture did not exceed the configured disk budget.");
    }
    finally
    {
        await maintenancePipeline.DisposeAsync();
    }

    var afterShutdownStats =
        await maintenanceCache
            .GetStatsAsync();
    var finalMaintenanceDiagnostics =
        maintenancePipeline
            .MaintenanceDiagnostics;

    Require(
        afterShutdownStats.TotalBytes
            <= maintenanceCache.ConfiguredByteLimit,
        "Pipeline shutdown did not converge persistent thumbnail cache below its configured disk budget.");
    Require(
        finalMaintenanceDiagnostics.RunsCompleted
            >= 2
        && finalMaintenanceDiagnostics.RunsFailed
            == 0
        && finalMaintenanceDiagnostics.LastBytesAfter
            <= maintenanceCache.ConfiguredByteLimit,
        "Final cache-maintenance diagnostics did not report clean shutdown convergence.");

    var orientedFullSource = new FullResolutionSource(
        orientedPath,
        new FileInfo(orientedPath).Length,
        File.GetLastWriteTimeUtc(orientedPath).Ticks);
    var orientedFullInfo = await FullResolutionDecoder.ProbeAsync(
        orientedFullSource);
    Require(
        orientedFullInfo.Width == 60 && orientedFullInfo.Height == 120,
        $"Full-resolution EXIF orientation was not applied: {orientedFullInfo.Width}x{orientedFullInfo.Height}.");

    var orientedRows = 0;
    var orientedStripeCount = 0;
    await FullResolutionDecoder.DecodeAsync(
        orientedFullSource,
        16L * 1024 * 1024,
        stripe =>
        {
            Require(
                stripe.Y == orientedRows,
                "Full-resolution stripes were not emitted in top-to-bottom order.");
            Require(
                stripe.Width == 60,
                "Full-resolution oriented stripe width mismatch.");
            Require(
                stripe.RowBytes == stripe.Width * 4,
                "Full-resolution stripe row-byte contract mismatch.");

            orientedRows += stripe.Height;
            orientedStripeCount++;
        },
        stripeHeight: 17);
    Require(
        orientedRows == 120 && orientedStripeCount > 1,
        "Full-resolution oriented decode did not stream the complete image.");

    var alphaFullSource = new FullResolutionSource(
        pngPath,
        new FileInfo(pngPath).Length,
        File.GetLastWriteTimeUtc(pngPath).Ticks);
    var alphaFullInfo = await FullResolutionDecoder.ProbeAsync(alphaFullSource);
    Require(alphaFullInfo.HasAlpha, "Full-resolution PNG probe lost alpha metadata.");

    byte[]? alphaFirstStripe = null;
    await FullResolutionDecoder.DecodeAsync(
        alphaFullSource,
        16L * 1024 * 1024,
        stripe => alphaFirstStripe ??= stripe.RgbaBytes,
        stripeHeight: 32);
    Require(
        alphaFirstStripe is { Length: > 4 }
        && alphaFirstStripe[3] is >= 120 and <= 136,
        "Full-resolution PNG decode did not preserve straight alpha.");

    var p3FullSource = new FullResolutionSource(
        p3Path,
        new FileInfo(p3Path).Length,
        File.GetLastWriteTimeUtc(p3Path).Ticks);
    byte[]? p3FirstStripe = null;
    await FullResolutionDecoder.DecodeAsync(
        p3FullSource,
        16L * 1024 * 1024,
        stripe => p3FirstStripe ??= stripe.RgbaBytes,
        stripeHeight: 16);
    Require(
        p3FirstStripe is { Length: > 4 }
        && p3FirstStripe[0] < 50
        && p3FirstStripe[1] > 170
        && p3FirstStripe[2] < 50,
        "Full-resolution embedded P3 profile was not normalized to sRGB.");

    var p3PngFullSource = new FullResolutionSource(
        p3PngPath,
        new FileInfo(p3PngPath).Length,
        File.GetLastWriteTimeUtc(p3PngPath).Ticks);
    byte[]? p3PngFirstStripe = null;
    await FullResolutionDecoder.DecodeAsync(
        p3PngFullSource,
        16L * 1024 * 1024,
        stripe => p3PngFirstStripe ??= stripe.RgbaBytes,
        stripeHeight: 23);
    Require(
        p3PngFirstStripe is { Length: > 4 }
        && p3PngFirstStripe[0] < 80
        && p3PngFirstStripe[1] > 140
        && p3PngFirstStripe[2] < 100
        && p3PngFirstStripe[3] is >= 170 and <= 190,
        "Full-resolution PNG ICC Random fallback did not preserve sRGB-normalized color and alpha.");

    var webpFullSource = new FullResolutionSource(
        webpPath,
        new FileInfo(webpPath).Length,
        File.GetLastWriteTimeUtc(webpPath).Ticks);
    var webpFullInfo = await FullResolutionDecoder.ProbeAsync(webpFullSource);
    Require(
        webpFullInfo.Width == 320 && webpFullInfo.Height == 200,
        $"Full-resolution WebP probe mismatch: {webpFullInfo.Width}x{webpFullInfo.Height}.");

    var webpRows = 0;
    await FullResolutionDecoder.DecodeAsync(
        webpFullSource,
        16L * 1024 * 1024,
        stripe => webpRows += stripe.Height,
        stripeHeight: 31);
    Require(
        webpRows == 200,
        "Full-resolution WebP decode did not stream the complete image.");

    await VerifyFullResolutionAsync(corruptSourcePath, 320, 200, "JPEG");
    await VerifyFullResolutionAsync(tiffPath, 320, 200, "TIFF");
    await VerifyFullResolutionAsync(
        bmp24BottomUpPath,
        5,
        3,
        "BMP 24-bit bottom-up");
    await VerifyFullResolutionAsync(
        bmp24TopDownPath,
        5,
        3,
        "BMP 24-bit top-down");
    await VerifyFullResolutionAsync(
        bmp32RgbPath,
        4,
        3,
        "BMP 32-bit BI_RGB");
    await VerifyFullResolutionAsync(
        bmp32AlphaPath,
        4,
        3,
        "BMP 32-bit BITFIELDS alpha");
    await VerifyFullResolutionAsync(
        bmp32AlphaFieldsPath,
        4,
        3,
        "BMP 32-bit ALPHABITFIELDS alpha");

    byte[]? bmpBottomUpFirstStripe = null;
    await FullResolutionDecoder.DecodeAsync(
        new FullResolutionSource(
            bmp24BottomUpPath,
            new FileInfo(bmp24BottomUpPath).Length,
            File.GetLastWriteTimeUtc(
                bmp24BottomUpPath).Ticks),
        4L * 1024 * 1024,
        stripe =>
            bmpBottomUpFirstStripe ??=
                stripe.RgbaBytes,
        stripeHeight: 8);
    Require(
        bmpBottomUpFirstStripe is { Length: >= 4 }
        && bmpBottomUpFirstStripe[0] == 20
        && bmpBottomUpFirstStripe[1] == 40
        && bmpBottomUpFirstStripe[2] == 60
        && bmpBottomUpFirstStripe[3] == 255,
        "Bottom-up BMP was not normalized to top-down RGBA order.");

    byte[]? bmpTopDownFirstStripe = null;
    await FullResolutionDecoder.DecodeAsync(
        new FullResolutionSource(
            bmp24TopDownPath,
            new FileInfo(bmp24TopDownPath).Length,
            File.GetLastWriteTimeUtc(
                bmp24TopDownPath).Ticks),
        4L * 1024 * 1024,
        stripe =>
            bmpTopDownFirstStripe ??=
                stripe.RgbaBytes,
        stripeHeight: 8);
    Require(
        bmpTopDownFirstStripe is { Length: >= 4 }
        && bmpTopDownFirstStripe[0] == 20
        && bmpTopDownFirstStripe[1] == 40
        && bmpTopDownFirstStripe[2] == 60
        && bmpTopDownFirstStripe[3] == 255,
        "Top-down BMP was not normalized to top-down RGBA order.");

    byte[]? bmpRgbFirstStripe = null;
    await FullResolutionDecoder.DecodeAsync(
        new FullResolutionSource(
            bmp32RgbPath,
            new FileInfo(bmp32RgbPath).Length,
            File.GetLastWriteTimeUtc(
                bmp32RgbPath).Ticks),
        4L * 1024 * 1024,
        stripe =>
            bmpRgbFirstStripe ??=
                stripe.RgbaBytes,
        stripeHeight: 8);
    Require(
        bmpRgbFirstStripe is { Length: >= 4 }
        && bmpRgbFirstStripe[3] == 255,
        "32-bit BI_RGB BMP incorrectly treated its reserved byte as alpha.");

    byte[]? bmpAlphaFirstStripe = null;
    await FullResolutionDecoder.DecodeAsync(
        new FullResolutionSource(
            bmp32AlphaPath,
            new FileInfo(bmp32AlphaPath).Length,
            File.GetLastWriteTimeUtc(
                bmp32AlphaPath).Ticks),
        4L * 1024 * 1024,
        stripe =>
            bmpAlphaFirstStripe ??=
                stripe.RgbaBytes,
        stripeHeight: 8);
    Require(
        bmpAlphaFirstStripe is { Length: >= 4 }
        && bmpAlphaFirstStripe[0] == 90
        && bmpAlphaFirstStripe[1] == 110
        && bmpAlphaFirstStripe[2] == 130
        && bmpAlphaFirstStripe[3] == 128,
        "32-bit BITFIELDS BMP did not preserve explicit alpha.");

    var gifFullSource = new FullResolutionSource(
        gifPath,
        new FileInfo(gifPath).Length,
        File.GetLastWriteTimeUtc(gifPath).Ticks);
    var gifFullInfo = await FullResolutionDecoder.ProbeAsync(gifFullSource);
    Require(
        gifFullInfo.Width == 48 && gifFullInfo.Height == 32,
        "Animated GIF full-resolution fallback must use the first frame.");

    try
    {
        await FullResolutionDecoder.DecodeAsync(
            new FullResolutionSource(
                changedPath,
                new FileInfo(changedPath).Length,
                File.GetLastWriteTimeUtc(changedPath).Ticks),
            1_000,
            _ => { });
        throw new InvalidOperationException(
            "Full-resolution decode ignored the hard byte budget.");
    }
    catch (FullResolutionBudgetExceededException exception)
    {
        Require(
            exception.RequiredBytes > exception.BudgetBytes,
            "Full-resolution budget exception reported invalid byte accounting.");
    }

    var staleIdentityFile = new FileInfo(sourceChangePath);
    var staleIdentitySource = new FullResolutionSource(
        sourceChangePath,
        staleIdentityFile.Length,
        staleIdentityFile.LastWriteTimeUtc.Ticks);
    var staleIdentityInfo =
        await FullResolutionDecoder.ProbeAsync(staleIdentitySource);

    await Task.Delay(20);
    WriteRgb(sourceChangePath, 640, 480);
    File.SetLastWriteTimeUtc(
        sourceChangePath,
        DateTime.UtcNow.AddSeconds(2));

    var staleIdentityStripes = 0;
    try
    {
        await FullResolutionDecoder.DecodeAsync(
            staleIdentitySource,
            32L * 1024 * 1024,
            _ => staleIdentityStripes++,
            expectedInfo: staleIdentityInfo);
        throw new InvalidOperationException(
            "Full-resolution decode accepted a source that changed after probe.");
    }
    catch (FullResolutionSourceChangedException)
    {
    }

    Require(
        staleIdentityStripes == 0,
        "Changed source emitted pixels before its identity mismatch was rejected.");

    var replacementFile = new FileInfo(sourceChangePath);
    var replacementSource = new FullResolutionSource(
        sourceChangePath,
        replacementFile.Length,
        replacementFile.LastWriteTimeUtc.Ticks);
    var mismatchedProbeStripes = 0;

    try
    {
        await FullResolutionDecoder.DecodeAsync(
            replacementSource,
            32L * 1024 * 1024,
            _ => mismatchedProbeStripes++,
            expectedInfo: staleIdentityInfo);
        throw new InvalidOperationException(
            "Full-resolution decode accepted dimensions that no longer match the probed bitmap.");
    }
    catch (FullResolutionSourceChangedException)
    {
    }

    Require(
        mismatchedProbeStripes == 0,
        "Probe/decode dimension mismatch emitted pixels before rejection.");

    using (var fullCancellation = new CancellationTokenSource())
    {
        var stripesObserved = 0;
        var fullCancellationTask = FullResolutionDecoder.DecodeAsync(
            new FullResolutionSource(
                cancellationPath,
                new FileInfo(cancellationPath).Length,
                File.GetLastWriteTimeUtc(cancellationPath).Ticks),
            200L * 1024 * 1024,
            _ =>
            {
                stripesObserved++;
                if (stripesObserved == 1)
                {
                    fullCancellation.Cancel();
                }
            },
            stripeHeight: 32,
            cancellationToken: fullCancellation.Token);

        try
        {
            await fullCancellationTask;
            throw new InvalidOperationException(
                "Full-resolution strip decode ignored in-flight cancellation.");
        }
        catch (OperationCanceledException)
        {
        }

        Require(
            stripesObserved == 1,
            $"Full-resolution cancellation continued for {stripesObserved} stripes.");
    }

    if (capabilities.AvifRoundTrip)
    {
        var avifPath = Path.Combine(sourceRoot, "sample.avif");
        await File.WriteAllBytesAsync(
            avifPath,
            CreateHeifFixture(Enums.ForeignHeifCompression.Av1));
        var avifResult = await pipeline.RequestAsync(
            SourceFor(50, 1, avifPath),
            ThumbnailProfiles.GridSmall);
        Require(File.Exists(avifResult.CachePath), "AVIF capability was reported but AVIF smoke failed.");
        await VerifyFullResolutionAsync(avifPath, 8, 6, "AVIF");
        await VerifyRecommendedAccessPolicyAsync(
            avifPath,
            FullResolutionAccessPolicy.Random,
            "AVIF");
    }

    if (capabilities.HeicRoundTrip)
    {
        var heicPath = Path.Combine(sourceRoot, "sample.heic");
        await File.WriteAllBytesAsync(
            heicPath,
            CreateHeifFixture(Enums.ForeignHeifCompression.Hevc));
        var heicResult = await pipeline.RequestAsync(
            SourceFor(51, 1, heicPath),
            ThumbnailProfiles.GridSmall);
        Require(File.Exists(heicResult.CachePath), "HEIC capability was reported but HEIC smoke failed.");
        await VerifyFullResolutionAsync(heicPath, 8, 6, "HEIC");
        await VerifyRecommendedAccessPolicyAsync(
            heicPath,
            FullResolutionAccessPolicy.Random,
            "HEIC");
    }

    var memoryCacheRoot = Path.Combine(
        root,
        "memory-only-cache");
    var memoryBackingCache =
        new ThumbnailCache(memoryCacheRoot);

    await using (var memoryPipeline =
        new ThumbnailPipeline(
            memoryBackingCache,
            new ThumbnailPipelineOptions
            {
                WorkerCount = 1,
                QueueCapacity = 8,
                StorageMode =
                    ThumbnailStorageMode.MemoryOnly,
                EncodedMemoryByteLimit =
                    16L * 1024 * 1024
            }))
    {
        var memorySource =
            SourceFor(
                4800,
                1,
                jpgPath);
        var firstMemory =
            await memoryPipeline.RequestAsync(
                memorySource,
                ThumbnailProfiles.GridSmall);

        Require(
            !firstMemory.CacheHit
            && firstMemory.EncodedBytes
                is { Length: > 0 }
            && string.IsNullOrEmpty(
                firstMemory.CachePath)
            && firstMemory.SourceMetadata
                is not null,
            "Memory-only first thumbnail did not return an encoded in-memory payload.");

        var secondMemory =
            await memoryPipeline.RequestAsync(
                memorySource.WithMetadata(
                    firstMemory.SourceMetadata!),
                ThumbnailProfiles.GridSmall);

        Require(
            secondMemory.CacheHit
            && secondMemory.EncodedBytes
                is { Length: > 0 }
            && string.IsNullOrEmpty(
                secondMemory.CachePath),
            "Memory-only second thumbnail did not reuse the bounded encoded-memory cache.");

        var memoryStats =
            memoryPipeline.MemoryCacheStats;
        Require(
            memoryStats.HitCount >= 1
            && memoryStats.EntryCount >= 1
            && memoryStats.EncodedBytes > 0
            && memoryStats.EncodedBytes
                <= memoryStats.ByteLimit,
            "Memory-only encoded thumbnail cache diagnostics escaped their bounds.");

        var memoryDiskStats =
            await memoryBackingCache.GetStatsAsync();
        Require(
            memoryDiskStats.FileCount == 0
            && memoryDiskStats.TotalBytes == 0
            && memoryDiskStats.InterruptedWriteCount
                == 0,
            "Memory-only thumbnail pipeline wrote persistent cache files.");

        var memoryMaintenance =
            memoryPipeline.MaintenanceDiagnostics;
        Require(
            memoryMaintenance.RunsScheduled == 0
            && memoryMaintenance.RunsStarted == 0
            && memoryMaintenance.RunsCompleted
                == 0,
            "Memory-only thumbnail pipeline unexpectedly scheduled disk-cache maintenance.");
    }

    // From this point on the smoke exercises ThumbnailCache maintenance
    // directly. Drain the production pipeline first so its owned background
    // maintenance cannot race the explicit prune/recovery assertions below.
    await pipeline.DisposeAsync();

    var statsBeforePrune = await cache.GetStatsAsync();
    Require(statsBeforePrune.FileCount >= 6, "Expected persisted thumbnail cache entries.");
    Require(statsBeforePrune.TotalBytes > 0, "Thumbnail cache accounting returned zero bytes.");

    var fakeInterrupted = Path.Combine(cacheRoot, "dead.tmp.webp");
    await File.WriteAllBytesAsync(fakeInterrupted, [1, 2, 3]);
    _ = new ThumbnailCache(cacheRoot);
    Require(
        File.Exists(fakeInterrupted),
        "Cache construction unexpectedly scanned/deleted temporary files.");

    var freshRecovered = await cache.RecoverInterruptedWritesAsync();
    Require(freshRecovered == 0, "Fresh temporary write was treated as interrupted.");
    Require(File.Exists(fakeInterrupted), "Fresh temporary write was deleted as interrupted.");

    File.SetLastWriteTimeUtc(
        fakeInterrupted,
        DateTime.UtcNow - ThumbnailCache.InterruptedWriteGracePeriod - TimeSpan.FromMinutes(1));
    var staleRecovered = await cache.RecoverInterruptedWritesAsync();
    Require(staleRecovered == 1, "Stale interrupted thumbnail write was not reported as recovered.");
    Require(!File.Exists(fakeInterrupted), "Stale interrupted thumbnail write was not cleaned up.");

    var partialTarget = Math.Max(1, statsBeforePrune.TotalBytes - 1);
    var partialPrune = await cache.PruneAsync(partialTarget);
    Require(partialPrune.FilesDeleted > 0, "Bounded partial prune removed no files.");
    Require(
        partialPrune.BytesAfter <= partialTarget,
        "Bounded partial prune did not reach its disk budget.");

    var prune = await cache.PruneAsync(0);
    Require(prune.FilesDeleted > 0, "Thumbnail zero-budget prune removed no remaining files.");
    Require(prune.BytesAfter == 0, "Thumbnail prune did not enforce zero-byte target.");

    Console.WriteLine(
        $"Image smoke: jpeg/png/webp/gif/BMP OK; external HEIC/HEIF correctly rejected; BMP native={capabilities.BmpNativeLoad}, fallback={capabilities.BmpFallbackLoad}; HEIF ops load={capabilities.HeifLoadOperation}, save={capabilities.HeifSaveOperation}, " +
        $"AVIF={capabilities.AvifRoundTrip}, HEIC={capabilities.HeicRoundTrip}; " +
        $"hits={pipeline.Diagnostics.CacheHits}, generated={pipeline.Diagnostics.Generated}, source-opens={pipeline.Diagnostics.SourceOpens}");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

static class SmokeConstants
{
    public static readonly int[] RgbaPixel = [20, 80, 160, 128];
}
