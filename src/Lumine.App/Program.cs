using Avalonia;
using Lumine.Core;
using Lumine.Diagnostics;
using Lumine.Image;
using Lumine.Library;

namespace Lumine.App;

internal static class Program
{
    private static int _windowShutdownDrained;

    internal static AppHost? Host { get; private set; }

    internal static RealLibraryAcceptanceSession? Acceptance { get; private set; }

    internal static CoreResourcePolicy ResourcePolicy =>
        Host?.ResourcePolicy
        ?? CoreResourcePolicy.Default;

    internal static ThumbnailStorageMode ThumbnailStorageMode =>
        Host?.ThumbnailStorageMode
        ?? Lumine.Image.ThumbnailStorageMode.MemoryOnly;

    internal static DiagnosticsSession Diagnostics { get; } =
        DiagnosticsSession.Start();

    [STAThread]
    public static void Main(string[] args)
    {
        AppHost? host = null;

        try
        {
            var productSmokeRequested =
                ProductRuntimeSmoke.IsRequested(args);
            var acceptance =
                RealLibraryAcceptanceSession.TryCreate(args);

            if (productSmokeRequested
                && acceptance is not null)
            {
                throw new ArgumentException(
                    "Product smoke and real-library acceptance modes are mutually exclusive.");
            }

            Acceptance = acceptance;

            var dataPaths =
                productSmokeRequested
                    ? ProductRuntimeSmoke.ResolveDataPaths(args)
                    : acceptance?.DataPaths
                      ?? AppDataPaths.ResolveDefault();

            host =
                AppHost.StartAsync(dataPaths)
                    .GetAwaiter()
                    .GetResult();
            Host = host;

            host.Log.Write(
                "render",
                $"Win32 composition override: {GetWin32CompositionOverrideName()}; rendering override: {GetWin32RenderingOverrideName()}.");

            Volatile.Write(
                ref _windowShutdownDrained,
                0);

            var cleanShutdown = false;

            if (productSmokeRequested)
            {
                ProductRuntimeSmoke.RunAsync(
                        args,
                        host)
                    .GetAwaiter()
                    .GetResult();
                cleanShutdown = true;
            }
            else
            {
                var desktopExitCode =
                    BuildAvaloniaApp()
                        .StartWithClassicDesktopLifetime(args);

                host.Log.Write(
                    "shutdown",
                    "Avalonia desktop lifetime returned to managed Program.Main.");

                if (desktopExitCode != 0)
                {
                    Environment.ExitCode =
                        desktopExitCode;
                }

                cleanShutdown =
                    Volatile.Read(
                        ref _windowShutdownDrained) != 0;
            }

            if (cleanShutdown)
            {
                host.CompleteCleanShutdownAsync()
                    .GetAwaiter()
                    .GetResult();
                host.Log.Write(
                    "shutdown",
                    "Clean shutdown commit completed.");
            }
            else
            {
                host.Log.Write(
                    "shutdown",
                    "Application lifetime ended without a drained MainWindow shutdown; recovery marker retained.");
            }
        }
        catch (AppAlreadyRunningException exception)
        {
            Environment.ExitCode = 3;
            Console.Error.WriteLine(
                exception.Message);
        }
        finally
        {
            Acceptance = null;
            Host = null;

            if (host is not null)
            {
                host.Log.Write(
                    "shutdown",
                    "Disposing AppHost.");
                host.DisposeAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                host.Log.Write(
                    "shutdown",
                    "AppHost disposed.");
            }

            LibraryDatabase.ClearPools();

            host?.Log.Write(
                "shutdown",
                "Draining managed finalizers before libvips shutdown.");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            VipsRuntimePolicy.ShutdownProcessLifetime();

            host?.Log.Write(
                "shutdown",
                "libvips process runtime shut down; module remains pinned until process exit.");
            host?.Log.Write(
                "shutdown",
                "Managed Program.Main teardown completed after database pool clear.");
        }
    }

    internal static void MarkWindowShutdownDrained()
    {
        Interlocked.Exchange(
            ref _windowShutdownDrained,
            1);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder =
            AppBuilder.Configure<App>()
                .UsePlatformDetect();

        if (!OperatingSystem.IsWindows())
        {
            return builder;
        }

        var compositionMode =
            Environment.GetEnvironmentVariable(
                "LUMINE_WIN32_COMPOSITION_MODE");
        var renderingMode =
            Environment.GetEnvironmentVariable(
                "LUMINE_WIN32_RENDERING_MODE");

        var options = new Win32PlatformOptions();
        var overridden = false;

        if (string.Equals(
                compositionMode,
                "RedirectionSurface",
                StringComparison.OrdinalIgnoreCase))
        {
            options.CompositionMode =
            [
                Win32CompositionMode.RedirectionSurface
            ];
            overridden = true;
        }

        if (string.Equals(
                renderingMode,
                "Software",
                StringComparison.OrdinalIgnoreCase))
        {
            options.RenderingMode =
            [
                Win32RenderingMode.Software
            ];
            overridden = true;
        }

        return overridden
            ? builder.With(options)
            : builder;
    }

    private static string GetWin32CompositionOverrideName()
    {
        var mode =
            Environment.GetEnvironmentVariable(
                "LUMINE_WIN32_COMPOSITION_MODE");

        return string.IsNullOrWhiteSpace(mode)
            ? "Default"
            : mode;
    }

    private static string GetWin32RenderingOverrideName()
    {
        var mode =
            Environment.GetEnvironmentVariable(
                "LUMINE_WIN32_RENDERING_MODE");

        return string.IsNullOrWhiteSpace(mode)
            ? "Default"
            : mode;
    }
}
