using NetVips;

namespace Lumine.Image;

internal sealed class ThumbnailGenerator
{
    private const int MetadataMemoryCacheLimit = 4096;

    private readonly ThumbnailCache _cache;
    private readonly Dictionary<string, KeyGate> _keyGates = new(StringComparer.Ordinal);
    private readonly object _keyGatesLock = new();
    private readonly object _metadataGate = new();
    private readonly Dictionary<SourceMetadataKey, MetadataCacheEntry>
        _metadataCache = [];
    private readonly LinkedList<SourceMetadataKey> _metadataLru = [];
    private long _cacheHits;
    private long _cacheMisses;
    private long _generated;
    private long _failed;
    private long _sourceOpens;
    private long _metadataProbes;
    private long _metadataBytesHashed;
    private long _metadataMemoryHits;

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
            Interlocked.Read(ref _sourceOpens),
            Interlocked.Read(ref _metadataProbes),
            Interlocked.Read(ref _metadataBytesHashed),
            Interlocked.Read(ref _metadataMemoryHits));

    public ThumbnailResult GetOrCreate(
        ThumbnailSource source,
        ThumbnailProfile profile,
        CancellationToken cancellationToken)
    {
        ThumbnailCache.ValidateSource(source);
        ThumbnailCache.ValidateProfile(profile);
        cancellationToken.ThrowIfCancellationRequested();

        var persisted = source.PersistedMetadata
            ?? TryGetRememberedMetadata(source);

        if (persisted is not null)
        {
            source = source.WithMetadata(persisted);
            var cacheKey = ThumbnailCache.GetCacheKey(source, profile);
            var cached = _cache.TryOpenValid(cacheKey, cancellationToken);
            if (cached is not null)
            {
                Interlocked.Increment(ref _cacheHits);
                return cached with { SourceMetadata = persisted };
            }

            return GetOrCreateForPreparedSource(
                source,
                persisted,
                profile,
                cacheKey,
                snapshot: null,
                cancellationToken);
        }

        using var snapshot = OpenSnapshot(
            source,
            expectedContentSha256: null,
            cancellationToken);
        RememberMetadata(source, snapshot.Metadata);
        var prepared = source.WithMetadata(snapshot.Metadata);
        var preparedKey = ThumbnailCache.GetCacheKey(prepared, profile);

        var preparedCached = _cache.TryOpenValid(
            preparedKey,
            cancellationToken);
        if (preparedCached is not null)
        {
            Interlocked.Increment(ref _cacheHits);
            return preparedCached with
            {
                SourceMetadata = snapshot.Metadata
            };
        }

        return GetOrCreateForPreparedSource(
            prepared,
            snapshot.Metadata,
            profile,
            preparedKey,
            snapshot,
            cancellationToken);
    }

    private ThumbnailResult GetOrCreateForPreparedSource(
        ThumbnailSource source,
        SourceTechnicalMetadata metadata,
        ThumbnailProfile profile,
        string cacheKey,
        ImageSourceSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        var keyGate = AcquireKeyGate(cacheKey);
        try
        {
            keyGate.Semaphore.Wait(cancellationToken);
            try
            {
                var cached = _cache.TryOpenValid(
                    cacheKey,
                    cancellationToken);
                if (cached is not null)
                {
                    Interlocked.Increment(ref _cacheHits);
                    return cached with { SourceMetadata = metadata };
                }

                Interlocked.Increment(ref _cacheMisses);

                if (snapshot is not null)
                {
                    return Generate(
                        source,
                        metadata,
                        profile,
                        cacheKey,
                        cancellationToken);
                }

                using var validated = OpenSnapshot(
                    source,
                    metadata.ContentSha256,
                    cancellationToken);
                EnsureSameMetadata(
                    source.SourcePath,
                    metadata,
                    validated.Metadata);

                return Generate(
                    source,
                    validated.Metadata,
                    profile,
                    cacheKey,
                    cancellationToken);
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

    private ImageSourceSnapshot OpenSnapshot(
        ThumbnailSource source,
        string? expectedContentSha256,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _metadataProbes);
        Interlocked.Add(ref _metadataBytesHashed, source.FileSize);

        return ImageSourceSnapshot.Open(
            source.SourcePath,
            source.FileSize,
            source.ModifiedAtUtcTicks,
            expectedContentSha256,
            cancellationToken);
    }

    private ThumbnailResult Generate(
        ThumbnailSource source,
        SourceTechnicalMetadata metadata,
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
                outputProfile: "srgb",
                failOn: Enums.FailOn.Error);

            cancellationToken.ThrowIfCancellationRequested();

            using var nativeCancellation = cancellationToken.Register(
                static state => ((NetVips.Image)state!).SetKill(true),
                thumbnail);

            try
            {
                thumbnail.Webpsave(
                    temporaryPath,
                    q: profile.Quality,
                    smartSubsample: true,
                    keep: Enums.ForeignKeep.None);
            }
            catch (VipsException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var cachePath = _cache.CommitTemporaryFile(
                cacheKey,
                temporaryPath);

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
                new FileInfo(cachePath).Length,
                metadata);

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

    private static void EnsureSameMetadata(
        string sourcePath,
        SourceTechnicalMetadata expected,
        SourceTechnicalMetadata actual)
    {
        if (!string.Equals(
                expected.ContentSha256,
                actual.ContentSha256,
                StringComparison.OrdinalIgnoreCase)
            || expected.Width != actual.Width
            || expected.Height != actual.Height
            || expected.RawWidth != actual.RawWidth
            || expected.RawHeight != actual.RawHeight
            || expected.HasAlpha != actual.HasAlpha
            || !string.Equals(
                expected.Format,
                actual.Format,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ImageSourceChangedException(
                sourcePath,
                "Persisted technical metadata no longer describes the current source snapshot.");
        }
    }

    private SourceTechnicalMetadata? TryGetRememberedMetadata(
        ThumbnailSource source)
    {
        var key = SourceMetadataKey.From(source);

        lock (_metadataGate)
        {
            if (!_metadataCache.TryGetValue(key, out var entry))
            {
                return null;
            }

            _metadataLru.Remove(entry.Node);
            _metadataLru.AddFirst(entry.Node);
            Interlocked.Increment(ref _metadataMemoryHits);
            return entry.Metadata;
        }
    }

    private void RememberMetadata(
        ThumbnailSource source,
        SourceTechnicalMetadata metadata)
    {
        var key = SourceMetadataKey.From(source);

        lock (_metadataGate)
        {
            if (_metadataCache.TryGetValue(key, out var existing))
            {
                existing.Metadata = metadata;
                _metadataLru.Remove(existing.Node);
                _metadataLru.AddFirst(existing.Node);
                return;
            }

            var node = _metadataLru.AddFirst(key);
            _metadataCache.Add(
                key,
                new MetadataCacheEntry(metadata, node));

            while (_metadataCache.Count > MetadataMemoryCacheLimit)
            {
                var last = _metadataLru.Last;
                if (last is null)
                {
                    break;
                }

                _metadataLru.RemoveLast();
                _metadataCache.Remove(last.Value);
            }
        }
    }

    private readonly record struct SourceMetadataKey(
        long AssetId,
        long SourceRevision,
        long FileSize,
        long ModifiedAtUtcTicks)
    {
        public static SourceMetadataKey From(ThumbnailSource source) =>
            new(
                source.AssetId,
                source.SourceRevision,
                source.FileSize,
                source.ModifiedAtUtcTicks);
    }

    private sealed class MetadataCacheEntry(
        SourceTechnicalMetadata metadata,
        LinkedListNode<SourceMetadataKey> node)
    {
        public SourceTechnicalMetadata Metadata { get; set; } = metadata;

        public LinkedListNode<SourceMetadataKey> Node { get; } = node;
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
