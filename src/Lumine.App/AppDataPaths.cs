namespace Lumine.App;

internal sealed record AppDataPaths(
    string RootPath,
    string DatabasePath,
    string ThumbnailCachePath)
{
    public static AppDataPaths ResolveDefault()
    {
        var overridePath =
            Environment.GetEnvironmentVariable(
                "LUMINE_DATA_DIR");

        var root = !string.IsNullOrWhiteSpace(overridePath)
            ? overridePath
            : Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Lumine",
                "v2");

        return FromRoot(root);
    }

    public static AppDataPaths FromRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var root = Path.GetFullPath(rootPath);
        return new AppDataPaths(
            root,
            Path.Combine(root, "library.db"),
            Path.Combine(root, "thumbnails"));
    }
}
