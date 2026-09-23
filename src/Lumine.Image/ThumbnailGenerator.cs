using NetVips;

namespace Lumine.Image;

internal sealed class ThumbnailGenerator
{
    private readonly ThumbnailCache _cache;
    private readonly Dictionary<string, KeyGate> _keyGates = new(StringComparer.Ordinal);
    private readonly object _keyGatesLock = new();
    private long _cacheHits;
    private long _cacheMisses;
    private long _generated;
    private long _failed;
    private long _sourceOpens;

    public ThumbnailGenerator(ThumbnailCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public ThumbnailDiagnosticsSnapshot SnapshotDiagnostics() =>
        new(
            Interlocked.Read(ref _cacheHits),
            Interlocked.Read(ref _cacheMisses),
            Interlocked.Read(ref _generated),
            Interlocked.Read(ref _failed),
            Interlocked.Read(ref _sourceOpens));

    public ThumbnailResult GetOrCreate(
        ThumbnailSource source,
        ThumbnailProfile profile,
        CancellationToken cancellationToken)
    {
        ThumbnailCache.ValidateSource(source);
        ThumbnailCache.ValidateProfile(profile);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = ThumbnailCache.GetCacheKey(source, profile);
        var cached = _cache.TryOpenValid(cacheKey, cancellationToken);
        if (cached is not null)
        {
            Interlocked.Increment(ref _cacheHits);
            return cached;
        }

        var keyGate = AcquireKeyGate(cacheKey);
        try
        {
            keyGate.Semaphore.Wait(cancellationToken);
            try
            {
                // Another worker may have generated this exact revision while
                // this request was waiting. Re-check before touching original.
                cached = _cache.TryOpenValid(cacheKey, cancellationToken);
                if (cached is not null)
                {
                    Interlocked.Increment(ref _cacheHits);
                    return cached;
                }

                Interlocked.Increment(ref _cacheMisses);
                return Generate(source, profile, cacheKey, cancellationToken);
            }
            finally
            {
                keyGate.Semaphore.Release();
            }
        }
        finally
        {
            ReleaseKeyGate(cacheKey, keyGate);
        }
    }

    private ThumbnailResult Generate(
        ThumbnailSource source,
        ThumbnailProfile profile,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var temporaryPath = _cache.CreateTemporaryPath(cacheKey);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _sourceOpens);

            using var thumbnail = NetVips.Image.Thumbnail(
                source.SourcePath,
                profile.MaxWidth,
                height: profile.MaxHeight,
                size: Enums.Size.Down,
                noRotate: false,
                linear: true,
                failOn: Enums.FailOn.Error);

            cancellationToken.ThrowIfCancellationRequested();

            using var normalized = thumbnail.Interpretation == Enums.Interpretation.Srgb
                ? thumbnail.Copy()
                : thumbnail.Colourspace(Enums.Interpretation.Srgb);

            normalized.Webpsave(
                temporaryPath,
                q: profile.Quality,
                smartSubsample: true,
                keep: Enums.ForeignKeep.None);

            cancellationToken.ThrowIfCancellationRequested();
            var cachePath = _cache.CommitTemporaryFile(cacheKey, temporaryPath);

            using var persisted = NetVips.Image.NewFromFile(
                cachePath,
                access: Enums.Access.Sequential,
                failOn: Enums.FailOn.Error);

            var width = persisted.Width;
            var height = persisted.Height;
            persisted.Invalidate();

            var result = new ThumbnailResult(
                cacheKey,
                cachePath,
                false,
                width,
                height,
                new FileInfo(cachePath).Length);

            Interlocked.Increment(ref _generated);
            return result;
        }
        catch
        {
            Interlocked.Increment(ref _failed);
            TryDelete(temporaryPath);
            throw;
        }
    }

    private KeyGate AcquireKeyGate(string cacheKey)
    {
        lock (_keyGatesLock)
        {
            if (!_keyGates.TryGetValue(cacheKey, out var gate))
            {
                gate = new KeyGate();
                _keyGates.Add(cacheKey, gate);
            }

            gate.Users++;
            return gate;
        }
    }

    private void ReleaseKeyGate(string cacheKey, KeyGate gate)
    {
        lock (_keyGatesLock)
        {
            gate.Users--;

            if (gate.Users == 0
                && _keyGates.TryGetValue(cacheKey, out var current)
                && ReferenceEquals(current, gate))
            {
                _keyGates.Remove(cacheKey);
                gate.Semaphore.Dispose();
            }
        }
    }

    private sealed class KeyGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Users { get; set; }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException)
        {
        }
    }
}
