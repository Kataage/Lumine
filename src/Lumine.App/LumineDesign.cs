using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Lumine.Core;

namespace Lumine.App;

internal static class LumineDesign
{
    public static readonly Color BackgroundColor =
        Color.Parse(LumineVisualPalette.Background);
    public static readonly Color SurfaceColor =
        Color.Parse(LumineVisualPalette.Surface);
    public static readonly Color SurfaceRaisedColor =
        Color.Parse(LumineVisualPalette.SurfaceRaised);
    public static readonly Color ControlSurfaceColor =
        Color.Parse(LumineVisualPalette.ControlSurface);
    public static readonly Color ControlHoverColor =
        Color.Parse(LumineVisualPalette.ControlHover);
    public static readonly Color BorderColor =
        Color.Parse(LumineVisualPalette.Border);
    public static readonly Color BorderStrongColor =
        Color.Parse(LumineVisualPalette.BorderStrong);
    public static readonly Color ForegroundColor =
        Color.Parse(LumineVisualPalette.Foreground);
    public static readonly Color MutedForegroundColor =
        Color.Parse(LumineVisualPalette.MutedForeground);
    public static readonly Color AccentColor =
        Color.Parse(LumineVisualPalette.Accent);
    public static readonly Color AccentMutedColor =
        Color.Parse(LumineVisualPalette.AccentMuted);
    public static readonly Color DangerColor =
        Color.Parse(LumineVisualPalette.Danger);
    public static readonly Color WarningColor =
        Color.Parse(LumineVisualPalette.Warning);
    public static readonly Color FocusColor =
        Color.Parse(LumineVisualPalette.Focus);

    public static readonly IBrush Background =
        new SolidColorBrush(BackgroundColor);
    public static readonly IBrush Surface =
        new SolidColorBrush(SurfaceColor);
    public static readonly IBrush SurfaceRaised =
        new SolidColorBrush(SurfaceRaisedColor);
    public static readonly IBrush ControlSurface =
        new SolidColorBrush(ControlSurfaceColor);
    public static readonly IBrush ControlHover =
        new SolidColorBrush(ControlHoverColor);
    public static readonly IBrush Border =
        new SolidColorBrush(BorderColor);
    public static readonly IBrush BorderStrong =
        new SolidColorBrush(BorderStrongColor);
    public static readonly IBrush ModalScrim =
        new SolidColorBrush(
            Color.FromArgb(190, 0, 0, 0));
    public static readonly IBrush Foreground =
        new SolidColorBrush(ForegroundColor);
    public static readonly IBrush MutedForeground =
        new SolidColorBrush(MutedForegroundColor);
    public static readonly IBrush Accent =
        new SolidColorBrush(AccentColor);
    public static readonly IBrush AccentMuted =
        new SolidColorBrush(AccentMutedColor);
    public static readonly IBrush Danger =
        new SolidColorBrush(DangerColor);
    public static readonly IBrush Warning =
        new SolidColorBrush(WarningColor);
    public static readonly IBrush Focus =
        new SolidColorBrush(FocusColor);

    public static FontFamily UiFont { get; } =
        new("Yu Gothic UI, Yu Gothic, Meiryo, Segoe UI");

    public const double NavigationWidth = 64;
    public const double BodyFontSize = 14;
    public const double CaptionFontSize = 12;
    public const double CompactLabelFontSize = 12;
    public const double HeaderHeight = 56;
    public const double CompactControlHeight = 34;
    public const double ContentGap = 12;
    public const double SurfaceRadius = 10;
    public const double StateCardMaxWidth = 560;

    internal sealed record NavigationItem(
        string Label,
        string IconPath);

