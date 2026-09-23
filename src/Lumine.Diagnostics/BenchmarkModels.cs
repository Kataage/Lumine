namespace Lumine.Diagnostics;

public static class CoreMetricNames
{
    public const string StartupWindowReady = "startup.window_ready";
    public const string DatabaseOpenMigration = "library.database_open_migration";
    public const string DatabaseReopen = "library.database_reopen";
    public const string LibraryBulkUpsert = "library.bulk_upsert";
    public const string LibraryQuery = "library.query";
    public const string LibraryKeysetTraversal = "library.keyset_traversal";
    public const string ThumbnailGenerate = "image.thumbnail_generate";
    public const string ThumbnailCacheHit = "image.thumbnail_cache_hit";
    public const string ViewerFirstPaint = "viewer.first_paint";
    public const string ViewerFastScrollRefresh = "viewer.fast_scroll_refresh";
    public const string FilesystemEventToDatabase = "filesystem.event_to_database";
    public const string FixtureMetadataGeneration = "fixture.metadata_generation";
    public const string FixtureMetadataWrite = "fixture.metadata_write";
    public const string FixtureTreeMaterialize = "fixture.tree_materialize";
}

public sealed record BenchmarkEnvironment(
    string HardwareId,
    string MachineName,
    string OsDescription,
    string FrameworkDescription,
    string ProcessArchitecture,
    string OsArchitecture,
    int ProcessorCount,
    long TotalAvailableMemoryBytes,
    string AppRevision,
    string AppVersion,
    DateTimeOffset CapturedAtUtc);

public readonly record struct ResourceSnapshot(
    long WorkingSetBytes,
    long ManagedHeapBytes,
    long TotalAllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    double TotalProcessorTimeMs);

public sealed record BenchmarkMeasurement(
    string Name,
    DateTimeOffset StartedAtUtc,
    double DurationMs,
    ResourceSnapshot Before,
    ResourceSnapshot After);

public sealed class BenchmarkResult
{
    public int SchemaVersion { get; init; } = 1;

    public required BenchmarkEnvironment Environment { get; init; }

    public required List<BenchmarkMeasurement> Measurements { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
}

public sealed record FixtureAsset(
    long Id,
    string RelativePath,
    string FileName,
    string Extension,
    long FileSize,
    DateTimeOffset ModifiedAtUtc,
    int Width,
    int Height);
