namespace Lumine.Library;

public static class LibraryFileTypes
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [
            ".jpg", ".jpeg", ".png", ".webp", ".gif",
            ".avif", ".heif", ".heic", ".tif", ".tiff"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static bool IsSupportedPath(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    public static string GetFormat(string path) =>
        Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
}
