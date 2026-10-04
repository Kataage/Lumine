using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
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
    public static readonly Color InteractionNeutralColor =
        Color.Parse(LumineVisualPalette.InteractionNeutral);
    public static readonly Color InteractionHoverColor =
        Color.Parse(LumineVisualPalette.InteractionHover);
    public static readonly Color InteractionPressedColor =
        Color.Parse(LumineVisualPalette.InteractionPressed);
    public static readonly Color InteractionSelectedColor =
        Color.Parse(LumineVisualPalette.InteractionSelected);
    public static readonly Color InteractionSelectedHoverColor =
        Color.Parse(LumineVisualPalette.InteractionSelectedHover);
    public static readonly Color InteractionFocusColor =
        Color.Parse(LumineVisualPalette.InteractionFocus);
    public static readonly Color InteractionDisabledColor =
        Color.Parse(LumineVisualPalette.InteractionDisabled);
    public static readonly Color InteractionDangerHoverColor =
        Color.Parse(LumineVisualPalette.InteractionDangerHover);
    public static readonly Color InteractionDangerPressedColor =
        Color.Parse(LumineVisualPalette.InteractionDangerPressed);
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
    public static readonly IBrush InteractionNeutral =
        new SolidColorBrush(InteractionNeutralColor);
    public static readonly IBrush InteractionHover =
        new SolidColorBrush(InteractionHoverColor);
    public static readonly IBrush InteractionPressed =
        new SolidColorBrush(InteractionPressedColor);
    public static readonly IBrush InteractionSelected =
        new SolidColorBrush(InteractionSelectedColor);
    public static readonly IBrush InteractionSelectedHover =
        new SolidColorBrush(InteractionSelectedHoverColor);
    public static readonly IBrush InteractionFocus =
        new SolidColorBrush(InteractionFocusColor);
    public static readonly IBrush InteractionDisabled =
        new SolidColorBrush(InteractionDisabledColor);
    public static readonly IBrush InteractionDangerHover =
        new SolidColorBrush(InteractionDangerHoverColor);
    public static readonly IBrush InteractionDangerPressed =
        new SolidColorBrush(InteractionDangerPressedColor);
    public static readonly IBrush Border =
        new SolidColorBrush(BorderColor);
    public static readonly IBrush BorderStrong =
        new SolidColorBrush(BorderStrongColor);
    public static readonly IBrush ModalScrim =
        new SolidColorBrush(
            Color.Parse(LumineVisualPalette.ModalScrim));
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

    public const double NavigationWidth = 72;
    public static double BodyFontSize =>
        14 * LumineVisualMetrics.TextScaleFactor;
    public static double CaptionFontSize =>
        12 * LumineVisualMetrics.TextScaleFactor;
    public static double CompactLabelFontSize =>
        12 * LumineVisualMetrics.TextScaleFactor;
    public static double DialogTitleFontSize =>
        15 * LumineVisualMetrics.TextScaleFactor;
    public static double EmphasisFontSize =>
        18 * LumineVisualMetrics.TextScaleFactor;
    public static double BrandTitleFontSize =>
        24 * LumineVisualMetrics.TextScaleFactor;
    public static double BodyLineHeight =>
        18 * LumineVisualMetrics.TextScaleFactor;
    public const double Space2 = 2;
    public const double Space4 = 4;
    public const double Space6 = 6;
    public const double Space8 = 8;
    public const double Space12 = 12;
    public const double Space16 = 16;
    public const double Space24 = 24;

    public const double HeaderHeight = 56;
    public const double CompactControlHeight = 34;
    public const double CompactCommandHeight = 32;
    public const double ControlRadius = 8;
    public const double PanelRadius = 10;
    public const double PageGutter = Space24;
    public const double ContentGap = Space12;
    public const double SurfaceRadius = PanelRadius;
    public const double StateCardMaxWidth = 560;
    public const double ReadablePageMaxWidth = 920;

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
    public const string PinIconPath =
        "M14.25 3.75l6 6-2.25 2.25-2.25-.75-3.75 3.75.75 2.25-1.5 1.5-6-6 1.5-1.5 2.25.75 3.75-3.75-.75-2.25 2.25-2.25z M8.25 15.75l-4.5 4.5";
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
        button.Padding =
            new Thickness(Space12, Space6);
        button.FontSize = CaptionFontSize;
        button.CornerRadius =
            new CornerRadius(ControlRadius);
        button.Background = Accent;
        button.Foreground =
            new SolidColorBrush(BackgroundColor);
        button.BorderThickness = new Thickness(0);
        button.FontWeight = FontWeight.SemiBold;
        ConfigurePrimaryButtonStateResources(button);
        return button;
    }

    public static Button ConfigureSecondaryButton(
        Button button)
    {
        ArgumentNullException.ThrowIfNull(button);

        button.MinHeight = CompactControlHeight;
        button.Padding =
            new Thickness(Space12, Space6);
        button.FontSize = CaptionFontSize;
        button.CornerRadius =
            new CornerRadius(ControlRadius);
        button.Background = AccentMuted;
        button.Foreground = Foreground;
        button.BorderBrush = Border;
        button.BorderThickness = new Thickness(1);
        ConfigureNeutralButtonStateResources(button);
        return button;
    }

    public static Button ConfigureDangerButton(
        Button button)
    {
        ArgumentNullException.ThrowIfNull(button);

        button.MinHeight = CompactControlHeight;
        button.Padding =
            new Thickness(Space12, Space6);
        button.FontSize = CaptionFontSize;
        button.CornerRadius =
            new CornerRadius(ControlRadius);
        button.Background =
            new SolidColorBrush(
                DangerColor,
                0.10);
        button.Foreground = Danger;
        button.BorderBrush = Danger;
        button.BorderThickness =
            new Thickness(1);
        button.FontWeight =
            FontWeight.SemiBold;
        ConfigureDangerButtonStateResources(
            button);
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
        textBox.Padding =
            new Thickness(
                Space12,
                Space6);
        ConfigureTextControlStateResources(textBox);
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
        comboBox.Padding =
            new Thickness(
                Space8,
                Space4);
        ConfigureComboBoxStateResources(comboBox);
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
        ConfigureCheckBoxStateResources(checkBox);
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
        button.Padding =
            new Thickness(Space8);
        button.CornerRadius =
            new CornerRadius(ControlRadius);
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
        if (primary)
        {
            ConfigurePrimaryButtonStateResources(button);
        }
        else
        {
            ConfigureNeutralButtonStateResources(button);
        }
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

    internal static void ConfigureNeutralButtonStateResources(
        Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.Resources["ButtonBackgroundPointerOver"] =
            InteractionHover;
        button.Resources["ButtonBorderBrushPointerOver"] =
            BorderStrong;
        button.Resources["ButtonForegroundPointerOver"] =
            Foreground;
        button.Resources["ButtonBackgroundPressed"] =
            InteractionPressed;
        button.Resources["ButtonBorderBrushPressed"] =
            InteractionFocus;
        button.Resources["ButtonForegroundPressed"] =
            Foreground;
        button.Resources["ButtonBackgroundDisabled"] =
            InteractionDisabled;
        button.Resources["ButtonBorderBrushDisabled"] =
            Border;
        button.Resources["ButtonForegroundDisabled"] =
            MutedForeground;
    }

    internal static void ConfigurePrimaryButtonStateResources(
        Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.Resources["ButtonBackgroundPointerOver"] =
            new SolidColorBrush(Color.Parse(LumineVisualPalette.Selection));
        button.Resources["ButtonBorderBrushPointerOver"] =
            Brushes.Transparent;
        button.Resources["ButtonForegroundPointerOver"] =
            Background;
        button.Resources["ButtonBackgroundPressed"] =
            InteractionFocus;
        button.Resources["ButtonBorderBrushPressed"] =
            Brushes.Transparent;
        button.Resources["ButtonForegroundPressed"] =
            Background;
        button.Resources["ButtonBackgroundDisabled"] =
            InteractionDisabled;
        button.Resources["ButtonBorderBrushDisabled"] =
            Border;
        button.Resources["ButtonForegroundDisabled"] =
            MutedForeground;
    }

    internal static void ConfigureSelectedButtonStateResources(
        Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.Resources["ButtonBackgroundPointerOver"] =
            InteractionSelectedHover;
        button.Resources["ButtonBorderBrushPointerOver"] =
            InteractionFocus;
        button.Resources["ButtonForegroundPointerOver"] =
            Foreground;
        button.Resources["ButtonBackgroundPressed"] =
            InteractionSelected;
        button.Resources["ButtonBorderBrushPressed"] =
            InteractionFocus;
        button.Resources["ButtonForegroundPressed"] =
            Foreground;
        button.Resources["ButtonBackgroundDisabled"] =
            InteractionDisabled;
        button.Resources["ButtonBorderBrushDisabled"] =
            Border;
        button.Resources["ButtonForegroundDisabled"] =
            MutedForeground;
    }

    internal static void ConfigureDangerButtonStateResources(
        Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.Resources["ButtonBackgroundPointerOver"] =
            InteractionDangerHover;
        button.Resources["ButtonBorderBrushPointerOver"] =
            InteractionDangerHover;
        button.Resources["ButtonForegroundPointerOver"] =
            Background;
        button.Resources["ButtonBackgroundPressed"] =
            InteractionDangerPressed;
        button.Resources["ButtonBorderBrushPressed"] =
            InteractionDangerPressed;
        button.Resources["ButtonForegroundPressed"] =
            Foreground;
    }

    private static void ConfigureTextControlStateResources(
        TextBox textBox)
    {
        textBox.Resources["TextControlBackgroundPointerOver"] =
            InteractionHover;
        textBox.Resources["TextControlBorderBrushPointerOver"] =
            BorderStrong;
        textBox.Resources["TextControlForegroundPointerOver"] =
            Foreground;
        textBox.Resources["TextControlBackgroundFocused"] =
            InteractionNeutral;
        textBox.Resources["TextControlBorderBrushFocused"] =
            InteractionFocus;
        textBox.Resources["TextControlForegroundFocused"] =
            Foreground;
        textBox.Resources["TextControlBackgroundDisabled"] =
            InteractionDisabled;
        textBox.Resources["TextControlBorderBrushDisabled"] =
            Border;
        textBox.Resources["TextControlForegroundDisabled"] =
            MutedForeground;
        textBox.Resources["TextControlSelectionHighlightColor"] =
            InteractionSelectedHover;
    }

    private static void ConfigureComboBoxStateResources(
        ComboBox comboBox)
    {
        comboBox.Resources["ComboBoxBackgroundPointerOver"] =
            InteractionHover;
        comboBox.Resources["ComboBoxBorderBrushPointerOver"] =
            BorderStrong;
        comboBox.Resources["ComboBoxBackgroundPressed"] =
            InteractionPressed;
        comboBox.Resources["ComboBoxBorderBrushPressed"] =
            InteractionFocus;
        comboBox.Resources["ComboBoxBackgroundBorderBrushFocused"] =
            InteractionFocus;
        comboBox.Resources["ComboBoxForegroundFocused"] =
            Foreground;
        comboBox.Resources["ComboBoxForegroundFocusedPressed"] =
            Foreground;
        comboBox.Resources["ComboBoxBackgroundDisabled"] =
            InteractionDisabled;
        comboBox.Resources["ComboBoxBorderBrushDisabled"] =
            Border;
        comboBox.Resources["ComboBoxForegroundDisabled"] =
            MutedForeground;
    }

    private static void ConfigureCheckBoxStateResources(
        CheckBox checkBox)
    {
        checkBox.Resources["CheckBoxBackgroundUncheckedPointerOver"] =
            InteractionHover;
        checkBox.Resources["CheckBoxBorderBrushUncheckedPointerOver"] =
            BorderStrong;
        checkBox.Resources["CheckBoxBackgroundUncheckedPressed"] =
            InteractionPressed;
        checkBox.Resources["CheckBoxBorderBrushUncheckedPressed"] =
            InteractionFocus;
        checkBox.Resources["CheckBoxCheckBackgroundFillChecked"] =
            InteractionSelected;
        checkBox.Resources["CheckBoxCheckBackgroundStrokeChecked"] =
            InteractionFocus;
        checkBox.Resources["CheckBoxCheckBackgroundFillCheckedPointerOver"] =
            InteractionSelectedHover;
        checkBox.Resources["CheckBoxCheckBackgroundStrokeCheckedPointerOver"] =
            InteractionFocus;
        checkBox.Resources["CheckBoxCheckBackgroundFillCheckedPressed"] =
            InteractionPressed;
        checkBox.Resources["CheckBoxCheckBackgroundStrokeCheckedPressed"] =
            InteractionFocus;
        checkBox.Resources["CheckBoxCheckGlyphForegroundChecked"] =
            Foreground;
        checkBox.Resources["CheckBoxCheckGlyphForegroundCheckedPointerOver"] =
            Foreground;
        checkBox.Resources["CheckBoxCheckGlyphForegroundCheckedPressed"] =
            Foreground;
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

            var destination =
                new StackPanel
                {
                    Spacing = 3,
                    HorizontalAlignment =
                        HorizontalAlignment.Center,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };
            destination.Children.Add(
                CreateStrokeIcon(
                    item.IconPath,
                    20,
                    selected
                        ? Foreground
                        : MutedForeground));
            // At large Windows accessibility text scales, keep the rail
            // icon-first instead of squeezing oversized labels into a narrow
            // destination. Tooltip and automation name keep the full label.
            if (LumineVisualMetrics.TextScaleFactor < 1.75)
            {
                destination.Children.Add(
                    new TextBlock
                    {
                        Text = item.Label,
                        Width = 62,
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
            }

            var content =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions("3,*")
                };
            content.Children.Add(
                new Border
                {
                    Width = 3,
                    Height = 26,
                    CornerRadius =
                        new CornerRadius(2),
                    Background =
                        selected
                            ? Accent
                            : Brushes.Transparent,
                    HorizontalAlignment =
                        HorizontalAlignment.Left,
                    VerticalAlignment =
                        VerticalAlignment.Center
                });
            Grid.SetColumn(
                destination,
                1);
            content.Children.Add(destination);

            var button =
                new Button
                {
                    Content = content,
                    MinHeight = 56,
                    CornerRadius = new CornerRadius(8),
                    Background =
                        selected
                            ? AccentMuted
                            : Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(2, 5, 4, 5),
                    HorizontalContentAlignment =
                        HorizontalAlignment.Stretch
                };
            if (selected)
            {
                ConfigureSelectedButtonStateResources(button);
            }
            else
            {
                ConfigureNeutralButtonStateResources(button);
            }
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
        var destinationButtons =
            new List<Button>(
                NavigationItems.Count);

        for (var index = 0;
             index < NavigationItems.Count - 1;
             index++)
        {
            var destination =
                CreateDestinationButton(
                    NavigationItems[index]);
            destinationButtons.Add(
                destination);
            stack.Children.Add(
                destination);
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
                Child = CreateBrandImage(28)
            };
        ToolTip.SetTip(brand, "Lumine");
        content.Children.Add(brand);

        Grid.SetRow(stack, 1);
        stack.Margin = new Thickness(6, 4);
        content.Children.Add(stack);

        var settings =
            CreateDestinationButton(
                NavigationItems[^1]);
        destinationButtons.Add(
            settings);
        settings.Margin = new Thickness(6, 4, 6, 8);
        Grid.SetRow(settings, 2);
        content.Children.Add(settings);

        for (var index = 0;
             index < destinationButtons.Count;
             index++)
        {
            var currentIndex = index;
            destinationButtons[index].KeyDown +=
                (_, args) =>
                {
                    var target =
                        args.Key switch
                        {
                            Key.Up =>
                                Math.Max(
                                    0,
                                    currentIndex - 1),
                            Key.Down =>
                                Math.Min(
                                    destinationButtons.Count - 1,
                                    currentIndex + 1),
                            Key.Home => 0,
                            Key.End =>
                                destinationButtons.Count - 1,
                            _ => currentIndex
                        };
                    if (target == currentIndex)
                    {
                        return;
                    }

                    destinationButtons[target]
                        .Focus();
                    args.Handled = true;
                };
        }

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
                Spacing = Space12,
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
                FontSize = showBrand
                    ? BrandTitleFontSize
                    : EmphasisFontSize,
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
                    LineHeight = BodyLineHeight
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
                    Margin =
                        new Thickness(PageGutter),
                    Padding =
                        new Thickness(
                            showBrand
                                ? PageGutter + Space8
                                : PageGutter),
                    CornerRadius =
                        new CornerRadius(
                            PanelRadius + Space2),
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
