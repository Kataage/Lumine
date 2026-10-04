using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lumine.Library;

namespace Lumine.App;

internal sealed record BrowseFilterState(
    string SearchText = "",
    string? FolderPath = null,
    IReadOnlyList<string>? RequiredTags = null,
    int? MinRating = null,
    string? StatusLabel = null,
    bool FavoriteOnly = false,
    string? ColorLabel = null,
    AssetSortOrder SortOrder =
        AssetSortOrder.ModifiedNewest)
{
    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(SearchText)
        || !string.IsNullOrWhiteSpace(FolderPath)
        || TagNames.Count > 0
        || MinRating.HasValue
        || !string.IsNullOrWhiteSpace(StatusLabel)
        || FavoriteOnly
        || !string.IsNullOrWhiteSpace(ColorLabel);

    public IReadOnlyList<string> TagNames =>
        RequiredTags
        ?? Array.Empty<string>();

    public bool EquivalentTo(
        BrowseFilterState other) =>
        string.Equals(
            SearchText,
            other.SearchText,
            StringComparison.Ordinal)
        && string.Equals(
            FolderPath,
            other.FolderPath,
            StringComparison.Ordinal)
        && TagNames.SequenceEqual(
            other.TagNames,
            StringComparer.Ordinal)
        && MinRating == other.MinRating
        && string.Equals(
            StatusLabel,
            other.StatusLabel,
            StringComparison.Ordinal)
        && FavoriteOnly == other.FavoriteOnly
        && string.Equals(
            ColorLabel,
            other.ColorLabel,
            StringComparison.Ordinal)
        && SortOrder == other.SortOrder;
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Search debounce lifetime is explicitly cancelled on library replacement and window shutdown.")]
internal sealed class BrowseWorkspaceControls : UserControl
{
    private readonly Func<BrowseFilterState, Task> _filtersChanged;
    private readonly Func<BrowsePreferences, Task> _preferencesChanged;
    private readonly TextBox _search;
    private readonly ComboBox _sort;
    private readonly ComboBox _rating;
    private readonly ComboBox _status;
    private readonly ComboBox _tag;
    private readonly CheckBox _favorite;
    private readonly ComboBox _color;
    private readonly ScrollViewer _directFilterScroll;
    private readonly StackPanel _directFilterRow;
    private readonly Button _grid;
    private readonly Button _list;
    private readonly Slider _density;
    private readonly WrapPanel _chips;
    private CancellationTokenSource? _searchDebounce;
    private bool _suppressEvents;

    public BrowseWorkspaceControls(
        BrowseFilterState state,
        BrowsePreferences preferences,
        IReadOnlyList<LibraryTagInfo> tags,
        LibraryBrowseFacets facets,
        Func<BrowseFilterState, Task> filtersChanged,
        Func<BrowsePreferences, Task> preferencesChanged)
    {
        State = state
            ?? throw new ArgumentNullException(nameof(state));
        Preferences = preferences
            ?? throw new ArgumentNullException(nameof(preferences));
        _filtersChanged = filtersChanged
            ?? throw new ArgumentNullException(nameof(filtersChanged));
        _preferencesChanged = preferencesChanged
            ?? throw new ArgumentNullException(nameof(preferencesChanged));

        _search =
            LumineDesign.ConfigureTextBox(
                new TextBox
                {
                    PlaceholderText = "検索",
                    MinWidth = 260,
                    Text = State.SearchText
                });
        var searchHeight =
            Math.Max(
                32,
                LumineDesign.BodyLineHeight + 8);
        _search.MinHeight = searchHeight;
        _search.Height = searchHeight;
        _search.Padding = new Thickness(10, 4);
        ToolTip.SetTip(
            _search,
            "ファイル名・パス・ノート・タグを検索");

        _sort =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    MinWidth = 150,
                    ItemsSource = BrowseSortChoice.All
                });

        _rating =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    MinWidth = 112,
                    ItemsSource = RatingChoice.All
                });

        _status =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    MinWidth = 122
                });

        _tag =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    MinWidth = 122
                });

        _favorite =
            LumineDesign.ConfigureCheckBox(
                new CheckBox
                {
                    Content = "お気に入り"
                });

        _color =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    MinWidth = 112
                });

        _grid = LumineDesign.ConfigureIconButton(
            new Button
            {
                Content =
                    LumineDesign.CreateStrokeIcon(
                        LumineDesign.GridIconPath,
                        17)
            },
            "グリッド表示");

        _list = LumineDesign.ConfigureIconButton(
            new Button
            {
                Content =
                    LumineDesign.CreateStrokeIcon(
                        LumineDesign.ListIconPath,
                        17)
            },
            "リスト表示");

        foreach (var button in new[] { _grid, _list })
        {
            button.Width = 32;
            button.Height = 32;
            button.MinWidth = 32;
            button.MinHeight = 32;
            button.Padding = new Thickness(6);
            button.CornerRadius = new CornerRadius(7);
        }

        _density = new Slider
        {
            Minimum = 0,
            Maximum = 2,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Width = 92,
            Value = Preferences.Density,
            Foreground = LumineDesign.Focus,
            Background = LumineDesign.ControlSurface,
            VerticalAlignment = VerticalAlignment.Center
        };

        _chips = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            IsVisible = State.HasFilters
        };

        var primaryRow = new Grid
        {
            ColumnDefinitions =
                new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 6,
            VerticalAlignment =
                VerticalAlignment.Center
        };
        primaryRow.Children.Add(_search);

        var mode =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(0)
            };
        mode.Children.Add(_grid);
        mode.Children.Add(_list);
        Grid.SetColumn(mode, 1);
        primaryRow.Children.Add(mode);

        var densityPanel =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };
        densityPanel.Children.Add(
            LumineDesign.CreateStrokeIcon(
                LumineDesign.GridIconPath,
                13,
                LumineDesign.MutedForeground));
        densityPanel.Children.Add(_density);
        densityPanel.Children.Add(
            LumineDesign.CreateStrokeIcon(
                LumineDesign.GridIconPath,
                19,
                LumineDesign.MutedForeground));
        ToolTip.SetTip(
            densityPanel,
            "サムネイルサイズ");
        Grid.SetColumn(densityPanel, 2);
        primaryRow.Children.Add(densityPanel);

        _directFilterRow =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                Spacing =
                    LumineDesign.Space8,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        void AddDirectFilter(
            string label,
            Control control)
        {
            control.VerticalAlignment =
                VerticalAlignment.Center;

            var group =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,
                    Spacing =
                        LumineDesign.Space4,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };
            group.Children.Add(
                new TextBlock
                {
                    Text = label,
                    Foreground =
                        LumineDesign.MutedForeground,
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    VerticalAlignment =
                        VerticalAlignment.Center
                });
            group.Children.Add(control);
            _directFilterRow.Children.Add(group);
        }

        AddDirectFilter(
            "並び順",
            _sort);
        AddDirectFilter(
            "評価",
            _rating);
        AddDirectFilter(
            "状態",
            _status);
        AddDirectFilter(
            "タグ",
            _tag);
        AddDirectFilter(
            "色",
            _color);

        _favorite.Margin =
            new Thickness(
                LumineDesign.Space2,
                0,
                LumineDesign.Space4,
                0);
        _directFilterRow.Children.Add(
            _favorite);

        _directFilterScroll =
            new ScrollViewer
            {
                Content =
                    _directFilterRow,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Disabled,
                HorizontalContentAlignment =
                    HorizontalAlignment.Left
            };

        var root = new StackPanel
        {
            Spacing = 3
        };
        root.Children.Add(primaryRow);
        root.Children.Add(_directFilterScroll);
        root.Children.Add(_chips);

        Content =
            new Border
            {
                Background = LumineDesign.Surface,
                BorderBrush = LumineDesign.Border,
                BorderThickness =
                    new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 5),
                Child = root
            };

        UpdateFacetData(tags, facets);
        SynchronizeControls();
        AttachHandlers();
    }

    private void AttachHandlers()
    {
        _search.TextChanged += OnSearchTextChanged;
        _sort.SelectionChanged += OnSortChanged;
        _rating.SelectionChanged += OnRatingChanged;
        _status.SelectionChanged += OnStatusChanged;
        _tag.SelectionChanged += OnTagChanged;
        _favorite.Click += OnFavoriteChanged;
        _color.SelectionChanged += OnColorChanged;

        _grid.Click +=
            async (_, _) =>
                await SetViewModeAsync(
                    BrowseViewMode.Grid);
        _list.Click +=
            async (_, _) =>
                await SetViewModeAsync(
                    BrowseViewMode.List);

        _density.PropertyChanged += OnDensityChanged;
    }

    private async void OnDensityChanged(
        object? sender,
        AvaloniaPropertyChangedEventArgs args)
    {
        if (_suppressEvents
            || args.Property
                != RangeBase.ValueProperty)
        {
            return;
        }

        var density =
            (int)Math.Round(_density.Value);
        if (density == Preferences.Density)
        {
            return;
        }

        Preferences =
            Preferences with
            {
                Density = density
            };
        await PublishPreferencesAsync();
    }

    internal bool PrimaryToolbarIsContainedForSmoke
    {
        get
        {
            if (Bounds.Width <= 0
                || Bounds.Height <= 0)
            {
                return false;
            }

            foreach (var control in new Control[]
                     {
                         _search,
                         _grid,
                         _list,
                         _density
                     })
            {
                var origin =
                    control.TranslatePoint(
                        new Point(0, 0),
                        this);
                if (origin is not { } point)
                {
                    return false;
                }

                var bounds =
                    new Rect(
                        point,
                        control.Bounds.Size);
                if (bounds.Left < -0.5
                    || bounds.Top < -0.5
                    || bounds.Right > Bounds.Width + 0.5
                    || bounds.Bottom > Bounds.Height + 0.5)
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal bool DirectFiltersAreVisibleForSmoke =>
        _sort.IsVisible
        && _rating.IsVisible
        && _status.IsVisible
        && _tag.IsVisible
        && _color.IsVisible
        && _favorite.IsVisible;

    internal bool DirectFiltersFitWithoutScrollForSmoke =>
        _directFilterScroll.Viewport.Width > 0
        && _directFilterScroll.Extent.Width
            <= _directFilterScroll.Viewport.Width + 0.5;

    internal double DirectFilterExtentWidthForSmoke =>
        _directFilterScroll.Extent.Width;

    internal double DirectFilterViewportWidthForSmoke =>
        _directFilterScroll.Viewport.Width;

    internal double SearchHeightForSmoke =>
        _search.Bounds.Height;

    internal Thickness SearchPaddingForSmoke =>
        _search.Padding;

    public BrowseFilterState State { get; private set; }

    public BrowsePreferences Preferences { get; private set; }

    public void UpdateFacetData(
        IReadOnlyList<LibraryTagInfo> tags,
        LibraryBrowseFacets facets)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(facets);

        _suppressEvents = true;
        try
        {
            _tag.ItemsSource =
                new[]
                {
                    FilterChoice.AllLabel
                }
                .Concat(
                    tags.Select(
                        static item => item.Name))
                .ToArray();

            _status.ItemsSource =
                new[]
                {
                    FilterChoice.AllLabel
                }
                .Concat(facets.StatusLabels)
                .ToArray();

            _color.ItemsSource =
                new[]
                {
                    FilterChoice.AllLabel
                }
                .Concat(facets.ColorLabels)
                .ToArray();

            _tag.SelectedItem =
                FilterChoice.AllLabel;
            _status.SelectedItem =
                State.StatusLabel
                ?? FilterChoice.AllLabel;
            _color.SelectedItem =
                State.ColorLabel
                ?? FilterChoice.AllLabel;
        }
        finally
        {
            _suppressEvents = false;
        }

        RenderChips();
    }

    public Task SetFolderScopeAsync(
        string? folderPath) =>
        SetStateAsync(
            State with
            {
                FolderPath =
                    NormalizeOptional(folderPath)
            });

    public Task SetTagScopeAsync(
        string? tag)
    {
        var normalized =
            NormalizeOptional(tag);
        return SetStateAsync(
            State with
            {
                RequiredTags =
                    normalized is null
                        ? Array.Empty<string>()
                        : [normalized]
            });
    }

    public Task ToggleTagScopeAsync(
        string tag)
    {
        var normalized =
            NormalizeOptional(tag)
            ?? throw new ArgumentException(
                "Tag is required.",
                nameof(tag));

        var tags =
            State.TagNames
                .ToList();
        var index =
            tags.FindIndex(
                value =>
                    string.Equals(
                        value,
                        normalized,
                        StringComparison.Ordinal));
        if (index >= 0)
        {
            tags.RemoveAt(index);
        }
        else
        {
            tags.Add(normalized);
        }

        tags.Sort(
            StringComparer.Ordinal);
        return SetStateAsync(
            State with
            {
                RequiredTags = tags
            });
    }

    public Task ReplaceTagScopeAsync(
        string oldName,
        string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        if (!State.TagNames.Any(
                value =>
                    string.Equals(
                        value,
                        oldName,
                        StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        var tags =
            State.TagNames
                .Select(value =>
                    string.Equals(
                        value,
                        oldName,
                        StringComparison.OrdinalIgnoreCase)
                        ? newName.Trim()
                        : value)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    static value => value,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return SetStateAsync(
            State with
            {
                RequiredTags = tags
            });
    }

    public Task ClearTagScopesAsync() =>
        SetStateAsync(
            State with
            {
                RequiredTags =
                    Array.Empty<string>()
            });

    public void DisposeTransientWork()
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = null;
    }

    private async void OnSearchTextChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();

        var text =
            _search.Text?.Trim()
            ?? string.Empty;

        _searchDebounce =
            new CancellationTokenSource();
        var token =
            _searchDebounce.Token;

        try
        {
            if (text.Length > 0)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(220),
                    token);
            }

            token.ThrowIfCancellationRequested();
            await SetStateAsync(
                State with
                {
                    SearchText = text
                });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void OnSortChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressEvents
            || _sort.SelectedItem
                is not BrowseSortChoice choice)
        {
            return;
        }

        if (choice.SortOrder
            == State.SortOrder)
        {
            return;
        }

        Preferences =
            Preferences with
            {
                SortOrder = choice.SortOrder
            };

        await _preferencesChanged(Preferences);
        await SetStateAsync(
            State with
            {
                SortOrder = choice.SortOrder
            });
    }

    private async void OnRatingChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressEvents
            || _rating.SelectedItem
                is not RatingChoice choice)
        {
            return;
        }

        await SetStateAsync(
            State with
            {
                MinRating = choice.Minimum
            });
    }

    private async void OnStatusChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        await SetStateAsync(
            State with
            {
                StatusLabel =
                    NormalizeChoice(
                        _status.SelectedItem)
            });
    }

    private async void OnTagChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var selected =
            NormalizeChoice(
                _tag.SelectedItem);
        if (selected is null)
        {
            return;
        }

        var next =
            State.TagNames
                .Append(selected)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    static value => value,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        await SetStateAsync(
            State with
            {
                RequiredTags = next
            });
    }

    private async void OnFavoriteChanged(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        await SetStateAsync(
            State with
            {
                FavoriteOnly =
                    _favorite.IsChecked == true
            });
    }

    private async void OnColorChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        await SetStateAsync(
            State with
            {
                ColorLabel =
                    NormalizeChoice(
                        _color.SelectedItem)
            });
    }

    private async Task SetViewModeAsync(
        BrowseViewMode viewMode)
    {
        if (Preferences.ViewMode == viewMode)
        {
            return;
        }

        Preferences =
            Preferences with
            {
                ViewMode = viewMode
            };
        await PublishPreferencesAsync();
    }

    private async Task PublishPreferencesAsync()
    {
        UpdateViewButtons();
        await _preferencesChanged(
            Preferences);
    }

    private async Task SetStateAsync(
        BrowseFilterState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (State.EquivalentTo(state))
        {
            // ComboBox ItemsSource refreshes may emit SelectionChanged even
            // when the effective browse constraint did not change. Treat
            // those notifications as initialization/facet-refresh noise
            // rather than rebuilding the Viewer sessions.
            return;
        }

        State = state;
        SynchronizeControls();
        await _filtersChanged(State);
    }

    private void SynchronizeControls()
    {
        _suppressEvents = true;
        try
        {
            if (!string.Equals(
                    _search.Text,
                    State.SearchText,
                    StringComparison.Ordinal))
            {
                _search.Text =
                    State.SearchText;
            }

            _sort.SelectedItem =
                BrowseSortChoice.All.First(
                    item =>
                        item.SortOrder
                        == State.SortOrder);

            _rating.SelectedItem =
                RatingChoice.All.First(
                    item =>
                        item.Minimum
                        == State.MinRating);

            _status.SelectedItem =
                State.StatusLabel
                ?? FilterChoice.AllLabel;
            _tag.SelectedItem =
                FilterChoice.AllLabel;
            _color.SelectedItem =
                State.ColorLabel
                ?? FilterChoice.AllLabel;
            _favorite.IsChecked =
                State.FavoriteOnly;

            _density.Value =
                Preferences.Density;
        }
        finally
        {
            _suppressEvents = false;
        }

        UpdateViewButtons();
        RenderChips();
    }

    private void UpdateViewButtons()
    {
        var gridSelected =
            Preferences.ViewMode
            == BrowseViewMode.Grid;
        _grid.Background =
            gridSelected
                ? LumineDesign.AccentMuted
                : Brushes.Transparent;
        _grid.BorderBrush =
            gridSelected
                ? LumineDesign.BorderStrong
                : LumineDesign.Border;

        var listSelected =
            Preferences.ViewMode
            == BrowseViewMode.List;
        _list.Background =
            listSelected
                ? LumineDesign.AccentMuted
                : Brushes.Transparent;
        _list.BorderBrush =
            listSelected
                ? LumineDesign.BorderStrong
                : LumineDesign.Border;
    }

    private void RenderChips()
    {
        _chips.Children.Clear();
        _chips.IsVisible = State.HasFilters;

        AddChip(
            "検索",
            NormalizeOptional(State.SearchText),
            () => SetStateAsync(
                State with
                {
                    SearchText = string.Empty
                }));

        AddChip(
            "フォルダー",
            State.FolderPath,
            () => SetStateAsync(
                State with
                {
                    FolderPath = null
                }));

        foreach (var tag in State.TagNames)
        {
            var currentTag = tag;
            AddChip(
                "タグ",
                currentTag,
                () => SetStateAsync(
                    State with
                    {
                        RequiredTags =
                            State.TagNames
                                .Where(value =>
                                    !string.Equals(
                                        value,
                                        currentTag,
                                        StringComparison.Ordinal))
                                .ToArray()
                    }));
        }

        AddChip(
            "評価",
            State.MinRating.HasValue
                ? $"★{State.MinRating.Value}以上"
                : null,
            () => SetStateAsync(
                State with
                {
                    MinRating = null
                }));

        AddChip(
            "状態",
            State.StatusLabel,
            () => SetStateAsync(
                State with
                {
                    StatusLabel = null
                }));

        AddChip(
            "お気に入り",
            State.FavoriteOnly
                ? "のみ"
                : null,
            () => SetStateAsync(
                State with
                {
                    FavoriteOnly = false
                }));

        AddChip(
            "色",
            State.ColorLabel,
            () => SetStateAsync(
                State with
                {
                    ColorLabel = null
                }));

        if (State.HasFilters)
        {
            var clear =
                LumineDesign.ConfigureSecondaryButton(
                    new Button
                    {
                        Content = "すべて解除",
                        MinHeight = 26,
                        Padding =
                            new Thickness(8, 3),
                        FontSize = LumineDesign.CaptionFontSize,
                        Margin =
                            new Thickness(3)
                    });
            clear.Click +=
                async (_, _) =>
                    await SetStateAsync(
                        new BrowseFilterState(
                            SortOrder:
                                State.SortOrder));
            _chips.Children.Add(clear);
        }
    }

    private void AddChip(
        string label,
        string? value,
        Func<Task> remove)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var chip =
            new Button
            {
                Content =
                    $"{label}: {value}  ×",
                Background =
                    LumineDesign.AccentMuted,
                Foreground =
                    LumineDesign.Foreground,
                BorderBrush =
                    LumineDesign.Border,
                BorderThickness =
                    new Thickness(1),
                CornerRadius =
                    new CornerRadius(12),
                MinHeight = 26,
                Padding =
                    new Thickness(9, 3),
                FontSize = LumineDesign.CaptionFontSize,
                Margin =
                    new Thickness(3)
            };
        chip.Click +=
            async (_, _) =>
                await remove();
        _chips.Children.Add(chip);
    }

    private static string? NormalizeChoice(
        object? value)
    {
        var text =
            value as string;
        return string.Equals(
                text,
                FilterChoice.AllLabel,
                StringComparison.Ordinal)
            ? null
            : NormalizeOptional(text);
    }

    private static string? NormalizeOptional(
        string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private sealed record BrowseSortChoice(
        string Label,
        AssetSortOrder SortOrder)
    {
        public static IReadOnlyList<BrowseSortChoice> All { get; } =
        [
            new(
                "更新日 新しい順",
                AssetSortOrder.ModifiedNewest),
            new(
                "更新日 古い順",
                AssetSortOrder.ModifiedOldest),
            new(
                "作成日 新しい順",
                AssetSortOrder.CreatedNewest),
            new(
                "作成日 古い順",
                AssetSortOrder.CreatedOldest),
            new(
                "ファイル名 A→Z",
                AssetSortOrder.FileNameAscending),
            new(
                "ファイル名 Z→A",
                AssetSortOrder.FileNameDescending),
            new(
                "サイズ 大きい順",
                AssetSortOrder.FileSizeLargest),
            new(
                "サイズ 小さい順",
                AssetSortOrder.FileSizeSmallest),
            new(
                "評価 高い順",
                AssetSortOrder.RatingHighest),
            new(
                "評価 低い順",
                AssetSortOrder.RatingLowest),
            new(
                "状態 A→Z",
                AssetSortOrder.StatusAscending),
            new(
                "状態 Z→A",
                AssetSortOrder.StatusDescending)
        ];

        public override string ToString() =>
            Label;
    }

    private sealed record RatingChoice(
        string Label,
        int? Minimum)
    {
        public static IReadOnlyList<RatingChoice> All { get; } =
        [
            new("すべて", null),
            new("★1以上", 1),
            new("★2以上", 2),
            new("★3以上", 3),
            new("★4以上", 4),
            new("★5", 5)
        ];

        public override string ToString() =>
            Label;
    }

    private static class FilterChoice
    {
        public const string AllLabel = "すべて";
    }
}
