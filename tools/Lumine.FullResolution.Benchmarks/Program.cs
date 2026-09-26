using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
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

static byte[] CreateEntropyPixels(
    int width,
    int height,
    int bands,
    bool alpha)
{
    var bytes = new byte[checked(width * height * bands)];
    uint state = 0x6d2b79f5;

    for (var index = 0; index < bytes.Length; index++)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;

        var value = (byte)(state & 0xff);
        if (alpha && ((index + 1) % bands == 0))
        {
            value = (byte)(64 + (value % 192));
        }

        bytes[index] = value;
    }

    return bytes;
}

static void WriteEntropyRgb(
    string path,
    int width,
    int height,
    Action<NetVips.Image, string>? writer = null)
{
    var pixels = CreateEntropyPixels(
        width,
        height,
        3,
        alpha: false);

    using var memory = NetVips.Image.NewFromMemory<byte>(
        pixels,
        width,
        height,
        3,
        Enums.BandFormat.Uchar);
    using var image = memory.Copy(
        interpretation: Enums.Interpretation.Srgb);

    if (writer is null)
    {
        image.WriteToFile(path);
    }
    else
    {
        writer(image, path);
    }
}

static void WriteEntropyRgba(
    string path,
    int width,
    int height)
{
    var pixels = CreateEntropyPixels(
        width,
        height,
        4,
        alpha: true);

    using var memory = NetVips.Image.NewFromMemory<byte>(
        pixels,
        width,
        height,
        4,
        Enums.BandFormat.Uchar);
    using var image = memory.Copy(
        interpretation: Enums.Interpretation.Srgb);

    image.WriteToFile(path);
}

static void WriteSpecialJpeg(
    string path,
    int width,
    int height,
    bool useIcc,
    int orientation)
{
    var pixels = CreateEntropyPixels(
        width,
        height,
        3,
        alpha: false);

    using var memory = NetVips.Image.NewFromMemory<byte>(
        pixels,
        width,
        height,
        3,
        Enums.BandFormat.Uchar);
    using var srgb = memory.Copy(
        interpretation: Enums.Interpretation.Srgb);

    NetVips.Image? profiled = null;
    NetVips.Image? oriented = null;

    try
    {
        var source = srgb;

        if (useIcc)
        {
            profiled = srgb.IccTransform(
                "p3",
                inputProfile: "srgb");
            source = profiled;
        }

        if (orientation != 1)
        {
            oriented = source.Mutate(
                image => image.Set(
                    GValue.GIntType,
                    "orientation",
                    orientation));
            source = oriented;
        }

        source.Jpegsave(
            path,
            q: 90,
            keep: Enums.ForeignKeep.All);
    }
    finally
    {
        oriented?.Dispose();
        profiled?.Dispose();
    }
}

static void WriteLargeBackingPng(
    string path,
    int width,
    int height)
{
    var pixels = CreateEntropyPixels(
        width,
        height,
        4,
        alpha: true);

    using var memory = NetVips.Image.NewFromMemory<byte>(
        pixels,
        width,
        height,
        4,
        Enums.BandFormat.Uchar);
    using var rgba = memory.Copy(
        interpretation: Enums.Interpretation.Srgb);

    rgba.Pngsave(path);
}

static void WriteIccPng(
    string path,
    int width,
    int height)
{
    var pixels = CreateEntropyPixels(
        width,
        height,
        4,
        alpha: true);

    using var memory = NetVips.Image.NewFromMemory<byte>(
        pixels,
        width,
        height,
        4,
        Enums.BandFormat.Uchar);
    using var srgb = memory.Copy(
        interpretation: Enums.Interpretation.Srgb);
    using var p3 = srgb.IccTransform(
        "p3",
        inputProfile: "srgb");

    p3.Pngsave(
        path,
        keep: Enums.ForeignKeep.Icc);
}

static FullResolutionSource SourceFor(string path)
{
    var file = new FileInfo(path);
    file.Refresh();

    return new FullResolutionSource(
        path,
        file.Length,
        file.LastWriteTimeUtc.Ticks);
}

