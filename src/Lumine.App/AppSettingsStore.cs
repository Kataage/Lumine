using System.Text.Json;
using System.Text.Json.Serialization;
using Lumine.Core;

namespace Lumine.App;

internal sealed record AppSettingsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } =
        CurrentSchemaVersion;

    public ResourcePolicySettings? ResourcePolicy { get; init; } =
        new();
}

internal sealed record AppSettingsLoadResult(
    AppSettingsDocument Settings,
    string? Warning);

internal sealed class AppSettingsStore
{
    private readonly string _path;

    public AppSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<AppSettingsLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new AppSettingsLoadResult(
                new AppSettingsDocument(),
                null);
        }

        try
        {
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var settings =
                await JsonSerializer.DeserializeAsync(
                    stream,
                    AppSettingsJsonContext.Default.AppSettingsDocument,
                    cancellationToken).ConfigureAwait(false);

            if (settings is null)
            {
                return Fallback(
                    "Settings file was empty; defaults were used.");
            }

            if (settings.SchemaVersion
                != AppSettingsDocument.CurrentSchemaVersion)
            {
                return Fallback(
                    $"Unsupported settings schema {settings.SchemaVersion}; defaults were used.");
            }

            if (settings.ResourcePolicy is null)
            {
                return new AppSettingsLoadResult(
                    settings with
                    {
                        ResourcePolicy =
                            new ResourcePolicySettings()
                    },
                    "Settings resource policy was null; default resource settings were used.");
            }

            return new AppSettingsLoadResult(
                settings,
                null);
        }
        catch (Exception exception) when (
            exception is JsonException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return Fallback(
                $"Settings could not be loaded ({exception.GetType().Name}); defaults were used.");
        }
    }

    public async Task SaveAsync(
        AppSettingsDocument settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = settings with
        {
            SchemaVersion =
                AppSettingsDocument.CurrentSchemaVersion,
            ResourcePolicy =
                settings.ResourcePolicy
                ?? new ResourcePolicySettings()
        };

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath =
            _path
            + "."
            + Guid.NewGuid().ToString("N")
            + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous
                             | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    normalized,
                    AppSettingsJsonContext.Default.AppSettingsDocument,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(
                    cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(
                temporaryPath,
                _path,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (
                    exception is IOException
                    or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static AppSettingsLoadResult Fallback(
        string warning) =>
        new(
            new AppSettingsDocument(),
            warning);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(AppSettingsDocument))]
[JsonSerializable(typeof(ResourcePolicySettings))]
internal sealed partial class AppSettingsJsonContext
    : JsonSerializerContext
{
}
