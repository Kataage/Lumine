using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lumine.Library;

namespace Lumine.App;

internal sealed class ManagedTagPicker : UserControl
{
    private readonly CoreViewerRuntime _runtime;
    private readonly Func<IReadOnlyList<string>, Task> _applyTags;
    private readonly WrapPanel _assignedHost;
    private readonly TextBox _search;
    private readonly TextBlock _summary;
    private readonly StackPanel _candidateHost;
    private readonly Border _createSurface;
    private readonly TextBlock _createLabel;
    private readonly TagColorEditor _colorEditor;
    private readonly Button _create;
    private readonly HashSet<string> _selected =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<LibraryTagInfo> _allTags =
        Array.Empty<LibraryTagInfo>();
    private bool _busy;

    public ManagedTagPicker(
        CoreViewerRuntime runtime,
        Func<IReadOnlyList<string>, Task> applyTags)
    {
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
        _applyTags = applyTags
            ?? throw new ArgumentNullException(nameof(applyTags));

        _assignedHost =
            new WrapPanel();

        _search =
            LumineDesign.ConfigureTextBox(
                new TextBox
                {
                    PlaceholderText = "タグを検索・追加",
                    MinHeight =
                        LumineDesign.CompactCommandHeight,
                    Padding =
                        new Thickness(
                            LumineDesign.Space8,
                            LumineDesign.Space4)
                });
        _search.TextChanged +=
            (_, _) =>
            {
                RenderCandidates();
                UpdateCreateButtonState();
            };

        _summary =
            new TextBlock
            {
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                TextWrapping =
                    TextWrapping.Wrap
            };

        _candidateHost =
            new StackPanel
            {
                Spacing = LumineDesign.Space2
            };

        var candidateScroll =
            new ScrollViewer
            {
                MaxHeight = 184,
                Content = _candidateHost,
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

        _colorEditor =
            new TagColorEditor();
        _colorEditor.StateChanged +=
            (_, _) =>
                UpdateCreateButtonState();

        _create =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "作成して追加",
                    HorizontalAlignment =
                        HorizontalAlignment.Right
                });
        _create.Click +=
            async (_, _) =>
            {
                try
                {
                    await CreateAndAssignAsync();
                }
                catch (Exception exception)
                {
                    _summary.Text =
                        $"タグを作成できませんでした: {exception.Message}";
                }
            };

        var createBody =
            new StackPanel
            {
                Spacing = LumineDesign.Space6
            };
        createBody.Children.Add(_createLabel);
        createBody.Children.Add(_colorEditor);
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

        var candidateSurface =
            new Border
            {
                Background =
                    LumineDesign.Background,
                BorderBrush =
                    LumineDesign.Border,
                BorderThickness =
                    new Thickness(1),
                CornerRadius =
                    new CornerRadius(
                        LumineDesign.ControlRadius),
                Padding =
                    new Thickness(
                        LumineDesign.Space4),
                Child = candidateScroll
            };

        var root =
            new StackPanel
            {
                Spacing = LumineDesign.Space6
            };
        root.Children.Add(_assignedHost);
        root.Children.Add(_search);
        root.Children.Add(_summary);
        root.Children.Add(candidateSurface);
        root.Children.Add(_createSurface);
        Content = root;

