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
    private readonly Grid _presetHost;
    private readonly Button _advancedToggle;
    private readonly Border _advancedSurface;
    private string? _selectedColor;
    private bool _suppressCustomTextChanged;
    private bool _suppressVisualPickerChanged;

    public TagColorEditor(
        string initialColor = TagColor.Default)
    {
        _preview =
            new Border
            {
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        _preview.Classes.Add(
            "lumine-tag-preview");

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
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "Auto,Auto,Auto,Auto,Auto"),
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,Auto"),
                ColumnSpacing =
                    LumineDesign.Space6,
                RowSpacing =
                    LumineDesign.Space6,
                HorizontalAlignment =
                    HorizontalAlignment.Left
            };

        for (var presetIndex = 0;
             presetIndex < TagColor.Presets.Count;
             presetIndex++)
        {
            var value =
                TagColor.Presets[presetIndex];
            var dot =
                new Border
                {
                    Background =
                        TagColor.ToBrush(value)
                };
            dot.Classes.Add(
                "lumine-tag-swatch-dot");

            var button =
                new Button
                {
                    Content = dot
                };
            button.Classes.Add(
                "lumine-tag-swatch");
            AutomationProperties.SetName(
                button,
                $"タグカラー {value}");
            ToolTip.SetTip(
                button,
                value);
            button.Click +=
                (_, _) =>
                    SetColor(value);
            Grid.SetColumn(
                button,
                presetIndex % 5);
            Grid.SetRow(
                button,
                presetIndex / 5);
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

        var advancedBody =
            new StackPanel
            {
                Spacing =
                    LumineDesign.Space6
            };
        advancedBody.Children.Add(
            _visualPicker);
        advancedBody.Children.Add(
            new TextBlock
            {
                Text = "HEX",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize
            });
        advancedBody.Children.Add(
            customRow);
        advancedBody.Children.Add(
            _validation);

        _advancedSurface =
            new Border
            {
                IsVisible = false,
                Child = advancedBody
            };
        _advancedSurface.Classes.Add(
            "lumine-tag-advanced");

        _advancedToggle =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "カスタム…",
                    HorizontalAlignment =
                        HorizontalAlignment.Left
                });
        AutomationProperties.SetName(
            _advancedToggle,
            "カスタムカラーを開く");
        ToolTip.SetTip(
            _advancedToggle,
            "スペクトラムやHEXで自由に色を指定");
        _advancedToggle.Click +=
            (_, _) =>
                SetAdvancedVisible(
                    !_advancedSurface.IsVisible);

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
            _presetHost);
        root.Children.Add(
            _advancedToggle);
        root.Children.Add(
            _advancedSurface);
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

    internal bool UsesSharedThemeForSmoke =>
        _preview.Classes.Contains(
            "lumine-tag-preview")
        && _presetHost.Children
            .OfType<Button>()
            .All(
                static button =>
                    button.Classes.Contains(
                        "lumine-tag-swatch")
                    && button.Content
                        is Border dot
                    && dot.Classes.Contains(
                        "lumine-tag-swatch-dot"))
        && _advancedSurface.Classes.Contains(
            "lumine-tag-advanced")
        && _advancedToggle.Classes.Contains(
            "lumine-secondary");

    internal double FirstPresetWidthForSmoke =>
        _presetHost.Children
            .OfType<Button>()
            .First()
            .Bounds.Width;

    internal bool PresetGridIsFiveByTwoForSmoke =>
        _presetHost.ColumnDefinitions.Count == 5
        && _presetHost.RowDefinitions.Count == 2
        && _presetHost.Children.Count
            == TagColor.Presets.Count
        && _presetHost.Children
            .OfType<Button>()
            .Select(
                static button =>
                    (
                        Column: Grid.GetColumn(button),
                        Row: Grid.GetRow(button)))
            .Distinct()
            .Count()
            == TagColor.Presets.Count
        && _presetHost.Children
            .OfType<Button>()
            .All(
                static button =>
                    Grid.GetColumn(button) is >= 0 and < 5
                    && Grid.GetRow(button) is >= 0 and < 2);

    internal bool ValidationVisibleForSmoke =>
        _validation.IsVisible;

    internal bool AdvancedVisibleForSmoke =>
        _advancedSurface.IsVisible;

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

    internal bool VisualPickerSpectrumRenderedForSmoke
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

            var spectrum =
                content
                    .GetVisualDescendants()
                    .OfType<
                        Avalonia.Controls.Primitives.ColorSpectrum>()
                    .FirstOrDefault();
            if (spectrum is null)
            {
                return false;
            }

            spectrum.ApplyTemplate();
            var spectrumRectangle =
                spectrum
                    .GetVisualDescendants()
                    .OfType<
                        Avalonia.Controls.Shapes.Rectangle>()
                    .FirstOrDefault(
                        rectangle =>
                            string.Equals(
                                rectangle.Name,
                                "PART_SpectrumRectangle",
                                StringComparison.Ordinal));

            return spectrumRectangle?.Fill
                is ImageBrush;
        }
    }

    internal void OpenVisualPickerForSmoke()
    {
        SetAdvancedVisible(true);
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

    internal void CloseVisualPickerForSmoke()
    {
        var button =
            _visualPicker
                .GetVisualDescendants()
                .OfType<DropDownButton>()
                .SingleOrDefault();
        button?.Flyout?.Hide();
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

    public void Reset()
    {
        SetColor(
            TagColor.Default);
        SetAdvancedVisible(false);
    }

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

    internal void SetAdvancedVisibleForSmoke(
        bool visible) =>
        SetAdvancedVisible(visible);

    private void SetAdvancedVisible(
        bool visible)
    {
        _advancedSurface.IsVisible =
            visible;
        if (visible)
        {
            if (!_advancedToggle.Classes.Contains(
                    "active"))
            {
                _advancedToggle.Classes.Add(
                    "active");
            }
        }
        else
        {
            _advancedToggle.Classes.Remove(
                "active");
        }
        _advancedToggle.Content =
            visible
                ? "カスタムを閉じる"
                : "カスタム…";
        AutomationProperties.SetName(
            _advancedToggle,
            visible
                ? "カスタムカラーを閉じる"
                : "カスタムカラーを開く");
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
        if (!_preview.Classes.Contains(
                "invalid"))
        {
            _preview.Classes.Add(
                "invalid");
        }
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
        _preview.Classes.Remove(
            "invalid");

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
            var selected =
                _selectedColor is not null
                && string.Equals(
                    preset,
                    _selectedColor,
                    StringComparison.Ordinal);
            if (selected)
            {
                if (!button.Classes.Contains(
                        "selected"))
                {
                    button.Classes.Add(
                        "selected");
                }
            }
            else
            {
                button.Classes.Remove(
                    "selected");
            }
        }
    }
}
