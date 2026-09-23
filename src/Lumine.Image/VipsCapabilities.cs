namespace Lumine.Image;

public sealed record VipsFormatCapabilities(
    bool JpegLoad,
    bool PngLoad,
    bool WebpLoad,
    bool WebpSave,
    bool GifLoad,
    bool HeifLoad,
    bool HeifSave);

public static class VipsCapabilities
{
    public static VipsFormatCapabilities Probe()
    {
        var operations = global::NetVips.NetVips.GetOperations()
            .ToHashSet(StringComparer.Ordinal);

        return new VipsFormatCapabilities(
            operations.Contains("jpegload"),
            operations.Contains("pngload"),
            operations.Contains("webpload"),
            operations.Contains("webpsave"),
            operations.Contains("gifload") || operations.Contains("nsgifload"),
            operations.Contains("heifload"),
            operations.Contains("heifsave"));
    }
}
