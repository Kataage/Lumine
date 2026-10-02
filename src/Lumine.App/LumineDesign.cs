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

    public const double NavigationWidth = 76;
    public const double HeaderHeight = 56;
    public const double CompactControlHeight = 34;
    public const double ContentGap = 12;
    public const double SurfaceRadius = 10;
    public const double StateCardMaxWidth = 560;

    public static readonly IReadOnlyList<string> NavigationLabels =
    [
        "ライブラリ",
        "フォルダー",
        "タグ",
        "公開履歴",
        "設定"
    ];

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

        var stack =
            new StackPanel
            {
                Spacing = 6
            };

        foreach (var label in NavigationLabels)
        {
            var selected =
                string.Equals(
                    label,
                    selectedLabel,
                    StringComparison.Ordinal);

            var button =
                new Button
                {
                    Content =
                        new TextBlock
                        {
                            Text = label,
                            FontSize = 10.5,
                            FontWeight =
                                selected
                                    ? FontWeight.SemiBold
                                    : FontWeight.Normal,
                            Foreground =
                                selected
                                    ? Foreground
                                    : MutedForeground,
                            TextAlignment =
                                TextAlignment.Center,
                            TextWrapping =
                                TextWrapping.Wrap
                        },
                    MinHeight = 46,
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
                    Padding = new Thickness(6, 7),
                    HorizontalContentAlignment =
                        HorizontalAlignment.Stretch
                };
            button.Click +=
                (_, _) => navigate(label);
            stack.Children.Add(button);
        }

        var content =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions("Auto,*,Auto")
            };

        var brand =
            new StackPanel
            {
                Spacing = 5,
                Margin = new Thickness(8, 10, 8, 12)
            };
        brand.Children.Add(
            CreateBrandImage(34));
        brand.Children.Add(
            new TextBlock
            {
                Text = "Lumine",
                Foreground = Foreground,
                FontWeight = FontWeight.Bold,
                FontSize = 11,
                TextAlignment = TextAlignment.Center
            });
        content.Children.Add(brand);

        Grid.SetRow(stack, 1);
        stack.Margin = new Thickness(7, 2);
        content.Children.Add(stack);

        var footer =
            new TextBlock
            {
                Text = "v2",
                Foreground = MutedForeground,
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4, 10)
            };
        Grid.SetRow(footer, 2);
        content.Children.Add(footer);

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
