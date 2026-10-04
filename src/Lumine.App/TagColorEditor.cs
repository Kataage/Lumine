using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lumine.App;

internal static class TagColor
{
    public const string Default = "#6366f1";

    public static IReadOnlyList<string> Presets { get; } =
    [
        "#6366f1",
        "#ef4444",
        "#f97316",
        "#eab308",
        "#22c55e",
        "#06b6d4",
        "#3b82f6",
        "#ec4899",
        "#8b5cf6",
        "#71717a"
    ];

    public static bool TryNormalize(
        string? value,
        out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate =
            value.Trim().ToLowerInvariant();
        if (candidate.Length is not (4 or 7 or 9)
            || candidate[0] != '#')
        {
            return false;
        }

        for (var index = 1;
             index < candidate.Length;
             index++)
        {
            if (!Uri.IsHexDigit(candidate[index]))
            {
                return false;
            }
        }

        normalized = candidate;
        return true;
    }

    public static Color ToColor(
        string? value)
    {
        if (!TryNormalize(
                value,
                out var normalized))
        {
            normalized = Default;
        }

        var span =
            normalized.AsSpan(1);

        if (span.Length == 3)
        {
            var red = ExpandNibble(span[0]);
            var green = ExpandNibble(span[1]);
            var blue = ExpandNibble(span[2]);
            return Color.FromRgb(
                red,
                green,
                blue);
        }

        var r =
            Convert.ToByte(
                span[..2].ToString(),
                16);
        var g =
            Convert.ToByte(
                span.Slice(2, 2).ToString(),
                16);
        var b =
            Convert.ToByte(
                span.Slice(4, 2).ToString(),
                16);

        if (span.Length == 6)
        {
            return Color.FromRgb(
                r,
                g,
                b);
        }

        var a =
            Convert.ToByte(
                span.Slice(6, 2).ToString(),
                16);
        return Color.FromArgb(
            a,
            r,
            g,
            b);
    }

    public static IBrush ToBrush(
        string? value) =>
        new SolidColorBrush(
            ToColor(value));

    private static byte ExpandNibble(
        char value)
    {
        var nibble =
            Convert.ToByte(
                value.ToString(),
                16);
        return (byte)(
            nibble * 17);
    }
}

internal sealed class TagColorEditor : UserControl
{
    private readonly Border _preview;
    private readonly TextBox _custom;
    private readonly TextBlock _validation;
    private readonly WrapPanel _presetHost;
    private string? _selectedColor;

    public TagColorEditor(
        string initialColor = TagColor.Default)
    {
        _preview =
            new Border
            {
                Width = 30,
                Height = 30,
                MinWidth = 30,
                MinHeight = 30,
                CornerRadius =
                    new CornerRadius(15),
                BorderBrush =
                    LumineDesign.BorderStrong,
                BorderThickness =
                    new Thickness(1),
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        _custom =
            LumineDesign.ConfigureTextBox(
                new TextBox
                {
                    Watermark = "#RRGGBB",
                    MinHeight =
                        LumineDesign.CompactCommandHeight,
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch
                });
        AutomationProperties.SetName(
            _custom,
            "タグカラーの16進数");
        ToolTip.SetTip(
            _custom,
            "#RGB / #RRGGBB / #RRGGBBAA");

        _validation =
            new TextBlock
            {
                Foreground =
                    LumineDesign.Danger,
                FontSize =
                    LumineDesign.CaptionFontSize,
                TextWrapping =
                    TextWrapping.Wrap,
                IsVisible = false
            };

        _presetHost =
            new WrapPanel
            {
                HorizontalAlignment =
                    HorizontalAlignment.Left
            };

        foreach (var preset in
                 TagColor.Presets)
        {
            var value = preset;
            var button =
                new Button
                {
                    Width = 30,
                    Height = 30,
                    MinWidth = 30,
                    MinHeight = 30,
                    Padding =
                        new Thickness(4),
                    Background =
                        Brushes.Transparent,
                    BorderThickness =
                        new Thickness(2),
                    CornerRadius =
                        new CornerRadius(15),
                    Margin =
                        new Thickness(
                            0,
                            0,
                            LumineDesign.Space4,
                            LumineDesign.Space4),
                    Content =
                        new Border
                        {
                            Width = 18,
                            Height = 18,
                            CornerRadius =
                                new CornerRadius(9),
                            Background =
                                TagColor.ToBrush(value)
                        }
                };
            AutomationProperties.SetName(
                button,
                $"タグカラー {value}");
            ToolTip.SetTip(
                button,
                value);
            button.Click +=
                (_, _) =>
                    SetColor(value);
            _presetHost.Children.Add(
                button);
        }

        var customRow =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "Auto,*"),
                ColumnSpacing =
                    LumineDesign.Space8
            };
        customRow.Children.Add(
            _preview);
        Grid.SetColumn(
            _custom,
            1);
        customRow.Children.Add(
            _custom);

