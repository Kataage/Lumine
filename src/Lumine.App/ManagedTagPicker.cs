using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lumine.Library;

namespace Lumine.App;

internal sealed class ManagedTagPicker : UserControl
{
    private static readonly string[] Palette =
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

    private readonly CoreViewerRuntime _runtime;
    private readonly Func<IReadOnlyList<string>, Task> _applyTags;
    private readonly WrapPanel _assigned;
    private readonly TextBox _search;
    private readonly StackPanel _candidates;
    private readonly TextBlock _candidateSummary;
    private readonly Border _createSurface;
    private readonly TextBlock _createLabel;
    private readonly WrapPanel _palette;
    private readonly Button _create;
    private readonly HashSet<string> _selected =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<LibraryTagInfo> _allTags =
        Array.Empty<LibraryTagInfo>();
    private string _selectedColor = Palette[0];
    private bool _busy;

    public ManagedTagPicker(
        CoreViewerRuntime runtime,
        Func<IReadOnlyList<string>, Task> applyTags)
    {
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
        _applyTags = applyTags
            ?? throw new ArgumentNullException(nameof(applyTags));

        _assigned =
            new WrapPanel
            {
                Spacing = LumineDesign.Space4
            };

        _search =
            LumineDesign.ConfigureTextBox(
                new TextBox
                {
                    PlaceholderText =
                        "タグを検索・追加",
                    MinHeight =
                        LumineDesign.CompactCommandHeight,
                    Padding =
                        new Thickness(
                            LumineDesign.Space8,
                            LumineDesign.Space4)
                });
        _search.TextChanged +=
            (_, _) =>
                RenderCandidates();

        _candidateSummary =
            new TextBlock
            {
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize
            };

        _candidates =
            new StackPanel
            {
                Spacing = LumineDesign.Space2
            };

        var candidatesScroll =
            new ScrollViewer
            {
                MaxHeight = 184,
                Content = _candidates,
                HorizontalScrollBarVisibility =
                    Avalonia.Controls.Primitives
                        .ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility =
                    Avalonia.Controls.Primitives
                        .ScrollBarVisibility.Auto
            };

        _createLabel =
            new TextBlock
            {
                Foreground =
                    LumineDesign.Foreground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                TextWrapping =
                    TextWrapping.Wrap
            };

        _palette =
            new WrapPanel
            {
                Spacing = LumineDesign.Space4
            };
        foreach (var color in Palette)
        {
            var colorValue = color;
            var button =
                new Button
                {
                    Width = 24,
                    Height = 24,
                    MinWidth = 24,
                    MinHeight = 24,
                    Padding = new Thickness(3),
                    CornerRadius =
                        new CornerRadius(12),
                    Background =
                        Brushes.Transparent,
                    BorderThickness =
                        new Thickness(2),
                    Content =
                        new Border
                        {
                            Width = 14,
                            Height = 14,
                            CornerRadius =
                                new CornerRadius(7),
                            Background =
                                new SolidColorBrush(
                                    Color.Parse(color))
                        }
                };
            ToolTip.SetTip(button, color);
            button.Click +=
                (_, _) =>
                {
                    _selectedColor = colorValue;
                    RenderPaletteSelection();
                };
            _palette.Children.Add(button);
        }

        _create =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content =
                        "作成して追加",
                    HorizontalAlignment =
                        HorizontalAlignment.Right
                });
        _create.Click +=
            async (_, _) =>
                await CreateAndAssignAsync();

        var createBody =
            new StackPanel
            {
                Spacing = LumineDesign.Space6
            };
        createBody.Children.Add(_createLabel);
        createBody.Children.Add(_palette);
        createBody.Children.Add(_create);

        _createSurface =
            new Border
            {
                IsVisible = false,
                Background =
                    LumineDesign.SurfaceRaised,
                BorderBrush =
                    LumineDesign.Border,
                BorderThickness =
                    new Thickness(1),
                CornerRadius =
                    new CornerRadius(
                        LumineDesign.ControlRadius),
                Padding =
                    new Thickness(
                        LumineDesign.Space8),
                Child = createBody
            };

        var root =
            new StackPanel
            {
                Spacing = LumineDesign.Space6
            };
        root.Children.Add(_assigned);
        root.Children.Add(_search);
        root.Children.Add(_candidateSummary);
        root.Children.Add(candidatesScroll);
        root.Children.Add(_createSurface);

        Content = root;
        RenderPaletteSelection();
        RenderAssigned();
        RenderCandidates();
    }

    internal IReadOnlyList<string> SelectedTags =>
        _selected
            .OrderBy(
                static value => value,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal int CandidateCountForSmoke =>
        _candidates.Children.Count;

    internal bool CreateSurfaceVisibleForSmoke =>
        _createSurface.IsVisible;

    internal void SetSelectedTags(
        IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        _selected.Clear();
        foreach (var tag in tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                _selected.Add(tag.Trim());
            }
        }

        RenderAssigned();
        RenderCandidates();
    }

    internal async Task RefreshAsync(
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default)
    {
        SetSelectedTags(tags);
        _allTags =
            await _runtime.LibraryService.ListTagsAsync(
                _runtime.Library.Id,
                limit: 512,
                cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        RenderAssigned();
        RenderCandidates();
    }

    internal void SetInteractionEnabled(
        bool enabled)
    {
        IsEnabled = enabled;
        _search.IsEnabled = enabled;
        _create.IsEnabled = enabled && !_busy;
    }

    internal async Task ToggleForSmokeAsync(
        string tag)
    {
        var info =
            _allTags.FirstOrDefault(
                item =>
                    string.Equals(
                        item.Name,
                        tag,
                        StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Unknown tag '{tag}'.");
        await ToggleAsync(info);
    }

    private void RenderAssigned()
    {
        _assigned.Children.Clear();

        if (_selected.Count == 0)
        {
            _assigned.Children.Add(
                new TextBlock
                {
                    Text = "タグなし",
                    Foreground =
                        LumineDesign.MutedForeground,
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    VerticalAlignment =
                        VerticalAlignment.Center
                });
            return;
        }

        foreach (var name in
                 _selected.OrderBy(
                     static value => value,
                     StringComparer.OrdinalIgnoreCase))
        {
            var info =
                _allTags.FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Name,
                            name,
                            StringComparison.OrdinalIgnoreCase));
            var color =
                info?.Color
                ?? "#6366f1";

            var label =
                new TextBlock
                {
                    Text = name,
                    Foreground =
                        LumineDesign.Foreground,
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };
            var close =
                new TextBlock
                {
                    Text = "×",
                    Foreground =
                        LumineDesign.MutedForeground,
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };
            var row =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,
                    Spacing =
                        LumineDesign.Space4
                };
            row.Children.Add(
                new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius =
                        new CornerRadius(4),
                    Background =
                        new SolidColorBrush(
                            Color.Parse(color)),
                    VerticalAlignment =
                        VerticalAlignment.Center
                });
            row.Children.Add(label);
            row.Children.Add(close);

            var chip =
                new Button
                {
                    Content = row,
                    Padding =
                        new Thickness(
                            LumineDesign.Space6,
                            LumineDesign.Space2),
                    MinHeight = 26,
                    Background =
                        LumineDesign.AccentMuted,
                    BorderBrush =
                        LumineDesign.BorderStrong,
                    BorderThickness =
                        new Thickness(1),
                    CornerRadius =
                        new CornerRadius(13)
                };
            ToolTip.SetTip(
                chip,
                $"{name} を外す");
            var current = name;
            chip.Click +=
                async (_, _) =>
                    await RemoveAsync(current);
            _assigned.Children.Add(chip);
        }
    }

    private void RenderCandidates()
    {
        _candidates.Children.Clear();

        var search =
            _search.Text?.Trim()
            ?? string.Empty;
        var candidates =
            _allTags
                .Where(tag =>
                    !_selected.Contains(tag.Name)
                    && (search.Length == 0
                        || tag.Name.Contains(
                            search,
                            StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(
                    static tag => tag.AssetCount)
                .ThenBy(
                    static tag => tag.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Take(80)
                .ToArray();

        _candidateSummary.Text =
            search.Length == 0
                ? $"候補 {candidates.Length:N0}件"
                : $"「{search}」の候補 {candidates.Length:N0}件";

        foreach (var tag in candidates)
        {
            var row =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing =
                        LumineDesign.Space6
                };
            row.Children.Add(
                new Border
                {
                    Width = 9,
                    Height = 9,
                    CornerRadius =
                        new CornerRadius(5),
                    Background =
                        new SolidColorBrush(
                            Color.Parse(tag.Color)),
                    VerticalAlignment =
                        VerticalAlignment.Center
                });
            var label =
                new TextBlock
                {
                    Text = tag.Name,
                    Foreground =
                        LumineDesign.Foreground,
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    TextTrimming =
                        TextTrimming.CharacterEllipsis,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var count =
                new TextBlock
                {
                    Text =
                        tag.AssetCount.ToString("N0"),
                    Foreground =
                        LumineDesign.MutedForeground,
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };
            Grid.SetColumn(count, 2);
            row.Children.Add(count);

            var button =
                LumineDesign.ConfigureSecondaryButton(
                    new Button
                    {
                        Content = row,
                        HorizontalAlignment =
                            HorizontalAlignment.Stretch,
                        HorizontalContentAlignment =
                            HorizontalAlignment.Stretch,
                        MinHeight = 30,
                        Padding =
                            new Thickness(
                                LumineDesign.Space8,
                                LumineDesign.Space4)
                    });
            var current = tag;
            button.Click +=
                async (_, _) =>
                    await ToggleAsync(current);
            _candidates.Children.Add(button);
        }

        var exactExists =
            _allTags.Any(
                tag =>
                    string.Equals(
                        tag.Name,
                        search,
                        StringComparison.OrdinalIgnoreCase));
        _createSurface.IsVisible =
            search.Length > 0
            && !exactExists;
        _createLabel.Text =
            _createSurface.IsVisible
                ? $"「{search}」を新しいタグとして作成"
                : string.Empty;
    }

    private async Task ToggleAsync(
        LibraryTagInfo tag)
    {
        if (_busy)
        {
            return;
        }

        if (_selected.Contains(tag.Name))
        {
            await RemoveAsync(tag.Name);
            return;
        }

        var before =
            SelectedTags;
        _selected.Add(tag.Name);
        RenderAssigned();
        RenderCandidates();
        await CommitAsync(before);
    }

    private async Task RemoveAsync(
        string tag)
    {
        if (_busy
            || !_selected.Contains(tag))
        {
            return;
        }

        var before =
            SelectedTags;
        _selected.Remove(tag);
        RenderAssigned();
        RenderCandidates();
        await CommitAsync(before);
    }

    private async Task CreateAndAssignAsync()
    {
        if (_busy)
        {
            return;
        }

        var name =
            _search.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _busy = true;
        SetBusyVisualState();
        try
        {
            var created =
                await _runtime.LibraryService.CreateTagAsync(
                    _runtime.Library.Id,
                    name,
                    _selectedColor);
            _allTags =
                await _runtime.LibraryService.ListTagsAsync(
                    _runtime.Library.Id,
                    limit: 512);
            _selected.Add(created.Name);
            await _applyTags(SelectedTags);
            _search.Text = string.Empty;
            RenderAssigned();
            RenderCandidates();
        }
        finally
        {
            _busy = false;
            SetBusyVisualState();
        }
    }

    private async Task CommitAsync(
        IReadOnlyList<string> before)
    {
        _busy = true;
        SetBusyVisualState();
        try
        {
            await _applyTags(SelectedTags);
        }
        catch
        {
            _selected.Clear();
            foreach (var tag in before)
            {
                _selected.Add(tag);
            }

            RenderAssigned();
            RenderCandidates();
            throw;
        }
        finally
        {
            _busy = false;
            SetBusyVisualState();
        }
    }

    private void SetBusyVisualState()
    {
        _search.IsEnabled =
            !_busy && IsEnabled;
        _create.IsEnabled =
            !_busy && IsEnabled;
    }

    private void RenderPaletteSelection()
    {
        foreach (var button in
                 _palette.Children
                     .OfType<Button>())
        {
            button.BorderBrush =
                string.Equals(
                    ToolTip.GetTip(button)
                        as string,
                    _selectedColor,
                    StringComparison.Ordinal)
                    ? LumineDesign.InteractionFocus
                    : Brushes.Transparent;
        }
    }
}
