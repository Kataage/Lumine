namespace Lumine.Diagnostics;

public sealed class DiagnosticsSession
{
    private readonly MeasurementStart _startupStart;
    private int _windowReady;

    private DiagnosticsSession(BenchmarkRecorder recorder)
    {
        Recorder = recorder;
        _startupStart = recorder.CaptureStart();
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

    public Task FlushRequestedAsync(CancellationToken cancellationToken = default)
    {
        var outputPath = Environment.GetEnvironmentVariable("LUMINE_DIAGNOSTICS_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return Task.CompletedTask;
        }

        return Recorder.WriteJsonAsync(
            outputPath,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kind"] = "app-session",
                ["window_ready"] = Volatile.Read(ref _windowReady) == 0 ? "false" : "true"
            },
            cancellationToken);
    }
}
