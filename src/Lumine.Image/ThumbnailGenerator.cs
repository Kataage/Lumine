using Lumine.Core;
using NetVips;

namespace Lumine.Image;

internal sealed class ThumbnailGenerator
{
    private const int MetadataMemoryCacheLimit = 4096;

    private readonly ThumbnailCache _cache;
    private readonly ThumbnailStorageMode _storageMode;
    private readonly long _memoryByteLimit;
    private readonly object _memoryGate = new();
    private readonly Dictionary<string, MemoryCacheEntry> _memoryCache =
        new(StringComparer.Ordinal);
    private readonly LinkedList<string> _memoryLru = [];
    private long _memoryCacheBytes;
    private long _memoryCacheHits;
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
    private long _sourceOpenCancellations;
    private long _metadataProbes;
    private long _metadataBytesHashed;
    private long _metadataMemoryHits;
    private long _metadataFastIdentityHits;
    private long _metadataNtfsUsnIdentityHits;
    private long _metadataWindowsFileIdIdentityHits;
    private long _metadataFullHashFallbacks;

    public ThumbnailGenerator(
        ThumbnailCache cache,
        ThumbnailStorageMode storageMode,
        long memoryByteLimit)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(memoryByteLimit);
        _storageMode = storageMode;
        _memoryByteLimit = memoryByteLimit;
    }

    public ThumbnailMemoryCacheStats MemoryCacheStats
    {
        get
        {
            lock (_memoryGate)
            {
                return new ThumbnailMemoryCacheStats(
                    _memoryCache.Count,
                    _memoryCacheBytes,
                    _memoryByteLimit,
                    Interlocked.Read(ref _memoryCacheHits));
            }
        }
    }

    public ThumbnailDiagnosticsSnapshot SnapshotDiagnostics() =>
        new(
            Interlocked.Read(ref _cacheHits),
            Interlocked.Read(ref _cacheMisses),
            Interlocked.Read(ref _generated),
            Interlocked.Read(ref _failed),
            Interlocked.Read(ref _sourceOpens),
            Interlocked.Read(ref _sourceOpenCancellations),
            Interlocked.Read(ref _metadataProbes),
            Interlocked.Read(ref _metadataBytesHashed),
            Interlocked.Read(ref _metadataMemoryHits),
            Interlocked.Read(ref _metadataFastIdentityHits),
            Interlocked.Read(ref _metadataNtfsUsnIdentityHits),
            Interlocked.Read(ref _metadataWindowsFileIdIdentityHits),
            Interlocked.Read(ref _metadataFullHashFallbacks));

    public ThumbnailResult? TryGetMemoryCached(
        ThumbnailSource source,
        ThumbnailProfile profile,
        CancellationToken cancellationToken)
    {
        if (_storageMode != ThumbnailStorageMode.MemoryOnly)
        {
            return null;
        }

        ThumbnailCache.ValidateSource(source);
        ThumbnailCache.ValidateProfile(profile);
        cancellationToken.ThrowIfCancellationRequested();

        var metadata =
            source.PersistedMetadata
            ?? TryGetRememberedMetadata(source);
        if (metadata is null)
        {
            return null;
        }

        var prepared =
            source.WithMetadata(metadata);
        var cacheKey =
            ThumbnailCache.GetCacheKey(
                prepared,
                profile);
        var cached =
            TryOpenValid(
                cacheKey,
                cancellationToken);
        if (cached is null)
        {
            return null;
        }

        Interlocked.Increment(
            ref _cacheHits);
        return cached with
        {
            SourceMetadata = metadata
        };
    }

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
            var cached = TryOpenValid(cacheKey, cancellationToken);
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
            expectedSourceIdentity: null,
            cancellationToken);
        RememberMetadata(source, snapshot.Metadata);
        var prepared = source.WithMetadata(snapshot.Metadata);
        var preparedKey = ThumbnailCache.GetCacheKey(prepared, profile);

        var preparedCached = TryOpenValid(
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
                var cached = TryOpenValid(
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
                        snapshot,
                        cancellationToken);
                }

                using var validated = OpenSnapshot(
                    source,
                    metadata.SourceIdentity,
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
                    validated,
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
        string? expectedSourceIdentity,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _metadataProbes);

        var snapshot = ImageSourceSnapshot.Open(
            source.SourcePath,
            source.FileSize,
            source.ModifiedAtUtcTicks,
            expectedSourceIdentity,
            cancellationToken);

        switch (FileSourceIdentityProbe.GetKind(
                    snapshot.Metadata.SourceIdentity))
        {
            case FileSourceIdentityKind.NtfsUsn:
                Interlocked.Increment(
                    ref _metadataFastIdentityHits);
                Interlocked.Increment(
                    ref _metadataNtfsUsnIdentityHits);
                break;

            case FileSourceIdentityKind.WindowsFileId:
                Interlocked.Increment(
                    ref _metadataFastIdentityHits);
                Interlocked.Increment(
                    ref _metadataWindowsFileIdIdentityHits);
                break;

            case FileSourceIdentityKind.Sha256:
                Interlocked.Increment(
                    ref _metadataFullHashFallbacks);
                Interlocked.Add(
                    ref _metadataBytesHashed,
                    snapshot.Metadata.BytesHashed);
                break;

            default:
                throw new InvalidOperationException(
                    "Unknown source identity strategy.");
        }

        return snapshot;
    }

    private ThumbnailResult Generate(
        ThumbnailSource source,
        SourceTechnicalMetadata metadata,
        ThumbnailProfile profile,
        string cacheKey,
        ImageSourceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var temporaryPath =
            _storageMode == ThumbnailStorageMode.PersistentDisk
                ? _cache.CreateTemporaryPath(cacheKey)
                : string.Empty;
        NetVips.Image? bmpMemory = null;
        NetVips.Image? thumbnail = null;
        var sourceOpened = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _sourceOpens);
            sourceOpened = true;

            if (snapshot.BmpInfo is { } bmp)
            {
                using var bmpStream =
                    snapshot.OpenStableReadStream();
                var decoded =
                    BmpFallbackDecoder.DecodeThumbnail(
                        bmpStream,
                        bmp,
                        profile.MaxWidth,
                        profile.MaxHeight,
                        cancellationToken);

                bmpMemory =
                    NetVips.Image.NewFromMemory<byte>(
                        decoded.Rgba,
                        decoded.Width,
                        decoded.Height,
                        4,
                        Enums.BandFormat.Uchar);
                thumbnail = bmpMemory.Copy(
                    interpretation:
                        Enums.Interpretation.Srgb);
            }
            else
            {
                thumbnail = NetVips.Image.Thumbnail(
                    source.SourcePath,
                    profile.MaxWidth,
                    height: profile.MaxHeight,
                    size: Enums.Size.Down,
                    noRotate: false,
                    linear: profile.LinearLight,
                    outputProfile: "srgb",
                    failOn: Enums.FailOn.Error);
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var nativeCancellation =
                cancellationToken.Register(
                    static state =>
                        ((NetVips.Image)state!).SetKill(true),
                    thumbnail);

            if (_storageMode == ThumbnailStorageMode.MemoryOnly)
            {
                byte[] encoded;
                try
                {
                    encoded = thumbnail.WebpsaveBuffer(
                        q: profile.Quality,
                        smartSubsample: true,
                        keep: Enums.ForeignKeep.None);
                }
                catch (VipsException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(
                        cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                var width = thumbnail.Width;
                var height = thumbnail.Height;
                StoreMemory(
                    cacheKey,
                    encoded,
                    width,
                    height);

                var memoryResult = new ThumbnailResult(
                    cacheKey,
                    string.Empty,
                    false,
                    width,
                    height,
                    encoded.LongLength,
                    metadata,
                    encoded);

                Interlocked.Increment(ref _generated);
                return memoryResult;
            }

            try
            {
                thumbnail.Webpsave(
                    temporaryPath,
                    q: profile.Quality,
                    smartSubsample: true,
                    keep: Enums.ForeignKeep.None);
            }
            catch (VipsException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var cachePath = _cache.CommitTemporaryFile(
                cacheKey,
                temporaryPath);

            using var persisted = NetVips.Image.NewFromFile(
                cachePath,
                access: Enums.Access.Sequential,
                failOn: Enums.FailOn.Error);

            var persistedWidth = persisted.Width;
            var persistedHeight = persisted.Height;
            persisted.Invalidate();

            var result = new ThumbnailResult(
                cacheKey,
                cachePath,
                false,
                persistedWidth,
                persistedHeight,
                new FileInfo(cachePath).Length,
                metadata);

            Interlocked.Increment(ref _generated);
            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            if (sourceOpened)
            {
                Interlocked.Increment(
                    ref _sourceOpenCancellations);
            }

            if (!string.IsNullOrEmpty(temporaryPath))
            {
                TryDelete(temporaryPath);
            }
            throw;
        }
        catch
        {
            Interlocked.Increment(ref _failed);
            if (!string.IsNullOrEmpty(temporaryPath))
            {
                TryDelete(temporaryPath);
            }
            throw;
        }
        finally
        {
            thumbnail?.Dispose();
            bmpMemory?.Dispose();
        }
    }

    private ThumbnailResult? TryOpenValid(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_storageMode == ThumbnailStorageMode.PersistentDisk)
        {
            return _cache.TryOpenValid(
                cacheKey,
                cancellationToken);
        }

        lock (_memoryGate)
        {
            if (!_memoryCache.TryGetValue(
                    cacheKey,
                    out var entry))
            {
                return null;
            }

            _memoryLru.Remove(entry.Node);
            _memoryLru.AddFirst(entry.Node);
            Interlocked.Increment(ref _memoryCacheHits);

            return new ThumbnailResult(
                cacheKey,
                string.Empty,
                true,
                entry.Width,
                entry.Height,
                entry.Encoded.LongLength,
                EncodedBytes: entry.Encoded);
        }
    }

    private void StoreMemory(
        string cacheKey,
        byte[] encoded,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        if (encoded.LongLength > _memoryByteLimit)
        {
            return;
        }

        lock (_memoryGate)
        {
            if (_memoryCache.TryGetValue(
                    cacheKey,
                    out var existing))
            {
                _memoryLru.Remove(existing.Node);
                _memoryCache.Remove(cacheKey);
                _memoryCacheBytes -=
                    existing.Encoded.LongLength;
            }

            while (_memoryCache.Count > 0
                   && _memoryCacheBytes
                      + encoded.LongLength
                      > _memoryByteLimit)
            {
                var last =
                    _memoryLru.Last
                    ?? throw new InvalidOperationException(
                        "Thumbnail memory LRU lost its tail node.");
                var evictedKey = last.Value;
                var evicted = _memoryCache[evictedKey];

                _memoryLru.RemoveLast();
                _memoryCache.Remove(evictedKey);
                _memoryCacheBytes -=
                    evicted.Encoded.LongLength;
            }

            var node = _memoryLru.AddFirst(cacheKey);
            _memoryCache.Add(
                cacheKey,
                new MemoryCacheEntry(
                    encoded,
                    width,
                    height,
                    node));
            _memoryCacheBytes += encoded.LongLength;
        }
    }

    private static void EnsureSameMetadata(
        string sourcePath,
        SourceTechnicalMetadata expected,
        SourceTechnicalMetadata actual)
    {
        if (!string.Equals(
                expected.SourceIdentity,
                actual.SourceIdentity,
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

    private sealed record MemoryCacheEntry(
        byte[] Encoded,
        int Width,
        int Height,
        LinkedListNode<string> Node);

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
