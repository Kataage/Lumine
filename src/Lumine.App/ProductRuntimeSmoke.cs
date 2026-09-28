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

    public static async Task RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var dataDirectory = GetRequiredValue(
            args,
            "--data-dir=");
        var libraryDirectory = GetRequiredValue(
            args,
            "--library-dir=");

        var dataPaths =
            AppDataPaths.FromRoot(dataDirectory);

        await using var runtime =
            await CoreViewerRuntime.OpenAsync(
                libraryDirectory,
                dataPaths,
                Program.ResourcePolicy,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (runtime.AssetCount < 0)
        {
            throw new InvalidOperationException(
                "Product runtime returned a negative asset count.");
        }
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
