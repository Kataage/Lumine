using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Lumine.Diagnostics;
using Lumine.Image;
using NetVips;

static string? ReadOption(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.Ordinal))
        {
            return args[index + 1];
        }
    }

    return null;
}

var output = ReadOption(args, "--output")
    ?? Path.Combine("artifacts", "benchmarks", "detail-full-resolution.json");

const int width = 6000;
const int height = 4000;
const long budgetBytes = 128L * 1024 * 1024;
var requiredBytes = checked((long)width * height * 4L);

var root = Path.Combine(
    Path.GetTempPath(),
    $"lumine-detail-benchmark-{Guid.NewGuid():N}");
var sourcePath = Path.Combine(root, "detail-source.jpg");
Directory.CreateDirectory(root);

try
{
    VipsRuntimePolicy.EnsureConfigured();

    using (var black = NetVips.Image.Black(width, height, bands: 3))
    using (var gradientX = black.NewFromImage([40, 110, 220]))
    using (var source = gradientX.Copy(interpretation: Enums.Interpretation.Srgb))
    {
        source.Jpegsave(
            sourcePath,
            q: 90,
            keep: Enums.ForeignKeep.None);
    }

    var file = new FileInfo(sourcePath);
    var sourceInfo = new FullResolutionSource(
        sourcePath,
        file.Length,
        file.LastWriteTimeUtc.Ticks);

    await using var headless = HeadlessUnitTestSession.StartNew(
        typeof(DetailBenchmarkApplication));

    long peakWorkingSet = 0;
    long peakAdditional = 0;
    long startWorkingSet = 0;
    long finalWorkingSet = 0;
    long elapsedMs = 0;
    long allocatedBytes = 0;
    int stripeCount = 0;
    int decodedRows = 0;

    await headless.Dispatch(
        async () =>
        {
            var probe = await FullResolutionDecoder.ProbeAsync(sourceInfo);
            if (probe.Width != width
                || probe.Height != height
                || probe.EstimatedRgbaBytes != requiredBytes)
            {
                throw new InvalidOperationException(
                    $"Full-resolution probe mismatch: {probe.Width}x{probe.Height}, {probe.EstimatedRgbaBytes:N0} bytes.");
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            startWorkingSet = Environment.WorkingSet;
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var timer = Stopwatch.StartNew();
            var peak = PeakWorkingSetMonitor.Start(TimeSpan.FromMilliseconds(5));

            using var bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormats.Rgba8888,
                AlphaFormat.Unpremul);

            await FullResolutionDecoder.DecodeAsync(
                sourceInfo,
                budgetBytes,
                stripe =>
                {
                    if (stripe.Width != width
                        || stripe.RowBytes != width * 4)
                    {
                        throw new InvalidOperationException(
                            "Full-resolution benchmark stripe contract mismatch.");
                    }

                    using var framebuffer = bitmap.Lock();

                    for (var row = 0; row < stripe.Height; row++)
                    {
                        var destination = IntPtr.Add(
                            framebuffer.Address,
                            checked((stripe.Y + row) * framebuffer.RowBytes));

                        System.Runtime.InteropServices.Marshal.Copy(
                            stripe.RgbaBytes,
                            checked(row * stripe.RowBytes),
                            destination,
                            stripe.RowBytes);
                    }

                    stripeCount++;
                    decodedRows += stripe.Height;
                },
                expectedInfo: probe,
                cancellationToken: CancellationToken.None);

            timer.Stop();
            await peak.DisposeAsync();

            elapsedMs = timer.ElapsedMilliseconds;
            peakWorkingSet = peak.PeakWorkingSetBytes;
            peakAdditional = peak.PeakAdditionalWorkingSetBytes;
            allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            finalWorkingSet = Environment.WorkingSet;

            if (decodedRows != height)
            {
                throw new InvalidOperationException(
                    $"Full-resolution benchmark decoded {decodedRows} rows; expected {height}.");
            }

            if (stripeCount < 2)
            {
                throw new InvalidOperationException(
                    "Full-resolution benchmark did not exercise striped decode.");
            }

            return 0;
        },
        CancellationToken.None);

    var recorder = new BenchmarkRecorder();
    await recorder.WriteJsonAsync(
        output,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kind"] = "detail-full-resolution",
            ["width"] = width.ToString(CultureInfo.InvariantCulture),
            ["height"] = height.ToString(CultureInfo.InvariantCulture),
            ["required_rgba_bytes"] = requiredBytes.ToString(CultureInfo.InvariantCulture),
            ["configured_budget_bytes"] = budgetBytes.ToString(CultureInfo.InvariantCulture),
            ["stripe_height"] = FullResolutionDecoder.DefaultStripeHeight.ToString(CultureInfo.InvariantCulture),
            ["stripe_count"] = stripeCount.ToString(CultureInfo.InvariantCulture),
            ["decode_elapsed_ms"] = elapsedMs.ToString(CultureInfo.InvariantCulture),
            ["starting_working_set_bytes"] = startWorkingSet.ToString(CultureInfo.InvariantCulture),
            ["peak_working_set_bytes"] = peakWorkingSet.ToString(CultureInfo.InvariantCulture),
            ["peak_additional_working_set_bytes"] = peakAdditional.ToString(CultureInfo.InvariantCulture),
            ["final_working_set_bytes"] = finalWorkingSet.ToString(CultureInfo.InvariantCulture),
            ["allocated_bytes"] = allocatedBytes.ToString(CultureInfo.InvariantCulture),
            ["vips_tracked_mem_highwater_bytes"] = NetVips.Stats.MemHighwater.ToString(CultureInfo.InvariantCulture),
            ["vips_open_files"] = NetVips.Stats.Files.ToString(CultureInfo.InvariantCulture),
            ["vips_operation_cache_size"] = NetVips.Cache.Size.ToString(CultureInfo.InvariantCulture)
        });

    Console.WriteLine(
        $"Detail full-resolution benchmark: {width}x{height}, decode={elapsedMs} ms, peak+={peakAdditional / 1024d / 1024d:F1} MiB");
    Console.WriteLine($"Result: {Path.GetFullPath(output)}");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

internal sealed class DetailBenchmarkApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<DetailBenchmarkApplication>()
            .UseSkia()
            .UseHeadless(
                new AvaloniaHeadlessPlatformOptions
                {
                    UseHeadlessDrawing = false,
                    OverlayPopups = false
                });
}
