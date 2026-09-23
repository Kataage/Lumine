using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Lumine.Diagnostics;

public readonly record struct MeasurementStart(
    long Timestamp,
    DateTimeOffset StartedAtUtc,
    ResourceSnapshot Resources);

public sealed class BenchmarkRecorder
{
    private readonly ConcurrentQueue<BenchmarkMeasurement> _measurements = new();

    public BenchmarkRecorder()
        : this(CaptureEnvironment())
    {
    }

    public BenchmarkRecorder(BenchmarkEnvironment environment)
    {
        Environment = environment;
    }

    public BenchmarkEnvironment Environment { get; }

    public MeasurementStart CaptureStart() =>
        new(Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow, CaptureResources());

    public BenchmarkOperation Measure(string name) =>
        new(this, name, CaptureStart());

    public void Complete(string name, MeasurementStart start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _measurements.Enqueue(new BenchmarkMeasurement(
            name,
            start.StartedAtUtc,
            Stopwatch.GetElapsedTime(start.Timestamp).TotalMilliseconds,
            start.Resources,
            CaptureResources()));
    }

    public BenchmarkResult Snapshot(IReadOnlyDictionary<string, string>? metadata = null)
    {
        var copy = metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : metadata.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

        return new BenchmarkResult
        {
            Environment = Environment,
            Measurements = _measurements.ToList(),
            Metadata = copy
        };
    }

    public async Task WriteJsonAsync(
        string outputPath,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var stream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        await JsonSerializer.SerializeAsync(
            stream,
            Snapshot(metadata),
            DiagnosticsJsonContext.Default.BenchmarkResult,
            cancellationToken).ConfigureAwait(false);
    }

    internal static ResourceSnapshot CaptureResources()
    {
        using var process = Process.GetCurrentProcess();

        return new ResourceSnapshot(
            process.WorkingSet64,
            GC.GetTotalMemory(forceFullCollection: false),
            GC.GetTotalAllocatedBytes(precise: false),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            process.TotalProcessorTime.TotalMilliseconds);
    }

    private static BenchmarkEnvironment CaptureEnvironment()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        var hardwareId = Environment.GetEnvironmentVariable("LUMINE_HARDWARE_ID");
        if (string.IsNullOrWhiteSpace(hardwareId))
        {
            hardwareId = Environment.MachineName;
        }

        var revision = Environment.GetEnvironmentVariable("LUMINE_REVISION");
        if (string.IsNullOrWhiteSpace(revision))
        {
            revision = "unknown";
        }

        var version = Environment.GetEnvironmentVariable("LUMINE_APP_VERSION");
        if (string.IsNullOrWhiteSpace(version))
        {
            version = "dev";
        }

        return new BenchmarkEnvironment(
            hardwareId,
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.OSArchitecture.ToString(),
            Environment.ProcessorCount,
            gcInfo.TotalAvailableMemoryBytes,
            revision,
            version,
            DateTimeOffset.UtcNow);
    }
}

public sealed class BenchmarkOperation : IDisposable
{
    private BenchmarkRecorder? _recorder;
    private readonly string _name;
    private readonly MeasurementStart _start;

    internal BenchmarkOperation(BenchmarkRecorder recorder, string name, MeasurementStart start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _recorder = recorder;
        _name = name;
        _start = start;
    }

    public void Dispose()
    {
        var recorder = Interlocked.Exchange(ref _recorder, null);
        recorder?.Complete(_name, _start);
    }
}
