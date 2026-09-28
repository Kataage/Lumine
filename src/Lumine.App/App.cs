using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Lumine.App;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window =
                new MainWindow();
            desktop.MainWindow =
                window;

            if (Program.Acceptance is { } acceptance)
            {
                desktop.ShutdownMode =
                    ShutdownMode.OnExplicitShutdown;

                window.Opened +=
                    (_, _) =>
                        acceptance.Start(
                            window,
                            desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
