using System.Buffers.Binary;
using System.Numerics;

namespace Lumine.Image;

internal sealed record BmpSourceInfo(
    int Width,
    int Height,
    bool TopDown,
    int BitsPerPixel,
    uint Compression,
    long PixelOffset,
    int RowStride,
    bool HasAlpha,
    uint RedMask,
    uint GreenMask,
    uint BlueMask,
    uint AlphaMask)
{
    public long EstimatedRgbaBytes =>
        checked((long)Width * Height * 4L);
}

internal static class BmpFallbackDecoder
{
    private const uint BiRgb = 0;
    private const uint BiBitfields = 3;
    private const uint BiAlphaBitfields = 6;
    private const int BitmapFileHeaderSize = 14;
    private const uint MinimumWindowsDibHeaderSize = 40;
    private const uint MaximumAcceptedDibHeaderSize = 124;
    private const int MaximumSourceRowBytes =
        64 * 1024 * 1024;

    public static bool LooksLikeBmp(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek || !stream.CanRead || stream.Length < 2)
        {
            return false;
        }

        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            Span<byte> signature = stackalloc byte[2];
            ReadExactly(stream, signature);
            return signature[0] == (byte)'B'
                && signature[1] == (byte)'M';
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    public static BmpSourceInfo Probe(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new InvalidDataException(
                "BMP fallback requires a readable, seekable source stream.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (stream.Length < BitmapFileHeaderSize + MinimumWindowsDibHeaderSize)
        {
            throw new InvalidDataException("BMP file is too small.");
        }

        stream.Position = 0;
        Span<byte> fileHeader = stackalloc byte[BitmapFileHeaderSize];
        ReadExactly(stream, fileHeader);

        if (fileHeader[0] != (byte)'B'
            || fileHeader[1] != (byte)'M')
        {
            throw new InvalidDataException(
                "BMP signature is not 'BM'.");
        }

        var declaredFileSize =
            BinaryPrimitives.ReadUInt32LittleEndian(
                fileHeader[2..6]);
        var pixelOffset =
            BinaryPrimitives.ReadUInt32LittleEndian(
                fileHeader[10..14]);

        Span<byte> dibSizeBytes = stackalloc byte[4];
        ReadExactly(stream, dibSizeBytes);
        var dibHeaderSize =
            BinaryPrimitives.ReadUInt32LittleEndian(
                dibSizeBytes);

        if (dibHeaderSize < MinimumWindowsDibHeaderSize
            || dibHeaderSize > MaximumAcceptedDibHeaderSize)
        {
            throw new BmpUnsupportedException(
                $"Unsupported BMP DIB header size {dibHeaderSize}. Only Windows BITMAPINFOHEADER/V2/V3/V4/V5 headers are supported.");
        }

        var dib = new byte[checked((int)dibHeaderSize)];
        dibSizeBytes.CopyTo(dib);
        ReadExactly(
            stream,
            dib.AsSpan(4));

        var width =
            BinaryPrimitives.ReadInt32LittleEndian(
                dib.AsSpan(4, 4));
        var storedHeight =
            BinaryPrimitives.ReadInt32LittleEndian(
                dib.AsSpan(8, 4));
        var planes =
            BinaryPrimitives.ReadUInt16LittleEndian(
                dib.AsSpan(12, 2));
        var bitsPerPixel =
            BinaryPrimitives.ReadUInt16LittleEndian(
                dib.AsSpan(14, 2));
        var compression =
            BinaryPrimitives.ReadUInt32LittleEndian(
                dib.AsSpan(16, 4));

        if (width <= 0
            || storedHeight == 0
            || storedHeight == int.MinValue)
        {
            throw new InvalidDataException(
                $"Invalid BMP dimensions {width}x{storedHeight}.");
        }

        if (planes != 1)
        {
            throw new InvalidDataException(
                $"BMP planes must equal 1; found {planes}.");
        }

        if (bitsPerPixel is not (24 or 32))
        {
            throw new BmpUnsupportedException(
                $"Unsupported BMP bit depth {bitsPerPixel}; Lumine supports 24-bit and 32-bit BMP.");
        }

        if (compression != BiRgb
            && compression != BiBitfields
            && compression != BiAlphaBitfields)
        {
            throw new BmpUnsupportedException(
                $"Unsupported BMP compression {compression}; Lumine supports BI_RGB, BI_BITFIELDS and BI_ALPHABITFIELDS.");
        }

        if (bitsPerPixel == 24
            && compression != BiRgb)
        {
            throw new BmpUnsupportedException(
                "24-bit BMP is supported only with BI_RGB.");
        }

        var topDown = storedHeight < 0;
        var height = Math.Abs(storedHeight);

        var rowBits = checked((long)width * bitsPerPixel);
        var rowStrideLong = checked(
            ((rowBits + 31L) / 32L) * 4L);

        if (rowStrideLong > int.MaxValue)
        {
            throw new InvalidDataException(
                "BMP row stride exceeds supported bounds.");
        }

        var rowStride = (int)rowStrideLong;

        if (rowStride > MaximumSourceRowBytes)
        {
            throw new BmpUnsupportedException(
                $"BMP source row requires {rowStride:N0} bytes, above the {MaximumSourceRowBytes:N0}-byte safety bound.");
        }

        var pixelBytes = checked(
            rowStrideLong * height);
        var requiredPixelEnd = checked(
            (long)pixelOffset + pixelBytes);

        if (pixelOffset < BitmapFileHeaderSize + dibHeaderSize
            || pixelOffset > stream.Length
            || pixelBytes > stream.Length - pixelOffset)
        {
            throw new InvalidDataException(
                "BMP pixel-data range is outside the source file.");
        }

        if (declaredFileSize != 0
            && (declaredFileSize > stream.Length
                || declaredFileSize < requiredPixelEnd))
        {
            throw new InvalidDataException(
                "BMP declared file size does not contain the complete pixel range.");
        }

        uint redMask = 0;
        uint greenMask = 0;
        uint blueMask = 0;
        uint alphaMask = 0;

        if (bitsPerPixel == 32)
        {
            if (compression == BiRgb)
            {
                redMask = 0x00ff0000;
                greenMask = 0x0000ff00;
                blueMask = 0x000000ff;
            }
            else
            {
                ReadMasks(
                    stream,
                    dib,
                    dibHeaderSize,
                    compression,
                    pixelOffset,
                    out redMask,
                    out greenMask,
                    out blueMask,
                    out alphaMask);

                ValidateMask(redMask, nameof(redMask));
                ValidateMask(greenMask, nameof(greenMask));
                ValidateMask(blueMask, nameof(blueMask));

                if ((redMask & greenMask) != 0
                    || (redMask & blueMask) != 0
                    || (greenMask & blueMask) != 0
                    || (alphaMask != 0
                        && ((alphaMask & redMask) != 0
                            || (alphaMask & greenMask) != 0
                            || (alphaMask & blueMask) != 0)))
                {
                    throw new InvalidDataException(
                        "BMP BITFIELDS channel masks overlap.");
                }

                if (compression == BiAlphaBitfields
                    && alphaMask == 0)
                {
                    throw new InvalidDataException(
                        "BMP BI_ALPHABITFIELDS requires an explicit alpha mask.");
                }

                if (alphaMask != 0)
                {
                    ValidateMask(
                        alphaMask,
                        nameof(alphaMask));
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new BmpSourceInfo(
            width,
            height,
            topDown,
            bitsPerPixel,
            compression,
            pixelOffset,
            rowStride,
            alphaMask != 0,
            redMask,
            greenMask,
            blueMask,
            alphaMask);
    }

    public static void DecodeStripes(
        Stream stream,
        BmpSourceInfo info,
        int stripeHeight,
        Action<FullResolutionStripe> consume,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(consume);

        var rowBuffer = new byte[info.RowStride];
        var rowBytes = checked(info.Width * 4);

        for (var stripeY = 0;
             stripeY < info.Height;
             stripeY += stripeHeight)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rows = Math.Min(
                stripeHeight,
                info.Height - stripeY);
            var output = new byte[
                checked(rowBytes * rows)];

            for (var localY = 0; localY < rows; localY++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var y = stripeY + localY;
                ReadSourceRow(
                    stream,
                    info,
                    y,
                    rowBuffer);

                DecodeRow(
                    rowBuffer,
                    output.AsSpan(
                        localY * rowBytes,
                        rowBytes),
                    info);
            }

            consume(
                new FullResolutionStripe(
                    stripeY,
                    info.Width,
                    rows,
                    rowBytes,
                    output));
        }
    }

    public static (byte[] Rgba, int Width, int Height)
        DecodeThumbnail(
            Stream stream,
            BmpSourceInfo info,
            int maxWidth,
            int maxHeight,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(info);

        if (maxWidth <= 0 || maxHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxWidth));
        }

        var scale = Math.Min(
            1d,
            Math.Min(
                maxWidth / (double)info.Width,
                maxHeight / (double)info.Height));

        var outputWidth = Math.Max(
            1,
            (int)Math.Round(
                info.Width * scale,
                MidpointRounding.AwayFromZero));
        var outputHeight = Math.Max(
            1,
            (int)Math.Round(
                info.Height * scale,
                MidpointRounding.AwayFromZero));

        var output = new byte[
            checked(outputWidth * outputHeight * 4)];

        var row0 = new byte[info.RowStride];
        var row1 = new byte[info.RowStride];

        var x0 = new int[outputWidth];
        var x1 = new int[outputWidth];
        var wx = new double[outputWidth];

        for (var x = 0; x < outputWidth; x++)
        {
            var sourceX =
                ((x + 0.5d) * info.Width / outputWidth)
                - 0.5d;
            var floor = (int)Math.Floor(sourceX);
            x0[x] = Math.Clamp(
                floor,
                0,
                info.Width - 1);
            x1[x] = Math.Min(
                x0[x] + 1,
                info.Width - 1);
            wx[x] = Math.Clamp(
                sourceX - floor,
                0d,
                1d);
        }

        var cachedY0 = -1;
        var cachedY1 = -1;

        for (var y = 0; y < outputHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceY =
                ((y + 0.5d) * info.Height / outputHeight)
                - 0.5d;
            var floor = (int)Math.Floor(sourceY);
            var y0 = Math.Clamp(
                floor,
                0,
                info.Height - 1);
            var y1 = Math.Min(
                y0 + 1,
                info.Height - 1);
            var wy = Math.Clamp(
                sourceY - floor,
                0d,
                1d);

            if (cachedY0 != y0)
            {
                ReadSourceRow(
                    stream,
                    info,
                    y0,
                    row0);
                cachedY0 = y0;
            }

            if (cachedY1 != y1)
            {
                ReadSourceRow(
                    stream,
                    info,
                    y1,
                    row1);
                cachedY1 = y1;
            }

            var outputRowStart =
                checked(y * outputWidth * 4);

            for (var x = 0; x < outputWidth; x++)
            {
                var p00 = ReadPixel(
                    row0,
                    x0[x],
                    info);
                var p10 = ReadPixel(
                    row0,
                    x1[x],
                    info);
                var p01 = ReadPixel(
                    row1,
                    x0[x],
                    info);
                var p11 = ReadPixel(
                    row1,
                    x1[x],
                    info);

                var destination =
                    outputRowStart + (x * 4);

                output[destination] =
                    Interpolate(
                        p00.R,
                        p10.R,
                        p01.R,
                        p11.R,
                        wx[x],
                        wy);
                output[destination + 1] =
                    Interpolate(
                        p00.G,
                        p10.G,
                        p01.G,
                        p11.G,
                        wx[x],
                        wy);
                output[destination + 2] =
                    Interpolate(
                        p00.B,
                        p10.B,
                        p01.B,
                        p11.B,
                        wx[x],
                        wy);
                output[destination + 3] =
                    Interpolate(
                        p00.A,
                        p10.A,
                        p01.A,
                        p11.A,
                        wx[x],
                        wy);
            }
        }

        return (
            output,
            outputWidth,
            outputHeight);
    }

    private static void ReadMasks(
        Stream stream,
        byte[] dib,
        uint dibHeaderSize,
        uint compression,
        uint pixelOffset,
        out uint redMask,
        out uint greenMask,
        out uint blueMask,
        out uint alphaMask)
    {
        if (dibHeaderSize >= 52)
        {
            redMask =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    dib.AsSpan(40, 4));
            greenMask =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    dib.AsSpan(44, 4));
            blueMask =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    dib.AsSpan(48, 4));
            alphaMask = dibHeaderSize >= 56
                ? BinaryPrimitives.ReadUInt32LittleEndian(
                    dib.AsSpan(52, 4))
                : 0;
            return;
        }

        var maskCount =
            compression == BiAlphaBitfields
                ? 4
                : 3;
        var maskBytes = checked(maskCount * 4);

        if (BitmapFileHeaderSize
            + dibHeaderSize
            + maskBytes > pixelOffset)
        {
            throw new InvalidDataException(
                "BMP BITFIELDS masks overlap pixel data.");
        }

        var masks = new byte[maskBytes];
        stream.Position =
            BitmapFileHeaderSize + dibHeaderSize;
        ReadExactly(stream, masks);

        redMask =
            BinaryPrimitives.ReadUInt32LittleEndian(
                masks.AsSpan(0, 4));
        greenMask =
            BinaryPrimitives.ReadUInt32LittleEndian(
                masks.AsSpan(4, 4));
        blueMask =
            BinaryPrimitives.ReadUInt32LittleEndian(
                masks.AsSpan(8, 4));
        alphaMask = maskCount == 4
            ? BinaryPrimitives.ReadUInt32LittleEndian(
                masks.AsSpan(12, 4))
            : 0;
    }

    private static void ValidateMask(
        uint mask,
        string name)
    {
        if (mask == 0)
        {
            throw new InvalidDataException(
                $"BMP {name} cannot be zero.");
        }

        var shifted =
            mask >> BitOperations.TrailingZeroCount(mask);

        if ((shifted & (shifted + 1)) != 0)
        {
            throw new InvalidDataException(
                $"BMP {name} must be contiguous.");
        }
    }

    private static void ReadSourceRow(
        Stream stream,
        BmpSourceInfo info,
        int topDownY,
        byte[] destination)
    {
        var storedY = info.TopDown
            ? topDownY
            : info.Height - 1 - topDownY;
        var offset = checked(
            info.PixelOffset
            + ((long)storedY * info.RowStride));

        stream.Position = offset;
        ReadExactly(stream, destination);
    }

    private static void DecodeRow(
        byte[] source,
        Span<byte> destination,
        BmpSourceInfo info)
    {
        for (var x = 0; x < info.Width; x++)
        {
            var pixel = ReadPixel(
                source,
                x,
                info);
            var offset = x * 4;
            destination[offset] = pixel.R;
            destination[offset + 1] = pixel.G;
            destination[offset + 2] = pixel.B;
            destination[offset + 3] = pixel.A;
        }
    }

    private static RgbaPixel ReadPixel(
        byte[] row,
        int x,
        BmpSourceInfo info)
    {
        if (info.BitsPerPixel == 24)
        {
            var offset = checked(x * 3);
            return new RgbaPixel(
                row[offset + 2],
                row[offset + 1],
                row[offset],
                255);
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(
            row.AsSpan(
                checked(x * 4),
                4));

        if (info.Compression == BiRgb)
        {
            return new RgbaPixel(
                (byte)((value >> 16) & 0xff),
                (byte)((value >> 8) & 0xff),
                (byte)(value & 0xff),
                255);
        }

        return new RgbaPixel(
            ScaleMaskedChannel(
                value,
                info.RedMask),
            ScaleMaskedChannel(
                value,
                info.GreenMask),
            ScaleMaskedChannel(
                value,
                info.BlueMask),
            info.AlphaMask == 0
                ? (byte)255
                : ScaleMaskedChannel(
                    value,
                    info.AlphaMask));
    }

    private static byte ScaleMaskedChannel(
        uint value,
        uint mask)
    {
        var shift =
            BitOperations.TrailingZeroCount(mask);
        var bits =
            BitOperations.PopCount(mask);
        var component =
            (value & mask) >> shift;
        var max =
            bits == 32
                ? uint.MaxValue
                : (1u << bits) - 1u;

        return (byte)(
            ((ulong)component * 255UL
             + (max / 2UL))
            / max);
    }

    private static byte Interpolate(
        byte p00,
        byte p10,
        byte p01,
        byte p11,
        double wx,
        double wy)
    {
        var top =
            p00 + ((p10 - p00) * wx);
        var bottom =
            p01 + ((p11 - p01) * wx);
        var value =
            top + ((bottom - top) * wy);

        return (byte)Math.Clamp(
            (int)Math.Round(value),
            0,
            255);
    }

    private static void ReadExactly(
        Stream stream,
        Span<byte> destination)
    {
        var offset = 0;

        while (offset < destination.Length)
        {
            var read = stream.Read(
                destination[offset..]);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "Unexpected end of BMP data.");
            }

            offset += read;
        }
    }

    private readonly record struct RgbaPixel(
        byte R,
        byte G,
        byte B,
        byte A);
}

internal sealed class BmpUnsupportedException
    : InvalidDataException
{
    public BmpUnsupportedException(string message)
        : base(message)
    {
    }
}
