using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lumine.Library;

namespace Lumine.App;

internal sealed class AssetTagPickerControl : UserControl
{
    private const int MaxVisibleCandidates = 80;

    private readonly CoreViewerRuntime _runtime;
    private readonly Func<Task>? _metadataChanged;
    private readonly WrapPanel _assignedHost;
    private readonly TextBox _search;
    private readonly TextBox _newColor;
    private readonly Button _create;
    private readonly TextBlock _candidateSummary;
    private readonly StackPanel _candidateHost;
    private readonly TextBlock _status;
    private IReadOnlyList<LibraryTagInfo> _allTags =
        Array.Empty<LibraryTagInfo>();
    private readonly List<string> _assigned = [];
    private long _assetId;
    private bool _busy;

    public AssetTagPickerControl(
        CoreViewerRuntime runtime,
        Func<Task>? metadataChanged = null)
    {
        _runtime =
            runtime
            ?? throw new ArgumentNullException(nameof(runtime));
        _metadataChanged = metadataChanged;

        _assignedHost =
            new WrapPanel
            {
                Orientation = Orientation.Horizontal
            };

        _search =
            LumineDesign.ConfigureTextBox(
                new TextBox
                {
                    PlaceholderText = "タグを検索・追加…"
                });
        _search.TextChanged +=
            (_, _) => RenderCandidates();

        _newColor =
            LumineDesign.ConfigureTextBox(
                new TextBox
                {
                    Text = "#6366f1",
                    Width = 96
                });

        _create =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    IsVisible = false,
                    MinHeight =
                        LumineDesign.CompactCommandHeight
                });
        _create.Click +=
            async (_, _) =>
                await CreateAndAssignAsync();

        _candidateSummary =
            new TextBlock
            {
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize
            };

        _candidateHost =
            new StackPanel
            {
                Spacing = LumineDesign.Space2
            };

        _status =
            new TextBlock
            {
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                TextWrapping =
                    TextWrapping.Wrap
            };

        var searchRow =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto"),
                ColumnSpacing =
                    LumineDesign.Space6
            };
        searchRow.Children.Add(_search);
        Grid.SetColumn(_newColor, 1);
        searchRow.Children.Add(_newColor);

        var candidates =
            new Border
            {
                BorderBrush = LumineDesign.Border,
                BorderThickness = new Thickness(1),
                CornerRadius =
                    new CornerRadius(
                        LumineDesign.ControlRadius),
                Background =
                    LumineDesign.Background,
                Padding =
                    new Thickness(
                        LumineDesign.Space4),
                Child =
                    new ScrollViewer
                    {
                        MaxHeight = 180,
                        VerticalScrollBarVisibility =
                            Avalonia.Controls.Primitives
                                .ScrollBarVisibility.Auto,
                        Content = _candidateHost
                    }
            };

        var root =
            new StackPanel
            {
                Spacing = LumineDesign.Space6
            };
        root.Children.Add(_assignedHost);
        root.Children.Add(searchRow);
        root.Children.Add(_create);
        root.Children.Add(_candidateSummary);
        root.Children.Add(candidates);
        root.Children.Add(_status);
        Content = root;

        Reset();
    }

    public IReadOnlyList<string> AssignedTags =>
        _assigned.ToArray();

    internal int AssignedTagCountForSmoke =>
        _assigned.Count;

    internal int CandidateCountForSmoke =>
        _candidateHost.Children.Count;

    public async Task SetAssetAsync(
        long assetId,
        IReadOnlyList<string> assignedTags,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(assetId);
        ArgumentNullException.ThrowIfNull(assignedTags);

        _assetId = assetId;
        _assigned.Clear();
        _assigned.AddRange(
            assignedTags
                .Where(
                    static tag =>
                        !string.IsNullOrWhiteSpace(tag))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase));
        _assigned.Sort(
            StringComparer.CurrentCultureIgnoreCase);

        _status.Text = "タグを読み込んでいます…";
        _allTags =
            await _runtime.LibraryService.ListTagsAsync(
                _runtime.Library.Id,
                cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_assetId != assetId)
        {
            return;
        }

        _status.Text = string.Empty;
        Render();
    }

    public void SetAssignedTags(
        IReadOnlyList<string> assignedTags)
    {
        ArgumentNullException.ThrowIfNull(assignedTags);
        _assigned.Clear();
        _assigned.AddRange(
            assignedTags
                .Where(
                    static tag =>
                        !string.IsNullOrWhiteSpace(tag))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase));
        _assigned.Sort(
            StringComparer.CurrentCultureIgnoreCase);
        RenderAssigned();
        RenderCandidates();
    }

    public void Reset()
    {
        _assetId = 0;
        _allTags = Array.Empty<LibraryTagInfo>();
        _assigned.Clear();
        _search.Text = string.Empty;
        _newColor.Text = "#6366f1";
        _status.Text = string.Empty;
        Render();
        IsEnabled = false;
    }

    private void Render()
    {
        RenderAssigned();
        RenderCandidates();
    }

    private void RenderAssigned()
    {
        _assignedHost.Children.Clear();

        if (_assigned.Count == 0)
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
                            LumineDesign.Space2,
                            0,
                            LumineDesign.Space2)
                });
            return;
        }

        foreach (var name in _assigned)
        {
            var tag =
                _allTags.FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Name,
                            name,
                            StringComparison.OrdinalIgnoreCase));

            var content =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,
                    Spacing =
                        LumineDesign.Space4
                };
            content.Children.Add(
                new Border
                {
                    Width = 9,
                    Height = 9,
                    CornerRadius =
                        new CornerRadius(5),
                    Background =
                        ResolveTagBrush(tag?.Color),
                    VerticalAlignment =
                        VerticalAlignment.Center
                });
            content.Children.Add(
                new TextBlock
                {
                    Text = name,
                    VerticalAlignment =
                        VerticalAlignment.Center
                });
            content.Children.Add(
                new TextBlock
                {
                    Text = "×",
                    Foreground =
                        LumineDesign.MutedForeground,
                    VerticalAlignment =
                        VerticalAlignment.Center
                });

            var chip =
                LumineDesign.ConfigureSelectedButtonStateResources(
                    new Button
                    {
                        Content = content,
                        MinHeight = 26,
                        Padding =
                            new Thickness(
                                LumineDesign.Space8,
                                LumineDesign.Space4),
                        Margin =
                            new Thickness(
                                0,
                                0,
                                LumineDesign.Space4,
                                LumineDesign.Space4),
                        CornerRadius =
                            new CornerRadius(13),
                        Background =
                            LumineDesign.AccentMuted,
                        BorderBrush =
                            LumineDesign.BorderStrong,
                        BorderThickness =
                            new Thickness(1),
                        FontSize =
                            LumineDesign.CaptionFontSize
                    });
            var current = name;
            chip.Click +=
                async (_, _) =>
                    await RemoveAsync(current);
            _assignedHost.Children.Add(chip);
        }
    }

    private void RenderCandidates()
    {
        _candidateHost.Children.Clear();

        var query =
            _search.Text?.Trim()
            ?? string.Empty;
        var assigned =
            new HashSet<string>(
                _assigned,
                StringComparer.OrdinalIgnoreCase);

        var candidates =
            _allTags
                .Where(
                    tag =>
                        !assigned.Contains(tag.Name)
                        && (query.Length == 0
                            || tag.Name.Contains(
                                query,
                                StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(
                    static tag => tag.AssetCount)
                .ThenBy(
                    static tag => tag.Name,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        var visible =
            candidates
                .Take(MaxVisibleCandidates)
                .ToArray();

        _candidateSummary.Text =
            query.Length == 0
                ? $"未付与 {candidates.Length:N0}件"
                : $"候補 {candidates.Length:N0}件";

        foreach (var tag in visible)
        {
            var row =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "Auto,*,Auto"),
                    ColumnSpacing =
                        LumineDesign.Space6
                };
            row.Children.Add(
                new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius =
                        new CornerRadius(5),
                    Background =
                        ResolveTagBrush(tag.Color),
                    VerticalAlignment =
                        VerticalAlignment.Center
                });
            var label =
                new TextBlock
                {
                    Text = tag.Name,
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
                    Text = $"{tag.AssetCount:N0}  ＋",
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
            var current = tag.Name;
            button.Click +=
                async (_, _) =>
                    await AddAsync(current);
            _candidateHost.Children.Add(button);
        }

        var exact =
            _allTags.Any(
                tag =>
                    string.Equals(
                        tag.Name,
                        query,
                        StringComparison.OrdinalIgnoreCase));
        _create.IsVisible =
            query.Length > 0
            && !exact;
        _create.Content =
            _create.IsVisible
                ? $"＋「{query}」を作成して付与"
                : string.Empty;

        if (visible.Length == 0
            && !_create.IsVisible)
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

    private async Task AddAsync(
        string tag)
    {
        if (_busy
            || _assetId <= 0
            || _assigned.Contains(
                tag,
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var next =
            _assigned
                .Append(tag)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    static value => value,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        await PersistAsync(next);
        _search.Text = string.Empty;
    }

    private async Task RemoveAsync(
        string tag)
    {
        if (_busy
            || _assetId <= 0)
        {
            return;
        }

        var next =
            _assigned
                .Where(
                    value =>
                        !string.Equals(
                            value,
                            tag,
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();
        await PersistAsync(next);
    }

    private async Task CreateAndAssignAsync()
    {
        if (_busy
            || _assetId <= 0)
        {
            return;
        }

        var name =
            _search.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await ExecuteBusyAsync(
            async () =>
            {
                var created =
                    await _runtime.LibraryService.CreateTagAsync(
                        _runtime.Library.Id,
                        name,
                        string.IsNullOrWhiteSpace(
                            _newColor.Text)
                            ? "#6366f1"
                            : _newColor.Text.Trim());

                var next =
                    _assigned
                        .Append(created.Name)
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase)
                        .OrderBy(
                            static value => value,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ToArray();
                await _runtime.LibraryService.SetAssetTagsAsync(
                    _runtime.Library.Id,
                    _assetId,
                    next);

                _assigned.Clear();
                _assigned.AddRange(next);
                _allTags =
                    await _runtime.LibraryService.ListTagsAsync(
                        _runtime.Library.Id);
                _search.Text = string.Empty;
                _status.Text = "タグを作成して付与しました。";
                Render();

                if (_metadataChanged is not null)
                {
                    await _metadataChanged();
                }
            });
    }

    private async Task PersistAsync(
        IReadOnlyList<string> tags)
    {
        await ExecuteBusyAsync(
            async () =>
            {
                var saved =
                    await _runtime.LibraryService.SetAssetTagsAsync(
                        _runtime.Library.Id,
                        _assetId,
                        tags);
                _assigned.Clear();
                _assigned.AddRange(saved);
                _allTags =
                    await _runtime.LibraryService.ListTagsAsync(
                        _runtime.Library.Id);
                _status.Text = "タグを更新しました。";
                Render();

                if (_metadataChanged is not null)
                {
                    await _metadataChanged();
                }
            });
    }

    private async Task ExecuteBusyAsync(
        Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _status.Foreground =
                LumineDesign.Warning;
            _status.Text =
                $"タグを更新できませんでした: {exception.Message}";
        }
        finally
        {
            _busy = false;
            IsEnabled = _assetId > 0;
        }
    }

    private static IBrush ResolveTagBrush(
        string? color)
    {
        try
        {
            return new SolidColorBrush(
                Color.Parse(
                    string.IsNullOrWhiteSpace(color)
                        ? "#6366f1"
                        : color));
        }
        catch (FormatException)
        {
            return LumineDesign.Accent;
        }
    }
}
