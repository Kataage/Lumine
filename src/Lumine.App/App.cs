using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Lumine.Core;

namespace Lumine.App;

public sealed class App : Application
{
    public override void Initialize() =>
        ApplyProductTheme(this);

    internal static void ApplyProductTheme(
        Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        LumineVisualMetrics.ConfigureTextScaleFactor(
            WindowsTextScale.Resolve());

        application.RequestedThemeVariant =
            ThemeVariant.Dark;
        application.Resources["Lumine.Background"] =
            LumineDesign.Background;
        application.Resources["Lumine.Surface"] =
            LumineDesign.Surface;
        application.Resources["Lumine.SurfaceRaised"] =
            LumineDesign.SurfaceRaised;
        application.Resources["Lumine.Border"] =
            LumineDesign.Border;
        application.Resources["Lumine.Foreground"] =
            LumineDesign.Foreground;
        application.Resources["Lumine.MutedForeground"] =
            LumineDesign.MutedForeground;
        application.Resources["Lumine.Accent"] =
            LumineDesign.Accent;
        application.Resources["Lumine.Focus"] =
            LumineDesign.Focus;
        application.Resources["Lumine.Warning"] =
            LumineDesign.Warning;
        application.Resources["Lumine.Danger"] =
            LumineDesign.Danger;

        application.Resources["ButtonBackgroundPointerOver"] =
            LumineDesign.InteractionHover;
        application.Resources["ButtonBorderBrushPointerOver"] =
            LumineDesign.BorderStrong;
        application.Resources["ButtonForegroundPointerOver"] =
            LumineDesign.Foreground;
        application.Resources["ButtonBackgroundPressed"] =
            LumineDesign.InteractionPressed;
        application.Resources["ButtonBorderBrushPressed"] =
            LumineDesign.InteractionFocus;
        application.Resources["ButtonForegroundPressed"] =
            LumineDesign.Foreground;
        application.Resources["ButtonBackgroundDisabled"] =
            LumineDesign.InteractionDisabled;
        application.Resources["ButtonBorderBrushDisabled"] =
            LumineDesign.Border;
        application.Resources["ButtonForegroundDisabled"] =
            LumineDesign.MutedForeground;

        application.Resources["TextControlBackgroundPointerOver"] =
            LumineDesign.InteractionHover;
        application.Resources["TextControlBorderBrushPointerOver"] =
            LumineDesign.BorderStrong;
        application.Resources["TextControlForegroundPointerOver"] =
            LumineDesign.Foreground;
        application.Resources["TextControlBackgroundFocused"] =
            LumineDesign.InteractionNeutral;
        application.Resources["TextControlBorderBrushFocused"] =
            LumineDesign.InteractionFocus;
        application.Resources["TextControlForegroundFocused"] =
            LumineDesign.Foreground;
        application.Resources["TextControlBackgroundDisabled"] =
            LumineDesign.InteractionDisabled;
        application.Resources["TextControlBorderBrushDisabled"] =
            LumineDesign.Border;
        application.Resources["TextControlForegroundDisabled"] =
            LumineDesign.MutedForeground;

        application.Resources["ComboBoxBackgroundPointerOver"] =
            LumineDesign.InteractionHover;
        application.Resources["ComboBoxBorderBrushPointerOver"] =
            LumineDesign.BorderStrong;
        application.Resources["ComboBoxBackgroundPressed"] =
            LumineDesign.InteractionPressed;
        application.Resources["ComboBoxBorderBrushPressed"] =
            LumineDesign.InteractionFocus;
        application.Resources["ComboBoxBackgroundBorderBrushFocused"] =
            LumineDesign.InteractionFocus;
        application.Resources["ComboBoxForegroundFocused"] =
            LumineDesign.Foreground;
        application.Resources["ComboBoxForegroundFocusedPressed"] =
            LumineDesign.Foreground;
        application.Resources["ComboBoxBackgroundDisabled"] =
            LumineDesign.InteractionDisabled;
        application.Resources["ComboBoxBorderBrushDisabled"] =
            LumineDesign.Border;
        application.Resources["ComboBoxForegroundDisabled"] =
            LumineDesign.MutedForeground;

        var fluentTheme =
            new FluentTheme();
        fluentTheme.Palettes[ThemeVariant.Dark] =
            new ColorPaletteResources
            {
                Accent = LumineDesign.FocusColor,
                RegionColor =
                    LumineDesign.BackgroundColor
            };
        application.Styles.Add(fluentTheme);
        application.Styles.Add(
            new ColorPickerFluentStyles());
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

    internal static Avalonia.Controls.Window
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
