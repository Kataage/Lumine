using System.Buffers.Binary;
using NetVips;

namespace Lumine.Image;

public sealed record VipsFormatCapabilities(
    bool JpegLoad,
    bool PngLoad,
    bool WebpLoad,
    bool WebpSave,
    bool GifLoad,
    bool BmpNativeLoad,
    bool BmpFallbackLoad,
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
            operations.Contains("gifload")
                || operations.Contains("nsgifload"),
            CanLoadBmpNatively(),
            BmpFallbackLoad: true,
            heifLoad,
            heifSave,
            heifLoad
                && heifSave
                && CanRoundTrip(
                    Enums.ForeignHeifCompression.Av1),
            heifLoad
                && heifSave
                && CanRoundTrip(
                    Enums.ForeignHeifCompression.Hevc));
    }

    internal static byte[] CreateHeifFixture(
        Enums.ForeignHeifCompression compression)
    {
        using var blank = NetVips.Image.Black(
            8,
            6,
            bands: 3);
        using var image = blank.Copy(
            interpretation:
                Enums.Interpretation.Srgb);

        return image.HeifsaveBuffer(
            q: 80,
            compression: compression,
            effort: 0,
            keep: Enums.ForeignKeep.None);
    }

    private static bool CanRoundTrip(
        Enums.ForeignHeifCompression compression)
    {
        try
        {
            var bytes = CreateHeifFixture(
                compression);
            using var decoded =
                NetVips.Image.NewFromBuffer(
                    bytes,
                    access: Enums.Access.Sequential,
                    failOn: Enums.FailOn.Error);

            var valid =
                decoded.Width == 8
                && decoded.Height == 6;
            decoded.Invalidate();
            return valid;
        }
        catch (VipsException)
        {
            return false;
        }
    }

    private static bool CanLoadBmpNatively()
    {
        try
        {
            var bytes = CreateBmpFixture();
            using var decoded =
                NetVips.Image.NewFromBuffer(
                    bytes,
                    access: Enums.Access.Sequential,
                    failOn: Enums.FailOn.Error);

            var valid =
                decoded.Width == 2
                && decoded.Height == 2;
            decoded.Invalidate();
            return valid;
        }
        catch (VipsException)
        {
            return false;
        }
    }

    private static byte[] CreateBmpFixture()
    {
        const int width = 2;
        const int height = 2;
        const int rowStride = 8;
        const int pixelOffset = 54;
        const int fileSize =
            pixelOffset
            + (rowStride * height);

        var bytes = new byte[fileSize];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';

        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(2, 4),
            fileSize);
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
            24);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(34, 4),
            rowStride * height);

        bytes[pixelOffset] = 255;
        bytes[pixelOffset + 3] = 255;
        bytes[pixelOffset + rowStride + 1] =
            255;
        bytes[pixelOffset + rowStride + 5] =
            255;

        return bytes;
    }
}