    // Keep navigation visual and terse. These vector paths intentionally
    // mirror the proven v1 icon metaphors so users can identify destinations
    // before reading their labels.
    public const string LibraryIconPath =
        "M3.75 6.75A2.25 2.25 0 016 4.5h3.879c.621 0 1.216.257 1.641.71l1.21 1.29H18a2.25 2.25 0 012.25 2.25v8.5A2.25 2.25 0 0118 19.5H6a2.25 2.25 0 01-2.25-2.25V6.75z";
    public const string FolderIconPath =
        "M2.25 12.75V12A2.25 2.25 0 014.5 9.75h15A2.25 2.25 0 0121.75 12v.75m-8.25-4.5L17.25 12l-3.75 3.75M17.25 12H3";
    public const string TagIconPath =
        "M9.568 3H5.25A2.25 2.25 0 003 5.25v4.318c0 .597.237 1.17.659 1.591l9.581 9.581c.699.699 1.78.872 2.607.33a18.095 18.095 0 005.223-5.223c.542-.827.369-1.908-.33-2.607L11.16 3.66A2.25 2.25 0 009.568 3z";
    public const string PublicationIconPath =
        "M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625A1.125 1.125 0 004.5 3.375v17.25c0 .621.504 1.125 1.125 1.125h12.75a1.125 1.125 0 001.125-1.125V11.25a9 9 0 00-9-9z";
    public const string SettingsIconPath =
        "M9.594 3.94c.09-.542.56-.94 1.11-.94h2.593c.55 0 1.02.398 1.11.94l.213 1.281c.063.374.313.686.645.87l1.295.747 1.217-.456a1.125 1.125 0 011.37.49l1.296 2.247a1.125 1.125 0 01-.26 1.431l-1.003.827a1.125 1.125 0 000 1.735l1.004.828c.424.35.534.954.26 1.43l-1.298 2.247a1.125 1.125 0 01-1.369.491l-1.217-.456-1.295.748a1.125 1.125 0 00-.645.869l-.213 1.28c-.09.543-.56.941-1.11.941h-2.594c-.55 0-1.02-.398-1.11-.94l-.213-1.281a1.125 1.125 0 00-.644-.87l-1.296-.747-1.217.456a1.125 1.125 0 01-1.369-.49l-1.297-2.247a1.125 1.125 0 01.26-1.431l1.004-.827a1.125 1.125 0 000-1.735l-1.004-.828a1.125 1.125 0 01-.26-1.43l1.297-2.247a1.125 1.125 0 011.37-.491l1.216.456 1.296-.748a1.125 1.125 0 00.644-.869l.214-1.281z M15 12a3 3 0 11-6 0 3 3 0 016 0z";
    public const string ViewIconPath =
        "M3.75 12s3-5.25 8.25-5.25S20.25 12 20.25 12 17.25 17.25 12 17.25 3.75 12 3.75 12z M12 9.5a2.5 2.5 0 100 5 2.5 2.5 0 000-5z";
    public const string InfoIconPath =
        "M12 21a9 9 0 100-18 9 9 0 000 18z M12 10.5v6 M12 7.5h.01";
    public const string BackIconPath =
        "M15.75 5.25L9 12l6.75 6.75";
    public const string CloseIconPath =
        "M6 6l12 12M18 6L6 18";
    public const string ChevronLeftIconPath =
        "M15.75 5.25L9 12l6.75 6.75";
    public const string MoreIconPath =
        "M6.75 12h.01M12 12h.01M17.25 12h.01";
    public const string GridIconPath =
        "M4 4h6v6H4z M14 4h6v6h-6z M4 14h6v6H4z M14 14h6v6h-6z";
    public const string ListIconPath =
        "M4 5h3v3H4z M10 5h10 M4 10.5h3v3H4z M10 10.5h10 M4 16h3v3H4z M10 16h10";

    private static readonly IReadOnlyList<NavigationItem> NavigationItems =
    [
        new("ライブラリ", LibraryIconPath),
        new("フォルダー", FolderIconPath),
        new("タグ", TagIconPath),
        new("公開履歴", PublicationIconPath),
        new("設定", SettingsIconPath)
    ];

    public static IReadOnlyList<string> NavigationLabels { get; } =
        NavigationItems.Select(static item => item.Label).ToArray();

    private static readonly Uri BrandIconUri =
        new("avares://Lumine.App/Assets/appicon.png");

    private static readonly Lazy<Bitmap> BrandBitmap =
        new(
            static () =>
            {
                using var stream =
                    AssetLoader.Open(BrandIconUri);
                return new Bitmap(stream);
            });

