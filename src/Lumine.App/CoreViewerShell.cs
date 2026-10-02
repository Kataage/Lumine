using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private readonly WrapPanel _bulkActions;
    private Button? _lineageAction;
    private readonly TextBlock _selectionCount;
    private readonly TextBlock _selectionMetadataSummary;
    private readonly TextBlock _bulkStatus;
    private readonly ContextualAssetDetailPanel _contextDetail;
    private readonly Border _contextSurface;
    private readonly Border _focusedSurface;
    private CancellationTokenSource? _selectionSummaryCancellation;
    private bool _compactInspectorLayout;
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
                _afterBulkMutation,
                ShowCreateWorkDialogAsync,
                ShowCreateGenerationGroupDialogAsync,
                ShowCreatePublicationDialogAsync);
        _contextSurface =
            new Border
            {
                Width = 360,
                MinWidth = 300,
                MaxWidth = 420,
                Background = LumineDesign.Surface,
                BorderBrush = LumineDesign.Border,
                BorderThickness =
                    new Thickness(1, 0, 0, 0),
                IsVisible = false,
                Child = _contextDetail
            };

        var focusedClose =
            LumineDesign.ConfigureIconButton(
                new Button
                {
                    Content =
                        LumineDesign.CreateStrokeIcon(
                            LumineDesign.CloseIconPath,
                            18,
                            LumineDesign.Foreground)
                },
                "閉じる (Esc)",
                automationId: "viewer.close",
                acceleratorKey: "Esc");
        focusedClose.Background =
            LumineDesign.ControlSurface;
        focusedClose.BorderBrush =
            LumineDesign.BorderStrong;
        focusedClose.Click +=
            (_, _) =>
                CloseFocusedView();

        var focusedHeader =
            new Border
            {
                Background = Brushes.Transparent,
                Margin = new Thickness(10),
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                VerticalAlignment =
                    VerticalAlignment.Top,
                Child = focusedClose
            };

        var focusedLayout =
            new Grid
            {
                Background = LumineDesign.Background
            };
        focusedLayout.Children.Add(_detail);
        focusedLayout.Children.Add(
            focusedHeader);

        _focusedSurface =
            new Border
            {
                Background = LumineDesign.Background,
                Padding = new Thickness(0),
                IsVisible = false,
                Focusable = true,
                ClipToBounds = true,
                Child = focusedLayout
            };
        _focusedSurface.SetValue(
            KeyboardNavigation.TabNavigationProperty,
            KeyboardNavigationMode.Cycle);
        _focusedSurface.KeyDown +=
            (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    CloseFocusedView();
                    e.Handled = true;
                }
            };

        _bulkActions =
            new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                IsVisible = false
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
                FontSize = LumineDesign.CaptionFontSize,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        _bulkStatus =
            new TextBlock
            {
                Foreground = LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        _selectionBar = CreateSelectionBar();
        _selectionBar.IsVisible = false;

        _grid.SelectionChanged += OnSelectionChanged;
        _grid.AssetInvoked += OnAssetInvoked;
        _detail.FullScreenToggleRequested +=
            OnFullScreenToggleRequested;
        _grid.AssetDetailRequested += OnAssetDetailRequested;
        _grid.AssetContextRequested += OnAssetContextRequested;
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

        void ApplyResponsiveBrowseLayout(double width)
        {
            _compactInspectorLayout =
                width <= 1080;

            if (_compactInspectorLayout)
            {
                browseViewer.ColumnDefinitions =
                    new ColumnDefinitions("*");
                Grid.SetColumn(_contextSurface, 0);
                _contextSurface.HorizontalAlignment =
                    HorizontalAlignment.Right;
                _contextSurface.Width =
                    Math.Clamp(
                        width * 0.44,
                        300,
                        400);
            }
            else
            {
                browseViewer.ColumnDefinitions =
                    new ColumnDefinitions("*,Auto");
                Grid.SetColumn(_contextSurface, 1);
                _contextSurface.HorizontalAlignment =
                    HorizontalAlignment.Stretch;
                _contextSurface.Width =
                    Math.Clamp(
                        width * 0.28,
                        320,
                        400);
            }
        }

        SizeChanged +=
            (_, e) =>
                ApplyResponsiveBrowseLayout(
                    e.NewSize.Width);
        ApplyResponsiveBrowseLayout(
            Math.Max(1100, Bounds.Width));

        var browseLayout =
            new Grid
            {
                Background = LumineDesign.Background,
                RowDefinitions =
                    new RowDefinitions("Auto,*")
            };
        browseLayout.Children.Add(
            _selectionBar);
        Grid.SetRow(browseViewer, 1);
        browseLayout.Children.Add(
            browseViewer);

        var layers =
            new Grid
            {
                Background = LumineDesign.Background
            };
        layers.Children.Add(
            browseLayout);

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

    internal bool IsBulkSelectionBarVisible =>
        _selectionBar.IsVisible;


    internal bool IsCompactInspectorLayout =>
        _compactInspectorLayout;

    internal Rect ContextSurfaceBounds =>
        _contextSurface.Bounds;

    internal Rect GridViewerBounds =>
        _grid.Bounds;


    internal (
        Rect ImageBounds,
        Rect ViewportBounds)
        FocusedViewerGeometryForSmoke
    {
        get
        {
            var image =
                _detail.GetVisualDescendants()
                    .OfType<Avalonia.Controls.Image>()
                    .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "Focused viewer image visual is unavailable.");
            var viewport =
                _detail.GetVisualDescendants()
                    .OfType<ScrollViewer>()
                    .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "Focused viewer viewport is unavailable.");

            var imageOrigin =
                image.TranslatePoint(
                    new Point(0, 0),
                    _detail)
                ?? throw new InvalidOperationException(
                    "Unable to map focused image bounds.");
            var viewportOrigin =
                viewport.TranslatePoint(
                    new Point(0, 0),
                    _detail)
                ?? throw new InvalidOperationException(
                    "Unable to map focused viewport bounds.");

            return (
                new Rect(
                    imageOrigin,
                    image.Bounds.Size),
                new Rect(
                    viewportOrigin,
                    viewport.Bounds.Size));
        }
    }

    internal bool IsAssetFocusedForSmoke(
        long index) =>
        _grid.IsAssetFocused(index);

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

        var restoreFocusTarget =
            _grid.GetAssetFocusTarget(index)
            ?? _grid;
        restoreFocusTarget.Focus();

        _focusedSurface.IsVisible = true;
        var owner =
            TopLevel.GetTopLevel(this)
            as MainWindow;
        owner?.ShowLightbox(
            _focusedSurface,
            restoreFocusTarget);

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
            owner?.HideLightbox(
                _focusedSurface);
            _focusedSurface.IsVisible = false;
            throw;
        }
    }

    private void OnFullScreenToggleRequested(
        object? sender,
        EventArgs e)
    {
        var owner =
            TopLevel.GetTopLevel(_focusedSurface)
            as MainWindow
            ?? TopLevel.GetTopLevel(this)
                as MainWindow;
        owner?.ToggleLightboxFullScreen();
    }

    internal void CloseFocusedView()
    {
        if (!_focusedSurface.IsVisible)
        {
            return;
        }

        var returnIndex =
            _detail.SelectedAssetIndex;
        var owner =
            TopLevel.GetTopLevel(_focusedSurface)
            as MainWindow
            ?? TopLevel.GetTopLevel(this)
                as MainWindow;
        _focusedSurface.IsVisible = false;
        _detail.UnbindGrid();
        _runtime.DetailSession.Clear();

        if (returnIndex >= 0)
        {
            _grid.ScrollToAsset(returnIndex);
        }

        owner?.HideLightbox(
            _focusedSurface);

        Dispatcher.UIThread.Post(
            () =>
            {
                if (_detached)
                {
                    return;
                }

                if (returnIndex >= 0
                    && _grid.FocusAsset(returnIndex))
                {
                    return;
                }

                _grid.Focus();
            },
            DispatcherPriority.Input);
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

    private void OnAssetContextRequested(
        object? sender,
        ThumbnailViewerControl.ViewerAssetContextRequestedEventArgs request)
    {
        var menu =
            new ContextMenu
            {
                Placement = PlacementMode.Pointer
            };

        menu.Items.Add(
            CreateContextMenuItem(
                "画像を表示",
                () => OpenFocusedViewAsync(
                    request.Index)));
        menu.Items.Add(
            CreateContextMenuItem(
                "詳細",
                async () =>
                {
                    _grid.SelectAsset(
                        request.Index,
                        scrollIntoView: false);
                    await ShowContextDetailAsync();
                }));
        menu.Items.Add(new Separator());

        var rating =
            new MenuItem
            {
                Header = "評価"
            };
        rating.Items.Add(
            CreateContextMenuItem(
                "評価なし",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        SetRating: true,
                        Rating: null))));
        for (var value = 1; value <= 5; value++)
        {
            var ratingValue = value;
            rating.Items.Add(
                CreateContextMenuItem(
                    new string('★', ratingValue),
                    () => ApplyPatchAsync(
                        new AssetUserMetadataPatch(
                            SetRating: true,
                            Rating: ratingValue))));
        }

        menu.Items.Add(rating);
        menu.Items.Add(
            CreateContextMenuItem(
                "お気に入りを切り替え",
                () => ToggleFavoriteFromAssetAsync(
                    request.Index)));

        menu.Items.Add(new Separator());
        menu.Items.Add(
            CreateContextMenuItem(
                "Workを作成",
                ShowCreateWorkDialogAsync));
        menu.Items.Add(
            CreateContextMenuItem(
                "生成グループを作成",
                ShowCreateGenerationGroupDialogAsync));
        menu.Items.Add(
            CreateContextMenuItem(
                "公開記録を作成",
                ShowCreatePublicationDialogAsync));

        if (_grid.SelectedAssetCount == 2)
        {
            menu.Items.Add(
                CreateContextMenuItem(
                    "Lineageを作成",
                    ShowCreateRelationDialogAsync));
        }

        menu.Items.Add(new Separator());
        var delete =
            CreateContextMenuItem(
                "元ファイルを削除…",
                DeleteSelectedSourcesAsync);
        delete.Foreground =
            LumineDesign.Danger;
        menu.Items.Add(delete);

        menu.Open(request.Anchor);
    }

    private MenuItem CreateContextMenuItem(
        string label,
        Func<Task> action)
    {
        var item =
            new MenuItem
            {
                Header = label
            };
        item.Click +=
            async (_, _) =>
            {
                item.IsEnabled = false;
                try
                {
                    await action();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    _bulkStatus.Foreground =
                        LumineDesign.Danger;
                    _bulkStatus.Text =
                        $"操作できませんでした: {exception.Message}";
                }
                finally
                {
                    item.IsEnabled = true;
                }
            };
        return item;
    }

    private async Task ToggleFavoriteFromAssetAsync(
        long index)
    {
        var asset =
            await _runtime.ViewerSession.GetAssetAsync(
                index);
        var metadata =
            await _runtime.LibraryService
                .GetUserMetadataAsync(
                    _runtime.Library.Id,
                    asset.Id);
        var current =
            metadata?.Favorite
            ?? asset.Favorite;

        await ApplyPatchAsync(
            new AssetUserMetadataPatch(
                SetFavorite: true,
                Favorite: !current));
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
                $"画像を表示できませんでした: {exception.Message}";
        }
    }

    private async void OnAssetDetailRequested(
        object? sender,
        long index)
    {
        try
        {
            if (_grid.SelectedAssetIndex != index)
            {
                _grid.SelectAsset(
                    index,
                    scrollIntoView: false);
            }

            _contextSurface.IsVisible = true;
            await LoadContextDetailAsync(index);
        }
        catch (Exception exception)
        {
            _bulkStatus.Text =
                $"詳細を表示できませんでした: {exception.Message}";
        }
    }

    private Border CreateSelectionBar()
    {
        var rating =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    MinWidth = 150,
                    ItemsSource =
                        new[]
                        {
                            "評価なし",
                            "★1",
                            "★2",
                            "★3",
                            "★4",
                            "★5"
                        },
                    SelectedIndex = 0
                });
        var ratingApply =
            CreateBulkButton(
                "評価を適用",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        SetRating: true,
                        Rating:
                            rating.SelectedIndex > 0
                                ? rating.SelectedIndex
                                : null)));

        var favoriteOn =
            CreateBulkButton(
                "お気に入りにする",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        SetFavorite: true,
                        Favorite: true)));
        var favoriteOff =
            CreateBulkButton(
                "お気に入りを解除",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        SetFavorite: true,
                        Favorite: false)));

        var statusLabels =
            new[]
            {
                "未整理",
                "確認済み",
                "候補",
                "公開済み"
            };
        var statusValues =
            new[]
            {
                "unsorted",
                "reviewed",
                "candidate",
                "published"
            };
        var status =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    MinWidth = 150,
                    ItemsSource = statusLabels,
                    SelectedIndex = 0
                });
        var statusApply =
            CreateBulkButton(
                "状態を適用",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        SetStatusLabel: true,
                        StatusLabel:
                            statusValues[
                                Math.Max(
                                    0,
                                    status.SelectedIndex)])));

        var colorLabels =
            new[]
            {
                "赤",
                "オレンジ",
                "黄",
                "緑",
                "青",
                "紫",
                "グレー"
            };
        var colorValues =
            new[]
            {
                "red",
                "orange",
                "yellow",
                "green",
                "blue",
                "purple",
                "gray"
            };
        var color =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    MinWidth = 150,
                    ItemsSource = colorLabels,
                    SelectedIndex = 4
                });
        var colorApply =
            CreateBulkButton(
                "カラーを適用",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        SetColorLabel: true,
                        ColorLabel:
                            colorValues[
                                Math.Max(
                                    0,
                                    color.SelectedIndex)])));

        var tag =
            LumineDesign.ConfigureTextBox(
                new TextBox
                {
                    MinWidth = 180,
                    PlaceholderText = "タグ"
                });
        var tagAdd =
            CreateBulkButton(
                "追加",
                async () =>
                {
                    var value =
                        tag.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return;
                    }

                    await ApplyPatchAsync(
                        new AssetUserMetadataPatch(
                            AddTags: [value]));
                    tag.Text = string.Empty;
                });
        var tagClear =
            CreateBulkButton(
                "タグをすべて解除",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        ClearTags: true)));

        var organizationPanel =
            new StackPanel
            {
                Width = 280,
                Spacing = 8,
                Margin = new Thickness(4)
            };
        organizationPanel.Children.Add(
            new TextBlock
            {
                Text = "整理",
                FontSize = LumineDesign.BodyFontSize,
                FontWeight = FontWeight.SemiBold,
                Foreground = LumineDesign.Foreground
            });
        organizationPanel.Children.Add(rating);
        organizationPanel.Children.Add(ratingApply);
        organizationPanel.Children.Add(favoriteOn);
        organizationPanel.Children.Add(favoriteOff);
        organizationPanel.Children.Add(status);
        organizationPanel.Children.Add(statusApply);
        organizationPanel.Children.Add(color);
        organizationPanel.Children.Add(colorApply);

        var tagRow =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 6
            };
        tagRow.Children.Add(tag);
        Grid.SetColumn(tagAdd, 1);
        tagRow.Children.Add(tagAdd);
        organizationPanel.Children.Add(tagRow);
        organizationPanel.Children.Add(tagClear);

        var organizationFlyout =
            new Flyout
            {
                Content =
                    new Border
                    {
                        Background =
                            LumineDesign.SurfaceRaised,
                        Padding = new Thickness(10),
                        Child = organizationPanel
                    }
            };

        var organize =
            LumineDesign.ConfigureSecondaryButton(
                new DropDownButton
                {
                    Content = "整理",
                    Flyout = organizationFlyout,
                    MinWidth = 76
                });

        var creativePanel =
            new StackPanel
            {
                Width = 230,
                Spacing = 6,
                Margin = new Thickness(4)
            };
        creativePanel.Children.Add(
            new TextBlock
            {
                Text = "制作",
                FontSize = LumineDesign.BodyFontSize,
                FontWeight = FontWeight.SemiBold,
                Foreground = LumineDesign.Foreground
            });
        creativePanel.Children.Add(
            CreateBulkButton(
                "Workを作成",
                ShowCreateWorkDialogAsync));
        creativePanel.Children.Add(
            CreateBulkButton(
                "生成グループを作成",
                ShowCreateGenerationGroupDialogAsync));
        _lineageAction =
            CreateBulkButton(
                "Lineageを作成",
                ShowCreateRelationDialogAsync);
        creativePanel.Children.Add(
            _lineageAction);
        creativePanel.Children.Add(
            CreateBulkButton(
                "公開記録を作成",
                ShowCreatePublicationDialogAsync));

        var delete =
            CreateBulkButton(
                "元ファイルを削除…",
                DeleteSelectedSourcesAsync);
        delete.Foreground =
            LumineDesign.Danger;
        creativePanel.Children.Add(delete);

        var creativeFlyout =
            new Flyout
            {
                Content =
                    new Border
                    {
                        Background =
                            LumineDesign.SurfaceRaised,
                        Padding = new Thickness(10),
                        Child = creativePanel
                    }
            };

        var creative =
            LumineDesign.ConfigureSecondaryButton(
                new DropDownButton
                {
                    Content = "制作",
                    Flyout = creativeFlyout,
                    MinWidth = 76
                });

        var clear =
            CreateBulkButton(
                "選択解除",
                () =>
                {
                    _grid.ClearSelection();
                    return Task.CompletedTask;
                });

        organize.Margin = new Thickness(3, 0);
        creative.Margin = new Thickness(3, 0);
        clear.Margin = new Thickness(3, 0);
        _bulkActions.Children.Add(organize);
        _bulkActions.Children.Add(creative);
        _bulkActions.Children.Add(clear);

        var top =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "Auto,*,Auto"),
                ColumnSpacing = 10,
                Margin = new Thickness(10, 6)
            };
        top.Children.Add(_selectionCount);

        var summary =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        summary.Children.Add(
            _selectionMetadataSummary);
        summary.Children.Add(
            _bulkStatus);
        Grid.SetColumn(summary, 1);
        top.Children.Add(summary);

        Grid.SetColumn(_bulkActions, 2);
        top.Children.Add(_bulkActions);

        return new Border
        {
            Background = LumineDesign.SurfaceRaised,
            BorderBrush = LumineDesign.Border,
            BorderThickness =
                new Thickness(0, 0, 0, 1),
            Child = top
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
                    FontSize = LumineDesign.CaptionFontSize,
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
        var isBulk =
            selection.Count > 1;
        _selectionBar.IsVisible =
            isBulk;
        if (_lineageAction is not null)
        {
            _lineageAction.IsEnabled =
                selection.Count == 2;
        }
        _bulkActions.IsVisible = isBulk;
        _selectionCount.IsVisible = isBulk;
        _selectionMetadataSummary.IsVisible = isBulk;
        _selectionCount.Text =
            isBulk
                ? $"{selection.Count:N0}件"
                : string.Empty;
        _bulkStatus.Text = string.Empty;
        const int detailedSummaryLimit = 256;
        var canSummarize =
            isBulk
            && selection.Count <= detailedSummaryLimit;
        _selectionMetadataSummary.Text =
            canSummarize
                ? "整理情報を確認中…"
                : isBulk
                    ? "複数選択"
                    : string.Empty;

        _selectionSummaryCancellation?.Cancel();
        _selectionSummaryCancellation?.Dispose();
        _selectionSummaryCancellation =
            canSummarize
                ? new CancellationTokenSource()
                : null;

        if (canSummarize)
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

        var assetIds =
            await ResolveSelectedAssetIdsAsync(
                cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var summary =
            await _runtime.LibraryService
                .GetUserMetadataSelectionSummaryAsync(
                    _runtime.Library.Id,
                    assetIds,
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

    private ValueTask<IReadOnlyList<long>>
        ResolveSelectedAssetIdsAsync(
            CancellationToken cancellationToken = default) =>
        _runtime.ViewerSession.GetAssetIdsAsync(
            _grid.SelectedAssetIndices,
            cancellationToken);

    private async Task ApplyPatchAsync(
        AssetUserMetadataPatch patch)
    {
        var assetIds =
            await ResolveSelectedAssetIdsAsync();
        if (assetIds.Count == 0)
        {
            return;
        }

        _bulkStatus.Text =
            "更新しています…";

        var updated =
            await _runtime.LibraryService.PatchUserMetadataAsync(
                _runtime.Library.Id,
                assetIds,
                patch);

        _bulkStatus.Text =
            $"{updated:N0}件を更新しました。";

        if (_afterBulkMutation is not null)
        {
            await _afterBulkMutation();
        }
    }

    internal async Task<WorkInfo?> CreateWorkFromSelectionAsync(
        CreativeWorkDialogResult input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var assetIds =
            await ResolveSelectedAssetIdsAsync();
        if (assetIds.Count == 0)
        {
            return null;
        }

        var created =
            await _runtime.LibraryService.CreateWorkAsync(
                _runtime.Library.Id,
                new WorkCreate(
                    input.Title,
                    input.Description,
                    assetIds));
        _bulkStatus.Text =
            $"Work「{created.Title}」を作成しました。";
        await RefreshContextAfterCreativeMutationAsync();
        return created;
    }

    internal async Task<GenerationGroupInfo?>
        CreateGenerationGroupFromSelectionAsync(
            CreativeGroupDialogResult input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var assetIds =
            await ResolveSelectedAssetIdsAsync();
        if (assetIds.Count == 0)
        {
            return null;
        }

        var created =
            await _runtime.LibraryService
                .CreateGenerationGroupAsync(
                    _runtime.Library.Id,
                    new GenerationGroupCreate(
                        input.Name,
                        assetIds,
                        WorkId: input.WorkId,
                        Prompt: input.Prompt,
                        NegativePrompt:
                            input.NegativePrompt,
                        ModelName: input.ModelName,
                        Sampler: input.Sampler,
                        Scheduler: input.Scheduler,
                        Steps: input.Steps,
                        CfgScale: input.CfgScale,
                        WorkflowJson:
                            input.WorkflowJson,
                        Notes: input.Notes));
        _bulkStatus.Text =
            $"Generation Group「{created.Name}」を作成しました。";
        await RefreshContextAfterCreativeMutationAsync();
        return created;
    }

    internal async Task<AssetRelationInfo?>
        CreateRelationFromSelectionAsync(
            CreativeRelationDialogResult input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var assets =
            await ResolveSelectedAssetsAsync();
        if (assets.Count != 2)
        {
            _bulkStatus.Text =
                "Lineageは2枚を選択して作成してください。";
            return null;
        }

        var parent =
            input.ReverseDirection
                ? assets[1]
                : assets[0];
        var child =
            input.ReverseDirection
                ? assets[0]
                : assets[1];

        var created =
            await _runtime.LibraryService
                .CreateAssetRelationAsync(
                    _runtime.Library.Id,
                    new AssetRelationCreate(
                        parent.Id,
                        child.Id,
                        input.RelationType,
                        input.Note));
        _bulkStatus.Text =
            $"{created.Parent.FileName} → {created.Child.FileName} · {created.RelationType} を保存しました。";
        await RefreshContextAfterCreativeMutationAsync();
        return created;
    }

    internal async Task<PublicationInfo?>
        CreatePublicationFromSelectionAsync(
            CreativePublicationDialogResult input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var assetIds =
            await ResolveSelectedAssetIdsAsync();
        if (assetIds.Count == 0)
        {
            return null;
        }

        var created =
            await _runtime.LibraryService
                .CreatePublicationAsync(
                    _runtime.Library.Id,
                    new PublicationCreate(
                        assetIds,
                        input.Destination,
                        input.PublishedAtUtc,
                        WorkId: input.WorkId,
                        Title: input.Title,
                        Body: input.Body,
                        TagsSnapshot: input.Tags,
                        Account: input.Account,
                        ExternalId: input.ExternalId,
                        ExternalUrl: input.ExternalUrl,
                        PlatformMetadataJson:
                            input.PlatformMetadataJson));
        _bulkStatus.Text =
            $"Publicationを{created.Destination}の履歴へ保存しました。";
        await RefreshContextAfterCreativeMutationAsync();
        _entryRequested?.Invoke(
            "publication");
        return created;
    }

    private async Task RefreshContextAfterCreativeMutationAsync()
    {
        if (!_contextSurface.IsVisible
            || _grid.SelectedAssetIndex < 0)
        {
            return;
        }

        await LoadContextDetailAsync(
            _grid.SelectedAssetIndex);
    }

    private async Task ShowCreateWorkDialogAsync()
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
            return;
        }

        var input =
            await CreativeArchiveDialogs.ShowWorkAsync(
                owner,
                assets);
        if (input is not null)
        {
            await CreateWorkFromSelectionAsync(
                input);
        }
    }

    private async Task ShowCreateGenerationGroupDialogAsync()
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
            return;
        }

        var works =
            await _runtime.LibraryService.ListWorksAsync(
                _runtime.Library.Id);
        var input =
            await CreativeArchiveDialogs
                .ShowGenerationGroupAsync(
                    owner,
                    assets,
                    works);
        if (input is not null)
        {
            await CreateGenerationGroupFromSelectionAsync(
                input);
        }
    }

    private async Task ShowCreateRelationDialogAsync()
    {
        var assets =
            await ResolveSelectedAssetsAsync();
        if (assets.Count != 2)
        {
            _bulkStatus.Text =
                "Lineageは2枚を選択してください。";
            return;
        }

        var owner =
            TopLevel.GetTopLevel(this)
                as Window;
        if (owner is null)
        {
            return;
        }

        var input =
            await CreativeArchiveDialogs.ShowRelationAsync(
                owner,
                assets);
        if (input is not null)
        {
            await CreateRelationFromSelectionAsync(
                input);
        }
    }

    private async Task ShowCreatePublicationDialogAsync()
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
            return;
        }

        var works =
            await _runtime.LibraryService.ListWorksAsync(
                _runtime.Library.Id);
        var input =
            await CreativeArchiveDialogs
                .ShowPublicationAsync(
                    owner,
                    assets,
                    works);
        if (input is not null)
        {
            await CreatePublicationFromSelectionAsync(
                input);
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
            await ProductDialogs.ConfirmAsync(
                owner,
                "元ファイルを削除しますか？",
                $"{assets.Count:N0}件の元画像ファイルをディスクから削除します。これはLumineの登録解除ではなく、実ファイルの削除です。",
                "この操作はLumineから元に戻せません。Work / Generation Group / Publication等の履歴は、参照可能なsnapshotを保持する場合があります。",
                confirmLabel: "元ファイルを削除",
                tone: ProductDialogTone.Danger);
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

        if (e.Key == Key.I)
        {
            e.Handled = true;
            await ShowContextDetailAsync();
            return;
        }

        if (e.Key == Key.F)
        {
            e.Handled = true;
            await ToggleFavoriteAsync();
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

    private async Task ToggleFavoriteAsync()
    {
        var primaryIndex =
            _grid.SelectedAssetIndex;
        if (primaryIndex < 0)
        {
            return;
        }

        var asset =
            await _runtime.ViewerSession.GetAssetAsync(
                primaryIndex);
        var metadata =
            await _runtime.LibraryService
                .GetUserMetadataAsync(
                    _runtime.Library.Id,
                    asset.Id);

        var current =
            metadata?.Favorite
            ?? asset.Favorite;

        await ApplyPatchAsync(
            new AssetUserMetadataPatch(
                SetFavorite: true,
                Favorite: !current));
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
        if (_focusedSurface.IsVisible)
        {
            CloseFocusedView();
        }

        _grid.SelectionChanged -= OnSelectionChanged;
        _grid.AssetInvoked -= OnAssetInvoked;
        _grid.AssetDetailRequested -= OnAssetDetailRequested;
        _grid.AssetContextRequested -= OnAssetContextRequested;
        _detail.FullScreenToggleRequested -=
            OnFullScreenToggleRequested;
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
