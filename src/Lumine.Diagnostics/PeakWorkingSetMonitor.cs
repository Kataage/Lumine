namespace Lumine.Diagnostics;

public sealed class PeakWorkingSetMonitor : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _samplingTask;
    private long _peakWorkingSetBytes;

    private PeakWorkingSetMonitor(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        StartingWorkingSetBytes = Environment.WorkingSet;
        _peakWorkingSetBytes = StartingWorkingSetBytes;
        _samplingTask = SampleAsync(interval);
    }

    public long StartingWorkingSetBytes { get; }

    public long PeakWorkingSetBytes => Interlocked.Read(ref _peakWorkingSetBytes);

    public long PeakAdditionalWorkingSetBytes =>
        Math.Max(0, PeakWorkingSetBytes - StartingWorkingSetBytes);

    public static PeakWorkingSetMonitor Start(TimeSpan? interval = null) =>
        new(interval ?? TimeSpan.FromMilliseconds(10));

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();

        try
        {
            await _samplingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        finally
        {
            UpdatePeak(Environment.WorkingSet);
            _stop.Dispose();
        }
    }

    private async Task SampleAsync(TimeSpan interval)
    {
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
        {
            UpdatePeak(Environment.WorkingSet);
        }
    }

    private void UpdatePeak(long value)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _peakWorkingSetBytes);
            if (value <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(
                    ref _peakWorkingSetBytes,
                    value,
                    current) == current)
            {
                return;
            }
        }
    }
}
