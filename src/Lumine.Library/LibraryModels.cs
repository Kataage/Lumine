namespace Lumine.Library;

public enum LibraryScanState
{
    Unknown = 0,
    InProgress = 1,
    Complete = 2,
    Partial = 3
}

public sealed record LibraryInfo(
    long Id,
    string Name,
    string RootPath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastScanCompletedAtUtc,
    DateTimeOffset? LastScanAttemptedAtUtc,
    LibraryScanState ScanState);

public sealed record AssetInfo(
    long Id,
    long LibraryId,
    long? FolderId,
    string RelativePath,
    string FileName,
    string Extension,
    long FileSize,
    DateTimeOffset ModifiedAtUtc,
    long SourceRevision,
    int? Width,
    int? Height,
    string? Format,
    string? SourceIdentity = null,
    int? RawWidth = null,
    int? RawHeight = null,
    bool? HasAlpha = null);

public sealed record AssetTechnicalMetadata(
    int Width,
    int Height,
    int RawWidth,
    int RawHeight,
    bool HasAlpha,
    string Format,
    string SourceIdentity);

public readonly record struct AssetCursor(long ModifiedAtUtcTicks, long Id)
{
    public static AssetCursor From(AssetInfo asset) =>
        new(asset.ModifiedAtUtc.UtcDateTime.Ticks, asset.Id);
}

public sealed record AssetPage(
    IReadOnlyList<AssetInfo> Items,
    AssetCursor? NextCursor);


public sealed record AssetUserMetadata(
    long AssetId,
    int? Rating,
    bool Favorite,
    string Notes,
    string? StatusLabel,
    string? ColorLabel,
    IReadOnlyList<string> Tags);

public sealed record AssetUserMetadataUpdate(
    int? Rating = null,
    bool Favorite = false,
    string Notes = "",
    string? StatusLabel = null,
    string? ColorLabel = null,
    IReadOnlyList<string>? Tags = null);

public enum AssetSortOrder
{
    ModifiedNewest = 0,
    ModifiedOldest = 1,
    FileNameAscending = 2,
    FileNameDescending = 3
}

public sealed record AssetQuery(
    string? SearchText = null,
    IReadOnlyList<string>? RequiredTags = null,
    int? MinRating = null,
    int? MaxRating = null,
    bool? Favorite = null,
    string? StatusLabel = null,
    string? ColorLabel = null,
    AssetSortOrder SortOrder = AssetSortOrder.ModifiedNewest);

public sealed record AssetQueryCursor(
    AssetSortOrder SortOrder,
    long Id,
    long? ModifiedAtUtcTicks = null,
    string? FileName = null);

public sealed record AssetQueryPage(
    IReadOnlyList<AssetInfo> Items,
    AssetQueryCursor? NextCursor);

public sealed record AssetUpsert(
    string RelativePath,
    long FileSize,
    DateTimeOffset ModifiedAtUtc,
    int? Width = null,
    int? Height = null,
    string? Format = null,
    long ObservationGeneration = 0,
    bool ForceSourceRevision = false);

public sealed record LibraryScanProgress(
    int Discovered,
    int Persisted,
    int Skipped,
    string? CurrentRelativePath);

public sealed record LibraryScanFailure(
    string Path,
    string Operation,
    string ErrorType);

public sealed record LibraryScanResult(
    int Discovered,
    int Persisted,
    int Skipped,
    bool Completed,
    DateTimeOffset FinishedAtUtc,
    IReadOnlyList<LibraryScanFailure> FailureSamples);

public sealed record LibrarySyncState(
    long LibraryId,
    long ReconcileGeneration,
    bool ReconcileRequired,
    string? UsnJournalId,
    long? NextUsn,
    DateTimeOffset? WatcherStoppedAtUtc,
    DateTimeOffset? LastReconciledAtUtc,
    string? LastError);


internal sealed record TrackedSourceIdentity(
    long AssetId,
    long SourceRevision,
    string RelativePathKey,
    long FileSize,
    long ModifiedAtUtcTicks,
    string SourceIdentity);
