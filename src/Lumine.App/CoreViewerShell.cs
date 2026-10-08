using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lumine.Core;
using Lumine.Library;
using Lumine.Viewer;

namespace Lumine.App;

internal sealed record CoreViewerQueryUiState(
    IReadOnlyList<long> SelectedAssetIds,
    long? PrimaryAssetId,
    bool InspectorVisible,
    bool FocusedVisible);

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
    private TextBox? _bulkTagSearch;
    private StackPanel? _bulkTagCandidates;
    private IReadOnlyList<LibraryTagInfo> _bulkTagChoices =
        Array.Empty<LibraryTagInfo>();
    private readonly ContextualAssetDetailPanel _contextDetail;
    private readonly Border _contextSurface;
    private readonly Grid _browseViewer;
    private readonly Border _focusedSurface;
    private readonly ContentControl _noMatchSurface;
    private Func<Task>? _clearNoMatchFilters;
    private Action? _editNoMatchFilters;
    private CancellationTokenSource? _selectionSummaryCancellation;
    private CancellationTokenSource? _bulkOperationCancellation;
    private Button? _cancelBulkOperationButton;
    private long _focusedViewReturnIndex = -1;
    private bool _compactInspectorLayout;
    private bool _inspectorPinned;
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
                ShowCreatePublicationDialogAsync,
                ShowAddToExistingWorkDialogAsync,
                ShowAddToExistingGenerationGroupDialogAsync,
                DeleteRelationFromInspectorAsync);
        _contextDetail.PinToggleRequested +=
            (_, _) =>
            {
                if (_compactInspectorLayout)
                {
                    return;
                }

                _inspectorPinned =
                    !_inspectorPinned;
                ApplyInspectorLayout(
                    ResolveInspectorLayoutWidth());
            };

        _contextSurface =
            new Border
            {
                Width = 360,
                MinWidth = 300,
                MaxWidth = 400,
                ClipToBounds = true,
                IsVisible = false,
                Child = _contextDetail
            };
        _contextSurface.Classes.Add("lumine-inspector-surface");

        var focusedLayout = new Grid();
        focusedLayout.Classes.Add("lumine-canvas-grid");
        focusedLayout.Children.Add(_detail);

        _focusedSurface =
            new Border
            {
                IsVisible = false,
                Focusable = true,
                ClipToBounds = true,
                Child = focusedLayout
            };
        _focusedSurface.Classes.Add("lumine-canvas-surface");
        _focusedSurface.SetValue(
            KeyboardNavigation.TabNavigationProperty,
            KeyboardNavigationMode.Cycle);
        _focusedSurface.KeyDown +=
            (_, e) =>
            {
                if (!e.Handled
                    && e.Key == Key.Escape
                    && e.KeyModifiers == KeyModifiers.None)
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
        _bulkActions.Classes.Add("lumine-bulk-primary-actions");

        _selectionCount =
            new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center
            };
        _selectionCount.Classes.Add("lumine-selection-count");
        _selectionMetadataSummary =
            new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        _selectionMetadataSummary.Classes.Add("lumine-muted-caption");
        _bulkStatus =
            new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        _bulkStatus.Classes.Add("lumine-muted-caption");
        _bulkStatus.IsVisible = false;
        _bulkStatus.PropertyChanged +=
            (_, change) =>
            {
                if (change.Property == TextBlock.TextProperty)
                {
                    _bulkStatus.IsVisible =
                        !string.IsNullOrWhiteSpace(_bulkStatus.Text);
                }
            };
        _selectionBar = CreateSelectionBar();
        _selectionBar.IsVisible = false;

        var clearNoMatch =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "条件をすべて解除"
                });
        clearNoMatch.Click +=
            async (_, _) =>
            {
                if (_clearNoMatchFilters is not null)
                {
                    await _clearNoMatchFilters();
                }
            };

        var editNoMatch =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "フィルターを見直す"
                });
        editNoMatch.Click +=
            (_, _) =>
                _editNoMatchFilters?.Invoke();

        var noMatchActions =
            new WrapPanel
            {
                HorizontalAlignment =
                    HorizontalAlignment.Center
            };
        clearNoMatch.Margin =
            new Thickness(
                0,
                0,
                LumineDesign.Space8,
                LumineDesign.Space8);
        editNoMatch.Margin =
            new Thickness(
                0,
                0,
                0,
                LumineDesign.Space8);
        noMatchActions.Children.Add(
            clearNoMatch);
        noMatchActions.Children.Add(
            editNoMatch);

        var noMatchActionHost =
            new Border
            {
                Child = noMatchActions
            };

        _noMatchSurface =
            new ContentControl
            {
                IsVisible = false,
                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,
                VerticalContentAlignment =
                    VerticalAlignment.Stretch,
                Content =
                    LumineDesign.CreateProductState(
                        "一致する画像がありません",
                        "条件を解除するか、フィルターを見直してください。",
                        noMatchActionHost)
            };

        _grid.SelectionChanged += OnSelectionChanged;
        _grid.AssetInvoked += OnAssetInvoked;
        _detail.FullScreenToggleRequested +=
            OnFullScreenToggleRequested;
        _detail.CloseRequested +=
            (_, _) => CloseFocusedView();
        _grid.AssetDetailRequested += OnAssetDetailRequested;
        _grid.AssetContextRequested += OnAssetContextRequested;
        KeyDown += OnShellKeyDown;
        Focusable = true;
        Classes.Add("lumine-canvas-shell");

        if (runtime.AssetCount == 0)
        {
            Content =
                LumineDesign.CreateProductState(
                    "画像がありません",
                    "このフォルダーには、Lumineで表示できる画像が見つかりませんでした。");
            return;
        }

        var gridSurface = new Border { Child = _grid };
        gridSurface.Classes.Add("lumine-canvas-surface");

        _browseViewer =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*")
            };
        _browseViewer.Classes.Add("lumine-canvas-grid");
        _browseViewer.Children.Add(
            gridSurface);
        Grid.SetColumn(
            _noMatchSurface,
            0);
        _noMatchSurface.ZIndex = 10;
        _browseViewer.Children.Add(
            _noMatchSurface);
        Grid.SetColumn(_contextSurface, 0);
        _contextSurface.ZIndex = 20;
        _browseViewer.Children.Add(
            _contextSurface);

        SizeChanged +=
            (_, e) =>
                ApplyInspectorLayout(
                    e.NewSize.Width);
        ApplyInspectorLayout(
            Math.Max(1100, Bounds.Width));

        var browseLayout = new Grid();
        browseLayout.Classes.Add("lumine-canvas-grid");
        browseLayout.Children.Add(
            _browseViewer);

        // Multi-selection is contextual chrome, not layout. Keep the image
        // canvas fixed in place while the action surface floats above it.
        _selectionBar.HorizontalAlignment =
            HorizontalAlignment.Left;
        _selectionBar.VerticalAlignment =
            VerticalAlignment.Bottom;
        _selectionBar.MaxWidth = 600;
        _selectionBar.Margin =
            new Thickness(16, 16, 16, 18);
        _selectionBar.ZIndex = 30;
        browseLayout.Children.Add(
            _selectionBar);

        var layers = new Grid();
        layers.Classes.Add("lumine-canvas-grid");
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

    internal bool IsNoMatchStateVisibleForSmoke =>
        _noMatchSurface.IsVisible;

    internal void SetNoMatchState(
        bool visible,
        Func<Task>? clearFilters = null,
        Action? editFilters = null)
    {
        _clearNoMatchFilters =
            visible
                ? clearFilters
                : null;
        _editNoMatchFilters =
            visible
                ? editFilters
                : null;
        _noMatchSurface.IsVisible =
            visible;
        _grid.IsHitTestVisible =
            !visible;

        if (visible)
        {
            HideContextDetail();
            _grid.ClearSelection();
        }
    }

    internal bool IsBulkSelectionBarVisible =>
        _selectionBar.IsVisible;


    internal bool IsCompactInspectorLayout =>
        _compactInspectorLayout;

    internal bool IsInspectorPinnedForSmoke =>
        _inspectorPinned;

    internal void SetInspectorPinnedForSmoke(
        bool pinned)
    {
        _inspectorPinned = pinned;
        ApplyInspectorLayout(
            ResolveInspectorLayoutWidth());
    }

    internal Rect ContextSurfaceBounds =>
        _contextSurface.Bounds;

    internal Rect GridViewerBounds =>
        _grid.Bounds;

    internal bool BulkSelectionCommandsAccessibleForSmoke
    {
        get
        {
            var commands =
                _bulkActions.Children
                    .OfType<DropDownButton>()
                    .ToArray();
            return commands.Length == 3
                && commands.Select(
                        static button => button.Content as string)
                    .SequenceEqual(
                        new[] { "タグ", "整理", "その他" })
                && commands.All(
                    static button =>
                        button.Flyout is Flyout { Content: not null })
                && _selectionBar.GetVisualDescendants()
                    .OfType<Button>()
                    .Any(
                        static button =>
                            AutomationProperties.GetName(button)
                                == "選択解除");
        }
    }

    internal bool SelectionToolbarAvoidsInspectorForSmoke
    {
        get
        {
            if (!_selectionBar.IsVisible
                || !_contextSurface.IsVisible)
            {
                return true;
            }

            var toolbarOrigin =
                _selectionBar.TranslatePoint(
                    new Point(0, 0), this);
            var inspectorOrigin =
                _contextSurface.TranslatePoint(
                    new Point(0, 0), this);
            return toolbarOrigin is { } toolbar
                && inspectorOrigin is { } inspector
                && toolbar.X + _selectionBar.Bounds.Width
                    <= inspector.X - 8;
        }
    }

    internal bool SelectionToolbarIsContainedForSmoke
    {
        get
        {
            if (!_selectionBar.IsVisible)
            {
                return true;
            }

            var origin =
                _bulkActions.TranslatePoint(
                    new Point(0, 0),
                    _selectionBar);
            if (origin is not { } point)
            {
                return false;
            }

            var bounds =
                new Rect(
                    point,
                    _bulkActions.Bounds.Size);
            if (bounds.Left < -0.5
                || bounds.Top < -0.5
                || bounds.Right
                    > _selectionBar.Bounds.Width + 0.5
                || bounds.Bottom
                    > _selectionBar.Bounds.Height + 0.5)
            {
                return false;
            }

            foreach (var child in
                     _bulkActions.Children
                         .OfType<Control>()
                         .Where(
                             static control =>
                                 control.IsEffectivelyVisible))
            {
                var childOrigin =
                    child.TranslatePoint(
                        new Point(0, 0),
                        _selectionBar);
                if (childOrigin is not { } childPoint)
                {
                    return false;
                }

                var childBounds =
                    new Rect(
                        childPoint,
                        child.Bounds.Size);
                if (childBounds.Left < -0.5
                    || childBounds.Top < -0.5
                    || childBounds.Right
                        > _selectionBar.Bounds.Width + 0.5
                    || childBounds.Bottom
                        > _selectionBar.Bounds.Height + 0.5)
                {
                    return false;
                }
            }

            var rowTops =
                _bulkActions.Children
                    .OfType<Control>()
                    .Where(static control => control.IsEffectivelyVisible)
                    .Select(control =>
                        control.TranslatePoint(new Point(0, 0), _selectionBar))
                    .ToArray();
            return rowTops.Length == 3
                && rowTops.All(static point => point is not null)
                && rowTops.Max(static point => point!.Value.Y)
                    - rowTops.Min(static point => point!.Value.Y) < 1;
        }
    }

    internal double SelectionToolbarHeightForSmoke =>
        _selectionBar.Bounds.Height;

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

    private double ResolveInspectorLayoutWidth() =>
        Bounds.Width > 0
            ? Bounds.Width
            : 1100;

    private void ApplyInspectorLayout(
        double width)
    {
        // Keep the Inspector drawer-first through normal 1440-class
        // desktop layouts. A permanent right column is reserved for genuinely
        // wide canvases so navigation and metadata cannot squeeze the image
        // surface from both sides at the common desktop acceptance size.
        _compactInspectorLayout =
            width < 1600;

        if (_compactInspectorLayout)
        {
            _inspectorPinned = false;
        }

        var canDock =
            !_compactInspectorLayout
            && _inspectorPinned
            && _contextSurface.IsVisible;

        if (canDock)
        {
            _browseViewer.ColumnDefinitions =
                new ColumnDefinitions("*,Auto");
            Grid.SetColumn(
                _contextSurface,
                1);
            _contextSurface.HorizontalAlignment =
                HorizontalAlignment.Stretch;
            _contextSurface.VerticalAlignment =
                VerticalAlignment.Stretch;
            _contextSurface.Margin =
                new Thickness(0);
            _contextSurface.CornerRadius =
                new CornerRadius(0);
            _contextSurface.BorderBrush =
                LumineDesign.Border;
            _contextSurface.BorderThickness =
                new Thickness(1, 0, 0, 0);
            _contextSurface.Width =
                Math.Clamp(
                    width * 0.26,
                    320,
                    380);
            _contextSurface.ZIndex = 0;
        }
        else
        {
            _browseViewer.ColumnDefinitions =
                new ColumnDefinitions("*");
            Grid.SetColumn(
                _contextSurface,
                0);
            _contextSurface.HorizontalAlignment =
                HorizontalAlignment.Right;
            _contextSurface.VerticalAlignment =
                VerticalAlignment.Stretch;
            _contextSurface.Margin =
                new Thickness(
                    LumineDesign.Space12);
            _contextSurface.CornerRadius =
                new CornerRadius(
                    LumineDesign.PanelRadius);
            _contextSurface.BorderBrush =
                LumineDesign.BorderStrong;
            _contextSurface.BorderThickness =
                new Thickness(1);
            _contextSurface.Width =
                _compactInspectorLayout
                    ? Math.Clamp(
                        width * 0.38,
                        300,
                        340)
                    : Math.Clamp(
                        width * 0.26,
                        320,
                        380);
            _contextSurface.ZIndex = 20;
        }

        ApplySelectionBarPlacement(width);

        _contextDetail.SetCompactPresentation(
            _compactInspectorLayout);
        _contextDetail.SetPinPresentation(
            _inspectorPinned,
            !_compactInspectorLayout);
    }

    private void ApplySelectionBarPlacement(double width)
    {
        // The toolbar overlays the canvas, never the Inspector. When the
        // Inspector is docked we also reserve its actual column width.
        var inspectorReserve = _contextSurface.IsVisible
            ? _contextSurface.Width
                + (_compactInspectorLayout
                    ? 2 * LumineDesign.Space12
                    : 0)
                + 12
            : 0;
        _selectionBar.Width = Math.Min(
            600,
            Math.Max(
                250,
                width - inspectorReserve - 48));
    }

    internal async Task<CoreViewerQueryUiState>
        PrepareForQueryChangeAsync()
    {
        const int preservedSelectionLimit = 256;

        var selectedIndices =
            _grid.SelectedAssetIndices;
        IReadOnlyList<long> trackedIndices;
        if (selectedIndices.Count <= preservedSelectionLimit)
        {
            trackedIndices =
                selectedIndices.ToArray();
        }
        else if (_grid.SelectedAssetIndex >= 0)
        {
            trackedIndices =
                [_grid.SelectedAssetIndex];
        }
        else
        {
            trackedIndices =
                Array.Empty<long>();
        }

        IReadOnlyList<long> selectedAssetIds =
            trackedIndices.Count == 0
                ? Array.Empty<long>()
                : await _runtime.ViewerSession
                    .GetAssetIdsAsync(
                        trackedIndices);

        long? primaryAssetId = null;
        if (_grid.SelectedAssetIndex >= 0)
        {
            for (var index = 0;
                 index < trackedIndices.Count;
                 index++)
            {
                if (trackedIndices[index]
                    == _grid.SelectedAssetIndex)
                {
                    primaryAssetId =
                        selectedAssetIds[index];
                    break;
                }
            }
        }

        var state =
            new CoreViewerQueryUiState(
                selectedAssetIds,
                primaryAssetId,
                _contextSurface.IsVisible,
                _focusedSurface.IsVisible);

        _selectionSummaryCancellation?.Cancel();
        _selectionSummaryCancellation?.Dispose();
        _selectionSummaryCancellation = null;

        _grid.SelectionChanged -=
            OnSelectionChanged;
        _detail.UnbindGrid();
        _detail.PrepareForSessionRebind();
        await _grid.PrepareForSessionRebindAsync();

        return state;
    }

    internal async Task CompleteQueryChangeAsync(
        CoreViewerQueryUiState state,
        IReadOnlyDictionary<long, long> assetIndices)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(assetIndices);

        _grid.RebindSession(
            _runtime.ViewerSession);
        _detail.RebindSession(
            _runtime.DetailSession);
        _grid.SelectionChanged +=
            OnSelectionChanged;

        var restoredIndices =
            state.SelectedAssetIds
                .Where(assetIndices.ContainsKey)
                .Select(assetId =>
                    assetIndices[assetId])
                .Distinct()
                .ToArray();

        var primaryIndex =
            state.PrimaryAssetId is { } primaryAssetId
            && assetIndices.TryGetValue(
                primaryAssetId,
                out var mappedPrimary)
                ? mappedPrimary
                : restoredIndices.Length == 0
                    ? -1
                    : restoredIndices[0];

        _grid.RestoreSelection(
            restoredIndices,
            primaryIndex);

        if (state.InspectorVisible
            && primaryIndex < 0)
        {
            HideContextDetail();
        }

        if (state.FocusedVisible)
        {
            if (primaryIndex < 0)
            {
                _focusedViewReturnIndex = -1;
                CloseFocusedView();
            }
            else
            {
                _focusedViewReturnIndex =
                    primaryIndex;
                _focusedSurface.IsVisible = true;
                await _detail.SelectAsync(
                    primaryIndex);
                _detail.BindGrid(_grid);
                _detail.Focus();
            }
        }
    }

    internal bool IsAssetFocusedForSmoke(
        long index) =>
        _grid.IsAssetFocused(index);

    internal async Task ShowContextDetailAsync()
    {
        _contextSurface.IsVisible = true;
        ApplyInspectorLayout(
            ResolveInspectorLayoutWidth());

        if (_grid.SelectedAssetIndex < 0)
        {
            _contextDetail.ShowNoSelection();
            return;
        }

        await LoadContextDetailAsync(
            _grid.SelectedAssetIndex);
    }

    internal void HideContextDetail()
    {
        _contextSurface.IsVisible = false;
        ApplyInspectorLayout(
            ResolveInspectorLayoutWidth());
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

        // Keep a stable semantic return target instead of a Control instance.
        // Virtualization may recycle the invoking tile while the lightbox is
        // open; the asset index lets us resolve the current realized tile at
        // close time. Ensure the invoking asset has a realized focus target
        // before entering the modal viewer so the return contract is valid
        // even after DPI/layout changes.
        _focusedViewReturnIndex = index;
        await _grid.EnsureAssetFocusTargetAsync(index);
        _grid.FocusAsset(index);

        _focusedSurface.IsVisible = true;
        var owner =
            TopLevel.GetTopLevel(this)
            as MainWindow;
        owner?.ShowLightbox(
            _focusedSurface);

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
            // Clear focus while the lightbox subtree is still attached. If a
            // focused subtree is detached first, Avalonia's detach lifecycle
            // can clear a newly restored browse focus afterward.
            owner?.FocusManager.Focus(
                null!,
                NavigationMethod.Unspecified,
                KeyModifiers.None);
            owner?.HideLightbox(
                _focusedSurface);
            _focusedSurface.IsVisible = false;
            var returnIndex =
                _focusedViewReturnIndex;
            _focusedViewReturnIndex = -1;
            if (returnIndex >= 0)
            {
                _grid.RestoreAssetFocus(returnIndex);
            }
            else
            {
                _grid.Focus(
                    NavigationMethod.Unspecified,
                    KeyModifiers.None);
            }

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
            _focusedViewReturnIndex;
        _focusedViewReturnIndex = -1;
        var owner =
            TopLevel.GetTopLevel(_focusedSurface)
            as MainWindow
            ?? TopLevel.GetTopLevel(this)
                as MainWindow;

        // End the modal focus scope before detaching it. This prevents the
        // lightbox subtree's detach lifecycle from clearing the browse focus
        // that we restore below.
        owner?.FocusManager.Focus(
                null!,
                NavigationMethod.Unspecified,
                KeyModifiers.None);
        _focusedSurface.IsVisible = false;
        _detail.UnbindGrid();
        _runtime.DetailSession.Clear();

        // Release modality before resolving focus so the browse surface is
        // enabled when Avalonia's FocusManager evaluates the target.
        owner?.HideLightbox(
            _focusedSurface);

        if (returnIndex >= 0)
        {
            _grid.RestoreAssetFocus(returnIndex);
            return;
        }

        _grid.Focus(
            NavigationMethod.Unspecified,
            KeyModifiers.None);
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
                    System.Diagnostics.Trace.TraceError(
                        exception.ToString());
                    _bulkStatus.Foreground =
                        LumineDesign.Danger;
                    _bulkStatus.Text =
                        "操作を完了できませんでした。もう一度お試しください。";
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
            System.Diagnostics.Trace.TraceError(
                exception.ToString());
            _bulkStatus.Text =
                "画像を表示できませんでした。もう一度お試しください。";
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
            System.Diagnostics.Trace.TraceError(
                exception.ToString());
            _bulkStatus.Text =
                "詳細を表示できませんでした。もう一度お試しください。";
        }
    }

    private Border CreateSelectionBar()
    {
        Button CreateRatingButton(int rating)
        {
            var button =
                LumineDesign.ConfigureSecondaryButton(
                    new Button
                    {
                        Content = $"★{rating}",
                        MinWidth = 36,
                        MinHeight = 30,
                        Padding =
                            new Thickness(
                                LumineDesign.Space6,
                                LumineDesign.Space4),
                        Foreground =
                            LumineDesign.Warning
                    });
            button.Click +=
                async (_, _) =>
                    await ApplyPatchAsync(
                        new AssetUserMetadataPatch(
                            SetRating: true,
                            Rating: rating));
            ToolTip.SetTip(
                button,
                $"選択画像の評価を★{rating}に設定");
            AutomationProperties.SetName(
                button,
                $"評価 {rating}つ星に設定");
            return button;
        }

        var ratingGroup =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                Spacing = LumineDesign.Space2,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        for (var rating = 1;
             rating <= 5;
             rating++)
        {
            ratingGroup.Children.Add(
                CreateRatingButton(rating));
        }

        var statusLabels =
            new[]
            {
                "状態…",
                "未整理",
                "確認済み",
                "候補",
                "公開済み"
            };
        var statusValues =
            new[]
            {
                string.Empty,
                "unsorted",
                "reviewed",
                "candidate",
                "published"
            };
        var status =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    Width = 112,
                    ItemsSource = statusLabels,
                    SelectedIndex = 0
                });
        AutomationProperties.SetName(
            status,
            "選択画像の状態を変更");
        status.SelectionChanged +=
            async (_, _) =>
            {
                if (status.SelectedIndex <= 0)
                {
                    return;
                }

                var selected =
                    status.SelectedIndex;
                status.SelectedIndex = 0;
                await ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        SetStatusLabel: true,
                        StatusLabel:
                            statusValues[selected]));
            };

        var colorGroup =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                Spacing = LumineDesign.Space2,
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        colorGroup.Children.Add(
            new TextBlock
            {
                Text = "色",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                Margin =
                    new Thickness(
                        0,
                        0,
                        LumineDesign.Space2,
                        0),
                VerticalAlignment =
                    VerticalAlignment.Center
            });

        var colors =
            new (string? Value, string Label, string? Hex)[]
            {
                (null, "カラーなし", null),
                ("red", "赤", "#EF4444"),
                ("orange", "オレンジ", "#F97316"),
                ("yellow", "黄", "#EAB308"),
                ("green", "緑", "#22C55E"),
                ("blue", "青", "#3B82F6"),
                ("purple", "紫", "#A855F7"),
                ("gray", "グレー", "#71717A")
            };
        foreach (var item in colors)
        {
            var swatch =
                new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius =
                        new CornerRadius(7),
                    BorderBrush =
                        LumineDesign.BorderStrong,
                    BorderThickness =
                        new Thickness(1),
                    Background =
                        item.Hex is null
                            ? Brushes.Transparent
                            : new SolidColorBrush(
                                Color.Parse(item.Hex)),
                    Child =
                        item.Hex is null
                            ? new TextBlock
                            {
                                Text = "×",
                                FontSize = 10,
                                Foreground =
                                    LumineDesign.MutedForeground,
                                HorizontalAlignment =
                                    HorizontalAlignment.Center,
                                VerticalAlignment =
                                    VerticalAlignment.Center
                            }
                            : null
                };
            var button =
                LumineDesign.ConfigureSecondaryButton(
                    new Button
                    {
                        Content = swatch,
                        Width = 28,
                        Height = 28,
                        MinWidth = 28,
                        MinHeight = 28,
                        Padding = new Thickness(5)
                    });
            ToolTip.SetTip(
                button,
                $"カラー: {item.Label}");
            AutomationProperties.SetName(
                button,
                $"カラー: {item.Label}");
            var value = item.Value;
            button.Click +=
                async (_, _) =>
                    await ApplyPatchAsync(
                        new AssetUserMetadataPatch(
                            SetColorLabel: true,
                            ColorLabel: value));
            colorGroup.Children.Add(button);
        }

        var favoriteOn =
            CreateBulkButton(
                "★ お気に入り",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        SetFavorite: true,
                        Favorite: true)));
        var favoriteOff =
            CreateBulkButton(
                "☆ 解除",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        SetFavorite: true,
                        Favorite: false)));

        // Bulk metadata editing stays discoverable as one cohesive
        // operation instead of permanent rating/color button clusters.
        var organizePanel =
            new StackPanel
            {
                Width = 320
            };
        organizePanel.Classes.Add("lumine-bulk-menu");
        organizePanel.Children.Add(
            new TextBlock
            {
                Text = "選択した画像を整理",
                FontWeight = FontWeight.SemiBold,
                FontSize = LumineDesign.BodyFontSize,
                TextWrapping = TextWrapping.Wrap
            });
        var ratingLabel = new TextBlock { Text = "評価" };
        ratingLabel.Classes.Add("lumine-muted-caption");
        organizePanel.Children.Add(ratingLabel);
        organizePanel.Children.Add(ratingGroup);
        organizePanel.Children.Add(status);

        // At accessibility text sizes, labeled choices are clearer and
        // offer larger targets than eight tiny adjacent color swatches.
        if (LumineVisualMetrics.TextScaleFactor >= 1.75)
        {
            organizePanel.Children.Add(
                new TextBlock
                {
                    Text = "色",
                    FontSize = LumineDesign.CaptionFontSize,
                    Foreground = LumineDesign.MutedForeground
                });
            var colorChoices =
                new[] { "カラーを選択…" }
                    .Concat(colors.Select(static item => item.Label))
                    .ToArray();
            var colorPicker =
                LumineDesign.ConfigureComboBox(
                    new ComboBox
                    {
                        Width = 260,
                        ItemsSource = colorChoices,
                        SelectedIndex = 0
                    });
            AutomationProperties.SetName(
                colorPicker,
                "選択画像のカラーを変更");
            colorPicker.SelectionChanged +=
                async (_, _) =>
                {
                    if (colorPicker.SelectedIndex <= 0)
                    {
                        return;
                    }

                    var value =
                        colors[colorPicker.SelectedIndex - 1].Value;
                    colorPicker.SelectedIndex = 0;
                    await ApplyPatchAsync(
                        new AssetUserMetadataPatch(
                            SetColorLabel: true,
                            ColorLabel: value));
                };
            organizePanel.Children.Add(colorPicker);
        }
        else
        {
            organizePanel.Children.Add(colorGroup);
        }
        var favoriteGroup =
            new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
        favoriteGroup.Classes.Add("lumine-bulk-favorite-actions");
        favoriteGroup.Children.Add(favoriteOn);
        favoriteGroup.Children.Add(favoriteOff);
        organizePanel.Children.Add(favoriteGroup);

        var organize =
            LumineDesign.ConfigureSecondaryButton(
                new DropDownButton
                {
                    Content = "整理",
                    Flyout = new Flyout
                    {
                        Content = new ScrollViewer
                        {
                            MaxHeight = 380,
                            VerticalScrollBarVisibility =
                                Avalonia.Controls.Primitives
                                    .ScrollBarVisibility.Auto,
                            Content = organizePanel
                        }
                    }
                });
        AutomationProperties.SetName(
            organize,
            "複数画像の評価・状態・色・お気に入りを整理");

        _bulkTagSearch =
            LumineDesign.ConfigureTextBox(
                new TextBox
                {
                    PlaceholderText = "タグを検索…",
                    MinWidth = 210
                });
        _bulkTagCandidates =
            new StackPanel
            {
                Spacing =
                    LumineDesign.Space2
            };
        _bulkTagSearch.TextChanged +=
            (_, _) => RenderBulkTagCandidates();

        var tagPanel =
            new StackPanel
            {
                Width = 280,
                Spacing =
                    LumineDesign.Space6,
                Margin =
                    new Thickness(
                        LumineDesign.Space4)
            };
        tagPanel.Children.Add(
            new TextBlock
            {
                Text = "既存タグを追加",
                Foreground =
                    LumineDesign.Foreground,
                FontWeight =
                    FontWeight.SemiBold,
                FontSize =
                    LumineDesign.BodyFontSize
            });
        tagPanel.Children.Add(_bulkTagSearch);
        tagPanel.Children.Add(
            new Border
            {
                MaxHeight = 220,
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
                Child =
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility =
                            Avalonia.Controls.Primitives
                                .ScrollBarVisibility.Auto,
                        Content =
                            _bulkTagCandidates
                    }
            });
        tagPanel.Children.Add(
            CreateBulkButton(
                "すべてのタグを解除",
                () => ApplyPatchAsync(
                    new AssetUserMetadataPatch(
                        ClearTags: true))));
        tagPanel.Children.Add(
            new TextBlock
            {
                Text =
                    "新しいタグはInspectorまたは「タグ」画面で作成できます。",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                TextWrapping =
                    TextWrapping.Wrap
            });

        var tagAction =
            LumineDesign.ConfigureSecondaryButton(
                new DropDownButton
                {
                    Content = "タグ",
                    MinWidth = 66,
                    Flyout =
                        new Flyout
                        {
                            Content =
                                new Border
                                {
                                    Background =
                                        LumineDesign.SurfaceRaised,
                                    Padding =
                                        new Thickness(
                                            LumineDesign.Space8),
                                    Child = tagPanel
                                }
                        }
                });

        var creativePanel =
            new StackPanel
            {
                Width = Math.Clamp(
                    238 + (LumineVisualMetrics.TextScaleFactor - 1) * 64,
                    238,
                    318),
                Spacing = LumineDesign.Space6,
                Margin =
                    new Thickness(
                        LumineDesign.Space4)
            };
        creativePanel.Children.Add(
            new TextBlock
            {
                Text = "制作・公開",
                FontSize =
                    LumineDesign.BodyFontSize,
                FontWeight =
                    FontWeight.SemiBold,
                Foreground =
                    LumineDesign.Foreground
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

        var publication =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "＋ 公開記録",
                    MinHeight = 30,
                    Padding =
                        new Thickness(
                            LumineDesign.Space8,
                            LumineDesign.Space4)
                });
        publication.Click +=
            async (_, _) =>
            {
                publication.IsEnabled = false;
                try
                {
                    await ShowCreatePublicationDialogAsync();
                }
                finally
                {
                    publication.IsEnabled = true;
                }
            };

        var delete =
            CreateBulkButton(
                "元ファイルを削除…",
                DeleteSelectedSourcesAsync);
        LumineDesign.ConfigureDangerButton(
            delete);
        delete.MinHeight = 28;
        delete.Padding =
            new Thickness(
                LumineDesign.Space8,
                LumineDesign.Space4);
        delete.Margin =
            new Thickness(
                LumineDesign.Space8,
                0,
                0,
                0);

        _cancelBulkOperationButton =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "処理をキャンセル",
                    MinHeight = 28,
                    Padding =
                        new Thickness(
                            LumineDesign.Space8,
                            LumineDesign.Space4),
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    IsVisible = false
                });
        _cancelBulkOperationButton.Click +=
            (_, _) => CancelBulkOperation();

        // Less frequent creation/publication commands remain directly
        // available within one short, explicitly grouped overflow menu.
        // Source deletion is visually separated from ordinary commands.
        creativePanel.Children.Add(publication);
        var dangerDivider = new Border();
        dangerDivider.Classes.Add("lumine-divider");
        creativePanel.Children.Add(dangerDivider);
        creativePanel.Children.Add(delete);
        foreach (var action in
                 creativePanel.Children.OfType<Button>())
        {
            action.HorizontalAlignment = HorizontalAlignment.Stretch;
            action.HorizontalContentAlignment = HorizontalAlignment.Left;
        }
        var more =
            LumineDesign.ConfigureSecondaryButton(
                new DropDownButton
                {
                    Content = "その他",
                    Flyout = new Flyout
                    {
                        Content = new ScrollViewer
                        {
                            MaxHeight = 380,
                            VerticalScrollBarVisibility =
                                Avalonia.Controls.Primitives
                                    .ScrollBarVisibility.Auto,
                            Content = creativePanel
                        }
                    }
                });
        AutomationProperties.SetName(
            more,
            "複数画像の制作・公開・ファイル操作");

        foreach (var command in
                 new Button[] { tagAction, organize, more })
        {
            command.Classes.Add("lumine-bulk-command");
            command.FontSize = LumineDesign.CaptionFontSize;
            _bulkActions.Children.Add(command);
        }

        var clear =
            CreateBulkButton(
                "選択解除",
                () =>
                {
                    _grid.ClearSelection();
                    return Task.CompletedTask;
                });
        AutomationProperties.SetName(
            clear,
            "選択解除");
        _cancelBulkOperationButton.Content = "停止";
        AutomationProperties.SetName(
            _cancelBulkOperationButton,
            "処理をキャンセル");
        ToolTip.SetTip(
            _cancelBulkOperationButton,
            "実行中の複数画像処理をキャンセル");

        var summary = new Grid
        {
            ColumnDefinitions =
                new ColumnDefinitions("Auto,*,Auto,Auto")
        };
        summary.Classes.Add("lumine-bulk-summary-grid");
        Grid.SetColumn(_selectionMetadataSummary, 1);
        Grid.SetColumn(_cancelBulkOperationButton, 2);
        Grid.SetColumn(clear, 3);
        summary.Children.Add(_selectionCount);
        summary.Children.Add(_selectionMetadataSummary);
        summary.Children.Add(_cancelBulkOperationButton);
        summary.Children.Add(clear);

        var root = new StackPanel();
        root.Classes.Add("lumine-bulk-layout");
        root.Children.Add(summary);
        root.Children.Add(_bulkActions);
        root.Children.Add(_bulkStatus);

        var bar = new Border { Child = root };
        bar.Classes.Add("lumine-bulk-selection-surface");
        return bar;
    }

    internal async Task SyncTagConsumersAsync()
    {
        await RefreshBulkTagChoicesAsync();

        if (_contextSurface.IsVisible
            && _grid.SelectedAssetIndex >= 0)
        {
            await LoadContextDetailAsync(
                _grid.SelectedAssetIndex);
        }
    }

    private async Task RefreshBulkTagChoicesAsync()
    {
        try
        {
            _bulkTagChoices =
                await _runtime.LibraryService
                    .ListTagsAsync(
                        _runtime.Library.Id,
                        limit: LibraryRepository.MaxTagListLimit);
            RenderBulkTagCandidates();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError(
                exception.ToString());
            _bulkStatus.Foreground =
                LumineDesign.Warning;
            _bulkStatus.Text =
                "タグ候補を読み込めませんでした。もう一度お試しください。";
        }
    }

    private void RenderBulkTagCandidates()
    {
        var host =
            _bulkTagCandidates;
        if (host is null)
        {
            return;
        }

        host.Children.Clear();
        var query =
            _bulkTagSearch?.Text?.Trim()
            ?? string.Empty;
        var visible =
            _bulkTagChoices
                .Where(
                    tag =>
                        query.Length == 0
                        || tag.Name.Contains(
                            query,
                            StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(
                    static tag =>
                        tag.AssetCount)
                .ThenBy(
                    static tag =>
                        tag.Name,
                    StringComparer.CurrentCultureIgnoreCase)
                .Take(80)
                .ToArray();

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
                        ResolveBulkTagBrush(
                            tag.Color),
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
            var name = tag.Name;
            button.Click +=
                async (_, _) =>
                {
                    await ApplyPatchAsync(
                        new AssetUserMetadataPatch(
                            AddTags: [name]));
                    _bulkTagSearch!.Text =
                        string.Empty;
                };
            host.Children.Add(button);
        }

        if (visible.Length == 0)
        {
            host.Children.Add(
                new TextBlock
                {
                    Text =
                        query.Length == 0
                            ? "タグがありません。"
                            : "一致するタグがありません。",
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

    private static IBrush ResolveBulkTagBrush(
        string? color)
    {
        try
        {
            return new SolidColorBrush(
                Color.Parse(
                    string.IsNullOrWhiteSpace(
                        color)
                        ? "#6366f1"
                        : color));
        }
        catch (FormatException)
        {
            return LumineDesign.Accent;
        }
    }

    private CancellationTokenSource BeginBulkOperation(
        string status)
    {
        var previous =
            _bulkOperationCancellation;
        _bulkOperationCancellation = null;
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        var source =
            new CancellationTokenSource();
        _bulkOperationCancellation =
            source;
        if (_cancelBulkOperationButton is not null)
        {
            _cancelBulkOperationButton.IsVisible = true;
            _cancelBulkOperationButton.IsEnabled = true;
        }

        _bulkStatus.Foreground =
            LumineDesign.MutedForeground;
        _bulkStatus.Text =
            status;
        return source;
    }

    private void CancelBulkOperation()
    {
        var source =
            _bulkOperationCancellation;
        if (source is null
            || source.IsCancellationRequested)
        {
            return;
        }

        source.Cancel();
        if (_cancelBulkOperationButton is not null)
        {
            _cancelBulkOperationButton.IsEnabled = false;
        }

        _bulkStatus.Text =
            "キャンセルしています…";
    }

    private void CompleteBulkOperation(
        CancellationTokenSource source)
    {
        if (ReferenceEquals(
                _bulkOperationCancellation,
                source))
        {
            _bulkOperationCancellation = null;
            if (_cancelBulkOperationButton is not null)
            {
                _cancelBulkOperationButton.IsVisible = false;
                _cancelBulkOperationButton.IsEnabled = true;
            }
        }

        source.Dispose();
    }

    private async Task<T> RunBulkOperationAsync<T>(
        string status,
        Func<CancellationToken, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var source =
            BeginBulkOperation(status);
        try
        {
            return await action(
                source.Token);
        }
        catch (OperationCanceledException)
            when (source.IsCancellationRequested)
        {
            _bulkStatus.Text =
                "操作をキャンセルしました。";
            throw;
        }
        finally
        {
            CompleteBulkOperation(source);
        }
    }

    private Button CreateBulkButton(
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
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Trace.TraceError(
                        exception.ToString());
                    _bulkStatus.Foreground =
                        LumineDesign.Danger;
                    _bulkStatus.Text =
                        "操作を完了できませんでした。";
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
        if (isBulk)
        {
            ApplySelectionBarPlacement(
                ResolveInspectorLayoutWidth());
        }
        if (!isBulk)
        {
            _grid.SetBottomOverlayInset(0);
        }
        else
        {
            Dispatcher.UIThread.Post(
                () =>
                    _grid.SetBottomOverlayInset(
                        Math.Max(
                            76,
                            _selectionBar.Bounds.Height + 28)),
                DispatcherPriority.Render);

            if (_bulkTagChoices.Count == 0)
            {
                _ = RefreshBulkTagChoicesAsync();
            }
        }
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
                System.Diagnostics.Trace.TraceError(
                    exception.ToString());
                _selectionMetadataSummary.Text =
                    "整理情報を取得できませんでした。";
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
                System.Diagnostics.Trace.TraceError(
                    exception.ToString());
                _bulkStatus.Text =
                    "詳細を更新できませんでした。もう一度お試しください。";
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
        ResolveSelectedRelationAssetsAsync(
            CancellationToken cancellationToken = default)
    {
        if (_grid.SelectedAssetCount != 2)
        {
            return Array.Empty<ViewerAsset>();
        }

        var indices =
            _grid.SelectedAssetIndices;
        var assets =
            new ViewerAsset[2];

        for (var position = 0;
             position < assets.Length;
             position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            assets[position] =
                await _runtime.ViewerSession.GetAssetAsync(
                    indices[position],
                    cancellationToken);
        }

        return assets;
    }

    private async Task<CreativeSelectionPreview>
        CreateSelectionPreviewAsync(
            int sampleLimit = 8,
            CancellationToken cancellationToken = default)
    {
        var count =
            _grid.SelectedAssetCount;
        if (count <= 0)
        {
            return new CreativeSelectionPreview(
                0,
                Array.Empty<string>());
        }

        var indices =
            _grid.SelectedAssetIndices;
        var sampleCount =
            Math.Min(
                count,
                Math.Max(0, sampleLimit));
        var names =
            new string[sampleCount];

        for (var position = 0;
             position < sampleCount;
             position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var asset =
                await _runtime.ViewerSession.GetAssetAsync(
                    indices[position],
                    cancellationToken);
            names[position] =
                asset.DisplayName;
        }

        return new CreativeSelectionPreview(
            count,
            names);
    }

    private ValueTask<IReadOnlyList<long>>
        ResolveSelectedAssetIdsAsync(
            CancellationToken cancellationToken = default) =>
        _runtime.ViewerSession.GetAssetIdsAsync(
            _grid.SelectedAssetIndices,
            cancellationToken);

    private async Task<IReadOnlyList<
        CreativePublicationAssetOption>>
        CreatePublicationAssetOrderAsync(
            CancellationToken cancellationToken = default)
    {
        var assetIds =
            await ResolveSelectedAssetIdsAsync(
                cancellationToken);
        if (assetIds.Count == 0)
        {
            return Array.Empty<
                CreativePublicationAssetOption>();
        }

        var result =
            new List<CreativePublicationAssetOption>(
                assetIds.Count);
        const int batchSize = 1000;

        for (var offset = 0;
             offset < assetIds.Count;
             offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count =
                Math.Min(
                    batchSize,
                    assetIds.Count - offset);
            var batch =
                assetIds
                    .Skip(offset)
                    .Take(count)
                    .ToArray();
            var assets =
                await _runtime.LibraryService
                    .GetAssetsByIdsAsync(
                        _runtime.Library.Id,
                        batch,
                        cancellationToken);
            if (assets.Count != batch.Length)
            {
                throw new InvalidOperationException(
                    "Publication selection changed while preparing image order.");
            }

            result.AddRange(
                assets.Select(
                    static asset =>
                        new CreativePublicationAssetOption(
                            asset.Id,
                            asset.FileName)));
        }

        return result;
    }

    private Task ApplyPatchAsync(
        AssetUserMetadataPatch patch) =>
        RunBulkOperationAsync(
            "更新しています…",
            async cancellationToken =>
            {
                var assetIds =
                    await ResolveSelectedAssetIdsAsync(
                        cancellationToken);
                if (assetIds.Count == 0)
                {
                    return 0;
                }

                var updated =
                    await _runtime.LibraryService.PatchUserMetadataAsync(
                        _runtime.Library.Id,
                        assetIds,
                        patch,
                        cancellationToken);

                _bulkStatus.Text =
                    $"{updated:N0}件を更新しました。";

                if (_afterBulkMutation is not null)
                {
                    await _afterBulkMutation();
                }

                return updated;
            });

    internal Task<WorkInfo?> CreateWorkFromSelectionAsync(
        CreativeWorkDialogResult input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return RunBulkOperationAsync<WorkInfo?>(
            "Workを作成しています…",
            async cancellationToken =>
            {
                var assetIds =
                    await ResolveSelectedAssetIdsAsync(
                        cancellationToken);
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
                            assetIds),
                        cancellationToken);
                _bulkStatus.Text =
                    $"Work「{created.Title}」を作成しました。";
                await RefreshContextAfterCreativeMutationAsync();
                return created;
            });
    }

    internal Task<WorkInfo?>
        AddSelectionToExistingWorkAsync(
            long workId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            workId);

        return RunBulkOperationAsync<WorkInfo?>(
            "既存Workへ追加しています…",
            async cancellationToken =>
            {
                var assetIds =
                    await ResolveSelectedAssetIdsAsync(
                        cancellationToken);
                if (assetIds.Count == 0)
                {
                    return null;
                }

                var updated =
                    await _runtime.LibraryService
                        .AddAssetsToWorkAsync(
                            _runtime.Library.Id,
                            workId,
                            assetIds,
                            cancellationToken);
                _bulkStatus.Text =
                    $"Work「{updated.Title}」へ追加しました。";
                await RefreshContextAfterCreativeMutationAsync();
                return updated;
            });
    }

    internal Task<GenerationGroupInfo?>
        CreateGenerationGroupFromSelectionAsync(
            CreativeGroupDialogResult input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return RunBulkOperationAsync<GenerationGroupInfo?>(
            "Generation Groupを作成しています…",
            async cancellationToken =>
            {
                var assetIds =
                    await ResolveSelectedAssetIdsAsync(
                        cancellationToken);
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
                                Notes: input.Notes),
                            cancellationToken);
                _bulkStatus.Text =
                    $"Generation Group「{created.Name}」を作成しました。";
                await RefreshContextAfterCreativeMutationAsync();
                return created;
            });
    }

    internal Task<GenerationGroupInfo?>
        AddSelectionToExistingGenerationGroupAsync(
            long groupId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            groupId);

        return RunBulkOperationAsync<GenerationGroupInfo?>(
            "既存Generation Groupへ追加しています…",
            async cancellationToken =>
            {
                var assetIds =
                    await ResolveSelectedAssetIdsAsync(
                        cancellationToken);
                if (assetIds.Count == 0)
                {
                    return null;
                }

                var updated =
                    await _runtime.LibraryService
                        .AddAssetsToGenerationGroupAsync(
                            _runtime.Library.Id,
                            groupId,
                            assetIds,
                            cancellationToken);
                _bulkStatus.Text =
                    $"Generation Group「{updated.Name}」へ追加しました。";
                await RefreshContextAfterCreativeMutationAsync();
                return updated;
            });
    }

    internal Task<AssetRelationInfo?>
        CreateRelationFromSelectionAsync(
            CreativeRelationDialogResult input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return RunBulkOperationAsync<AssetRelationInfo?>(
            "Lineageを作成しています…",
            async cancellationToken =>
            {
                var assets =
                    await ResolveSelectedRelationAssetsAsync(
                        cancellationToken);
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
                                input.Note),
                            cancellationToken);
                _bulkStatus.Text =
                    $"{created.Parent.FileName} → {created.Child.FileName} · {created.RelationType} を保存しました。";
                await RefreshContextAfterCreativeMutationAsync();
                return created;
            });
    }

    internal Task<PublicationInfo?>
        CreatePublicationFromSelectionAsync(
            CreativePublicationDialogResult input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return RunBulkOperationAsync<PublicationInfo?>(
            "Publicationを保存しています…",
            async cancellationToken =>
            {
                var selectedAssetIds =
                    await ResolveSelectedAssetIdsAsync(
                        cancellationToken);
                var orderedAssetIds =
                    input.OrderedAssetIds
                        ?.ToArray()
                    ?? Array.Empty<long>();
                if (selectedAssetIds.Count == 0
                    || orderedAssetIds.Length == 0)
                {
                    _bulkStatus.Text =
                        "Publicationの画像順が空です。選択し直してください。";
                    return null;
                }

                var selectedSet =
                    selectedAssetIds.ToHashSet();
                if (orderedAssetIds.Length
                        != selectedAssetIds.Count
                    || orderedAssetIds
                        .Distinct()
                        .Count()
                        != orderedAssetIds.Length
                    || orderedAssetIds.Any(
                        id =>
                            !selectedSet.Contains(id)))
                {
                    _bulkStatus.Text =
                        "Publicationの画像順が現在の選択と一致しません。もう一度Publicationを開いてください。";
                    return null;
                }

                var created =
                    await _runtime.LibraryService
                        .CreatePublicationAsync(
                            _runtime.Library.Id,
                            new PublicationCreate(
                                orderedAssetIds,
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
                                    input.PlatformMetadataJson),
                            cancellationToken);
                _bulkStatus.Text =
                    $"Publicationを{created.Destination}の履歴へ保存しました。";
                await RefreshContextAfterCreativeMutationAsync();
                _entryRequested?.Invoke(
                    "publication");
                return created;
            });
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
        var selection =
            await CreateSelectionPreviewAsync();
        if (selection.Count == 0)
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
                selection);
        if (input is not null)
        {
            await CreateWorkFromSelectionAsync(
                input);
        }
    }

    private async Task ShowCreateGenerationGroupDialogAsync()
    {
        var selection =
            await CreateSelectionPreviewAsync();
        if (selection.Count == 0)
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
                    selection,
                    works);
        if (input is not null)
        {
            await CreateGenerationGroupFromSelectionAsync(
                input);
        }
    }

    private async Task ShowAddToExistingWorkDialogAsync()
    {
        var selection =
            await CreateSelectionPreviewAsync();
        if (selection.Count == 0)
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
        if (works.Count == 0)
        {
            _bulkStatus.Text =
                "追加できる既存Workがありません。";
            return;
        }

        var target =
            await CreativeArchiveDialogs
                .ShowExistingWorkAsync(
                    owner,
                    selection,
                    works);
        if (target is not null)
        {
            await AddSelectionToExistingWorkAsync(
                target.Id);
        }
    }

    private async Task
        ShowAddToExistingGenerationGroupDialogAsync()
    {
        var selection =
            await CreateSelectionPreviewAsync();
        if (selection.Count == 0)
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

        var groups =
            await _runtime.LibraryService
                .ListGenerationGroupsAsync(
                    _runtime.Library.Id);
        if (groups.Count == 0)
        {
            _bulkStatus.Text =
                "追加できる既存Generation Groupがありません。";
            return;
        }

        var target =
            await CreativeArchiveDialogs
                .ShowExistingGenerationGroupAsync(
                    owner,
                    selection,
                    groups);
        if (target is not null)
        {
            await AddSelectionToExistingGenerationGroupAsync(
                target.Id);
        }
    }

    private async Task DeleteRelationFromInspectorAsync(
        AssetRelationInfo relation)
    {
        ArgumentNullException.ThrowIfNull(relation);

        var owner =
            TopLevel.GetTopLevel(this)
                as Window;
        if (owner is null)
        {
            return;
        }

        var approved =
            await ProductDialogs.ConfirmAsync(
                owner,
                "Lineageを削除しますか？",
                $"{relation.Parent.FileName} → {relation.Child.FileName} の関係を削除します。",
                string.IsNullOrWhiteSpace(
                    relation.Note)
                    ? $"種類: {relation.RelationType}"
                    : $"種類: {relation.RelationType}\nメモ: {relation.Note}",
                confirmLabel: "Lineageを削除",
                tone: ProductDialogTone.Danger);
        if (!approved)
        {
            return;
        }

        await RunBulkOperationAsync(
            "Lineageを削除しています…",
            async cancellationToken =>
            {
                var deleted =
                    await _runtime.LibraryService
                        .DeleteAssetRelationAsync(
                            _runtime.Library.Id,
                            relation.Id,
                            cancellationToken);
                _bulkStatus.Text =
                    deleted
                        ? "Lineageを削除しました。"
                        : "Lineageは既に削除されています。";
                await RefreshContextAfterCreativeMutationAsync();
                return deleted;
            });
    }

    private async Task ShowCreateRelationDialogAsync()
    {
        if (_grid.SelectedAssetCount != 2)
        {
            _bulkStatus.Text =
                "Lineageは2枚を選択してください。";
            return;
        }

        var assets =
            await ResolveSelectedRelationAssetsAsync();
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
        var publicationAssets =
            await CreatePublicationAssetOrderAsync();
        if (publicationAssets.Count == 0)
        {
            return;
        }

        var selection =
            new CreativeSelectionPreview(
                publicationAssets.Count,
                publicationAssets
                    .Take(8)
                    .Select(
                        static item =>
                            item.DisplayName)
                    .ToArray());

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
        var destinations =
            await _runtime.LibraryService
                .ListPublicationDestinationsAsync(
                    _runtime.Library.Id);
        var accounts =
            await _runtime.LibraryService
                .ListPublicationAccountsAsync(
                    _runtime.Library.Id);
        var input =
            await CreativeArchiveDialogs
                .ShowPublicationAsync(
                    owner,
                    selection,
                    publicationAssets,
                    works,
                    destinations,
                    accounts);
        if (input is not null)
        {
            await CreatePublicationFromSelectionAsync(
                input);
        }
    }

    private async Task DeleteSelectedSourcesAsync()
    {
        var selectedCount =
            _grid.SelectedAssetCount;
        if (selectedCount == 0)
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
                $"{selectedCount:N0}件の元画像ファイルをディスクから削除します。これはLumineの登録解除ではなく、実ファイルの削除です。",
                "この操作はLumineから元に戻せません。Work / Generation Group / Publicationなどの履歴には、削除前の参照情報が残る場合があります。",
                confirmLabel: "元ファイルを削除",
                tone: ProductDialogTone.Danger);
        if (!confirmed)
        {
            return;
        }

        await RunBulkOperationAsync(
            "元ファイルを削除しています…",
            async cancellationToken =>
            {
                var selectedIds =
                    await ResolveSelectedAssetIdsAsync(
                        cancellationToken);
                if (selectedIds.Count == 0)
                {
                    return 0;
                }

                // Resolve source paths through one background Library batch
                // rather than issuing one Viewer metadata request per item.
                var assets =
                    await _runtime.LibraryService.GetAssetsByIdsAsync(
                        _runtime.Library.Id,
                        selectedIds,
                        cancellationToken);

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
                var cancelled =
                    false;

                await Task.Run(
                    () =>
                    {
                        foreach (var asset in assets)
                        {
                            if (cancellationToken
                                .IsCancellationRequested)
                            {
                                cancelled = true;
                                break;
                            }

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
                            catch (IOException)
                            {
                                failures.Add(
                                    asset.RelativePath);
                            }
                            catch (UnauthorizedAccessException)
                            {
                                failures.Add(
                                    asset.RelativePath);
                            }
                        }
                    });

                // Cancellation can arrive after physical files have already
                // been deleted. Always reconcile those successful deletions
                // before surfacing cancellation so Library state never keeps
                // stale rows for files Lumine removed.
                if (removed.Count > 0)
                {
                    await _runtime.LibraryService.RemoveAssetsAsync(
                        _runtime.Library.Id,
                        removed,
                        CancellationToken.None);
                    _grid.ClearSelection();
                }

                if (_afterBulkMutation is not null
                    && removed.Count > 0)
                {
                    await _afterBulkMutation();
                }

                if (cancelled
                    || cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                _bulkStatus.Text =
                    failures.Count == 0
                        ? $"{removed.Count:N0}件の元ファイルを削除しました。"
                        : $"{removed.Count:N0}件を削除、{failures.Count:N0}件は削除できませんでした。";
                return removed.Count;
            });
    }

    private async void OnShellKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (_focusedSurface.IsVisible)
        {
            if (!e.Handled
                && e.Key == Key.Escape
                && e.KeyModifiers == KeyModifiers.None)
            {
                CloseFocusedView();
                e.Handled = true;
            }

            return;
        }

        // Shell metadata shortcuts belong to the unmodified key space.
        // A modified F must bubble to MainWindow's Ctrl+F Browse search,
        // never toggle a selected image's Favorite flag. Likewise, modified
        // ratings/Delete may belong to other global or OS commands.
        if (e.Handled
            || e.KeyModifiers != KeyModifiers.None)
        {
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
        _bulkOperationCancellation?.Cancel();
        _bulkOperationCancellation?.Dispose();
        _bulkOperationCancellation = null;
        KeyDown -= OnShellKeyDown;
        _contextDetail.PrepareForDetach();
        _detail.UnbindGrid();
        _detail.PrepareForDetach();
        _grid.PrepareForDetach();

        // Drop the shell-owned visual tree before awaiting compositor/native
        // drains. The controls retain only the explicit lifecycle objects
        // that are awaited below and by CoreViewerRuntime.DisposeAsync().
        Content = null;

        await _contextDetail.DrainPreviewBitmapReleasesAsync();
        await _grid.DrainBitmapReleasesAsync();
    }
}
