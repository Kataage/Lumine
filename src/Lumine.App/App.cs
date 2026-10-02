using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Lumine.App;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Resources["Lumine.Background"] = LumineDesign.Background;
        Resources["Lumine.Surface"] = LumineDesign.Surface;
        Resources["Lumine.SurfaceRaised"] = LumineDesign.SurfaceRaised;
        Resources["Lumine.Border"] = LumineDesign.Border;
        Resources["Lumine.Foreground"] = LumineDesign.Foreground;
        Resources["Lumine.MutedForeground"] = LumineDesign.MutedForeground;
        Resources["Lumine.Accent"] = LumineDesign.Accent;
        Resources["Lumine.Focus"] = LumineDesign.Focus;
        Resources["Lumine.Warning"] = LumineDesign.Warning;
        Resources["Lumine.Danger"] = LumineDesign.Danger;

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
                    Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

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