        SetSelectedTags(Array.Empty<string>());
        SetInteractionEnabled(false);
        UpdateCreateButtonState();
    }

    internal IReadOnlyList<string> SelectedTags =>
        _selected
            .OrderBy(
                static value => value,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal int AssignedTagCountForSmoke =>
        _selected.Count;

    internal int CandidateCountForSmoke =>
        _candidateHost.Children.Count;

    internal bool CreateSurfaceVisibleForSmoke =>
        _createSurface.IsVisible;

    internal bool ColorValidForSmoke =>
        _colorEditor.IsColorValid;

    internal string? SelectedColorForSmoke =>
        _colorEditor.SelectedColor;

    internal string CustomColorTextForSmoke =>
        _colorEditor.CustomTextForSmoke;

    internal bool CreateButtonEnabledForSmoke =>
        _create.IsEnabled;

    internal void SetSearchForSmoke(
        string value) =>
        _search.Text = value;

    internal void SetCustomColorForSmoke(
        string value) =>
        _colorEditor.SetCustomTextForSmoke(
            value);

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
        _summary.Text = "タグを読み込んでいます…";
        _allTags =
            await _runtime.LibraryService.ListTagsAsync(
                _runtime.Library.Id,
                limit: 512,
                cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        RenderAssigned();
        RenderCandidates();
    }

    internal void SetInteractionEnabled(
        bool enabled)
    {
        IsEnabled = enabled;
        _search.IsEnabled =
            enabled && !_busy;
        _colorEditor.IsEnabled =
            enabled && !_busy;
        UpdateCreateButtonState();
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
        _assignedHost.Children.Clear();

        if (_selected.Count == 0)
        {
            _assignedHost.Children.Add(
                new TextBlock
                {
                    Text = "付与済みタグなし",
                    Foreground =
                        LumineDesign.MutedForeground,
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    Margin =
                        new Thickness(
                            0,
                            LumineDesign.Space2)
                });
            return;
        }

        foreach (var name in SelectedTags)
        {
            var tag =
                _allTags.FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Name,
                            name,
                            StringComparison.OrdinalIgnoreCase));
            var color =
                tag?.Color
                ?? TagColor.Default;

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
                        TagColor.ToBrush(color),
                    VerticalAlignment =
                        VerticalAlignment.Center
                });
            row.Children.Add(
                new TextBlock
                {
                    Text = name,
                    Foreground =
                        LumineDesign.Foreground,
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    VerticalAlignment =
                        VerticalAlignment.Center
                });
            row.Children.Add(
                new TextBlock
                {
                    Text = "×",
                    Foreground =
                        LumineDesign.MutedForeground,
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    VerticalAlignment =
                        VerticalAlignment.Center
                });

            var chip =
                new Button
                {
                    Content = row,
                    MinHeight = 26,
                    Padding =
                        new Thickness(
                            LumineDesign.Space6,
                            LumineDesign.Space2),
                    Background =
                        LumineDesign.AccentMuted,
                    BorderBrush =
                        LumineDesign.BorderStrong,
                    BorderThickness =
                        new Thickness(1),
                    CornerRadius =
                        new CornerRadius(13),
                    Margin =
                        new Thickness(
                            0,
                            0,
                            LumineDesign.Space4,
                            LumineDesign.Space4)
                };
            LumineDesign
                .ConfigureSelectedButtonStateResources(
                    chip);
            ToolTip.SetTip(
                chip,
                $"{name} を外す");

            var current = name;
            chip.Click +=
                async (_, _) =>
                {
                    try
                    {
                        await RemoveAsync(current);
                    }
                    catch (Exception exception)
                    {
                        _summary.Text =
                            $"タグを外せませんでした: {exception.Message}";
                    }
                };
            _assignedHost.Children.Add(chip);
        }
    }

    private void RenderCandidates()
    {
        _candidateHost.Children.Clear();

        var query =
            _search.Text?.Trim()
            ?? string.Empty;
        var candidates =
            _allTags
                .Where(tag =>
                    !_selected.Contains(tag.Name)
                    && (query.Length == 0
                        || tag.Name.Contains(
                            query,
                            StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(
                    static tag => tag.AssetCount)
                .ThenBy(
                    static tag => tag.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        var visible =
            candidates
                .Take(80)
                .ToArray();

        _summary.Text =
            query.Length == 0
                ? $"未付与 {candidates.Length:N0}件"
                : $"「{query}」の候補 {candidates.Length:N0}件";

        foreach (var tag in visible)
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
                        TagColor.ToBrush(tag.Color),
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
                        $"{tag.AssetCount:N0}  ＋",
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
                {
                    try
                    {
                        await ToggleAsync(current);
                    }
                    catch (Exception exception)
                    {
                        _summary.Text =
                            $"タグを追加できませんでした: {exception.Message}";
                    }
                };
            _candidateHost.Children.Add(button);
        }

        var exactExists =
            _allTags.Any(
                tag =>
                    string.Equals(
                        tag.Name,
                        query,
                        StringComparison.OrdinalIgnoreCase));
        _createSurface.IsVisible =
            query.Length > 0
            && !exactExists;
        _createLabel.Text =
            _createSurface.IsVisible
                ? $"「{query}」を新しいタグとして作成"
                : string.Empty;
        UpdateCreateButtonState();

        if (visible.Length == 0
            && !_createSurface.IsVisible)
        {
            _candidateHost.Children.Add(
                new TextBlock
                {
                    Text =
                        query.Length == 0
                            ? "追加できるタグはありません。"
                            : "一致する未付与タグはありません。",
                    Foreground =
                        LumineDesign.MutedForeground,
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    Margin =
                        new Thickness(
                            LumineDesign.Space6)
                });
        }
    }

    private async Task ToggleAsync(
        LibraryTagInfo tag)
    {
        if (_selected.Contains(tag.Name))
        {
            await RemoveAsync(tag.Name);
            return;
        }

        var before = SelectedTags;
        _selected.Add(tag.Name);
        RenderAssigned();
        RenderCandidates();
        await CommitAsync(before);
        _search.Text = string.Empty;
    }

    private async Task RemoveAsync(
        string tag)
    {
        if (_busy
            || !_selected.Contains(tag))
        {
            return;
        }

        var before = SelectedTags;
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

        var before = SelectedTags;
        _busy = true;
        SetBusyVisualState();
        try
        {
            var created =
                await _runtime.LibraryService.CreateTagAsync(
                    _runtime.Library.Id,
                    name,
                    _colorEditor.SelectedColor
                    ?? TagColor.Default);
            _allTags =
                await _runtime.LibraryService.ListTagsAsync(
                    _runtime.Library.Id,
                    limit: 512);
            _selected.Add(created.Name);

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

                throw;
            }

            _search.Text = string.Empty;
            _colorEditor.Reset();
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
        if (_busy)
        {
            return;
        }

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
        _colorEditor.IsEnabled =
            !_busy && IsEnabled;
        UpdateCreateButtonState();
    }

    private void UpdateCreateButtonState()
    {
        var name =
            _search.Text?.Trim();
        _create.IsEnabled =
            !_busy
            && IsEnabled
            && _createSurface.IsVisible
            && !string.IsNullOrWhiteSpace(name)
            && _colorEditor.IsColorValid;
    }
}
