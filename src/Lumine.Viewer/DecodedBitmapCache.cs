using Avalonia.Media.Imaging;

namespace Lumine.Viewer;

public readonly record struct DecodedBitmapCacheDiagnostics(
    int EntryCount,
    long EstimatedBytes);

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

public sealed class DecodedBitmapCache : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly int _entryLimit;
    private readonly long _byteLimit;
    private long _estimatedBytes;
    private long _sequence;
    private bool _disposed;

    public DecodedBitmapCache(int entryLimit, long byteLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entryLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLimit);

        _entryLimit = entryLimit;
        _byteLimit = byteLimit;
    }

    public DecodedBitmapCacheDiagnostics Diagnostics
    {
        get
        {
            lock (_gate)
            {
                return new DecodedBitmapCacheDiagnostics(
                    _entries.Count,
                    _estimatedBytes);
            }
        }
    }

    public Task<DecodedBitmapLease> AcquireAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Task.Run(
            () => Acquire(path, cancellationToken),
            cancellationToken);
    }

    public void Dispose()
    {
        List<Bitmap> disposeNow = [];

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

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

        foreach (var bitmap in disposeNow)
        {
            bitmap.Dispose();
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

    private DecodedBitmapLease Acquire(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_entries.TryGetValue(fullPath, out var existing))
            {
                existing.Leases++;
                existing.LastAccess = NextSequence();
                return new DecodedBitmapLease(this, fullPath, existing.Bitmap);
            }
        }

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        var bitmap = new Bitmap(stream);
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
                    return new DecodedBitmapLease(this, fullPath, raced.Bitmap);
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
                    throw new InvalidOperationException(
                        "Decoded thumbnail cache is fully pinned and cannot admit another bitmap within its hard limits.");
                }

                var entry = new Entry(bitmap, estimatedBytes, NextSequence())
                {
                    Leases = 1
                };
                _entries[fullPath] = entry;
                _estimatedBytes += estimatedBytes;
                admitted = true;

                return new DecodedBitmapLease(this, fullPath, bitmap);
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
            var candidate = _entries
                .Where(static pair => pair.Value.Leases == 0)
                .MinBy(static pair => pair.Value.LastAccess);

            if (candidate.Key is null)
            {
                return;
            }

            _entries.Remove(candidate.Key);
            _estimatedBytes -= candidate.Value.EstimatedBytes;
            candidate.Value.Bitmap.Dispose();
        }
    }

    private long NextSequence() => ++_sequence;

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
