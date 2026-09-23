using NetVips;

namespace Lumine.Image;

public sealed record VipsFormatCapabilities(
    bool JpegLoad,
    bool PngLoad,
    bool WebpLoad,
    bool WebpSave,
    bool GifLoad,
    bool HeifLoadOperation,
    bool HeifSaveOperation,
    bool AvifRoundTrip,
    bool HeicRoundTrip);

public static class VipsCapabilities
{
    public static VipsFormatCapabilities Probe()
    {
        VipsRuntimePolicy.EnsureConfigured();

        var operations = global::NetVips.NetVips.GetOperations()
            .ToHashSet(StringComparer.Ordinal);

        var heifLoad = operations.Contains("heifload");
        var heifSave = operations.Contains("heifsave");

        return new VipsFormatCapabilities(
            operations.Contains("jpegload"),
            operations.Contains("pngload"),
            operations.Contains("webpload"),
            operations.Contains("webpsave"),
            operations.Contains("gifload") || operations.Contains("nsgifload"),
            heifLoad,
            heifSave,
            heifLoad && heifSave && CanRoundTrip(Enums.ForeignHeifCompression.Av1),
            heifLoad && heifSave && CanRoundTrip(Enums.ForeignHeifCompression.Hevc));
    }

    internal static byte[] CreateHeifFixture(Enums.ForeignHeifCompression compression)
    {
        using var blank = NetVips.Image.Black(8, 6, bands: 3);
        using var image = blank.Copy(interpretation: Enums.Interpretation.Srgb);

        return image.HeifsaveBuffer(
            q: 80,
            compression: compression,
            effort: 0,
            keep: Enums.ForeignKeep.None);
    }

    private static bool CanRoundTrip(Enums.ForeignHeifCompression compression)
    {
        try
        {
            var bytes = CreateHeifFixture(compression);
            using var decoded = NetVips.Image.NewFromBuffer(
                bytes,
                access: Enums.Access.Sequential,
                failOn: Enums.FailOn.Error);

            var valid = decoded.Width == 8 && decoded.Height == 6;
            decoded.Invalidate();
            return valid;
        }
        catch (VipsException)
        {
            return false;
        }
    }
}