static async Task<PolicyRun> DecodeOnceAsync(
    BenchmarkRecorder recorder,
    FixtureSpec fixture,
    FullResolutionAccessPolicy policy,
    string tempRoot,
    int iteration)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var source = SourceFor(fixture.Path);
    var prepareTimer = Stopwatch.StartNew();
    using var prepared = await FullResolutionDecoder.PrepareAsync(source);
    prepareTimer.Stop();

    if (prepared.Info.Width != fixture.ExpectedWidth
        || prepared.Info.Height != fixture.ExpectedHeight)
    {
        throw new InvalidOperationException(
            $"{fixture.Name}/{policy} prepared {prepared.Info.Width}x{prepared.Info.Height}; expected {fixture.ExpectedWidth}x{fixture.ExpectedHeight}.");
    }

    var allocatedBefore =
        GC.GetTotalAllocatedBytes(precise: true);
    var measurement = BenchmarkRecorder.CaptureStart();
    await using var monitor = RuntimeMonitor.Start(tempRoot);

    using var digest = IncrementalHash.CreateHash(
        HashAlgorithmName.SHA256);
    var decodedRows = 0;

    await FullResolutionDecoder.DecodePreparedAsync(
        prepared,
        fixture.BudgetBytes,
        stripe =>
        {
            decodedRows += stripe.Height;

            digest.AppendData(stripe.RgbaBytes);
        },
        accessPolicy: policy,
        cancellationToken: CancellationToken.None);

    recorder.Complete(
        $"fullres.{fixture.Name}.{policy.ToString().ToLowerInvariant()}.iteration-{iteration}",
        measurement);

    await monitor.StopAsync();

    if (decodedRows != fixture.ExpectedHeight)
    {
        throw new InvalidOperationException(
            $"{fixture.Name}/{policy} decoded {decodedRows} rows; expected {fixture.ExpectedHeight}.");
    }

    return new PolicyRun(
        prepareTimer.Elapsed.TotalMilliseconds,
        monitor.Elapsed.TotalMilliseconds,
        monitor.PeakWorkingSetBytes,
        monitor.PeakAdditionalWorkingSetBytes,
        monitor.PeakVipsTrackedBytes,
        monitor.PeakVipsOpenFiles,
        monitor.PeakTempBytes,
        monitor.PeakTempFiles,
        Math.Max(
            0,
            GC.GetTotalAllocatedBytes(precise: true)
                - allocatedBefore),
        Convert.ToHexString(
            digest.GetHashAndReset())
            .ToLowerInvariant());
}

static async Task<double> MeasureCancellationAsync(
    FixtureSpec fixture,
    FullResolutionAccessPolicy policy)
{
    var source = SourceFor(fixture.Path);
    using var prepared =
        await FullResolutionDecoder.PrepareAsync(source);
    using var cancellation = new CancellationTokenSource();

    long requestedAt = 0;
    var cancelTask = Task.Run(
        async () =>
        {
            await Task.Delay(20);
            requestedAt = Stopwatch.GetTimestamp();
            cancellation.Cancel();
        });

    try
    {
        await FullResolutionDecoder.DecodePreparedAsync(
            prepared,
            fixture.BudgetBytes,
            _ => { },
            accessPolicy: policy,
            cancellationToken: cancellation.Token);

        await cancelTask;

        throw new InvalidOperationException(
            $"{fixture.Name}/{policy} completed before the cancellation benchmark could interrupt it.");
    }
    catch (OperationCanceledException)
    {
        await cancelTask;

        if (requestedAt == 0)
        {
            throw new InvalidOperationException(
                $"{fixture.Name}/{policy} cancellation completed before the request timestamp was recorded.");
        }

        return Stopwatch.GetElapsedTime(
            requestedAt).TotalMilliseconds;
    }
}

