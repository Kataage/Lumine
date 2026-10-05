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
    bool? HasAlpha = null,
    DateTimeOffset? CreatedAtUtc = null,
    int? Rating = null,
    bool Favorite = false,
    string? StatusLabel = null,
    string? ColorLabel = null);

public sealed record AssetTechnicalMetadata(
    int Width,
    int Height,
    int RawWidth,
    int RawHeight,
    bool HasAlpha,
    string Format,
    string SourceIdentity);

public sealed record AssetExifMetadata(
    long AssetId,
    long SourceRevision,
    string? CameraModel,
    string? LensModel,
    string? FocalLength,
    string? Aperture,
    string? ShutterSpeed,
    int? Iso,
    string? CapturedAt,
    string? GpsLatitude,
    string? GpsLongitude)
{
    public bool HasValues =>
        !string.IsNullOrWhiteSpace(CameraModel)
        || !string.IsNullOrWhiteSpace(LensModel)
        || !string.IsNullOrWhiteSpace(FocalLength)
        || !string.IsNullOrWhiteSpace(Aperture)
        || !string.IsNullOrWhiteSpace(ShutterSpeed)
        || Iso.HasValue
        || !string.IsNullOrWhiteSpace(CapturedAt)
        || !string.IsNullOrWhiteSpace(GpsLatitude)
        || !string.IsNullOrWhiteSpace(GpsLongitude);
}

public sealed record AssetExifMetadataUpdate(
    string? CameraModel = null,
    string? LensModel = null,
    string? FocalLength = null,
    string? Aperture = null,
    string? ShutterSpeed = null,
    int? Iso = null,
    string? CapturedAt = null,
    string? GpsLatitude = null,
    string? GpsLongitude = null);

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

public sealed record AssetUserMetadataPatch(
    bool SetRating = false,
    int? Rating = null,
    bool SetFavorite = false,
    bool Favorite = false,
    bool SetStatusLabel = false,
    string? StatusLabel = null,
    bool SetColorLabel = false,
    string? ColorLabel = null,
    IReadOnlyList<string>? AddTags = null,
    bool ClearTags = false);

public sealed record AssetUserMetadataSelectionSummary(
    int SelectionCount,
    bool RatingMixed,
    int? Rating,
    bool FavoriteMixed,
    bool Favorite,
    bool StatusLabelMixed,
    string? StatusLabel,
    bool ColorLabelMixed,
    string? ColorLabel,
    bool NotesMixed,
    bool TagsMixed,
    IReadOnlyList<string> CommonTags);

public enum AssetSortOrder
{
    ModifiedNewest = 0,
    ModifiedOldest = 1,
    FileNameAscending = 2,
    FileNameDescending = 3,
    CreatedNewest = 4,
    CreatedOldest = 5,
    FileSizeLargest = 6,
    FileSizeSmallest = 7,
    RatingHighest = 8,
    RatingLowest = 9,
    StatusAscending = 10,
    StatusDescending = 11
}

public sealed record AssetQuery(
    string? SearchText = null,
    IReadOnlyList<string>? RequiredTags = null,
    int? MinRating = null,
    int? MaxRating = null,
    bool? Favorite = null,
    string? StatusLabel = null,
    string? ColorLabel = null,
    AssetSortOrder SortOrder = AssetSortOrder.ModifiedNewest,
    string? FolderPathPrefix = null);

public sealed record AssetQueryCursor(
    AssetSortOrder SortOrder,
    long Id,
    long? ModifiedAtUtcTicks = null,
    string? FileName = null,
    long? CreatedAtUtcTicks = null,
    long? FileSize = null,
    int? Rating = null,
    string? StatusLabel = null);

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
