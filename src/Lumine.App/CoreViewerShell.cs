using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumine.Library;
using Lumine.Viewer;

namespace Lumine.App;

internal sealed class CoreViewerShell : UserControl
{
    private readonly ThumbnailViewerControl _grid;
    private readonly DetailViewerControl _detail;
    private readonly CoreViewerRuntime _runtime;
    private readonly Func<Task>? _afterBulkMutation;
    private readonly Action<string>? _entryRequested;
    private readonly Border _selectionBar;
    private readonly TextBlock _selectionCount;
    private readonly TextBlock _selectionMetadataSummary;
    private readonly TextBlock _bulkStatus;
    private readonly ContextualAssetDetailPanel _contextDetail;
    private readonly Border _contextSurface;
    private readonly Border _focusedSurface;
    private readonly Button _detailToggle;
    private readonly Button _focusButton;
    private CancellationTokenSource? _selectionSummaryCancellation;
    private bool _detached;

    public CoreViewerShell(
        CoreViewerRuntime runtime,
        BrowsePreferences? preferences = null,
        Func<Task>? afterBulkMutation = null,
        Action<string>? entryRequested = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        _runtime = runtime;
        _afterBulkMutation = afterBulkMutation;
        _entryRequested = entryRequested;

        preferences ??=
            new BrowsePreferences(
                BrowseViewMode.Grid,
                1,
                Lumine.Library.AssetSortOrder.ModifiedNewest);

        _grid = new ThumbnailViewerControl(
            runtime.ViewerSession,
            preferences.ViewMode == BrowseViewMode.List
                ? ViewerLayoutMode.List
                : ViewerLayoutMode.Grid,
            preferences.Density);
        _detail = new DetailViewerControl(
            runtime.DetailSession);

        _contextDetail =
            new ContextualAssetDetailPanel(
                runtime,
                () =>
                {
                    HideContextDetail();
                    return Task.CompletedTask;
                },
                OpenFocusedViewAsync,
                _afterBulkMutation);
        _contextSurface =
            new Border
            {
                Width = 340,
                Background = LumineDesign.Surface,
                BorderBrush = LumineDesign.Border,
                BorderThickness =
                    new Thickness(1, 0, 0, 0),
                IsVisible = false,
                Child = _contextDetail
            };

        _detailToggle =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "詳細",
                    MinHeight = 30,
                    Padding = new Thickness(10, 5),
                    IsEnabled = false
                });
        _detailToggle.Click +=
            async (_, _) =>
            {
                if (_contextSurface.IsVisible)
                {
                    HideContextDetail();
                }
                else
                {
                    await ShowContextDetailAsync();
                }
            };

        _focusButton =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "集中表示",
                    MinHeight = 30,
                    Padding = new Thickness(12, 5),
                    IsEnabled = false
                });
        _focusButton.Click +=
            async (_, _) =>
                await OpenFocusedViewAsync();

        var focusedClose =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "一覧へ戻る  Esc",
                    MinHeight = 30,
                    Padding = new Thickness(12, 5)
                });
        focusedClose.Click +=
            (_, _) =>
                CloseFocusedView();

        var focusedHeader =
            new Grid
            {
                Background = Brushes.Black,
                ColumnDefinitions =
                    new ColumnDefinitions("Auto,*,Auto"),
                Margin = new Thickness(8, 6)
            };
        focusedHeader.Children.Add(
            focusedClose);
        var focusedHint =
            new TextBlock
            {
                Text = "← → 画像移動 · Ctrl+0 全体表示 · Ctrl+1 1:1 · Ctrl+ホイール ズーム · ドラッグ パン",
                Foreground = Brushes.LightGray,
                FontSize = 10,
                VerticalAlignment =
                    VerticalAlignment.Center,
                HorizontalAlignment =
                    HorizontalAlignment.Center
            };
        Grid.SetColumn(focusedHint, 1);
        focusedHeader.Children.Add(
            focusedHint);

        var focusedLayout =
            new Grid
            {
                Background = Brushes.Black,
                RowDefinitions =
                    new RowDefinitions("Auto,*")
            };
        focusedLayout.Children.Add(
            focusedHeader);
        Grid.SetRow(_detail, 1);
        focusedLayout.Children.Add(_detail);

        _focusedSurface =
            new Border
            {
                Background = Brushes.Black,
                IsVisible = false,
                Child = focusedLayout
            };

        _selectionCount =
            new TextBlock
            {
                Foreground = LumineDesign.Foreground,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
        _selectionMetadataSummary =
            new TextBlock
            {
                Foreground = LumineDesign.MutedForeground,
                FontSize = 9.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        _bulkStatus =
            new TextBlock
            {
                Foreground = LumineDesign.MutedForeground,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        _selectionBar = CreateSelectionBar();
        _selectionBar.IsVisible = false;

        _grid.SelectionChanged += OnSelectionChanged;
        _grid.AssetInvoked += OnAssetInvoked;
        KeyDown += OnShellKeyDown;
        Focusable = true;

        Background = LumineDesign.Background;

        if (runtime.AssetCount == 0)
        {
            Content =
                LumineDesign.CreateProductState(
                    "画像がありません",
                    "このフォルダーには、Lumineで表示できる画像が見つかりませんでした。");
            return;
        }

        var gridSurface =
            new Border
            {
                Background = LumineDesign.Background,
                Padding = new Thickness(0),
                Child = _grid
            };

        var browseActionContent =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto,Auto")
            };
        browseActionContent.Children.Add(
            new TextBlock
            {
                Text =
                    "画像を選択すると詳細表示・集中表示を利用できます。",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = 10,
                VerticalAlignment =
                    VerticalAlignment.Center
            });
        Grid.SetColumn(_detailToggle, 1);
        _detailToggle.Margin =
            new Thickness(4, 0);
        browseActionContent.Children.Add(
            _detailToggle);
        Grid.SetColumn(_focusButton, 2);
        _focusButton.Margin =
            new Thickness(4, 0);
        browseActionContent.Children.Add(
            _focusButton);

        var browseActions =
            new Border
            {
                Background = LumineDesign.Surface,
                BorderBrush = LumineDesign.Border,
                BorderThickness =
                    new Thickness(0, 0, 0, 1),
                Padding =
                    new Thickness(10, 6),
                Child = browseActionContent
            };

        var browseViewer =
            new Grid
            {
                Background = LumineDesign.Background,
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto")
            };
        browseViewer.Children.Add(
            gridSurface);
        Grid.SetColumn(_contextSurface, 1);
        browseViewer.Children.Add(
            _contextSurface);

        var browseLayout =
            new Grid
            {
                Background = LumineDesign.Background,
                RowDefinitions =
                    new RowDefinitions("Auto,Auto,*")
            };
        browseLayout.Children.Add(
            _selectionBar);
        Grid.SetRow(browseActions, 1);
        browseLayout.Children.Add(
            browseActions);
        Grid.SetRow(browseViewer, 2);
        browseLayout.Children.Add(
            browseViewer);

        var layers =
            new Grid
            {
                Background = LumineDesign.Background
            };
        layers.Children.Add(
            browseLayout);
        layers.Children.Add(
            _focusedSurface);

        Content = layers;
    }

    internal ThumbnailViewerControl GridViewer => _grid;

    internal DetailViewerControl DetailViewer => _detail;

    internal ContextualAssetDetailPanel ContextDetail =>
        _contextDetail;

    internal bool IsContextDetailVisible =>
        _contextSurface.IsVisible;

    internal bool IsFocusedViewVisible =>
        _focusedSurface.IsVisible;

    internal async Task ShowContextDetailAsync()
    {
        if (_grid.SelectedAssetIndex < 0)
        {
            _contextDetail.ShowNoSelection();
            _contextSurface.IsVisible = true;
            return;
        }

        _contextSurface.IsVisible = true;
        await LoadContextDetailAsync(
            _grid.SelectedAssetIndex);
    }

    internal void HideContextDetail()
    {
        _contextSurface.IsVisible = false;
        _grid.Focus();
    }

    internal Task OpenFocusedViewAsync() =>
        OpenFocusedViewAsync(
            _grid.SelectedAssetIndex);

    internal async Task OpenFocusedViewAsync(
        long index)
    {
        if ((ulong)index
            >= (ulong)_runtime.AssetCount)
        {
            return;
        }

        if (_grid.SelectedAssetIndex != index)
        {
            _grid.SelectAsset(index);
        }

        _focusedSurface.IsVisible = true;

        try
        {
            // Await the explicit selection before binding to Grid. BindGrid()
            // synchronizes asynchronously; doing it first can make the
            // explicit SelectAsync observe LoadingPreview and return early.
            await _detail.SelectAsync(index);
            _detail.BindGrid(_grid);
            _detail.Focus();
        }
        catch
        {
            _focusedSurface.IsVisible = false;
            throw;
        }
    }

    internal void CloseFocusedView()
    {
        if (!_focusedSurface.IsVisible)
        {
            return;
        }

        _focusedSurface.IsVisible = false;
        _detail.UnbindGrid();
        _runtime.DetailSession.Clear();
        _grid.Focus();
    }

    private async Task LoadContextDetailAsync(
        long index)
    {
        if ((ulong)index
            >= (ulong)_runtime.AssetCount)
        {
            _contextDetail.ShowNoSelection();
            return;
        }

        try
        {
            var asset =
                await _runtime.ViewerSession
                    .GetAssetAsync(index);
            await _contextDetail.ShowAssetAsync(
                asset);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void OnAssetInvoked(
        object? sender,
        long index)
    {
        try
        {
            await OpenFocusedViewAsync(index);
        }
        catch (Exception exception)
        {
            _bulkStatus.Text =
                $"集中表示を開けませんでした: {exception.Message}";
        }
    }

    private Border CreateSelectionBar()
    {
        var actions =
            new WrapPanel
            {
                Orientation = Orientation.Horizontal
            };

        actions.Children.Add(
            CreateBulkButton(
                "★0",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        SetRating: true,
                        Rating: null))));
        for (var rating = 1;
             rating <= 5;
             rating++)
        {
            var captured = rating;
            actions.Children.Add(
                CreateBulkButton(
                    $"★{captured}",
                    () => ApplyPatchAsync(
                        new AssetUserMetadataPatch(
                            SetRating: true,
                            Rating: captured))));
        }

        actions.Children.Add(
            CreateBulkButton(
                "お気に入り",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        SetFavorite: true,
                        Favorite: true))));
        actions.Children.Add(
            CreateBulkButton(
                "お気に入り解除",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        SetFavorite: true,
                        Favorite: false))));

        var status =
            new ComboBox
            {
                Width = 118,
                ItemsSource =
                    new[]
                    {
                        "unsorted",
                        "reviewed",
                        "candidate",
                        "published"
                    },
                SelectedIndex = 0,
                Margin = new Thickness(3)
            };
        actions.Children.Add(status);
        actions.Children.Add(
            CreateBulkButton(
                "状態を適用",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        SetStatusLabel: true,
                        StatusLabel:
                            status.SelectedItem as string))));

        var color =
            new ComboBox
            {
                Width = 104,
                ItemsSource =
                    new[]
                    {
                        "red",
                        "orange",
                        "yellow",
                        "green",
                        "blue",
                        "purple",
                        "gray"
                    },
                SelectedIndex = 4,
                Margin = new Thickness(3)
            };
        actions.Children.Add(color);
        actions.Children.Add(
            CreateBulkButton(
                "色を適用",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        SetColorLabel: true,
                        ColorLabel:
                            color.SelectedItem as string))));

        var tag =
            new TextBox
            {
                Width = 128,
                PlaceholderText = "タグを追加",
                Margin = new Thickness(3)
            };
        actions.Children.Add(tag);
        actions.Children.Add(
            CreateBulkButton(
                "タグ追加",
                async () =>
                {
                    var value = tag.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return;
                    }

                    await ApplyPatchAsync(
                        new AssetUserMetadataPatch(
                            AddTags: [value]));
                    tag.Text = string.Empty;
                }));
        actions.Children.Add(
            CreateBulkButton(
                "タグ全解除",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        ClearTags: true))));

        actions.Children.Add(
            CreateBulkButton(
                "Publication",
                () =>
                {
                    _entryRequested?.Invoke(
                        "publication");
                    return Task.CompletedTask;
                }));
        actions.Children.Add(
            CreateBulkButton(
                "Work / Group",
                () =>
                {
                    _entryRequested?.Invoke(
                        "creative");
                    return Task.CompletedTask;
                }));

        var delete =
            CreateBulkButton(
                "元ファイルを削除…",
                DeleteSelectedSourcesAsync);
        delete.Foreground =
            LumineDesign.Danger;
        actions.Children.Add(delete);

        var clear =
            CreateBulkButton(
                "選択解除",
                () =>
                {
                    _grid.ClearSelection();
                    return Task.CompletedTask;
                });
        actions.Children.Add(clear);

        var top =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("Auto,*,Auto"),
                Margin = new Thickness(10, 7)
            };
        top.Children.Add(_selectionCount);

        var middle =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(12, 0)
            };
        middle.Children.Add(
            _selectionMetadataSummary);
        middle.Children.Add(
            _bulkStatus);
        Grid.SetColumn(middle, 1);
        top.Children.Add(middle);

        var hint =
            new TextBlock
            {
                Text = "Ctrl: 追加/解除 · Shift: 範囲 · Ctrl+A: すべて",
                Foreground = LumineDesign.MutedForeground,
                FontSize = 9.5,
                VerticalAlignment = VerticalAlignment.Center
            };
        Grid.SetColumn(hint, 2);
        top.Children.Add(hint);

        var root =
            new StackPanel
            {
                Spacing = 2
            };
        root.Children.Add(top);
        root.Children.Add(actions);

        return new Border
        {
            Background = LumineDesign.SurfaceRaised,
            BorderBrush = LumineDesign.Border,
            BorderThickness =
                new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 2, 6, 6),
            Child = root
        };
    }

    private static Button CreateBulkButton(
        string label,
        Func<Task> action)
    {
        var button =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = label,
                    MinHeight = 28,
                    Padding = new Thickness(8, 4),
                    FontSize = 10,
                    Margin = new Thickness(3)
                });
        button.Click +=
            async (_, _) =>
            {
                button.IsEnabled = false;
                try
                {
                    await action();
                }
                finally
                {
                    button.IsEnabled = true;
                }
            };
        return button;
    }

    private async void OnSelectionChanged(
        object? sender,
        ViewerSelectionSnapshot selection)
    {
        _selectionBar.IsVisible =
            selection.Count > 0;
        _selectionCount.Text =
            selection.Count == 1
                ? "1件を選択"
                : $"{selection.Count:N0}件を選択";
        _bulkStatus.Text = string.Empty;
        _selectionMetadataSummary.Text =
            selection.Count > 0
                ? "整理情報を確認中…"
                : string.Empty;

        _selectionSummaryCancellation?.Cancel();
        _selectionSummaryCancellation?.Dispose();
        _selectionSummaryCancellation =
            selection.Count > 0
                ? new CancellationTokenSource()
                : null;

        if (selection.Count > 0)
        {
            try
            {
                await RefreshSelectionMetadataSummaryAsync(
                    selection,
                    _selectionSummaryCancellation!.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _selectionMetadataSummary.Text =
                    $"整理情報を取得できません: {exception.Message}";
            }
        }

        var hasPrimary =
            selection.PrimaryIndex >= 0;
        _detailToggle.IsEnabled =
            hasPrimary;
        _focusButton.IsEnabled =
            hasPrimary;

        if (!hasPrimary)
        {
            _contextDetail.ShowNoSelection();
            if (_focusedSurface.IsVisible)
            {
                CloseFocusedView();
            }

            return;
        }

        if (_contextSurface.IsVisible)
        {
            try
            {
                await LoadContextDetailAsync(
                    selection.PrimaryIndex);
            }
            catch (Exception exception)
            {
                _bulkStatus.Text =
                    $"詳細を更新できませんでした: {exception.Message}";
            }
        }
    }

    private async Task RefreshSelectionMetadataSummaryAsync(
        ViewerSelectionSnapshot selection,
        CancellationToken cancellationToken)
    {
        if (selection.Count <= 0)
        {
            _selectionMetadataSummary.Text =
                string.Empty;
            return;
        }

        var assets =
            await ResolveSelectedAssetsAsync(
                cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var summary =
            await _runtime.LibraryService
                .GetUserMetadataSelectionSummaryAsync(
                    _runtime.Library.Id,
                    assets.Select(
                            static asset => asset.Id)
                        .ToArray(),
                    cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        _selectionMetadataSummary.Text =
            FormatSelectionMetadataSummary(
                summary);
    }

    private static string FormatSelectionMetadataSummary(
        AssetUserMetadataSelectionSummary summary)
    {
        static string MixedOr(
            bool mixed,
            string value) =>
            mixed
                ? "mixed"
                : value;

        var rating =
            MixedOr(
                summary.RatingMixed,
                summary.Rating is { } ratingValue
                    ? $"★{ratingValue}"
                    : "評価なし");
        var favorite =
            MixedOr(
                summary.FavoriteMixed,
                summary.Favorite
                    ? "お気に入り"
                    : "お気に入りなし");
        var status =
            MixedOr(
                summary.StatusLabelMixed,
                summary.StatusLabel
                ?? "状態なし");
        var color =
            MixedOr(
                summary.ColorLabelMixed,
                summary.ColorLabel
                ?? "カラーなし");
        var tags =
            summary.TagsMixed
                ? summary.CommonTags.Count > 0
                    ? $"タグ mixed / 共通 {string.Join(", ", summary.CommonTags)}"
                    : "タグ mixed"
                : summary.CommonTags.Count > 0
                    ? $"タグ {string.Join(", ", summary.CommonTags)}"
                    : "タグなし";

        return
            $"{rating} · {favorite} · {status} · {color} · {tags}";
    }

    private async Task<IReadOnlyList<ViewerAsset>>
        ResolveSelectedAssetsAsync(
            CancellationToken cancellationToken = default)
    {
        var indices =
            _grid.SelectedAssetIndices;
        var assets =
            new List<ViewerAsset>(indices.Count);

        foreach (var index in indices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            assets.Add(
                await _runtime.ViewerSession.GetAssetAsync(
                    index,
                    cancellationToken));
        }

        return assets;
    }

    private async Task ApplyPatchAsync(
        AssetUserMetadataPatch patch)
    {
        var assets =
            await ResolveSelectedAssetsAsync();
        if (assets.Count == 0)
        {
            return;
        }

        _bulkStatus.Text =
            "更新しています…";

        var updated =
            await _runtime.LibraryService.PatchUserMetadataAsync(
                _runtime.Library.Id,
                assets.Select(
                        static asset => asset.Id)
                    .ToArray(),
                patch);

        _bulkStatus.Text =
            $"{updated:N0}件を更新しました。";

        if (_afterBulkMutation is not null)
        {
            await _afterBulkMutation();
        }
    }

    private async Task DeleteSelectedSourcesAsync()
    {
        var assets =
            await ResolveSelectedAssetsAsync();
        if (assets.Count == 0)
        {
            return;
        }

        var owner =
            TopLevel.GetTopLevel(this)
                as Window;
        if (owner is null)
        {
            throw new InvalidOperationException(
                "Bulk source deletion requires an owning window.");
        }

        var confirmed =
            await ShowDeleteConfirmationAsync(
                owner,
                assets.Count);
        if (!confirmed)
        {
            return;
        }

        _bulkStatus.Text =
            "元ファイルを削除しています…";

        var root =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    _runtime.LibraryRoot));
        var prefix =
            root
            + Path.DirectorySeparatorChar;
        var removed =
            new List<string>();
        var failures =
            new List<string>();

        await Task.Run(
            () =>
            {
                foreach (var asset in assets)
                {
                    var relative =
                        asset.RelativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar);
                    var source =
                        Path.GetFullPath(
                            Path.Combine(
                                root,
                                relative));

                    if (!source.StartsWith(
                            prefix,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add(
                            asset.RelativePath);
                        continue;
                    }

                    try
                    {
                        if (File.Exists(source))
                        {
                            File.Delete(source);
                        }

                        removed.Add(
                            asset.RelativePath);
                    }
                    catch (
                        IOException)
                    {
                        failures.Add(
                            asset.RelativePath);
                    }
                    catch (
                        UnauthorizedAccessException)
                    {
                        failures.Add(
                            asset.RelativePath);
                    }
                }
            });

        if (removed.Count > 0)
        {
            await _runtime.LibraryService.RemoveAssetsAsync(
                _runtime.Library.Id,
                removed);
        }

        _grid.ClearSelection();

        _bulkStatus.Text =
            failures.Count == 0
                ? $"{removed.Count:N0}件の元ファイルを削除しました。"
                : $"{removed.Count:N0}件を削除、{failures.Count:N0}件は削除できませんでした。";

        if (_afterBulkMutation is not null)
        {
            await _afterBulkMutation();
        }
    }

    private static async Task<bool>
        ShowDeleteConfirmationAsync(
            Window owner,
            int count)
    {
        var dialog =
            new Window
            {
                Title = "元ファイルを削除",
                Width = 460,
                Height = 210,
                CanResize = false,
                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,
                Background = LumineDesign.Background,
                Foreground = LumineDesign.Foreground,
                FontFamily = LumineDesign.UiFont
            };

        var message =
            new TextBlock
            {
                Text =
                    $"{count:N0}件の元画像ファイルをディスクから削除します。\nこの操作はLumineの登録解除ではなく、実ファイルの削除です。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = LumineDesign.Foreground
            };
        var warning =
            new TextBlock
            {
                Text =
                    "削除したファイルはLumineから元に戻せません。",
                Foreground = LumineDesign.Danger,
                FontWeight = FontWeight.SemiBold
            };

        var cancel =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "キャンセル"
                });
        var delete =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "元ファイルを削除"
                });
        delete.Foreground =
            LumineDesign.Danger;

        cancel.Click +=
            (_, _) =>
                dialog.Close(false);
        delete.Click +=
            (_, _) =>
                dialog.Close(true);

        var buttons =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                Spacing = 8
            };
        buttons.Children.Add(cancel);
        buttons.Children.Add(delete);

        var panel =
            new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 14
            };
        panel.Children.Add(message);
        panel.Children.Add(warning);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        return await dialog.ShowDialog<bool>(
            owner);
    }

    private async void OnShellKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (_focusedSurface.IsVisible)
        {
            if (e.Key == Key.Escape)
            {
                CloseFocusedView();
                e.Handled = true;
            }

            return;
        }

        var focused =
            TopLevel.GetTopLevel(this)
                ?.FocusManager
                ?.GetFocusedElement();

        if (focused is TextBox
            or ComboBox)
        {
            return;
        }

        if (_grid.SelectedAssetCount == 0)
        {
            return;
        }

        AssetUserMetadataPatch? patch =
            e.Key switch
            {
                Key.D0 =>
                    new AssetUserMetadataPatch(
                        SetRating: true,
                        Rating: null),
                Key.D1 =>
                    new AssetUserMetadataPatch(
                        SetRating: true,
                        Rating: 1),
                Key.D2 =>
                    new AssetUserMetadataPatch(
                        SetRating: true,
                        Rating: 2),
                Key.D3 =>
                    new AssetUserMetadataPatch(
                        SetRating: true,
                        Rating: 3),
                Key.D4 =>
                    new AssetUserMetadataPatch(
                        SetRating: true,
                        Rating: 4),
                Key.D5 =>
                    new AssetUserMetadataPatch(
                        SetRating: true,
                        Rating: 5),
                Key.F =>
                    new AssetUserMetadataPatch(
                        SetFavorite: true,
                        Favorite: true),
                _ => null
            };

        if (patch is not null)
        {
            e.Handled = true;
            await ApplyPatchAsync(patch);
            return;
        }

        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            await DeleteSelectedSourcesAsync();
        }
    }

    public void SetBrowseLayout(
        BrowsePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        _grid.SetLayout(
            preferences.ViewMode == BrowseViewMode.List
                ? ViewerLayoutMode.List
                : ViewerLayoutMode.Grid,
            preferences.Density);
    }

    public void SelectInitialAsset()
    {
        if (_grid.AssetCount <= 0)
        {
            return;
        }

        _grid.SelectAsset(0);
        _grid.Focus();
    }

    public async Task DetachAsync()
    {
        if (_detached)
        {
            await _grid.DrainBitmapReleasesAsync();
            return;
        }

        _detached = true;
        _grid.SelectionChanged -= OnSelectionChanged;
        _grid.AssetInvoked -= OnAssetInvoked;
        _selectionSummaryCancellation?.Cancel();
        _selectionSummaryCancellation?.Dispose();
        _selectionSummaryCancellation = null;
        KeyDown -= OnShellKeyDown;
        _contextDetail.PrepareForDetach();
        _detail.UnbindGrid();
        _detail.PrepareForDetach();
        _grid.PrepareForDetach();

        // Drop the shell-owned visual tree before awaiting compositor/native
        // drains. The controls retain only the explicit lifecycle objects
        // that are awaited below and by CoreViewerRuntime.DisposeAsync().
        Content = null;

        await _grid.DrainBitmapReleasesAsync();
    }
}
