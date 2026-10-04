using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lumine.App;

internal enum ProductDialogTone
{
    Default = 0,
    Danger = 1
}

internal static class ProductDialogs
{
    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string description,
        string? detail = null,
        string confirmLabel = "実行",
        string cancelLabel = "キャンセル",
        ProductDialogTone tone = ProductDialogTone.Default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var dialog =
            CreateDialog(
                title,
                height:
                    string.IsNullOrWhiteSpace(detail)
                        ? 250
                        : 340);

        var panel =
            CreateContent(
                title,
                description,
                detail,
                tone);

        var cancel =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = cancelLabel
                });
        cancel.Click +=
            (_, _) => dialog.Close(false);

        var confirm =
            tone == ProductDialogTone.Danger
                ? LumineDesign.ConfigureSecondaryButton(
                    new Button
                    {
                        Content = confirmLabel,
                        Foreground = LumineDesign.Danger
                    })
                : LumineDesign.ConfigurePrimaryButton(
                    new Button
                    {
                        Content = confirmLabel
                    });
        confirm.Click +=
            (_, _) => dialog.Close(true);

        panel.Children.Add(
            CreateButtons(
                cancel,
                confirm));
        dialog.Content = panel;

        var focusReturn =
            owner.FocusManager?.GetFocusedElement()
                as Control;
        var result =
            await dialog.ShowDialog<bool>(owner);
        focusReturn?.Focus();
        return result;
    }

    public static async Task NotifyAsync(
        Window owner,
        string title,
        string description,
        string? detail = null,
        string buttonLabel = "OK",
        ProductDialogTone tone = ProductDialogTone.Default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var dialog =
            CreateDialog(
                title,
                height:
                    string.IsNullOrWhiteSpace(detail)
                        ? 240
                        : 330);

        var panel =
            CreateContent(
                title,
                description,
                detail,
                tone);

        var close =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = buttonLabel
                });
        close.Click +=
            (_, _) => dialog.Close();

        panel.Children.Add(
            CreateButtons(close));
        dialog.Content = panel;

        var focusReturn =
            owner.FocusManager?.GetFocusedElement()
                as Control;
        await dialog.ShowDialog(owner);
        focusReturn?.Focus();
    }

    internal static Size ResolveDialogSizeForSmoke(
        double baseHeight)
    {
        var scale =
            Math.Clamp(
                LumineVisualMetrics.TextScaleFactor,
                1,
                2.25);
        return new Size(
            Math.Min(
                640,
                500
                + ((scale - 1) * 120)),
            Math.Min(
                520,
                baseHeight
                + ((scale - 1) * 120)));
    }

    private static Window CreateDialog(
        string title,
        double height)
    {
        var resolved =
            ResolveDialogSizeForSmoke(height);
        return new Window
        {
            Title = title,
            Icon = LumineDesign.CreateWindowIcon(),
            Width = resolved.Width,
            Height = resolved.Height,
            MinWidth = 380,
            MinHeight = 220,
            MaxWidth = 680,
            MaxHeight = 540,
            CanResize = true,
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Background = LumineDesign.Background,
            Foreground = LumineDesign.Foreground,
            FontFamily = LumineDesign.UiFont
        };
    }

    private static StackPanel CreateContent(
        string title,
        string description,
        string? detail,
        ProductDialogTone tone)
    {
        var panel =
            new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12
            };

        panel.Children.Add(
            new Border
            {
                Width = 40,
                Height = 40,
                HorizontalAlignment =
                    HorizontalAlignment.Left,
                Background =
                    tone == ProductDialogTone.Danger
                        ? new SolidColorBrush(
                            LumineDesign.DangerColor,
                            0.12)
                        : LumineDesign.AccentMuted,
                CornerRadius = new CornerRadius(10),
                Child =
                    new TextBlock
                    {
                        Text =
                            tone == ProductDialogTone.Danger
                                ? "!"
                                : "i",
                        Foreground =
                            tone == ProductDialogTone.Danger
                                ? LumineDesign.Danger
                                : LumineDesign.Accent,
                        FontWeight = FontWeight.Bold,
                        FontSize = LumineDesign.EmphasisFontSize,
                        HorizontalAlignment =
                            HorizontalAlignment.Center,
                        VerticalAlignment =
                            VerticalAlignment.Center
                    }
            });

        panel.Children.Add(
            new TextBlock
            {
                Text = title,
                Foreground = LumineDesign.Foreground,
                FontWeight = FontWeight.SemiBold,
                FontSize = LumineDesign.DialogTitleFontSize,
                TextWrapping = TextWrapping.Wrap
            });

        panel.Children.Add(
            new TextBlock
            {
                Text = description,
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize,
                LineHeight = LumineDesign.BodyLineHeight,
                TextWrapping = TextWrapping.Wrap
            });

        if (!string.IsNullOrWhiteSpace(detail))
        {
            panel.Children.Add(
                new Border
                {
                    Background =
                        LumineDesign.SurfaceRaised,
                    BorderBrush = LumineDesign.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10),
                    MaxHeight =
                        Math.Min(
                            180,
                            120
                            * Math.Max(
                                1,
                                LumineVisualMetrics.TextScaleFactor)),
                    Child =
                        new ScrollViewer
                        {
                            Content =
                                new TextBlock
                                {
                                    Text = detail,
                                    Foreground =
                                        LumineDesign.MutedForeground,
                                    FontSize = LumineDesign.CaptionFontSize,
                                    TextWrapping =
                                        TextWrapping.Wrap
                                }
                        }
                });
        }

        return panel;
    }

    private static StackPanel CreateButtons(
        params Button[] buttons)
    {
        var panel =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 6, 0, 0)
            };

        foreach (var button in buttons)
        {
            panel.Children.Add(button);
        }

        return panel;
    }
}