    public static Avalonia.Controls.Image CreateBrandImage(
        double size) =>
        new Avalonia.Controls.Image
        {
            Source = BrandBitmap.Value,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

    public static WindowIcon CreateWindowIcon()
    {
        using var stream =
            AssetLoader.Open(BrandIconUri);
        return new WindowIcon(stream);
    }

    public static Button ConfigurePrimaryButton(
        Button button)
    {
        ArgumentNullException.ThrowIfNull(button);

        button.MinHeight = CompactControlHeight;
        button.Padding = new Thickness(14, 7);
        button.FontSize = CaptionFontSize;
        button.CornerRadius = new CornerRadius(8);
        button.Background = Accent;
        button.Foreground =
            new SolidColorBrush(BackgroundColor);
        button.BorderThickness = new Thickness(0);
        button.FontWeight = FontWeight.SemiBold;
        return button;
    }

    public static Button ConfigureSecondaryButton(
        Button button)
    {
        ArgumentNullException.ThrowIfNull(button);

        button.MinHeight = CompactControlHeight;
        button.Padding = new Thickness(12, 7);
        button.FontSize = CaptionFontSize;
        button.CornerRadius = new CornerRadius(8);
        button.Background = AccentMuted;
        button.Foreground = Foreground;
        button.BorderBrush = Border;
        button.BorderThickness = new Thickness(1);
        return button;
    }

    public static TextBox ConfigureTextBox(
        TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        textBox.MinHeight = CompactControlHeight;
        textBox.FontSize = BodyFontSize;
        textBox.Background = ControlSurface;
        textBox.Foreground = Foreground;
        textBox.BorderBrush = Border;
        textBox.BorderThickness = new Thickness(1);
        textBox.Padding = new Thickness(10, 6);
        return textBox;
    }

    public static ComboBox ConfigureComboBox(
        ComboBox comboBox)
    {
        ArgumentNullException.ThrowIfNull(comboBox);

        comboBox.MinHeight = CompactControlHeight;
        comboBox.FontSize = CaptionFontSize;
        comboBox.Background = ControlSurface;
        comboBox.Foreground = Foreground;
        comboBox.BorderBrush = Border;
        comboBox.BorderThickness = new Thickness(1);
        comboBox.Padding = new Thickness(9, 5);
        return comboBox;
    }

    public static CheckBox ConfigureCheckBox(
        CheckBox checkBox)
    {
        ArgumentNullException.ThrowIfNull(checkBox);

        checkBox.FontSize = CaptionFontSize;
        checkBox.Foreground = Foreground;
        checkBox.VerticalAlignment =
            VerticalAlignment.Center;
        return checkBox;
    }

    public static Control CreateStrokeIcon(
        string pathData,
        double size = 19,
        IBrush? brush = null) =>
        new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(pathData),
            Stroke = brush ?? Foreground,
            StrokeThickness = 1.7,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

    public static Button ConfigureIconButton(
        Button button,
        string tooltip,
        bool primary = false,
        string? automationId = null,
        string? acceleratorKey = null)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentException.ThrowIfNullOrWhiteSpace(tooltip);

        button.Width = CompactControlHeight;
        button.Height = CompactControlHeight;
        button.MinWidth = CompactControlHeight;
        button.MinHeight = CompactControlHeight;
        button.Padding = new Thickness(7);
        button.CornerRadius = new CornerRadius(8);
        button.Background =
            primary ? Accent : Brushes.Transparent;
        button.Foreground =
            primary
                ? new SolidColorBrush(BackgroundColor)
                : Foreground;
        button.BorderBrush =
            primary ? Brushes.Transparent : Border;
        button.BorderThickness =
            primary
                ? new Thickness(0)
                : new Thickness(1);
        ToolTip.SetTip(button, tooltip);
        AutomationProperties.SetName(button, tooltip);
        if (!string.IsNullOrWhiteSpace(automationId))
        {
            AutomationProperties.SetAutomationId(
                button,
                automationId);
        }

        if (!string.IsNullOrWhiteSpace(acceleratorKey))
        {
            AutomationProperties.SetAcceleratorKey(
                button,
                acceleratorKey);
        }

        return button;
    }