        var root =
            new StackPanel
            {
                Spacing =
                    LumineDesign.Space6
            };
        root.Children.Add(
            new TextBlock
            {
                Text = "カラー",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize
            });
        root.Children.Add(
            customRow);
        root.Children.Add(
            _presetHost);
        root.Children.Add(
            _validation);
        Content = root;

        _custom.TextChanged +=
            (_, _) =>
                ApplyCustomText();

        SetColor(initialColor);
    }

    public event EventHandler? StateChanged;

    public string? SelectedColor =>
        _selectedColor;

    public bool IsColorValid =>
        _selectedColor is not null;

    internal string CustomTextForSmoke =>
        _custom.Text
        ?? string.Empty;

    internal int PresetCountForSmoke =>
        _presetHost.Children.Count;

    internal bool ValidationVisibleForSmoke =>
        _validation.IsVisible;

    internal void SetCustomTextForSmoke(
        string value)
    {
        _custom.Text = value;
        ApplyCustomText();
    }

    internal void SelectPresetForSmoke(
        int index)
    {
        if (index < 0
            || index >= TagColor.Presets.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index));
        }

        SetColor(
            TagColor.Presets[index]);
    }

    public void Reset() =>
        SetColor(
            TagColor.Default);

    private void SetColor(
        string value)
    {
        if (!TagColor.TryNormalize(
                value,
                out var normalized))
        {
            normalized =
                TagColor.Default;
        }

        if (string.Equals(
                _custom.Text,
                normalized,
                StringComparison.Ordinal))
        {
            ApplyNormalizedColor(
                normalized);
            return;
        }

        _custom.Text =
            normalized;
    }

    private void ApplyCustomText()
    {
        if (TagColor.TryNormalize(
                _custom.Text,
                out var normalized))
        {
            ApplyNormalizedColor(
                normalized);
            return;
        }

        _selectedColor = null;
        _preview.Background =
            LumineDesign.ControlSurface;
        _preview.BorderBrush =
            LumineDesign.Danger;
        _validation.Text =
            "カラーは #RGB / #RRGGBB / #RRGGBBAA で入力してください。";
        _validation.IsVisible = true;
        RenderPresetSelection();
        StateChanged?.Invoke(
            this,
            EventArgs.Empty);
    }

    private void ApplyNormalizedColor(
        string normalized)
    {
        _selectedColor = normalized;
        _preview.Background =
            TagColor.ToBrush(normalized);
        _preview.BorderBrush =
            LumineDesign.BorderStrong;
        _validation.Text =
            string.Empty;
        _validation.IsVisible = false;
        RenderPresetSelection();
        StateChanged?.Invoke(
            this,
            EventArgs.Empty);
    }

    private void RenderPresetSelection()
    {
        foreach (var button in
                 _presetHost.Children
                     .OfType<Button>())
        {
            var preset =
                ToolTip.GetTip(button)
                    as string;
            button.BorderBrush =
                _selectedColor is not null
                && string.Equals(
                    preset,
                    _selectedColor,
                    StringComparison.Ordinal)
                    ? LumineDesign.InteractionFocus
                    : Brushes.Transparent;
        }
    }
}
