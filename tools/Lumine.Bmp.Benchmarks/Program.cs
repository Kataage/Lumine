using System.Buffers.Binary;
using System.Globalization;
using Lumine.Diagnostics;
using Lumine.Image;

static string? ReadOption(
    string[] args,
    string name)
{
    for (var index = 0;
         index < args.Length - 1;
         index++)
    {
        if (string.Equals(
                args[index],
                name,
                StringComparison.Ordinal))
        {
            return args[index + 1];
        }
    }

    return null;
}

static void WriteHighEntropyBmp24(
    string path,
    int width,
    int height)
{
    var rowStride =
        checked(((width * 3 + 3) / 4) * 4);
    var pixelOffset = 54;
    var fileSize = checked(
        (long)pixelOffset
        + ((long)rowStride * height));

    using var stream = new FileStream(
        path,
        FileMode.Create,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 128 * 1024,
        FileOptions.SequentialScan);

    Span<byte> header = stackalloc byte[pixelOffset];
    header.Clear();
    header[0] = (byte)'B';
    header[1] = (byte)'M';
    BinaryPrimitives.WriteUInt32LittleEndian(
        header[2..6],
        checked((uint)fileSize));
    BinaryPrimitives.WriteUInt32LittleEndian(
        header[10..14],
        pixelOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(
        header[14..18],
        40);
    BinaryPrimitives.WriteInt32LittleEndian(
        header[18..22],
        width);
    BinaryPrimitives.WriteInt32LittleEndian(
        header[22..26],
        height);
    BinaryPrimitives.WriteUInt16LittleEndian(
        header[26..28],
        1);
    BinaryPrimitives.WriteUInt16LittleEndian(
        header[28..30],
        24);
    BinaryPrimitives.WriteUInt32LittleEndian(
        header[34..38],
        checked((uint)(
            (long)rowStride * height)));
    stream.Write(header);

    var row = new byte[rowStride];

    for (var storedY = 0;
         storedY < height;
         storedY++)
    {
        uint state =
            unchecked(
                0x9e3779b9u
                + ((uint)storedY * 0x85ebca6bu));

        for (var index = 0;
             index < width * 3;
             index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            row[index] = (byte)state;
        }

        row.AsSpan(width * 3).Clear();
        stream.Write(row);
    }
}

var output =
    ReadOption(args, "--output")
    ?? Path.Combine(
        "artifacts",
        "benchmarks",
        "bmp-fallback.json");

const int width = 4096;
const int height = 3072;
const long decodeBudget =
    64L * 1024 * 1024;

var root = Path.Combine(
    Path.GetTempPath(),
    $"lumine-bmp-benchmark-{Guid.NewGuid():N}");
var sourcePath = Path.Combine(
    root,
    "high-entropy-24.bmp");
var cacheRoot = Path.Combine(
    root,
    "cache");

Directory.CreateDirectory(root);

var recorder = new BenchmarkRecorder();
long thumbnailPeak = 0;
long thumbnailPeakAdditional = 0;
long thumbnailAllocated = 0;
long fullPeak = 0;
long fullPeakAdditional = 0;
long fullAllocated = 0;
long cacheBytes = 0;
long sourceBytes = 0;
var fullRows = 0;
var nativeBmpLoad = false;
var fallbackBmpLoad = false;

try
{
    WriteHighEntropyBmp24(
        sourcePath,
        width,
        height);

    var file = new FileInfo(sourcePath);
    sourceBytes = file.Length;

    var capabilities =
        VipsCapabilities.Probe();
    nativeBmpLoad =
        capabilities.BmpNativeLoad;
    fallbackBmpLoad =
        capabilities.BmpFallbackLoad;

    await using var pipeline =
        new ThumbnailPipeline(
            new ThumbnailCache(cacheRoot),
            new ThumbnailPipelineOptions
            {
                WorkerCount = 1,
                QueueCapacity = 2
            });

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var thumbnailAllocatedBefore =
        GC.GetTotalAllocatedBytes(
            precise: true);
    var thumbnailMonitor =
        PeakWorkingSetMonitor.Start(
            TimeSpan.FromMilliseconds(5));

    ThumbnailResult thumbnail;
    using (recorder.Measure(
               "image.bmp_thumbnail_generate"))
    {
        thumbnail =
            await pipeline.RequestAsync(
                new ThumbnailSource(
                    9001,
                    1,
                    sourcePath,
                    file.Length,
                    file.LastWriteTimeUtc.Ticks),
                ThumbnailProfiles.GridMedium);
    }

    await thumbnailMonitor.DisposeAsync();
    thumbnailPeak =
        thumbnailMonitor.PeakWorkingSetBytes;
    thumbnailPeakAdditional =
        thumbnailMonitor
            .PeakAdditionalWorkingSetBytes;
    thumbnailAllocated = Math.Max(
        0,
        GC.GetTotalAllocatedBytes(
            precise: true)
        - thumbnailAllocatedBefore);
    cacheBytes =
        new FileInfo(
            thumbnail.CachePath).Length;

    if (thumbnail.Width > 512
        || thumbnail.Height > 512
        || !string.Equals(
            thumbnail.SourceMetadata?.Format,
            "bmp",
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "BMP thumbnail benchmark returned an invalid result.");
    }

    var fullSource =
        new FullResolutionSource(
            sourcePath,
            file.Length,
            file.LastWriteTimeUtc.Ticks);

    var fullInfo =
        await FullResolutionDecoder.ProbeAsync(
            fullSource);

    if (fullInfo.Width != width
        || fullInfo.Height != height
        || !string.Equals(
            fullInfo.Format,
            "bmp",
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "BMP full-resolution probe benchmark returned invalid metadata.");
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var fullAllocatedBefore =
        GC.GetTotalAllocatedBytes(
            precise: true);
    var fullMonitor =
        PeakWorkingSetMonitor.Start(
            TimeSpan.FromMilliseconds(5));

    using (recorder.Measure(
               "image.bmp_full_decode"))
    {
        await FullResolutionDecoder.DecodeAsync(
            fullSource,
            decodeBudget,
            stripe =>
            {
                fullRows += stripe.Height;

                if (stripe.RgbaBytes.Length
                    != stripe.RowBytes
                       * stripe.Height)
                {
                    throw new InvalidOperationException(
                        "BMP full-resolution stripe size mismatch.");
                }
            });
    }

    await fullMonitor.DisposeAsync();
    fullPeak =
        fullMonitor.PeakWorkingSetBytes;
    fullPeakAdditional =
        fullMonitor.PeakAdditionalWorkingSetBytes;
    fullAllocated = Math.Max(
        0,
        GC.GetTotalAllocatedBytes(
            precise: true)
        - fullAllocatedBefore);

    if (fullRows != height)
    {
        throw new InvalidOperationException(
            $"BMP benchmark decoded {fullRows} rows; expected {height}.");
    }

    await recorder.WriteJsonAsync(
        output,
        new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["kind"] =
                "bmp-fallback",
            ["fixture_width"] =
                width.ToString(
                    CultureInfo.InvariantCulture),
            ["fixture_height"] =
                height.ToString(
                    CultureInfo.InvariantCulture),
            ["source_bytes"] =
                sourceBytes.ToString(
                    CultureInfo.InvariantCulture),
            ["cache_bytes"] =
                cacheBytes.ToString(
                    CultureInfo.InvariantCulture),
            ["native_bmp_load"] =
                nativeBmpLoad.ToString(
                    CultureInfo.InvariantCulture),
            ["fallback_bmp_load"] =
                fallbackBmpLoad.ToString(
                    CultureInfo.InvariantCulture),
            ["thumbnail_peak_working_set_bytes"] =
                thumbnailPeak.ToString(
                    CultureInfo.InvariantCulture),
            ["thumbnail_peak_additional_working_set_bytes"] =
                thumbnailPeakAdditional.ToString(
                    CultureInfo.InvariantCulture),
            ["thumbnail_allocated_bytes"] =
                thumbnailAllocated.ToString(
                    CultureInfo.InvariantCulture),
            ["full_peak_working_set_bytes"] =
                fullPeak.ToString(
                    CultureInfo.InvariantCulture),
            ["full_peak_additional_working_set_bytes"] =
                fullPeakAdditional.ToString(
                    CultureInfo.InvariantCulture),
            ["full_allocated_bytes"] =
                fullAllocated.ToString(
                    CultureInfo.InvariantCulture),
            ["full_rows"] =
                fullRows.ToString(
                    CultureInfo.InvariantCulture),
            ["new_native_dependencies"] =
                "0"
        });

    Console.WriteLine(
        $"BMP fallback benchmark: source={sourceBytes / 1048576d:F1} MiB, native={nativeBmpLoad}, fallback={fallbackBmpLoad}");
    Console.WriteLine(
        $"BMP thumbnail: {thumbnail.Width}x{thumbnail.Height}, +{thumbnailPeakAdditional / 1048576d:F1} MiB peak, allocated={thumbnailAllocated / 1048576d:F1} MiB");
    Console.WriteLine(
        $"BMP full-resolution: rows={fullRows}, +{fullPeakAdditional / 1048576d:F1} MiB peak, allocated={fullAllocated / 1048576d:F1} MiB");
    Console.WriteLine(
        $"Result: {Path.GetFullPath(output)}");
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
