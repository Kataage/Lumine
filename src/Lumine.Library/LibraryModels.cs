namespace Lumine.Library;

public sealed record LibraryInfo(
    long Id,
    string Name,
    string RootPath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastScanCompletedAtUtc);

public sealed record AssetInfo(
    long Id,
    string AssetKey,
    long LibraryId,
    long? FolderId,
    string RelativePath,
    string FileName,
    string Extension,
    long FileSize,
    DateTimeOffset ModifiedAtUtc,
    int? Width,
    int? Height,
    string? Format);

public readonly record struct AssetCursor(long ModifiedAtUtcTicks, long Id)
{
    public static AssetCursor From(AssetInfo asset) =>
        new(asset.ModifiedAtUtc.UtcDateTime.Ticks, asset.Id);
}

public sealed record AssetPage(
    IReadOnlyList<AssetInfo> Items,
    AssetCursor? NextCursor);

public sealed record AssetUpsert(
    string RelativePath,
    long FileSize,
    DateTimeOffset ModifiedAtUtc,
    int? Width = null,
    int? Height = null,
    string? Format = null);

public sealed record LibraryScanProgress(
    int Discovered,
    int Persisted,
    int Skipped,
    string? CurrentRelativePath);

public sealed record LibraryScanResult(
    int Discovered,
    int Persisted,
    int Skipped,
    bool Completed,
    DateTimeOffset FinishedAtUtc);
