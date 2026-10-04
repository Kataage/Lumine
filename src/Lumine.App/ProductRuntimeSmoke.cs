namespace Lumine.App;

internal static class ProductRuntimeSmoke
{
    public const string Switch = "--product-smoke";

    public static bool IsRequested(
        IReadOnlyList<string> args) =>
        args.Any(static arg =>
            string.Equals(
                arg,
                Switch,
                StringComparison.Ordinal));

    public static AppDataPaths ResolveDataPaths(
        IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var explicitDataDirectory =
            GetOptionalValue(
                args,
                "--data-dir=");
        return explicitDataDirectory is null
            ? AppDataPaths.Resolve(args)
            : AppDataPaths.FromRoot(
                explicitDataDirectory) with
            {
                LocationKind =
                    AppDataLocationKind.Custom
            };
    }

    public static async Task RunAsync(
        IReadOnlyList<string> args,
        AppHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        var libraryDirectory = GetRequiredValue(
            args,
            "--library-dir=");

        for (var iteration = 0;
             iteration < 2;
             iteration++)
        {
            await using var runtime =
                await CoreViewerRuntime.OpenAsync(
                    libraryDirectory,
                    host.DataPaths,
                    host.ResourcePolicy,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (runtime.AssetCount < 0)
            {
                throw new InvalidOperationException(
                    "Product runtime returned a negative asset count.");
            }

            if (runtime.ThumbnailCache.ConfiguredByteLimit
                != host.ResourcePolicy.ThumbnailCacheByteLimit)
            {
                throw new InvalidOperationException(
                    "Product runtime did not apply the persisted Core resource policy.");
            }
        }

        var walPath =
            host.DataPaths.DatabasePath + "-wal";
        if (File.Exists(walPath)
            && new FileInfo(walPath).Length != 0)
        {
            throw new InvalidOperationException(
                "Product runtime left a non-empty SQLite WAL after coordinated shutdown.");
        }
    }

    private static string? GetOptionalValue(
        IReadOnlyList<string> args,
        string prefix)
    {
        var value = args
            .FirstOrDefault(arg =>
                arg.StartsWith(
                    prefix,
                    StringComparison.Ordinal));

        if (value is null)
        {
            return null;
        }

        if (value.Length == prefix.Length)
        {
            throw new ArgumentException(
                $"Product smoke requires {prefix}<path> when the option is supplied.");
        }

        return value[prefix.Length..];
    }

    private static string GetRequiredValue(
        IReadOnlyList<string> args,
        string prefix)
    {
        var value = args
            .FirstOrDefault(arg =>
                arg.StartsWith(
                    prefix,
                    StringComparison.Ordinal));

        if (value is null
            || value.Length == prefix.Length)
        {
            throw new ArgumentException(
                $"Product smoke requires {prefix}<path>.");
        }

        return value[prefix.Length..];
    }
}
