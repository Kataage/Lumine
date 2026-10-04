using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

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

    public static string FromColor(
        Color value) =>
        value.A == byte.MaxValue
            ? $"#{value.R:x2}{value.G:x2}{value.B:x2}"
            : $"#{value.R:x2}{value.G:x2}{value.B:x2}{value.A:x2}";

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
    private readonly ColorPicker _visualPicker;
    private readonly TextBox _custom;
    private readonly TextBlock _validation;
    private readonly WrapPanel _presetHost;
    private string? _selectedColor;
    private bool _suppressCustomTextChanged;
    private bool _suppressVisualPickerChanged;

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

        _visualPicker =
            new ColorPicker
            {
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                MinHeight =
                    LumineDesign.CompactCommandHeight,
                IsAlphaEnabled = true,
                IsAlphaVisible = true,
                IsHexInputVisible = false,
                IsColorPaletteVisible = false,
                IsColorSpectrumVisible = true,
                IsColorSpectrumSliderVisible = true,
                IsColorComponentsVisible = true,
                IsColorModelVisible = true
            };
        AutomationProperties.SetName(
            _visualPicker,
            "タグカラーを視覚的に選択");
        ToolTip.SetTip(
            _visualPicker,
            "スペクトラムやRGB/HSVスライダーから自由に色を選択");

        _custom =
            LumineDesign.ConfigureTextBox(
                new TextBox
                {
                    PlaceholderText = "#RRGGBB",
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
            _visualPicker);
        root.Children.Add(
            new TextBlock
            {
                Text = "HEX",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize
            });
        root.Children.Add(
            customRow);
        root.Children.Add(
            new TextBlock
            {
                Text = "プリセット",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize
            });
        root.Children.Add(
            _presetHost);
        root.Children.Add(
            _validation);
        Content = root;

        _custom.TextChanged +=
            (_, _) =>
            {
                if (!_suppressCustomTextChanged)
                {
                    ApplyCustomText();
                }
            };
        _visualPicker.ColorChanged +=
            (_, args) =>
            {
                if (!_suppressVisualPickerChanged)
                {
                    SetColor(
                        TagColor.FromColor(
                            args.NewColor));
                }
            };
        _visualPicker.AttachedToVisualTree +=
            (_, _) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    RefreshVisualPickerAfterAttach);
            };

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

    internal Color VisualColorForSmoke =>
        _visualPicker.Color;

    internal bool VisualPickerFlyoutOpenForSmoke
    {
        get
        {
            var button =
                _visualPicker
                    .GetVisualDescendants()
                    .OfType<DropDownButton>()
                    .SingleOrDefault();
            return button?.Flyout?.IsOpen
                == true;
        }
    }

    internal bool VisualPickerFlyoutHasSpectrumForSmoke
    {
        get
        {
            var button =
                _visualPicker
                    .GetVisualDescendants()
                    .OfType<DropDownButton>()
                    .SingleOrDefault();
            if (button?.Flyout
                    is not Flyout
                    {
                        Content:
                            Control content
                    })
            {
                return false;
            }

            return content
                .GetVisualDescendants()
                .OfType<
                    Avalonia.Controls.Primitives.ColorSpectrum>()
                .Any(
                    spectrum =>
                        spectrum.IsEffectivelyVisible
                        && spectrum.Bounds.Width > 0
                        && spectrum.Bounds.Height > 0);
        }
    }

    internal void OpenVisualPickerForSmoke()
    {
        _visualPicker.ApplyTemplate();
        var button =
            _visualPicker
                .GetVisualDescendants()
                .OfType<DropDownButton>()
                .SingleOrDefault()
            ?? throw new InvalidOperationException(
                "ColorPicker drop-down button was not realized.");

        if (button.Flyout?.IsOpen
            == true)
        {
            return;
        }

        button.Flyout?.ShowAt(
            button);
    }

    internal void SetVisualColorForSmoke(
        Color value)
    {
        _visualPicker.Color = value;
    }

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

    public void SetColor(
        string value)
    {
        if (!TagColor.TryNormalize(
                value,
                out var normalized))
        {
            normalized =
                TagColor.Default;
        }

        if (!string.Equals(
                _custom.Text,
                normalized,
                StringComparison.Ordinal))
        {
            _suppressCustomTextChanged = true;
            try
            {
                _custom.Text =
                    normalized;
            }
            finally
            {
                _suppressCustomTextChanged = false;
            }
        }

        ApplyNormalizedColor(
            normalized);
    }

    private void RefreshVisualPickerAfterAttach()
    {
        if (!_visualPicker.IsAttachedToVisualTree()
            || _selectedColor is null)
        {
            return;
        }

        var target =
            TagColor.ToColor(
                _selectedColor);
        var pulse =
            target == Colors.Transparent
                ? Colors.White
                : Colors.Transparent;

        _suppressVisualPickerChanged = true;
        try
        {
            _visualPicker.Color =
                pulse;
            _visualPicker.Color =
                target;
        }
        finally
        {
            _suppressVisualPickerChanged = false;
        }
    }

    private void ApplyCustomText()
    {
        if (TagColor.TryNormalize(
                _custom.Text,
                out var normalized))
        {
            if (!string.Equals(
                    _custom.Text,
                    normalized,
                    StringComparison.Ordinal))
            {
                _suppressCustomTextChanged = true;
                try
                {
                    _custom.Text =
                        normalized;
                }
                finally
                {
                    _suppressCustomTextChanged = false;
                }
            }

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
        var color =
            TagColor.ToColor(normalized);
        _preview.Background =
            new SolidColorBrush(color);
        _preview.BorderBrush =
            LumineDesign.BorderStrong;

        if (!_visualPicker.Color.Equals(color))
        {
            _suppressVisualPickerChanged = true;
            try
            {
                _visualPicker.Color =
                    color;
            }
            finally
            {
                _suppressVisualPickerChanged = false;
            }
        }
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
