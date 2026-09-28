namespace Lumine.Diagnostics;

public sealed class DiagnosticsSession
{
    private readonly MeasurementStart _startupStart;
    private int _windowReady;

    private DiagnosticsSession(BenchmarkRecorder recorder)
    {
        Recorder = recorder;
        _startupStart = BenchmarkRecorder.CaptureStart();
    }

    public BenchmarkRecorder Recorder { get; }

    public static DiagnosticsSession Start() =>
        new(new BenchmarkRecorder());

    public void MarkWindowReady()
    {
        if (Interlocked.Exchange(ref _windowReady, 1) != 0)
        {
            return;
        }

        Recorder.Complete(CoreMetricNames.StartupWindowReady, _startupStart);
    }

    public Task FlushRequestedAsync(
        CancellationToken cancellationToken = default) =>
        FlushRequestedAsync(
            new Dictionary<string, string>(StringComparer.Ordinal),
            cancellationToken);

    public Task FlushRequestedAsync(
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var outputPath = Environment.GetEnvironmentVariable(
            "LUMINE_DIAGNOSTICS_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return Task.CompletedTask;
        }

        var effectiveMetadata = metadata.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.Ordinal);
        effectiveMetadata["kind"] = "app-session";
        effectiveMetadata["window_ready"] =
            Volatile.Read(ref _windowReady) == 0
                ? "false"
                : "true";

        return Recorder.WriteJsonAsync(
            outputPath,
            effectiveMetadata,
            cancellationToken);
    }
}
