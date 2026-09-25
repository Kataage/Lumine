using Avalonia.Media.Imaging;

namespace Lumine.Viewer;

public readonly record struct DecodedBitmapCacheDiagnostics(
    int EntryCount,
    long EstimatedBytes,
    int ActiveDecodes,
    int PeakConcurrentDecodes);

public sealed class DecodedBitmapLease : IDisposable
{
    private DecodedBitmapCache? _owner;
    private readonly string _key;

    internal DecodedBitmapLease(
        DecodedBitmapCache owner,
        string key,
        Bitmap bitmap)
    {
        _owner = owner;
        _key = key;
        Bitmap = bitmap;
    }

    public Bitmap Bitmap { get; }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        owner?.Release(_key);
    }
}

public sealed class DecodedBitmapCache : IDisposable, IAsyncDisposable
{
    public static int DecodeConcurrencyLimit { get; } =
        Math.Clamp(Environment.ProcessorCount / 2, 1, 2);

    private static readonly SemaphoreSlim DecodeGate =
        new(DecodeConcurrencyLimit, DecodeConcurrencyLimit);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Func<string, CancellationToken, Bitmap> _decodeBitmap;
    private readonly int _entryLimit;
    private readonly long _byteLimit;
    private long _estimatedBytes;
    private long _sequence;
    private TaskCompletionSource _capacityChanged = NewCapacitySignal();
    private TaskCompletionSource _operationsDrained =
        NewCompletedSignal();
    private readonly TaskCompletionSource _disposeCompletion =
        NewCapacitySignal();
    private int _activeOperations;
    private int _activeDecodes;
    private int _peakConcurrentDecodes;
    private bool _disposed;

    public DecodedBitmapCache(int entryLimit, long byteLimit)
        : this(entryLimit, byteLimit, DecodeBitmapFromFile)
    {
    }

