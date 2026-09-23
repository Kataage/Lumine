namespace Lumine.Library;

internal static class LibraryPaths
{
    public static string NormalizeRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
    }

    public static string RootKey(string rootPath) =>
        ToKey(NormalizeRoot(rootPath));

    public static string NormalizeRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Asset paths must be relative to the registered library root.", nameof(relativePath));
        }

        var parts = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0 || parts.Any(static part => part is "." or ".."))
        {
            throw new ArgumentException("Asset path contains an invalid relative segment.", nameof(relativePath));
        }

        return string.Join('/', parts);
    }

    public static string RelativePathKey(string relativePath) =>
        NormalizeRelativePath(relativePath).ToUpperInvariant();

    public static string FolderRelativePath(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var separator = normalized.LastIndexOf('/');
        return separator < 0 ? string.Empty : normalized[..separator];
    }

    public static string FolderPathKey(string folderRelativePath) =>
        folderRelativePath.Replace('\\', '/').Trim('/').ToUpperInvariant();

    private static string ToKey(string path) =>
        path.Replace('\\', '/').TrimEnd('/').ToUpperInvariant();
}
