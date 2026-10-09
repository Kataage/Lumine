using SkiaSharp;

namespace Lumine.Viewer.Benchmarks;

// The benchmark uses intentionally plain blue WebP thumbnail fixtures.
// Sample one known position per gallery column in the bottommost partially
// revealed row of the 1200x800 first-offset Skia snapshot. This is a raster
// diagnostic, not a general-purpose CV classifier or GPU-present fence.
internal readonly record struct RenderedFrameTileAudit(
    int SampledColumns,
    int BlueThumbnailSamples,
    int DarkPlaceholderSamples,
    int OtherSamples,
    int SampleY)
{
    internal static RenderedFrameTileAudit Inspect(
        string pngPath,
        int expectedColumns,
        double sampleFraction = 0.8875)
    {
        if (expectedColumns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedColumns));
        }

        if (!double.IsFinite(sampleFraction)
            || sampleFraction <= 0
            || sampleFraction >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleFraction));
        }

        using var image = SKBitmap.Decode(pngPath)
            ?? throw new InvalidOperationException(
                "Could not decode captured Skia-rendered PNG.");

        if (image.Width < expectedColumns * 4 || image.Height < 20)
        {
            throw new InvalidOperationException(
                "Rendered frame too small to sample gallery columns.");
        }

        // Default 0.8875*H = y710 remains unchanged for the original
        // 198px/200px offset probe. A single +50px wheel notch instead
        // reveals the next row at y764 in the fixed 1200x800 fixture;
        // its blue center lies near y780 (0.975*H). Do NOT sample y710
        // after one notch: it lies in the previous row's blue/black
        // caption gradient, not the newly exposed row.
        var y = Math.Clamp(
            (int)Math.Round(image.Height * sampleFraction),
            0, image.Height - 1);
        var blue = 0;
        var dark = 0;
        var other = 0;

        for (var column = 0; column < expectedColumns; column++)
        {
            var x = Math.Clamp(
                (int)Math.Round(
                    (column + 0.5) * image.Width / expectedColumns),
                0, image.Width - 1);
            var color = image.GetPixel(x, y);

            // Synthetic WebP tile content is (48, 112, 197) after
            // rasterization. The empty tile fill is (21, 21, 24).
            // Broad color checks tolerate compression but will not
            // confuse the dark placeholder with the blue fixture.
            if (color.Blue >= 150
                && color.Blue > color.Green + 40
                && color.Green >= 70
                && color.Red <= 100)
            {
                blue++;
            }
            else if (color.Red <= 40
                     && color.Green <= 40
                     && color.Blue <= 40)
            {
                dark++;
            }
            else
            {
                other++;
            }
        }

        if (blue + dark + other != expectedColumns)
        {
            throw new InvalidOperationException(
                "Rendered-frame sample accounting is inconsistent.");
        }

        return new RenderedFrameTileAudit(
            expectedColumns, blue, dark, other, y);
    }
}
