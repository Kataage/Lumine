using System.Text.Json;
using System.Text.Json.Serialization;
using Lumine.Core;
using Lumine.Image;

namespace Lumine.App;

internal sealed record AppSettingsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } =
        CurrentSchemaVersion;

    public ResourcePolicySettings? ResourcePolicy { get; init; } =
        new();

    public string ThumbnailStorageMode { get; init; } =
        nameof(Lumine.Image.ThumbnailStorageMode.MemoryOnly);

    public string BrowseViewMode { get; init; } =
        "Grid";

    public int BrowseDensity { get; init; } = 1;

    public string BrowseSortOrder { get; init; } =
        nameof(Lumine.Library.AssetSortOrder.ModifiedNewest);
}

internal enum BrowseViewMode
{
    Grid = 0,
    List = 1
}

internal sealed record BrowsePreferences(
    BrowseViewMode ViewMode,
    int Density,
    Lumine.Library.AssetSortOrder SortOrder);

internal static class BrowsePreferenceResolver
{
    public static BrowsePreferences Resolve(
        AppSettingsDocument settings,
        out string? warning)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var messages = new List<string>();

        if (!Enum.TryParse<BrowseViewMode>(
                settings.BrowseViewMode,
                ignoreCase: true,
                out var viewMode)
            || !Enum.IsDefined(viewMode))
        {
            viewMode = BrowseViewMode.Grid;
            messages.Add(
                $"Unsupported browse view mode '{settings.BrowseViewMode}'; Grid was used.");
        }

        var density = settings.BrowseDensity;
        if (density is < 0 or > 2)
        {
            density = 1;
            messages.Add(
                $"Unsupported browse density '{settings.BrowseDensity}'; Standard was used.");
        }

        if (!Enum.TryParse<Lumine.Library.AssetSortOrder>(
                settings.BrowseSortOrder,
                ignoreCase: true,
                out var sortOrder)
            || !Enum.IsDefined(sortOrder))
        {
            sortOrder =
                Lumine.Library.AssetSortOrder.ModifiedNewest;
            messages.Add(
                $"Unsupported browse sort order '{settings.BrowseSortOrder}'; ModifiedNewest was used.");
        }

        warning =
            messages.Count == 0
                ? null
                : string.Join(" ", messages);

        return new BrowsePreferences(
            viewMode,
            density,
            sortOrder);
    }

    public static AppSettingsDocument Apply(
        AppSettingsDocument settings,
        BrowsePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(preferences);

        if (!Enum.IsDefined(preferences.ViewMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferences));
        }

        if (preferences.Density is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferences));
        }

        if (!Enum.IsDefined(preferences.SortOrder))
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferences));
        }

        return settings with
        {
            BrowseViewMode =
                preferences.ViewMode.ToString(),
            BrowseDensity =
                preferences.Density,
            BrowseSortOrder =
                preferences.SortOrder.ToString()
        };
    }
}

internal static class ThumbnailStoragePreference
{
    public const string EnvironmentVariable =
        "LUMINE_THUMBNAIL_STORAGE_MODE";

    public static ThumbnailStorageMode ResolvePersisted(
        AppSettingsDocument settings,
        out string? warning)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (Enum.TryParse<ThumbnailStorageMode>(
                settings.ThumbnailStorageMode,
                ignoreCase: true,
                out var persisted)
            && Enum.IsDefined(persisted))
        {
            warning = null;
            return persisted;
        }

        warning =
            $"Unsupported persisted thumbnail storage mode '{settings.ThumbnailStorageMode}'; MemoryOnly was used.";
        return ThumbnailStorageMode.MemoryOnly;
    }

    public static ThumbnailStorageMode ResolveEffective(
        ThumbnailStorageMode persistedMode)
    {
        if (!Enum.IsDefined(persistedMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(persistedMode),
                persistedMode,
                "Unsupported persisted thumbnail storage mode.");
        }

        var environmentValue =
            Environment.GetEnvironmentVariable(
                EnvironmentVariable);

        if (string.IsNullOrWhiteSpace(
                environmentValue))
        {
            return persistedMode;
        }

        return ParseOrThrow(
            environmentValue,
            "environment override");
    }

    public static string Serialize(
        ThumbnailStorageMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unsupported thumbnail storage mode.");
        }

        return mode.ToString();
    }

    private static ThumbnailStorageMode ParseOrThrow(
        string value,
        string source)
    {
        if (Enum.TryParse<ThumbnailStorageMode>(
                value,
                ignoreCase: true,
                out var mode)
            && Enum.IsDefined(mode))
        {
            return mode;
        }

        throw new InvalidOperationException(
            $"Unsupported thumbnail storage mode '{value}' from {source}. Expected MemoryOnly or PersistentDisk.");
    }
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

        var persistedThumbnailStorageMode =
            ThumbnailStoragePreference.ResolvePersisted(
                settings,
                out _);

        var browse =
            BrowsePreferenceResolver.Resolve(
                settings,
                out _);

        var normalized =
            BrowsePreferenceResolver.Apply(
                settings with
                {
                    SchemaVersion =
                        AppSettingsDocument.CurrentSchemaVersion,
                    ResourcePolicy =
                        settings.ResourcePolicy
                        ?? new ResourcePolicySettings(),
                    ThumbnailStorageMode =
                        ThumbnailStoragePreference.Serialize(
                            persistedThumbnailStorageMode)
                },
                browse);

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
