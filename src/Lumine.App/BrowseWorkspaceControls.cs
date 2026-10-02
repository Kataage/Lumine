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
    string? Tag = null,
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
        || !string.IsNullOrWhiteSpace(Tag)
        || MinRating.HasValue
        || !string.IsNullOrWhiteSpace(StatusLabel)
        || FavoriteOnly
        || !string.IsNullOrWhiteSpace(ColorLabel);
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
    private readonly Button _grid;
    private readonly Button _list;
    private readonly Slider _density;
    private readonly WrapPanel _chips;
    private readonly TextBlock _searchHint;
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
        ToolTip.SetTip(
            _search,
            "ファイル名・パス・ノート・タグを検索");

        _searchHint = new TextBlock
        {
            Foreground = LumineDesign.MutedForeground,
            FontSize = 9.5,
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Center
        };

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
                new ColumnDefinitions("*,Auto,Auto,Auto")
        };
        primaryRow.Children.Add(_search);

        Grid.SetColumn(_searchHint, 1);
        _searchHint.Margin =
            new Thickness(8, 0);
        primaryRow.Children.Add(_searchHint);

        var mode =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Margin = new Thickness(8, 0)
            };
        mode.Children.Add(_grid);
        mode.Children.Add(_list);
        Grid.SetColumn(mode, 2);
        primaryRow.Children.Add(mode);

        var densityPanel =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
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
        Grid.SetColumn(densityPanel, 3);
        primaryRow.Children.Add(densityPanel);

        var filterRow =
            new WrapPanel
            {
                Orientation = Orientation.Horizontal
            };
        AddFilter(filterRow, "並び順", _sort);
        AddFilter(filterRow, "評価", _rating);
        AddFilter(filterRow, "状態", _status);
        AddFilter(filterRow, "タグ", _tag);
        AddFilter(filterRow, "色", _color);
        _favorite.Margin =
            new Thickness(10, 5, 4, 5);
        filterRow.Children.Add(_favorite);

        var root = new StackPanel
        {
            Spacing = 5
        };
        root.Children.Add(primaryRow);
        root.Children.Add(filterRow);
        root.Children.Add(_chips);

        Content =
            new Border
            {
                Background = LumineDesign.Surface,
                BorderBrush = LumineDesign.Border,
                BorderThickness =
                    new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 6),
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
                State.Tag
                ?? FilterChoice.AllLabel;
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
        string? tag) =>
        SetStateAsync(
            State with
            {
                Tag =
                    NormalizeOptional(tag)
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

        if (text.Length > 0
            && !IsSearchReady(text))
        {
            _searchHint.Text =
                "英数字は3文字以上、日本語/CJKは2文字以上";
            _searchHint.IsVisible = true;
            return;
        }

        _searchHint.IsVisible = false;
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

        await SetStateAsync(
            State with
            {
                Tag =
                    NormalizeChoice(
                        _tag.SelectedItem)
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

        if (State == state)
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
                State.Tag
                ?? FilterChoice.AllLabel;
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

        AddChip(
            "タグ",
            State.Tag,
            () => SetStateAsync(
                State with
                {
                    Tag = null
                }));

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
                        FontSize = 10,
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
                FontSize = 10,
                Margin =
                    new Thickness(3)
            };
        chip.Click +=
            async (_, _) =>
                await remove();
        _chips.Children.Add(chip);
    }

    private static void AddFilter(
        Panel panel,
        string label,
        Control control)
    {
        var group =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                Spacing = 5,
                Margin =
                    new Thickness(4, 3)
            };
        group.Children.Add(
            new TextBlock
            {
                Text = label,
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = 10,
                VerticalAlignment =
                    VerticalAlignment.Center
            });
        group.Children.Add(control);
        panel.Children.Add(group);
    }

    private static bool IsSearchReady(
        string text)
    {
        var terms =
            text.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);

        if (terms.Length == 0)
        {
            return true;
        }

        foreach (var term in terms)
        {
            if (term.Length >= 3)
            {
                continue;
            }

            if (term.Length == 2
                && term.All(IsCjk))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsCjk(char value) =>
        value is >= '\u3040' and <= '\u30ff'
        or >= '\u3400' and <= '\u4dbf'
        or >= '\u4e00' and <= '\u9fff'
        or >= '\uff66' and <= '\uff9f';

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
                "更新日 ↓",
                AssetSortOrder.ModifiedNewest),
            new(
                "更新日 ↑",
                AssetSortOrder.ModifiedOldest),
            new(
                "ファイル名 A→Z",
                AssetSortOrder.FileNameAscending),
            new(
                "ファイル名 Z→A",
                AssetSortOrder.FileNameDescending)
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
