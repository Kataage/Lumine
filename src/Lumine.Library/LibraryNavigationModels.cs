namespace Lumine.Library;

public sealed record LibraryCatalogItem(
    long Id,
    string Name,
    string RootPath,
    bool IsEnabled,
    LibraryScanState ScanState,
    long AssetCount,
    DateTimeOffset? LastScanCompletedAtUtc,
    string? SyncError);

public sealed record LibraryFolderInfo(
    long Id,
    string RelativePath,
    int Depth,
    long DirectAssetCount);

public sealed record LibraryTagInfo(
    long Id,
    string Name,
    long AssetCount);
