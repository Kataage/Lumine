using Avalonia;
using Lumine.Core;
using Lumine.Diagnostics;
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
                host.DisposeAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }

            LibraryDatabase.ClearPools();
        }
    }

    internal static void MarkWindowShutdownDrained()
    {
        Interlocked.Exchange(
            ref _windowShutdownDrained,
            1);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect();
}
