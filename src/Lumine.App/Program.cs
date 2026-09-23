using Avalonia;
using Lumine.Diagnostics;

namespace Lumine.App;

internal static class Program
{
    internal static DiagnosticsSession Diagnostics { get; } = DiagnosticsSession.Start();

    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect();
}
