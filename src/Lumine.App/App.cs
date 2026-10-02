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
            if (!string.IsNullOrWhiteSpace(
                    Program.StartupFailureMessage))
            {
                desktop.MainWindow =
                    CreateStartupFailureWindow(
                        Program.StartupFailureMessage);
            }
            else
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
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Avalonia.Controls.Window
        CreateStartupFailureWindow(
            string message)
    {
        var close =
            LumineDesign.ConfigurePrimaryButton(
                new Avalonia.Controls.Button
                {
                    Content = "閉じる"
                });

        var window =
            new Avalonia.Controls.Window
            {
                Title = "Lumine - 起動エラー",
                Icon = LumineDesign.CreateWindowIcon(),
                Width = 560,
                Height = 330,
                MinWidth = 460,
                MinHeight = 260,
                CanResize = true,
                Background = LumineDesign.Background,
                Foreground = LumineDesign.Foreground,
                FontFamily = LumineDesign.UiFont,
                WindowStartupLocation =
                    Avalonia.Controls.WindowStartupLocation.CenterScreen
            };

        close.Click +=
            (_, _) => window.Close();

        window.Content =
            LumineDesign.CreateProductState(
                "Lumineを起動できませんでした",
                message,
                close,
                showBrand: true);

        return window;
    }
}
