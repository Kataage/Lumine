using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Lumine.App;

internal static class LumineDesign
{
    public static readonly Color BackgroundColor =
        Color.Parse("#09090B");
    public static readonly Color SurfaceColor =
        Color.Parse("#0F0F12");
    public static readonly Color SurfaceRaisedColor =
        Color.Parse("#151519");
    public static readonly Color BorderColor =
        Color.Parse("#2B2B31");
    public static readonly Color ForegroundColor =
        Color.Parse("#FAFAFA");
    public static readonly Color MutedForegroundColor =
        Color.Parse("#A1A1AA");
    public static readonly Color AccentColor =
        Color.Parse("#E4E4E7");
    public static readonly Color AccentMutedColor =
        Color.Parse("#25252B");
    public static readonly Color DangerColor =
        Color.Parse("#991B1B");
    public static readonly Color WarningColor =
        Color.Parse("#A16207");
    public static readonly Color FocusColor =
        Color.Parse("#D4D4D8");

    public static readonly IBrush Background =
        new SolidColorBrush(BackgroundColor);
    public static readonly IBrush Surface =
        new SolidColorBrush(SurfaceColor);
    public static readonly IBrush SurfaceRaised =
        new SolidColorBrush(SurfaceRaisedColor);
    public static readonly IBrush Border =
        new SolidColorBrush(BorderColor);
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
        "M3 6.75A2.25 2.25 0 015.25 4.5h4.1c.57 0 1.12.217 1.537.607L12.38 6.5h6.37A2.25 2.25 0 0121 8.75v8A2.25 2.25 0 0118.75 19H5.25A2.25 2.25 0 013 16.75v-10z";
    public const string TagIconPath =
        "M9.568 3H5.25A2.25 2.25 0 003 5.25v4.318c0 .597.237 1.17.659 1.591l9.581 9.581c.699.699 1.78.872 2.607.33a18.095 18.095 0 005.223-5.223c.542-.827.369-1.908-.33-2.607L11.16 3.66A2.25 2.25 0 009.568 3z";
    public const string PublicationIconPath =
        "M6 3.75h7.5L18 8.25v12H6v-16.5z M13.5 3.75v4.5H18 M8.5 13h7 M8.5 16h5";
    public const string SettingsIconPath =
        "M12 8.25a3.75 3.75 0 100 7.5 3.75 3.75 0 000-7.5z M19.4 15a7.8 7.8 0 000-6l1.5-1.2-1.7-2.9-1.9.8a7.9 7.9 0 00-5.2-3l-.3-2.1H8.4l-.3 2.1a7.9 7.9 0 00-5.2 3L1 4.9-.7 7.8.8 9a7.8 7.8 0 000 6l-1.5 1.2L1 19.1l1.9-.8a7.9 7.9 0 005.2 3l.3 2.1h3.4l.3-2.1a7.9 7.9 0 005.2-3l1.9.8 1.7-2.9L19.4 15z";
    public const string ViewIconPath =
        "M3.75 12s3-5.25 8.25-5.25S20.25 12 20.25 12 17.25 17.25 12 17.25 3.75 12 3.75 12z M12 9.5a2.5 2.5 0 100 5 2.5 2.5 0 000-5z";
    public const string InfoIconPath =
        "M12 21a9 9 0 100-18 9 9 0 000 18z M12 10.5v6 M12 7.5h.01";
    public const string BackIconPath =
        "M15.75 5.25L9 12l6.75 6.75";
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
        button.CornerRadius = new CornerRadius(8);
        button.Background = AccentMuted;
        button.Foreground = Foreground;
        button.BorderBrush = Border;
        button.BorderThickness = new Thickness(1);
        return button;
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
        bool primary = false)
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
                    FontSize = 9,
                    FontWeight =
                        selected
                            ? FontWeight.SemiBold
                            : FontWeight.Normal,
                    Foreground =
                        selected
                            ? Foreground
                            : MutedForeground,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap
                });

            var button =
                new Button
                {
                    Content = content,
                    MinHeight = 52,
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

        stack.Children.Add(
            new TextBlock
            {
                Text = description,
                FontSize = 12,
                Foreground = MutedForeground,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19
            });

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