    internal DecodedBitmapCache(
        int entryLimit,
        long byteLimit,
        Func<string, CancellationToken, Bitmap> decodeBitmap)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entryLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLimit);
        ArgumentNullException.ThrowIfNull(decodeBitmap);

        _entryLimit = entryLimit;
        _byteLimit = byteLimit;
        _decodeBitmap = decodeBitmap;
    }

    public DecodedBitmapCacheDiagnostics Diagnostics
    {
        get
        {
            lock (_gate)
            {
                return new DecodedBitmapCacheDiagnostics(
                    _entries.Count,
                    _estimatedBytes,
                    Volatile.Read(ref _activeDecodes),
                    Volatile.Read(ref _peakConcurrentDecodes));
            }
        }
    }

    public async Task<DecodedBitmapLease> AcquireAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(path);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RegisterOperationLocked();
        }

        try
        {
            using var operation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _shutdown.Token);
            var operationToken = operation.Token;

            try
            {
                while (true)
                {
                    operationToken.ThrowIfCancellationRequested();

                    lock (_gate)
                    {
                        ObjectDisposedException.ThrowIf(_disposed, this);

                        if (_entries.TryGetValue(fullPath, out var existing))
                        {
                            existing.Leases++;
                            existing.LastAccess = NextSequence();
                            return new DecodedBitmapLease(
                                this,
                                fullPath,
                                existing.Bitmap);
                        }
                    }

                    await DecodeGate.WaitAsync(operationToken)
                        .ConfigureAwait(false);
                    var decodeRegistered = false;

                    AcquireAttempt attempt;
                    try
                    {
                        lock (_gate)
                        {
                            ObjectDisposedException.ThrowIf(
                                _disposed,
                                this);
                            var active = Interlocked.Increment(
                                ref _activeDecodes);
                            UpdatePeakConcurrentDecodes(active);
                            decodeRegistered = true;
                        }

                        attempt = await Task.Run(
                            () => TryAcquire(
                                fullPath,
                                operationToken),
                            operationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (decodeRegistered)
                        {
                            Interlocked.Decrement(
                                ref _activeDecodes);
                        }

                        DecodeGate.Release();
                    }

                    if (attempt.Lease is not null)
                    {
                        return attempt.Lease;
                    }

                    var capacityChanged =
                        attempt.CapacityChanged
                        ?? throw new InvalidOperationException(
                            "Decoded bitmap cache returned a blocked admission without a capacity signal.");

                    await capacityChanged.WaitAsync(operationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
                when (_shutdown.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested)
            {
                throw new ObjectDisposedException(
                    nameof(DecodedBitmapCache));
            }
        }
        finally
        {
            CompleteOperation();
        }
    }

    public void Dispose() =>
        BeginDispose();

    public ValueTask DisposeAsync()
    {
        BeginDispose();
        return new ValueTask(_disposeCompletion.Task);
    }

    private void BeginDispose()
    {
        List<Bitmap> disposeNow = [];
        Task operationDrain;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SignalCapacityChangedLocked();
            operationDrain = _operationsDrained.Task;

            foreach (var pair in _entries.ToArray())
            {
                var entry = pair.Value;
                if (entry.Leases == 0)
                {
                    _entries.Remove(pair.Key);
                    _estimatedBytes -= entry.EstimatedBytes;
                    disposeNow.Add(entry.Bitmap);
                }
                else
                {
                    entry.DisposeWhenReleased = true;
                }
            }
        }

        _shutdown.Cancel();

        foreach (var bitmap in disposeNow)
        {
            bitmap.Dispose();
        }

        _ = FinishDisposeAsync(operationDrain);
    }

    private async Task FinishDisposeAsync(Task operationDrain)
    {
        try
        {
            await operationDrain.ConfigureAwait(false);
            _shutdown.Dispose();
            _disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            _disposeCompletion.TrySetException(exception);
        }
    }

    internal void Release(string key)
    {
        Bitmap? dispose = null;

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                return;
            }

            if (entry.Leases > 0)
            {
                entry.Leases--;
                entry.LastAccess = NextSequence();

                if (entry.Leases == 0)
                {
                    SignalCapacityChangedLocked();
                }
            }

            if (entry.Leases == 0 && (_disposed || entry.DisposeWhenReleased))
            {
                _entries.Remove(key);
                _estimatedBytes -= entry.EstimatedBytes;
                dispose = entry.Bitmap;
            }
        }

        dispose?.Dispose();
    }

    private AcquireAttempt TryAcquire(
        string fullPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_entries.TryGetValue(fullPath, out var existing))
            {
                existing.Leases++;
                existing.LastAccess = NextSequence();
                return new AcquireAttempt(
                    new DecodedBitmapLease(
                        this,
                        fullPath,
                        existing.Bitmap),
                    null);
            }
        }

        var bitmap = _decodeBitmap(
            fullPath,
            cancellationToken);
        var admitted = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var estimatedBytes = checked(
                (long)bitmap.PixelSize.Width
                * bitmap.PixelSize.Height
                * 4L);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (_entries.TryGetValue(fullPath, out var raced))
                {
                    raced.Leases++;
                    raced.LastAccess = NextSequence();
                    return new AcquireAttempt(
                        new DecodedBitmapLease(
                            this,
                            fullPath,
                            raced.Bitmap),
                        null);
                }

                if (estimatedBytes > _byteLimit)
                {
                    throw new InvalidOperationException(
                        $"Decoded thumbnail requires {estimatedBytes:N0} bytes, above cache limit {_byteLimit:N0}.");
                }

                EvictForLocked(estimatedBytes);

                if (_entries.Count >= _entryLimit
                    || _estimatedBytes + estimatedBytes > _byteLimit)
                {
                    // Every eviction candidate is still leased. This is
                    // transient viewport pressure, not a decode failure.
                    // Capture the signal while holding the same lock so a
                    // lease release cannot be missed between detection and wait.
                    return new AcquireAttempt(
                        null,
                        _capacityChanged.Task);
                }

                var entry = new Entry(
                    bitmap,
                    estimatedBytes,
                    NextSequence())
                {
                    Leases = 1
                };
                _entries[fullPath] = entry;
                _estimatedBytes += estimatedBytes;
                admitted = true;

                return new AcquireAttempt(
                    new DecodedBitmapLease(
                        this,
                        fullPath,
                        bitmap),
                    null);
            }
        }
        finally
        {
            if (!admitted)
            {
                bitmap.Dispose();
            }
        }
    }

    private void EvictForLocked(long incomingBytes)
    {
        while (_entries.Count >= _entryLimit
               || _estimatedBytes + incomingBytes > _byteLimit)
        {
            KeyValuePair<string, Entry>? candidate = null;

            foreach (var pair in _entries)
            {
                if (pair.Value.Leases != 0)
                {
                    continue;
                }

                if (candidate is null
                    || pair.Value.LastAccess
                        < candidate.Value.Value.LastAccess)
                {
                    candidate = pair;
                }
            }

            if (candidate is not { } selected)
            {
                // All resident entries are leased. The caller will capture
                // the capacity-change signal and wait instead of treating
                // temporary viewport pressure as a decode failure.
                return;
            }

            _entries.Remove(selected.Key);
            _estimatedBytes -= selected.Value.EstimatedBytes;
            selected.Value.Bitmap.Dispose();
        }
    }

    private static Bitmap DecodeBitmapFromFile(
        string fullPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        var bitmap = new Bitmap(stream);

        if (cancellationToken.IsCancellationRequested)
        {
            bitmap.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }

        return bitmap;
    }

    private void RegisterOperationLocked()
    {
        if (_activeOperations == 0)
        {
            _operationsDrained = NewCapacitySignal();
        }

        _activeOperations++;
    }

    private void CompleteOperation()
    {
        TaskCompletionSource? drained = null;

        lock (_gate)
        {
            _activeOperations--;
            if (_activeOperations == 0)
            {
                drained = _operationsDrained;
            }
        }

        drained?.TrySetResult();
    }

    private static TaskCompletionSource NewCapacitySignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource NewCompletedSignal()
    {
        var signal = NewCapacitySignal();
        signal.TrySetResult();
        return signal;
    }

    private void SignalCapacityChangedLocked()
    {
        var previous = _capacityChanged;
        _capacityChanged = NewCapacitySignal();
        previous.TrySetResult();
    }

    private void UpdatePeakConcurrentDecodes(int active)
    {
        while (true)
        {
            var current = Volatile.Read(ref _peakConcurrentDecodes);
            if (active <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(
                    ref _peakConcurrentDecodes,
                    active,
                    current) == current)
            {
                return;
            }
        }
    }

    private long NextSequence() => ++_sequence;

    private readonly record struct AcquireAttempt(
        DecodedBitmapLease? Lease,
        Task? CapacityChanged);

    private sealed class Entry(
        Bitmap bitmap,
        long estimatedBytes,
        long lastAccess)
    {
        public Bitmap Bitmap { get; } = bitmap;

        public long EstimatedBytes { get; } = estimatedBytes;

        public long LastAccess { get; set; } = lastAccess;

        public int Leases { get; set; }

        public bool DisposeWhenReleased { get; set; }
    }
}