    public static Border CreateNavigationRail() =>
        CreateNavigationRail(
            "ライブラリ",
            static _ => { });

    public static Border CreateNavigationRail(
        string selectedLabel,
        Action<string> navigate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedLabel);
        ArgumentNullException.ThrowIfNull(navigate);

        Button CreateDestinationButton(NavigationItem item)
        {
            var selected =
                string.Equals(
                    item.Label,
                    selectedLabel,
                    StringComparison.Ordinal);

            var content =
                new StackPanel
                {
                    Spacing = 4,
                    HorizontalAlignment =
                        HorizontalAlignment.Center
                };
            content.Children.Add(
                CreateStrokeIcon(
                    item.IconPath,
                    20,
                    selected
                        ? Foreground
                        : MutedForeground));
            content.Children.Add(
                new TextBlock
                {
                    Text = item.Label,
                    Width = 56,
                    FontSize = CompactLabelFontSize,
                    FontWeight =
                        selected
                            ? FontWeight.SemiBold
                            : FontWeight.Normal,
                    Foreground =
                        selected
                            ? Foreground
                            : MutedForeground,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming =
                        TextTrimming.CharacterEllipsis
                });

            var button =
                new Button
                {
                    Content = content,
                    MinHeight = 58,
                    CornerRadius = new CornerRadius(9),
                    Background =
                        selected
                            ? AccentMuted
                            : Brushes.Transparent,
                    BorderBrush =
                        selected
                            ? Border
                            : Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4, 6),
                    HorizontalContentAlignment =
                        HorizontalAlignment.Center
                };
            ToolTip.SetTip(button, item.Label);
            AutomationProperties.SetName(button, item.Label);
            button.Click +=
                (_, _) => navigate(item.Label);
            return button;
        }

        var stack =
            new StackPanel
            {
                Spacing = 5
            };

        for (var index = 0;
             index < NavigationItems.Count - 1;
             index++)
        {
            stack.Children.Add(
                CreateDestinationButton(
                    NavigationItems[index]));
        }

        var content =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions("Auto,*,Auto")
            };

        var brand =
            new Border
            {
                Height = HeaderHeight,
                Child = CreateBrandImage(30)
            };
        ToolTip.SetTip(brand, "Lumine");
        content.Children.Add(brand);

        Grid.SetRow(stack, 1);
        stack.Margin = new Thickness(5, 5);
        content.Children.Add(stack);

        var settings =
            CreateDestinationButton(
                NavigationItems[^1]);
        settings.Margin = new Thickness(5, 5, 5, 8);
        Grid.SetRow(settings, 2);
        content.Children.Add(settings);

        return new Border
        {
            Width = NavigationWidth,
            Background = Surface,
            BorderBrush = Border,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = content
        };
    }

    public static Control CreateProductState(
        string title,
        string description,
        Control? action = null,
        bool showBrand = false)
    {
        var stack =
            new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch
            };

        if (showBrand)
        {
            stack.Children.Add(
                CreateBrandImage(72));
        }

        stack.Children.Add(
            new TextBlock
            {
                Text = title,
                FontSize = showBrand ? 24 : 18,
                FontWeight = FontWeight.Bold,
                Foreground = Foreground,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

        if (!string.IsNullOrWhiteSpace(
                description))
        {
            stack.Children.Add(
                new TextBlock
                {
                    Text = description,
                    FontSize = BodyFontSize,
                    Foreground = MutedForeground,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18
                });
        }

        if (action is not null)
        {
            action.HorizontalAlignment =
                HorizontalAlignment.Stretch;
            stack.Children.Add(action);
        }

        return new Grid
        {
            Background = Background,
            Children =
            {
                new Border
                {
                    MaxWidth = StateCardMaxWidth,
                    Margin = new Thickness(24),
                    Padding =
                        new Thickness(
                            showBrand ? 36 : 28),
                    CornerRadius =
                        new CornerRadius(SurfaceRadius + 4),
                    Background = Surface,
                    BorderBrush = Border,
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment =
                        HorizontalAlignment.Center,
                    VerticalAlignment =
                        VerticalAlignment.Center,
                    Child = stack
                }
            }
        };
    }
}
