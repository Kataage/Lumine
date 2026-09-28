namespace Lumine.Library;

public enum LumineV1ImportDiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public sealed record LumineV1ImportDiagnostic(
    LumineV1ImportDiagnosticSeverity Severity,
    string Code,
    string Message);

public sealed record LumineV1ImportLibraryPreview(
    long SourceLibraryId,
    string Name,
    string RootPath,
    bool SourceEnabled,
    int AssetCount,
    int ImportableAssetCount);

public sealed record LumineV1ImportPreview(
    string SourceDatabasePath,
    string SourceFingerprintSha256,
    long SourceBytes,
    int SourceSchemaVersion,
    IReadOnlyList<LumineV1ImportLibraryPreview> Libraries,
    int AssetCount,
    int ImportableAssetCount,
    int MeaningfulMetadataAssetCount,
    int TagAssignmentCount,
    IReadOnlyList<LumineV1ImportDiagnostic> Diagnostics)
{
    public bool CanImport =>
        Diagnostics.All(static diagnostic =>
            diagnostic.Severity !=
                LumineV1ImportDiagnosticSeverity.Error);
}

public sealed record LumineV1ImportProgress(
    int LibrariesProcessed,
    int AssetsProcessed);

public sealed record LumineV1ImportResult(
    LumineV1ImportPreview Preview,
    bool AlreadyImported,
    int LibrariesImported,
    int LibrariesMatched,
    int AssetsImported,
    int AssetsMatched,
    int MetadataImported,
    int MetadataConflicts,
    int TagAssignmentsImported);

public sealed class LumineV1ImportException
    : InvalidOperationException
{
    public LumineV1ImportException(string message)
        : base(message)
    {
    }

    public LumineV1ImportException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class LumineV1SourceChangedException
    : IOException
{
    public LumineV1SourceChangedException(string message)
        : base(message)
    {
    }
}