static void AddCaseMetadata(
    IDictionary<string, string> metadata,
    string fixture,
    FullResolutionAccessPolicy policy,
    CaseAggregate aggregate)
{
    var prefix =
        $"case.{fixture}.{policy.ToString().ToLowerInvariant()}";

    metadata[$"{prefix}.success"] =
        aggregate.Success.ToString(CultureInfo.InvariantCulture);
    metadata[$"{prefix}.error"] =
        aggregate.Error ?? string.Empty;
    metadata[$"{prefix}.decode_ms"] =
        aggregate.AverageDecodeMs.ToString(
            "F3",
            CultureInfo.InvariantCulture);
    metadata[$"{prefix}.prepare_ms"] =
        aggregate.AveragePrepareMs.ToString(
            "F3",
            CultureInfo.InvariantCulture);
    metadata[$"{prefix}.peak_working_set_bytes"] =
        aggregate.PeakWorkingSetBytes.ToString(
            CultureInfo.InvariantCulture);
    metadata[$"{prefix}.peak_additional_working_set_bytes"] =
        aggregate.PeakAdditionalWorkingSetBytes.ToString(
            CultureInfo.InvariantCulture);
    metadata[$"{prefix}.vips_peak_tracked_bytes"] =
        aggregate.PeakVipsTrackedBytes.ToString(
            CultureInfo.InvariantCulture);
    metadata[$"{prefix}.vips_peak_open_files"] =
        aggregate.PeakVipsOpenFiles.ToString(
            CultureInfo.InvariantCulture);
    metadata[$"{prefix}.temp_peak_bytes"] =
        aggregate.PeakTempBytes.ToString(
            CultureInfo.InvariantCulture);
    metadata[$"{prefix}.temp_peak_files"] =
        aggregate.PeakTempFiles.ToString(
            CultureInfo.InvariantCulture);
    metadata[$"{prefix}.managed_allocated_bytes"] =
        aggregate.AverageAllocatedBytes.ToString(
            CultureInfo.InvariantCulture);
    metadata[$"{prefix}.output_digest_sha256"] =
        aggregate.OutputDigest;
}

var output = ReadOption(args, "--output")
    ?? Path.Combine(
        "artifacts",
        "benchmarks",
        "full-resolution-access-policy.json");

const int width = 2560;
const int height = 1600;
const int largeWidth = 6500;
const int largeHeight = 4200;
const long standardBudget = 96L * 1024 * 1024;
const long largeBudget = 128L * 1024 * 1024;

var systemTemp = Path.GetTempPath();
var root = Path.Combine(
    systemTemp,
    $"lumine-fullres-policy-{Guid.NewGuid():N}");
var sources = Path.Combine(root, "sources");
var vipsTemp = Path.Combine(root, "vips-temp");

Directory.CreateDirectory(sources);
Directory.CreateDirectory(vipsTemp);

var previousTemp = Environment.GetEnvironmentVariable("TEMP");
var previousTmp = Environment.GetEnvironmentVariable("TMP");
var previousTmpDir = Environment.GetEnvironmentVariable("TMPDIR");

Environment.SetEnvironmentVariable("TEMP", vipsTemp);
Environment.SetEnvironmentVariable("TMP", vipsTemp);
Environment.SetEnvironmentVariable("TMPDIR", vipsTemp);

