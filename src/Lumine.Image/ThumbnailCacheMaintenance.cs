namespace Lumine.Image;

internal sealed class ThumbnailCacheMaintenance
{
    private readonly ThumbnailCache _cache;
    private readonly TimeSpan _quietPeriod;
    private readonly object _gate = new();
    private CancellationTokenSource? _backgroundCancellation;
    private Task _backgroundTask = Task.CompletedTask;
    private bool _pending = true;
    private bool _finalizing;
    private long _runsScheduled;
    private long _runsStarted;
    private long _runsCompleted;
    private long _runsCancelled;
    private long _runsFailed;
    private long _foregroundPreemptions;
    private long _filesDeleted;
    private long _bytesDeleted;
    private long _interruptedWritesDeleted;
    private long _lastBytesAfter = -1;
    private string? _lastError;

    public ThumbnailCacheMaintenance(
        ThumbnailCache cache,
        TimeSpan quietPeriod)
    {
        ArgumentNullException.ThrowIfNull(cache);

        if (quietPeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quietPeriod),
                "Maintenance quiet period cannot be negative.");
        }

        _cache = cache;
        _quietPeriod = quietPeriod;
    }

    public ThumbnailCacheMaintenanceDiagnosticsSnapshot Diagnostics =>
        new(
            Interlocked.Read(ref _runsScheduled),
            Interlocked.Read(ref _runsStarted),
            Interlocked.Read(ref _runsCompleted),
            Interlocked.Read(ref _runsCancelled),
            Interlocked.Read(ref _runsFailed),
            Interlocked.Read(ref _foregroundPreemptions),
            Interlocked.Read(ref _filesDeleted),
            Interlocked.Read(ref _bytesDeleted),
            Interlocked.Read(ref _interruptedWritesDeleted),
            Interlocked.Read(ref _lastBytesAfter),
            Volatile.Read(ref _lastError));

    public async Task PauseForRequestAsync(
        bool foreground)
    {
        Task task;
        var cancelled = false;

        lock (_gate)
        {
            task = _backgroundTask;

            if (!task.IsCompleted
                && _backgroundCancellation is { } cancellation)
            {
                cancelled = true;
                cancellation.Cancel();
            }
        }

        if (cancelled && foreground)
        {
            Interlocked.Increment(
                ref _foregroundPreemptions);
        }

        await task.ConfigureAwait(false);
    }

    public void NotifyRequestCompleted(
        ThumbnailResult? result,
        Func<bool> canRun)
    {
        ArgumentNullException.ThrowIfNull(canRun);

        lock (_gate)
        {
            if (_finalizing)
            {
                return;
            }

            if (result is { CacheHit: false })
            {
                _pending = true;
            }

            if (!_pending
                || !_backgroundTask.IsCompleted)
            {
                return;
            }

            var cancellation =
                new CancellationTokenSource();

            _backgroundCancellation =
                cancellation;
            Interlocked.Increment(
                ref _runsScheduled);

            _backgroundTask =
                RunBackgroundAsync(
                    canRun,
                    cancellation);
        }
    }

    public async Task StopBackgroundAsync()
    {
        Task task;

        lock (_gate)
        {
            task = _backgroundTask;
            _backgroundCancellation?.Cancel();
        }

        await task.ConfigureAwait(false);
    }

    public async Task FinalizeAsync()
    {
        lock (_gate)
        {
            _finalizing = true;
        }

        await StopBackgroundAsync()
            .ConfigureAwait(false);

        var interrupted =
            await _cache.RecoverInterruptedWritesAsync()
                .ConfigureAwait(false);
        Interlocked.Add(
            ref _interruptedWritesDeleted,
            interrupted);

        Interlocked.Increment(
            ref _runsStarted);

        try
        {
            var result =
                await _cache.PruneToConfiguredLimitAsync()
                    .ConfigureAwait(false);

            RecordCompleted(result);

            if (result.BytesAfter
                > _cache.ConfiguredByteLimit)
            {
                throw new InvalidOperationException(
                    $"Persistent thumbnail cache could not converge to its configured disk budget: {result.BytesAfter:N0} > {_cache.ConfiguredByteLimit:N0} bytes.");
            }
        }
        catch (Exception exception)
        {
            Interlocked.Increment(
                ref _runsFailed);
            Volatile.Write(
                ref _lastError,
                exception.Message);
            throw;
        }
    }

    private async Task RunBackgroundAsync(
        Func<bool> canRun,
        CancellationTokenSource cancellation)
    {
        try
        {
            if (_quietPeriod > TimeSpan.Zero)
            {
                await Task.Delay(
                    _quietPeriod,
                    cancellation.Token)
                    .ConfigureAwait(false);
            }

            cancellation.Token.ThrowIfCancellationRequested();

            if (!canRun())
            {
                return;
            }

            Interlocked.Increment(
                ref _runsStarted);

            var result =
                await _cache.PruneToConfiguredLimitAsync(
                    cancellation.Token)
                    .ConfigureAwait(false);

            cancellation.Token.ThrowIfCancellationRequested();

            if (result.BytesAfter
                > _cache.ConfiguredByteLimit)
            {
                throw new InvalidOperationException(
                    $"Background thumbnail cache maintenance could not converge to its configured disk budget: {result.BytesAfter:N0} > {_cache.ConfiguredByteLimit:N0} bytes.");
            }

            RecordCompleted(result);

            lock (_gate)
            {
                _pending = false;
            }
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
            Interlocked.Increment(
                ref _runsCancelled);
        }
        catch (Exception exception)
        {
            Interlocked.Increment(
                ref _runsFailed);
            Volatile.Write(
                ref _lastError,
                exception.Message);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(
                        _backgroundCancellation,
                        cancellation))
                {
                    _backgroundCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void RecordCompleted(
        ThumbnailPruneResult result)
    {
        Interlocked.Increment(
            ref _runsCompleted);
        Interlocked.Add(
            ref _filesDeleted,
            result.FilesDeleted);
        Interlocked.Add(
            ref _bytesDeleted,
            result.BytesDeleted);
        Interlocked.Exchange(
            ref _lastBytesAfter,
            result.BytesAfter);
        Volatile.Write(
            ref _lastError,
            null);
    }
}
