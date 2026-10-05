using System.Text;
using Lumine.Core;
using Lumine.Image;

namespace Lumine.App;

internal sealed class AppAlreadyRunningException
    : InvalidOperationException
{
    public AppAlreadyRunningException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class AppHost : IAsyncDisposable
{
    private readonly FileStream _instanceLock;
    private readonly AppSettingsStore _settingsStore;
    private readonly Task _retiredThumbnailCleanup;
    private int _disposed;
    private int _cleanShutdownCompleted;

    private AppHost(
        AppDataPaths dataPaths,
        FileStream instanceLock,
        AppSettingsStore settingsStore,
        AppSettingsDocument settings,
        CoreResourcePolicy resourcePolicy,
        ThumbnailStorageMode thumbnailStorageMode,
        Task retiredThumbnailCleanup,
        bool previousShutdownWasUnclean,
        string? settingsWarning,
        AppEventLog log)
    {
        DataPaths = dataPaths;
        _instanceLock = instanceLock;
        _settingsStore = settingsStore;
        Settings = settings;
        ResourcePolicy = resourcePolicy;
        ThumbnailStorageMode = thumbnailStorageMode;
        _retiredThumbnailCleanup = retiredThumbnailCleanup;
        PreviousShutdownWasUnclean =
            previousShutdownWasUnclean;
        SettingsWarning = settingsWarning;
        Log = log;
    }

    public AppDataPaths DataPaths { get; }

    public AppSettingsDocument Settings { get; private set; }

    public CoreResourcePolicy ResourcePolicy { get; }

    public ThumbnailStorageMode ThumbnailStorageMode { get; private set; }

    public bool PreviousShutdownWasUnclean { get; }

    public string? SettingsWarning { get; }

    public AppEventLog Log { get; }

    public static async Task<AppHost> StartAsync(
        AppDataPaths dataPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataPaths);

        Directory.CreateDirectory(dataPaths.RootPath);

        var instanceLock =
            AcquireInstanceLock(
                dataPaths.InstanceLockPath);
        var log =
            AppEventLog.Create(
                dataPaths.RuntimeLogPath);

        try
        {
            var previousShutdownWasUnclean =
                File.Exists(dataPaths.RuntimeMarkerPath);

            var settingsStore =
                new AppSettingsStore(
                    dataPaths.SettingsPath);
            var loaded =
                await settingsStore.LoadAsync(
                    cancellationToken).ConfigureAwait(false);

            var settings = loaded.Settings;
            var warning = loaded.Warning;
            CoreResourcePolicy resourcePolicy;

            try
            {
                resourcePolicy =
                    CoreResourcePolicy.Resolve(
                        settings.ResourcePolicy);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                warning = AppendWarning(
                    warning,
                    "Persisted resource settings were outside Lumine's bounded policy; defaults were used.");
                log.Write(
                    "settings",
                    exception.Message);

                settings =
                    new AppSettingsDocument
                    {
                        ThumbnailStorageMode =
                            settings.ThumbnailStorageMode,
                        BrowseViewMode =
                            settings.BrowseViewMode,
                        BrowseDensity =
                            settings.BrowseDensity,
                        BrowseSortOrder =
                            settings.BrowseSortOrder,
                        ScanExtensions =
                            settings.ScanExtensions
                    };
                resourcePolicy =
                    CoreResourcePolicy.Resolve(
                        settings.ResourcePolicy);
            }

            var persistedThumbnailStorageMode =
                ThumbnailStoragePreference.ResolvePersisted(
                    settings,
                    out var thumbnailWarning);
            if (!string.IsNullOrWhiteSpace(
                    thumbnailWarning))
            {
                warning = AppendWarning(
                    warning,
                    thumbnailWarning);
            }

            settings = settings with
            {
                ThumbnailStorageMode =
                    ThumbnailStoragePreference.Serialize(
                        persistedThumbnailStorageMode)
            };

            var browsePreferences =
                BrowsePreferenceResolver.Resolve(
                    settings,
                    out var browseWarning);
            if (!string.IsNullOrWhiteSpace(
                    browseWarning))
            {
                warning = AppendWarning(
                    warning,
                    browseWarning);
            }

            settings =
                BrowsePreferenceResolver.Apply(
                    settings,
                    browsePreferences);

            var scanExtensions =
                ScanExtensionPreference.Resolve(
                    settings,
                    out var scanExtensionWarning);
            if (!string.IsNullOrWhiteSpace(
                    scanExtensionWarning))
            {
                warning = AppendWarning(
                    warning,
                    scanExtensionWarning);
            }

            settings =
                ScanExtensionPreference.Apply(
                    settings,
                    scanExtensions);

            var thumbnailStorageMode =
                ThumbnailStoragePreference.ResolveEffective(
                    persistedThumbnailStorageMode);

            var retiredThumbnailCleanup =
                persistedThumbnailStorageMode
                    == Lumine.Image.ThumbnailStorageMode.MemoryOnly
                && thumbnailStorageMode
                    == Lumine.Image.ThumbnailStorageMode.MemoryOnly
                ? RetirePersistentThumbnailCache(
                    dataPaths.ThumbnailCachePath,
                    log)
                : Task.CompletedTask;

            await File.WriteAllTextAsync(
                dataPaths.RuntimeMarkerPath,
                $"pid={Environment.ProcessId}{Environment.NewLine}"
                + $"started_utc={DateTimeOffset.UtcNow:O}{Environment.NewLine}",
                cancellationToken).ConfigureAwait(false);

            log.Write(
                "startup",
                previousShutdownWasUnclean
                    ? "Started after an unclean previous shutdown."
                    : "Started after a clean or first launch.");
            log.Write(
                "image",
                $"Thumbnail storage mode: {thumbnailStorageMode}.");

            if (!string.IsNullOrWhiteSpace(warning))
            {
                log.Write(
                    "settings",
                    warning);
            }

            return new AppHost(
                dataPaths,
                instanceLock,
                settingsStore,
                settings,
                resourcePolicy,
                thumbnailStorageMode,
                retiredThumbnailCleanup,
                previousShutdownWasUnclean,
                warning,
                log);
        }
        catch
        {
            instanceLock.Dispose();
            throw;
        }
    }

    public async Task SaveSettingsAsync(
        ResourcePolicySettings resourcePolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourcePolicy);

        _ = CoreResourcePolicy.Resolve(resourcePolicy);

        var next =
            Settings with
            {
                ResourcePolicy = resourcePolicy
            };

        await _settingsStore.SaveAsync(
            next,
            cancellationToken).ConfigureAwait(false);

        Settings = next;
        Log.Write(
            "settings",
            "Settings saved; resource-policy changes take effect on the next launch.");
    }

    public async Task SaveScanExtensionsAsync(
        IEnumerable<string> extensions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            extensions);

        var next =
            ScanExtensionPreference.Apply(
                Settings,
                extensions);

        await _settingsStore.SaveAsync(
            next,
            cancellationToken).ConfigureAwait(false);

        Settings = next;
        Log.Write(
            "settings",
            "Scan extension preference saved: "
            + string.Join(
                ", ",
                next.ScanExtensions
                ?? Array.Empty<string>()));
    }

    public async Task SaveThumbnailStorageModeAsync(
        ThumbnailStorageMode thumbnailStorageMode,
        CancellationToken cancellationToken = default)
    {
        var serialized =
            ThumbnailStoragePreference.Serialize(
                thumbnailStorageMode);
        var next =
            Settings with
            {
                ThumbnailStorageMode = serialized
            };

        await _settingsStore.SaveAsync(
            next,
            cancellationToken).ConfigureAwait(false);

        Settings = next;
        Log.Write(
            "settings",
            $"Thumbnail storage preference saved as {thumbnailStorageMode}; it takes effect on the next launch.");
    }

    public async Task SaveBrowsePreferencesAsync(
        BrowsePreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var next =
            BrowsePreferenceResolver.Apply(
                Settings,
                preferences);

        await _settingsStore.SaveAsync(
            next,
            cancellationToken).ConfigureAwait(false);

        Settings = next;
        Log.Write(
            "settings",
            $"Browse preferences saved: view={preferences.ViewMode}, density={preferences.Density}, sort={preferences.SortOrder}.");
    }

    public async Task CompleteCleanShutdownAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(
                ref _cleanShutdownCompleted,
                1,
                0) != 0)
        {
            return;
        }

        try
        {
            try
            {
                await _settingsStore.SaveAsync(
                    Settings,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException)
            {
                Log.Write(
                    "settings",
                    $"Settings flush failed during shutdown: {exception.Message}");
            }

            try
            {
                await _retiredThumbnailCleanup
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException)
            {
                Log.Write(
                    "image",
                    $"Retired thumbnail cache cleanup did not complete: {exception.Message}");
            }

            File.Delete(
                DataPaths.RuntimeMarkerPath);
            Log.Write(
                "shutdown",
                "Clean shutdown marker committed.");
        }
        catch
        {
            Interlocked.Exchange(
                ref _cleanShutdownCompleted,
                0);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(
                ref _disposed,
                1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        if (Volatile.Read(
                ref _cleanShutdownCompleted) == 0)
        {
            Log.Write(
                "shutdown",
                "Host disposed without clean-shutdown commit; recovery marker retained.");
        }

        _instanceLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private static Task RetirePersistentThumbnailCache(
        string thumbnailCachePath,
        AppEventLog log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            thumbnailCachePath);
        ArgumentNullException.ThrowIfNull(log);

        var fullPath =
            Path.GetFullPath(
                thumbnailCachePath);
        var parent =
            Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(parent))
        {
            return Task.CompletedTask;
        }

        var retired = new List<string>();

        try
        {
            if (Directory.Exists(fullPath))
            {
                var retiredPath =
                    fullPath
                    + ".retired-"
                    + Guid.NewGuid().ToString("N");
                Directory.Move(
                    fullPath,
                    retiredPath);
                retired.Add(
                    retiredPath);
                log.Write(
                    "image",
                    $"Retired legacy persistent thumbnail cache to '{retiredPath}'.");
            }

            foreach (var path in Directory.EnumerateDirectories(
                         parent,
                         Path.GetFileName(fullPath)
                         + ".retired-*",
                         SearchOption.TopDirectoryOnly))
            {
                if (!retired.Contains(
                        path,
                        StringComparer.OrdinalIgnoreCase))
                {
                    retired.Add(path);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException)
        {
            log.Write(
                "image",
                $"Unable to retire legacy persistent thumbnail cache: {exception.Message}");
            return Task.CompletedTask;
        }

        if (retired.Count == 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(
            () =>
            {
                foreach (var path in retired)
                {
                    try
                    {
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(
                                path,
                                recursive: true);
                        }
                    }
                    catch (Exception exception) when (
                        exception is IOException
                        or UnauthorizedAccessException)
                    {
                        log.Write(
                            "image",
                            $"Unable to delete retired thumbnail cache '{path}': {exception.Message}");
                    }
                }
            });
    }

    private static FileStream AcquireInstanceLock(
        string path)
    {
        try
        {
            var stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                128,
                FileOptions.WriteThrough);

            var payload = Encoding.UTF8.GetBytes(
                $"pid={Environment.ProcessId}{Environment.NewLine}"
                + $"started_utc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");

            stream.SetLength(0);
            stream.Write(
                payload,
                0,
                payload.Length);
            stream.Flush(
                flushToDisk: true);
            stream.Position = 0;
            return stream;
        }
        catch (IOException exception)
            when (IsSharingViolation(exception))
        {
            throw new AppAlreadyRunningException(
                "Another Lumine instance is already using this application data directory.",
                exception);
        }
    }

    private static bool IsSharingViolation(
        IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code is 32 or 33;
    }

    private static string AppendWarning(
        string? current,
        string next) =>
        string.IsNullOrWhiteSpace(current)
            ? next
            : current + " " + next;
}

internal sealed class AppEventLog
{
    private const long MaxLogBytes =
        2L * 1024 * 1024;
    private const int RetainedLogFiles = 3;

    private readonly object _gate = new();
    private readonly bool _enabled;

    private AppEventLog(
        string path,
        bool enabled)
    {
        Path = path;
        _enabled = enabled;
    }

    public string Path { get; }

    public static AppEventLog Create(
        string path)
    {
        try
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            var directory =
                System.IO.Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var log =
                new AppEventLog(
                    fullPath,
                    true);
            log.RotateIfNeeded();
            return log;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException)
        {
            return new AppEventLog(
                System.IO.Path.GetFullPath(path),
                false);
        }
    }

    public void Write(
        string category,
        string message)
    {
        if (!_enabled)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                RotateIfNeeded();

                var normalized =
                    message
                        .Replace(
                            '\r',
                            ' ')
                        .Replace(
                            '\n',
                            ' ');

                File.AppendAllText(
                    Path,
                    $"{DateTimeOffset.UtcNow:O} [{category}] {normalized}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException)
            {
            }
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(Path)
            || new FileInfo(Path).Length < MaxLogBytes)
        {
            return;
        }

        for (var index = RetainedLogFiles - 1;
             index >= 1;
             index--)
        {
            var source =
                index == 1
                    ? Path
                    : Path + "." + (index - 1);
            var destination =
                Path + "." + index;

            if (File.Exists(source))
            {
                File.Move(
                    source,
                    destination,
                    overwrite: true);
            }
        }
    }
}