try
{
    VipsRuntimePolicy.EnsureConfigured();
    var capabilities = VipsCapabilities.Probe();

    var fixtures = new List<FixtureSpec>();

    var jpegPath = Path.Combine(sources, "photo-like.jpg");
    WriteEntropyRgb(
        jpegPath,
        width,
        height,
        static (image, path) =>
            image.Jpegsave(
                path,
                q: 90,
                keep: Enums.ForeignKeep.None));
    fixtures.Add(
        new FixtureSpec(
            "jpeg",
            jpegPath,
            width,
            height,
            standardBudget));

    var iccPath =
        Path.Combine(sources, "icc.jpg");
    WriteSpecialJpeg(
        iccPath,
        width,
        height,
        useIcc: true,
        orientation: 1);
    fixtures.Add(
        new FixtureSpec(
            "jpeg-icc",
            iccPath,
            width,
            height,
            standardBudget));

    var orientedPath =
        Path.Combine(sources, "oriented.jpg");
    WriteSpecialJpeg(
        orientedPath,
        width,
        height,
        useIcc: false,
        orientation: 6);
    fixtures.Add(
        new FixtureSpec(
            "jpeg-oriented",
            orientedPath,
            height,
            width,
            standardBudget));

    var iccOrientedPath =
        Path.Combine(sources, "icc-oriented.jpg");
    WriteSpecialJpeg(
        iccOrientedPath,
        width,
        height,
        useIcc: true,
        orientation: 6);
    fixtures.Add(
        new FixtureSpec(
            "jpeg-icc-oriented",
            iccOrientedPath,
            height,
            width,
            standardBudget));

    var pngPath = Path.Combine(sources, "alpha.png");
    WriteEntropyRgba(
        pngPath,
        width,
        height);
    fixtures.Add(
        new FixtureSpec(
            "png-alpha",
            pngPath,
            width,
            height,
            standardBudget));

    var pngIccPath =
        Path.Combine(sources, "icc.png");
    WriteIccPng(
        pngIccPath,
        width,
        height);
    fixtures.Add(
        new FixtureSpec(
            "png-icc",
            pngIccPath,
            width,
            height,
            standardBudget));

    var webpPath = Path.Combine(sources, "photo.webp");
    WriteEntropyRgba(
        webpPath,
        width,
        height);
    fixtures.Add(
        new FixtureSpec(
            "webp",
            webpPath,
            width,
            height,
            standardBudget));

    var tiffPath = Path.Combine(sources, "photo.tiff");
    WriteEntropyRgba(
        tiffPath,
        width,
        height);
    fixtures.Add(
        new FixtureSpec(
            "tiff",
            tiffPath,
            width,
            height,
            standardBudget));

    if (capabilities.AvifRoundTrip)
    {
        var avifPath =
            Path.Combine(sources, "photo.avif");
        var pixels = CreateEntropyPixels(
            width,
            height,
            3,
            alpha: false);

        using var memory =
            NetVips.Image.NewFromMemory<byte>(
                pixels,
                width,
                height,
                3,
                Enums.BandFormat.Uchar);
        using var image = memory.Copy(
            interpretation: Enums.Interpretation.Srgb);

        await File.WriteAllBytesAsync(
            avifPath,
            image.HeifsaveBuffer(
                q: 80,
                compression:
                    Enums.ForeignHeifCompression.Av1,
                effort: 0,
                keep: Enums.ForeignKeep.None));

        fixtures.Add(
            new FixtureSpec(
                "avif",
                avifPath,
                width,
                height,
                standardBudget));
    }

    if (capabilities.HeicRoundTrip)
    {
        var heicPath =
            Path.Combine(sources, "photo.heic");
        var pixels = CreateEntropyPixels(
            width,
            height,
            3,
            alpha: false);

        using var memory =
            NetVips.Image.NewFromMemory<byte>(
                pixels,
                width,
                height,
                3,
                Enums.BandFormat.Uchar);
        using var image = memory.Copy(
            interpretation: Enums.Interpretation.Srgb);

        await File.WriteAllBytesAsync(
            heicPath,
            image.HeifsaveBuffer(
                q: 80,
                compression:
                    Enums.ForeignHeifCompression.Hevc,
                effort: 0,
                keep: Enums.ForeignKeep.None));

        fixtures.Add(
            new FixtureSpec(
                "heic",
                heicPath,
                width,
                height,
                standardBudget));
    }

    var largePngPath =
        Path.Combine(sources, "large-backing.png");
    WriteLargeBackingPng(
        largePngPath,
        largeWidth,
        largeHeight);
    var largeFixture = new FixtureSpec(
        "png-large-backing",
        largePngPath,
        largeWidth,
        largeHeight,
        largeBudget);
    fixtures.Add(largeFixture);

    var recorder = new BenchmarkRecorder();
    var aggregates =
        new Dictionary<(string, FullResolutionAccessPolicy), CaseAggregate>();

    foreach (var fixture in fixtures)
    {
        var random = new CaseAggregate();
        var sequential = new CaseAggregate();
        aggregates[(fixture.Name, FullResolutionAccessPolicy.Random)] = random;
        aggregates[(fixture.Name, FullResolutionAccessPolicy.Sequential)] = sequential;

        for (var iteration = 0; iteration < 2; iteration++)
        {
            var order = iteration == 0
                ? new[]
                {
                    FullResolutionAccessPolicy.Random,
                    FullResolutionAccessPolicy.Sequential
                }
                : new[]
                {
                    FullResolutionAccessPolicy.Sequential,
                    FullResolutionAccessPolicy.Random
                };

            foreach (var policy in order)
            {
                var aggregate =
                    aggregates[(fixture.Name, policy)];

                if (!aggregate.Success)
                {
                    continue;
                }

                try
                {
                    var run = await DecodeOnceAsync(
                        recorder,
                        fixture,
                        policy,
                        vipsTemp,
                        iteration);

                    aggregate.Add(run);
                }
                catch (Exception exception)
                    when (exception is VipsException
                          or IOException
                          or InvalidOperationException)
                {
                    aggregate.AddFailure(exception);
                }
            }
        }

        if (random.Success
            && sequential.Success
            && random.OutputDigest != sequential.OutputDigest)
        {
            sequential.AddFailure(
                new InvalidOperationException(
                    $"{fixture.Name} full-output digest differs between Random ({random.OutputDigest}) and Sequential ({sequential.OutputDigest})."));
        }
    }

    var randomCancellation =
        await MeasureCancellationAsync(
            largeFixture,
            FullResolutionAccessPolicy.Random);
    double? sequentialCancellation = null;
    string? sequentialCancellationError = null;

    try
    {
        sequentialCancellation =
            await MeasureCancellationAsync(
                largeFixture,
                FullResolutionAccessPolicy.Sequential);
    }
    catch (Exception exception)
        when (exception is VipsException
              or IOException
              or InvalidOperationException)
    {
        sequentialCancellationError =
            $"{exception.GetType().Name}: {exception.Message}";
    }

    var metadata =
        new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["kind"] =
                "full-resolution-access-policy",
            ["fixture_width"] =
                width.ToString(
                    CultureInfo.InvariantCulture),
            ["fixture_height"] =
                height.ToString(
                    CultureInfo.InvariantCulture),
            ["large_fixture_width"] =
                largeWidth.ToString(
                    CultureInfo.InvariantCulture),
            ["large_fixture_height"] =
                largeHeight.ToString(
                    CultureInfo.InvariantCulture),
            ["production_policy"] =
                FullResolutionDecoder
                    .ProductionAccessPolicy
                    .ToString()
                    .ToLowerInvariant(),
            ["png_sequential_threshold_bytes"] =
                FullResolutionDecoder
                    .PngSequentialThresholdBytes
                    .ToString(
                        CultureInfo.InvariantCulture),
            ["capability.avif"] =
                capabilities.AvifRoundTrip
                    .ToString(
                        CultureInfo.InvariantCulture),
            ["capability.heic"] =
                capabilities.HeicRoundTrip
                    .ToString(
                        CultureInfo.InvariantCulture),
            ["cancellation.random_ms"] =
                randomCancellation.ToString(
                    "F3",
                    CultureInfo.InvariantCulture),
            ["cancellation.sequential_ms"] =
                sequentialCancellation?.ToString(
                    "F3",
                    CultureInfo.InvariantCulture)
                ?? string.Empty,
            ["cancellation.sequential_error"] =
                sequentialCancellationError
                ?? string.Empty,
            ["fixture_count"] =
                fixtures.Count.ToString(
                    CultureInfo.InvariantCulture)
        };

    foreach (var fixture in fixtures)
    {
        using (var prepared =
               await FullResolutionDecoder.PrepareAsync(
                   SourceFor(fixture.Path)))
        {
            metadata[$"case.{fixture.Name}.production_policy"] =
                prepared.RecommendedAccessPolicy
                    .ToString()
                    .ToLowerInvariant();
        }

        AddCaseMetadata(
            metadata,
            fixture.Name,
            FullResolutionAccessPolicy.Random,
            aggregates[
                (fixture.Name,
                 FullResolutionAccessPolicy.Random)]);
        AddCaseMetadata(
            metadata,
            fixture.Name,
            FullResolutionAccessPolicy.Sequential,
            aggregates[
                (fixture.Name,
                 FullResolutionAccessPolicy.Sequential)]);
    }

    await recorder.WriteJsonAsync(
        output,
        metadata);

    Console.WriteLine(
        $"Full-resolution access-policy benchmark: production={metadata["production_policy"]}, fixtures={fixtures.Count}");
    foreach (var fixture in fixtures)
    {
        var random =
            aggregates[
                (fixture.Name,
                 FullResolutionAccessPolicy.Random)];
        var sequential =
            aggregates[
                (fixture.Name,
                 FullResolutionAccessPolicy.Sequential)];

        Console.WriteLine(
            $"{fixture.Name}: random={(random.Success ? $"{random.AverageDecodeMs:F1} ms / +{random.PeakAdditionalWorkingSetBytes / 1048576d:F1} MiB / temp={random.PeakTempBytes / 1048576d:F1} MiB" : random.Error)}; sequential={(sequential.Success ? $"{sequential.AverageDecodeMs:F1} ms / +{sequential.PeakAdditionalWorkingSetBytes / 1048576d:F1} MiB / temp={sequential.PeakTempBytes / 1048576d:F1} MiB" : sequential.Error)}");
    }

    Console.WriteLine(
        $"Cancellation: random={randomCancellation:F1} ms, sequential={(sequentialCancellation.HasValue ? $"{sequentialCancellation.Value:F1} ms" : sequentialCancellationError)}");
    Console.WriteLine(
        $"Result: {Path.GetFullPath(output)}");
}
finally
{
    Environment.SetEnvironmentVariable(
        "TEMP",
        previousTemp);
    Environment.SetEnvironmentVariable(
        "TMP",
        previousTmp);
    Environment.SetEnvironmentVariable(
        "TMPDIR",
        previousTmpDir);

    if (Directory.Exists(root))
    {
        try
        {
            Directory.Delete(
                root,
                recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed record FixtureSpec(
    string Name,
    string Path,
    int ExpectedWidth,
    int ExpectedHeight,
    long BudgetBytes);

internal sealed record PolicyRun(
    double PrepareMs,
    double DecodeMs,
    long PeakWorkingSetBytes,
    long PeakAdditionalWorkingSetBytes,
    long PeakVipsTrackedBytes,
    int PeakVipsOpenFiles,
    long PeakTempBytes,
    int PeakTempFiles,
    long AllocatedBytes,
    string OutputDigest);

internal sealed class CaseAggregate
{
    private readonly List<PolicyRun> _runs = [];

    public bool Success =>
        Error is null;

    public string? Error { get; private set; }

    public double AveragePrepareMs =>
        _runs.Count == 0
            ? 0
            : _runs.Average(
                static run => run.PrepareMs);

    public double AverageDecodeMs =>
        _runs.Count == 0
            ? 0
            : _runs.Average(
                static run => run.DecodeMs);

    public long PeakWorkingSetBytes =>
        _runs.Count == 0
            ? 0
            : _runs.Max(
                static run => run.PeakWorkingSetBytes);

    public long PeakAdditionalWorkingSetBytes =>
        _runs.Count == 0
            ? 0
            : _runs.Max(
                static run =>
                    run.PeakAdditionalWorkingSetBytes);

    public long PeakVipsTrackedBytes =>
        _runs.Count == 0
            ? 0
            : _runs.Max(
                static run => run.PeakVipsTrackedBytes);

    public int PeakVipsOpenFiles =>
        _runs.Count == 0
            ? 0
            : _runs.Max(
                static run => run.PeakVipsOpenFiles);

    public long PeakTempBytes =>
        _runs.Count == 0
            ? 0
            : _runs.Max(
                static run => run.PeakTempBytes);

    public int PeakTempFiles =>
        _runs.Count == 0
            ? 0
            : _runs.Max(
                static run => run.PeakTempFiles);

    public long AverageAllocatedBytes =>
        _runs.Count == 0
            ? 0
            : (long)_runs.Average(
                static run =>
                    (double)run.AllocatedBytes);

    public string OutputDigest =>
        _runs.Count == 0
            ? string.Empty
            : _runs[0].OutputDigest;

    public void Add(PolicyRun run)
    {
        if (_runs.Count > 0
            && _runs[0].OutputDigest != run.OutputDigest)
        {
            AddFailure(
                new InvalidOperationException(
                    $"Repeated decode output digest changed from {_runs[0].OutputDigest} to {run.OutputDigest}."));
            return;
        }

        _runs.Add(run);
    }

    public void AddFailure(Exception exception)
    {
        Error ??=
            $"{exception.GetType().Name}: {exception.Message}";
    }
}

internal sealed class RuntimeMonitor
    : IAsyncDisposable
{
    private readonly string _tempRoot;
    private readonly CancellationTokenSource _stop =
        new();
    private readonly Task _samplingTask;
    private readonly long _startedAt;
    private bool _stopped;

    private RuntimeMonitor(string tempRoot)
    {
        _tempRoot = tempRoot;
        StartingWorkingSetBytes =
            Environment.WorkingSet;
        PeakWorkingSetBytes =
            StartingWorkingSetBytes;
        _startedAt = Stopwatch.GetTimestamp();
        _samplingTask = SampleAsync();
    }

    public long StartingWorkingSetBytes { get; }

    public long PeakWorkingSetBytes { get; private set; }

    public long PeakAdditionalWorkingSetBytes =>
        Math.Max(
            0,
            PeakWorkingSetBytes
                - StartingWorkingSetBytes);

    public long PeakVipsTrackedBytes { get; private set; }

    public int PeakVipsOpenFiles { get; private set; }

    public long PeakTempBytes { get; private set; }

    public int PeakTempFiles { get; private set; }

    public TimeSpan Elapsed =>
        Stopwatch.GetElapsedTime(_startedAt);

    public static RuntimeMonitor Start(string tempRoot) =>
        new(tempRoot);

    public async Task StopAsync()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _stop.Cancel();

        try
        {
            await _samplingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (_stop.IsCancellationRequested)
        {
        }

        Sample();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stop.Dispose();
    }

    private async Task SampleAsync()
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromMilliseconds(5));

        while (await timer.WaitForNextTickAsync(
                   _stop.Token).ConfigureAwait(false))
        {
            Sample();
        }
    }

    private void Sample()
    {
        PeakWorkingSetBytes = Math.Max(
            PeakWorkingSetBytes,
            Environment.WorkingSet);
        PeakVipsTrackedBytes = Math.Max(
            PeakVipsTrackedBytes,
            checked((long)NetVips.Stats.Mem));
        PeakVipsOpenFiles = Math.Max(
            PeakVipsOpenFiles,
            checked((int)NetVips.Stats.Files));

        try
        {
            long bytes = 0;
            var files = 0;

            foreach (var path in Directory.EnumerateFiles(
                         _tempRoot,
                         "*",
                         SearchOption.AllDirectories))
            {
                try
                {
                    bytes +=
                        new FileInfo(path).Length;
                    files++;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            PeakTempBytes = Math.Max(
                PeakTempBytes,
                bytes);
            PeakTempFiles = Math.Max(
                PeakTempFiles,
                files);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
