namespace Lumine.App;

internal enum AppDataLocationKind
{
    LocalUserData = 0,
    Portable = 1,
    Custom = 2
}

internal sealed record AppDataPaths(
    string RootPath,
    string DatabasePath,
    string ThumbnailCachePath,
    string SettingsPath,
    string RuntimeMarkerPath,
    string InstanceLockPath,
    string LogsPath,
    string RuntimeLogPath)
{
    public AppDataLocationKind LocationKind { get; init; } =
        AppDataLocationKind.Custom;

    public bool IsPortable =>
        LocationKind == AppDataLocationKind.Portable;

    public static AppDataPaths Resolve(
        IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Any(static arg =>
                string.Equals(
                    arg,
                    "--portable",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return CreatePortable();
        }

        return ResolveDefault();
    }

    public static AppDataPaths ResolveDefault()
    {
        var overridePath =
            Environment.GetEnvironmentVariable(
                "LUMINE_DATA_DIR");

        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return FromRoot(overridePath) with
            {
                LocationKind =
                    AppDataLocationKind.Custom
            };
        }

        var portableRequested =
            IsTruthy(
                Environment.GetEnvironmentVariable(
                    "LUMINE_PORTABLE"))
            || File.Exists(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "portable.flag"));

        if (portableRequested)
        {
            return CreatePortable();
        }

        var root =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Lumine",
                "v2");

        return FromRoot(root) with
        {
            LocationKind =
                AppDataLocationKind.LocalUserData
        };
    }

    public static AppDataPaths CreatePortable() =>
        FromRoot(
            Path.Combine(
                AppContext.BaseDirectory,
                "data")) with
        {
            LocationKind =
                AppDataLocationKind.Portable
        };

    public static AppDataPaths FromRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var root = Path.GetFullPath(rootPath);
        var logs = Path.Combine(root, "logs");

        return new AppDataPaths(
            root,
            Path.Combine(root, "library.db"),
            Path.Combine(root, "thumbnails"),
            Path.Combine(root, "settings.json"),
            Path.Combine(root, "runtime.unclean"),
            Path.Combine(root, "instance.lock"),
            logs,
            Path.Combine(logs, "runtime.log"));
    }

    public string DescribeLocation() =>
        LocationKind switch
        {
            AppDataLocationKind.Portable =>
                "ポータブル（Lumine.exe 配下）",
            AppDataLocationKind.LocalUserData =>
                "このWindowsユーザーのローカルデータ",
            _ =>
                "カスタム保存先"
        };

    private static bool IsTruthy(
        string? value) =>
        string.Equals(
            value,
            "1",
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            value,
            "true",
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            value,
            "yes",
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            value,
            "on",
            StringComparison.OrdinalIgnoreCase);
}
