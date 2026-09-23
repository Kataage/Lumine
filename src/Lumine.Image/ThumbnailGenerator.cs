using NetVips;

namespace Lumine.Image;

internal sealed class ThumbnailGenerator
{
    private readonly ThumbnailCache _cache;
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

        Interlocked.Increment(ref _cacheMisses);
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

            var result = new ThumbnailResult(
                cacheKey,
                cachePath,
                false,
                persisted.Width,
                persisted.Height,
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
