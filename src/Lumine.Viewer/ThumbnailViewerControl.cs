using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lumine.Viewer;

// UI bitmap assignment is measured, not a GPU/compositor-present fence.
// These diagnostics expose the stages causing an empty visible thumbnail.
// Count actual attached bitmap tiles intersecting the inner scroller's
// viewport. Row-index ranges can include virtualized or uninstantiated
// cells and cannot establish what is visually missing.
public readonly record struct ViewerViewportReadinessDiagnostics(
    int VisibleTiles,
    int UnreadyTiles);

public readonly record struct ViewerTileReadinessDiagnostics(
    long Started,
    long Ready,
    long ReadyFromBitmapCache,
    long CancelledBeforeReady,
    double MeanAttachToReadyMilliseconds,
    double MaxAttachToReadyMilliseconds,
    double MeanMetadataMilliseconds,
    double MeanThumbnailSourceMilliseconds,
    double MeanBitmapAcquireMilliseconds);

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The coalesced lookahead CTS is cancelled and disposed when the Viewer detaches or rebinds.")]
public sealed class ThumbnailViewerControl : UserControl
{
    private ViewerSession _session;
    private readonly ListBox _rows;
    private readonly object _bitmapReleaseGate = new();
    private readonly HashSet<Task> _pendingBitmapReleases = [];
    private readonly List<DecodedBitmapLease> _unfencedBitmapLeases = [];
    private readonly Dictionary<long, WarmPresentation> _warmPresentations = [];
    private readonly LinkedList<long> _warmPresentationLru = [];
    private long _warmPresentationHits;
    private long _tileLoadsStarted;
    private long _tileLoadsReady;
    private long _tileLoadsReadyWarm;
    private long _tileLoadsCancelled;
    private double _totalReadyMilliseconds;
    private double _maxReadyMilliseconds;
    private double _totalMetadataMilliseconds;
    private double _totalThumbnailSourceMilliseconds;
    private double _totalBitmapAcquireMilliseconds;
    private Exception? _bitmapReleaseFailure;

    // Tile decode tasks extend beyond ViewerSession.GetThumbnailAsync:
    // they also acquire/decode Avalonia bitmaps. Dynamic Grid/List rebuilds
    // can detach a tile while that final stage is still completing, so shell
    // shutdown must drain these tasks as well as composition-fenced leases.
    private readonly object _tileLoadGate = new();
    private readonly HashSet<Task> _pendingTileLoads = [];
    // One coalesced viewport lookahead, not one speculative queue per
    // realized row. Fast scroll repeatedly supersedes the previous range.
    private CancellationTokenSource? _lookaheadCancellation;
    // The sign of the last actual viewer scroll (forward = +1).
    // Wheel capture happens before virtualized rows are reattached.
    private int _lookaheadDirection = 1;
    private long _lookaheadScheduleCount;
    private ScrollViewer? _galleryScrollViewer;
    private double _lastGalleryOffsetY;
    private Compositor? _compositor;
    private int _columns = 1;
    private readonly ViewerRangeSelection _selection = new();
    private long _selectedIndex = -1;
    private long _selectionAnchor = -1;
    private long _pendingFocusIndex = -1;
    private bool _pendingFocusLayoutSubscribed;
    private ViewerLayoutMode _layoutMode;
    private int _densityLevel;
    private double _viewportWidth = 1;

    private const double GridOuterPadding = 12;
    private const double GridCornerRadius = 10;

    private int WarmPresentationLimit =>
        Math.Max(
            16,
            Math.Min(
                256,
                _session.Options.DecodedBitmapEntryLimit * 2));

    public ThumbnailViewerControl(
        ViewerSession session,
        ViewerLayoutMode layoutMode = ViewerLayoutMode.Grid,
        int densityLevel = 1)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        if (!Enum.IsDefined(layoutMode))
        {
            throw new ArgumentOutOfRangeException(nameof(layoutMode));
        }

        if (densityLevel is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(densityLevel));
        }

        _layoutMode = layoutMode;
        _densityLevel = densityLevel;

        Focusable = true;
        ClipToBounds = true;

        _rows = new ListBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(GridOuterPadding),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };

        Content = _rows;
        KeyDown += OnKeyDown;
        SizeChanged += OnSizeChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += (_, _) =>
        {
            CancelLookahead();
            StopScrollTracking();
        };
        // Capture wheel direction before the ListBox's ScrollViewer
        // handles the event and triggers virtualized row attachments.
        AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnGalleryWheel,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        // An offset property subscription on the actual inner
        // ScrollViewer handles keyboard, scrollbar and touch changes.
        // A bubble-only listener on the outer UserControl can miss
        // template-created scrollers in headless/realized layouts.

        RebuildRows();
    }

    public long AssetCount => _session.Count;

    public long SelectedAssetIndex => _selectedIndex;

    public int SelectedAssetCount => _selection.Count;

    public IReadOnlyList<long> SelectedAssetIndices =>
        _selection.AsReadOnlyList();

    internal int SelectionRangeCount =>
        _selection.RangeCount;

    public int Columns => _columns;

    public ViewerLayoutMode LayoutMode =>
        _layoutMode;

    public int DensityLevel =>
        _densityLevel;

    public ViewerTileReadinessDiagnostics TileReadiness =>
        new(
            _tileLoadsStarted,
            _tileLoadsReady,
            _tileLoadsReadyWarm,
            _tileLoadsCancelled,
            _tileLoadsReady > 0
                ? _totalReadyMilliseconds / _tileLoadsReady
                : 0,
            _maxReadyMilliseconds,
            _tileLoadsReady > _tileLoadsReadyWarm
                ? _totalMetadataMilliseconds
                    / (_tileLoadsReady - _tileLoadsReadyWarm)
                : 0,
            _tileLoadsReady > _tileLoadsReadyWarm
                ? _totalThumbnailSourceMilliseconds
                    / (_tileLoadsReady - _tileLoadsReadyWarm)
                : 0,
            _tileLoadsReady > _tileLoadsReadyWarm
                ? _totalBitmapAcquireMilliseconds
                    / (_tileLoadsReady - _tileLoadsReadyWarm)
                : 0);

    private void RecordTileReady(
        long startedAt,
        bool warm,
        double metadataMilliseconds = 0,
        double thumbnailMilliseconds = 0,
        double bitmapMilliseconds = 0)
    {
        var total = Stopwatch.GetElapsedTime(startedAt)
            .TotalMilliseconds;
        _tileLoadsReady++;
        if (warm)
        {
            _tileLoadsReadyWarm++;
        }
        else
        {
            _totalMetadataMilliseconds += metadataMilliseconds;
            _totalThumbnailSourceMilliseconds += thumbnailMilliseconds;
            _totalBitmapAcquireMilliseconds += bitmapMilliseconds;
        }

        _totalReadyMilliseconds += total;
        _maxReadyMilliseconds = Math.Max(
            _maxReadyMilliseconds,
            total);
    }

    public double BottomOverlayInset =>
        Math.Max(
            0,
            _rows.Padding.Bottom
            - GridOuterPadding);

    public void SetBottomOverlayInset(
        double inset)
    {
        var next =
            Math.Max(
                0,
                inset);
        _rows.Padding =
            new Thickness(
                GridOuterPadding,
                GridOuterPadding,
                GridOuterPadding,
                GridOuterPadding + next);
    }

    public void PrepareForDetach()
    {
        // Terminal shell teardown must detach realized rows synchronously.
        // Relying only on visual-tree event delivery leaves a timing window
        // where tile decode/file work can outlive the owning window.
        CancelPendingAssetFocus();
        CancelLookahead();
        _rows.ItemsSource = null;
        _warmPresentations.Clear();
        _warmPresentationLru.Clear();
        ClearSelection();
    }

    public async Task PrepareForSessionRebindAsync()
    {
        CancelPendingAssetFocus();
        CancelLookahead();
        _rows.ItemsSource = null;
        _warmPresentations.Clear();
        _warmPresentationLru.Clear();
        await DrainBitmapReleasesAsync()
            .ConfigureAwait(true);
    }

    public void RebindSession(
        ViewerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _selection.Clear();
        _selectedIndex = -1;
        _selectionAnchor = -1;
        _columns =
            CalculateColumns(
                Math.Max(1, _viewportWidth));
        RebuildRows();
    }

    public void RestoreSelection(
        IReadOnlyList<long> indices,
        long primaryIndex)
    {
        ArgumentNullException.ThrowIfNull(indices);

        _selection.Clear();

        foreach (var index in
                 indices
                     .Where(index =>
                         (ulong)index < (ulong)AssetCount)
                     .Distinct())
        {
            _selection.Toggle(index);
        }

        _selectedIndex =
            _selection.Contains(primaryIndex)
                ? primaryIndex
                : _selection.IsEmpty
                    ? -1
                    : indices.First(index =>
                        _selection.Contains(index));
        _selectionAnchor = _selectedIndex;

        SelectedAssetIndexChanged?.Invoke(
            this,
            _selectedIndex);
        PublishSelectionChanged();

        if (_selectedIndex >= 0)
        {
            ScrollToAsset(_selectedIndex);
        }
    }

    public void SetLayout(
        ViewerLayoutMode layoutMode,
        int densityLevel)
    {
        if (!Enum.IsDefined(layoutMode))
        {
            throw new ArgumentOutOfRangeException(nameof(layoutMode));
        }

        if (densityLevel is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(densityLevel));
        }

        if (_layoutMode == layoutMode
            && _densityLevel == densityLevel)
        {
            return;
        }

        var anchor =
            GetViewportAnchorAssetIndex();

        _layoutMode = layoutMode;
        _densityLevel = densityLevel;
        _columns =
            CalculateColumns(
                Math.Max(1, _viewportWidth));

        RebuildRows(anchor);
    }

    public int RealizedRowCount => _rows.GetRealizedContainers().Count();

    public int SelectedRealizedTileCount =>
        this.GetVisualDescendants()
            .OfType<ViewerTileControl>()
            .Count(static tile => tile.IsSelected);

    public long? FirstRealizedAssetIndex
    {
        get
        {
            var firstRow = GetFirstRealizedRowIndex();
            return firstRow >= 0
                ? checked((long)firstRow * _columns)
                : null;
        }
    }

    public long? FirstVisibleAssetIndex
    {
        get
        {
            var firstRow = GetFirstVisibleRowIndex();
            return firstRow >= 0
                ? checked((long)firstRow * _columns)
                : null;
        }
    }

    public long? LastVisibleAssetIndex
    {
        get
        {
            var lastRow = GetLastVisibleRowIndex();
            if (lastRow < 0)
            {
                return null;
            }

            return Math.Min(
                AssetCount - 1,
                checked(((long)lastRow + 1) * _columns - 1));
        }
    }

    public ViewerViewportReadinessDiagnostics ViewportReadiness
    {
        get
        {
            var scroller = _galleryScrollViewer
                ?? _rows.GetVisualDescendants()
                    .OfType<ScrollViewer>()
                    .FirstOrDefault();
            if (scroller is null)
            {
                return default;
            }

            var viewportWidth = scroller.Viewport.Width;
            var viewportHeight = scroller.Viewport.Height;
            if (viewportWidth <= 0 || viewportHeight <= 0)
            {
                return default;
            }

            var visible = 0;
            var unready = 0;
            foreach (var container in _rows.GetRealizedContainers())
            {
                foreach (var tile in container
                    .GetVisualDescendants()
                    .OfType<ViewerTileControl>())
                {
                    var origin = tile.TranslatePoint(default, scroller);
                    if (origin is not { } location
                        || location.Y + tile.Bounds.Height <= 0
                        || location.Y >= viewportHeight
                        || location.X + tile.Bounds.Width <= 0
                        || location.X >= viewportWidth)
                    {
                        continue;
                    }

                    visible++;
                    if (!tile.IsReady)
                    {
                        unready++;
                    }
                }
            }

            return new ViewerViewportReadinessDiagnostics(
                visible,
                unready);
        }
    }

    // Slow-path failure diagnostics only: never allocate per-tile strings
    // during the normal small-scroll readiness sampling loop.
    public string DescribeUnreadyVisibleTilesForDiagnostics()
    {
        var scroller = _galleryScrollViewer
            ?? _rows.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault();
        if (scroller is null)
        {
            return "no-scrollviewer";
        }

        var details = new List<string>(8);
        foreach (var container in _rows.GetRealizedContainers())
        {
            foreach (var tile in container
                .GetVisualDescendants()
                .OfType<ViewerTileControl>())
            {
                var location = tile.TranslatePoint(default, scroller);
                if (location is not { } origin
                    || origin.Y + tile.Bounds.Height <= 0
                    || origin.Y >= scroller.Viewport.Height
                    || origin.X + tile.Bounds.Width <= 0
                    || origin.X >= scroller.Viewport.Width
                    || tile.IsReady)
                {
                    continue;
                }

                details.Add(
                    $"{tile.Index}:" +
                    (tile.IsFailed ? "failed" :
                        tile.IsLoadingForDiagnostics ? "loading" : "idle") +
                    $":{tile.FailureReason ?? "-"}");
                if (details.Count == 12)
                {
                    return string.Join(",", details) + ",...";
                }
            }
        }

        return details.Count == 0 ? "none" : string.Join(",", details);
    }

    public bool IsAssetReady(long index)
    {
        if ((ulong)index >= (ulong)AssetCount)
        {
            return false;
        }

        return this.GetVisualDescendants()
            .OfType<ViewerTileControl>()
            .Any(tile =>
                tile.Index == index
                && tile.IsReady);
    }

    public ViewerRuntimeDiagnostics Diagnostics => _session.Diagnostics;

    internal int WarmTileEntryCountForSmoke =>
        _warmPresentations.Count;

    internal long WarmTileHitCountForSmoke =>
        _warmPresentationHits;

    internal bool IsAssetWarmForSmoke(long index) =>
        _warmPresentations.ContainsKey(index);

    internal long? FirstWarmAssetIndexForSmoke =>
        _warmPresentationLru.First?.Value;

    public async Task<bool> EnsureAssetFocusTargetAsync(
        long index)
    {
        if ((ulong)index >= (ulong)AssetCount)
        {
            return false;
        }

        if (GetAssetFocusTarget(index) is not null)
        {
            return true;
        }

        var row =
            checked((int)(index / _columns));

        // ScrollIntoView requests realization, but Avalonia's virtualizing
        // presenter completes container creation during a subsequent layout
        // pass. Coordinate with that lifecycle explicitly instead of keeping
        // a stale Control reference or retrying Focus on a non-existent tile.
        _rows.ScrollIntoView(row);
        _rows.InvalidateMeasure();
        InvalidateMeasure();

        await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                _rows.ScrollIntoView(row);
                _rows.UpdateLayout();
                UpdateLayout();
            },
            DispatcherPriority.Loaded);

        if (GetAssetFocusTarget(index) is not null)
        {
            return true;
        }

        await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                _rows.ScrollIntoView(row);
                _rows.UpdateLayout();
                UpdateLayout();
            },
            DispatcherPriority.Render);

        return GetAssetFocusTarget(index) is not null;
    }

    public bool FocusAsset(long index)
    {
        if ((ulong)index >= (ulong)AssetCount)
        {
            return false;
        }

        _rows.ScrollIntoView(
            checked((int)(index / _columns)));
        _rows.UpdateLayout();
        UpdateLayout();

        var tile =
            GetAssetFocusTarget(index)
            as ViewerTileControl;
        return tile is not null
            && TryFocusTile(tile);
    }

    public void RestoreAssetFocus(long index)
    {
        CancelPendingAssetFocus();

        if ((ulong)index >= (ulong)AssetCount)
        {
            Focus(
                NavigationMethod.Unspecified,
                KeyModifiers.None);
            return;
        }

        if (FocusAsset(index))
        {
            return;
        }

        // ScrollIntoView requests realization, but Avalonia's virtualizing
        // panel can complete that work in a later layout pass. Keep the
        // semantic asset identity and finish focus restoration from layout /
        // tile-realization events rather than timing-based retries.
        _pendingFocusIndex = index;
        EnsurePendingFocusLayoutSubscription();

        Focus(
            NavigationMethod.Unspecified,
            KeyModifiers.None);

        _rows.ScrollIntoView(
            checked((int)(index / _columns)));
        _rows.UpdateLayout();
        UpdateLayout();

        TryCompletePendingAssetFocus();
    }

    private bool TryFocusTile(
        ViewerTileControl tile)
    {
        if (!tile.IsEffectivelyVisible
            || !tile.IsEffectivelyEnabled)
        {
            return false;
        }

        var focusManager =
            TopLevel.GetTopLevel(this)
                ?.FocusManager;
        return focusManager?.Focus(
                   tile,
                   NavigationMethod.Unspecified,
                   KeyModifiers.None)
               ?? tile.Focus(
                   NavigationMethod.Unspecified,
                   KeyModifiers.None);
    }

    private void EnsurePendingFocusLayoutSubscription()
    {
        if (_pendingFocusLayoutSubscribed)
        {
            return;
        }

        _rows.LayoutUpdated += OnPendingFocusLayoutUpdated;
        _pendingFocusLayoutSubscribed = true;
    }

    private void OnPendingFocusLayoutUpdated(
        object? sender,
        EventArgs e)
    {
        if (_pendingFocusIndex < 0)
        {
            RemovePendingFocusLayoutSubscription();
            return;
        }

        if (TryCompletePendingAssetFocus())
        {
            return;
        }

        // If the requested row was still outside the realized window during
        // this pass, keep it as the scroll target for the next real layout
        // pass. No delay/retry counter is involved.
        _rows.ScrollIntoView(
            checked((int)(_pendingFocusIndex / _columns)));
    }

    private bool TryCompletePendingAssetFocus()
    {
        if (_pendingFocusIndex < 0)
        {
            return false;
        }

        var tile =
            GetAssetFocusTarget(_pendingFocusIndex)
            as ViewerTileControl;
        if (tile is null
            || !TryFocusTile(tile))
        {
            return false;
        }

        CancelPendingAssetFocus();
        return true;
    }

    private void CompletePendingAssetFocus(
        ViewerTileControl tile)
    {
        if (_pendingFocusIndex != tile.Index
            || !TryFocusTile(tile))
        {
            return;
        }

        CancelPendingAssetFocus();
    }

    private void CancelPendingAssetFocus()
    {
        _pendingFocusIndex = -1;
        RemovePendingFocusLayoutSubscription();
    }

    private void RemovePendingFocusLayoutSubscription()
    {
        if (!_pendingFocusLayoutSubscribed)
        {
            return;
        }

        _rows.LayoutUpdated -= OnPendingFocusLayoutUpdated;
        _pendingFocusLayoutSubscribed = false;
    }


    public Control? GetAssetFocusTarget(long index) =>
        this.GetVisualDescendants()
            .OfType<ViewerTileControl>()
            .FirstOrDefault(
                item => item.Index == index);

    public bool IsAssetFocused(long index) =>
        this.GetVisualDescendants()
            .OfType<ViewerTileControl>()
            .Any(
                tile =>
                    tile.Index == index
                    && tile.IsFocused);


    internal ViewerTilePresentation
        GetRealizedTilePresentationForSmoke(
            long index)
    {
        var tile =
            this.GetVisualDescendants()
                .OfType<ViewerTileControl>()
                .FirstOrDefault(
                    item => item.Index == index)
            ?? throw new InvalidOperationException(
                $"Asset {index} is not realized.");

        return tile.GetPresentationForSmoke();
    }

    internal IReadOnlyList<ViewerTileActionGeometry>
        GetRealizedTileActionGeometryForSmoke(
            long index)
    {
        var tile =
            this.GetVisualDescendants()
                .OfType<ViewerTileControl>()
                .FirstOrDefault(
                    item => item.Index == index)
            ?? throw new InvalidOperationException(
                $"Asset {index} is not realized.");

        return tile.GetActionGeometryForSmoke();
    }

    internal bool IsRealizedTileActionOverlayVisibleForSmoke(
        long index)
    {
        var tile =
            this.GetVisualDescendants()
                .OfType<ViewerTileControl>()
                .FirstOrDefault(
                    item => item.Index == index)
            ?? throw new InvalidOperationException(
                $"Asset {index} is not realized.");

        return tile.IsActionOverlayVisibleForSmoke();
    }

    internal bool IsRealizedTileFailedForSmoke(
        long index) =>
        FindRealizedTileForSmoke(index)?.IsFailed
        == true;

    internal string? GetRealizedTileFailureReasonForSmoke(
        long index) =>
        FindRealizedTileForSmoke(index)?.FailureReason;

    internal void RetryRealizedTileForSmoke(
        long index) =>
        (FindRealizedTileForSmoke(index)
         ?? throw new InvalidOperationException(
             $"Asset {index} is not realized."))
        .RetryForSmoke();

    private ViewerTileControl? FindRealizedTileForSmoke(
        long index) =>
        this.GetVisualDescendants()
            .OfType<ViewerTileControl>()
            .FirstOrDefault(
                item => item.Index == index);

    public event EventHandler<long>? SelectedAssetIndexChanged;

    public event EventHandler<ViewerSelectionSnapshot>? SelectionChanged;

    public event EventHandler<long>? AssetInvoked;

    public event EventHandler<long>? AssetDetailRequested;

    public event EventHandler<ViewerAssetContextRequestedEventArgs>?
        AssetContextRequested;

    public bool IsAssetSelected(long index) =>
        _selection.Contains(index);

    public void SelectAsset(
        long index,
        bool scrollIntoView = true,
        ViewerSelectionMode mode = ViewerSelectionMode.Replace)
    {
        if ((ulong)index >= (ulong)AssetCount)
        {
            return;
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        Focus();

        var previousPrimary = _selectedIndex;
        var changed = false;

        switch (mode)
        {
            case ViewerSelectionMode.Replace:
                changed =
                    _selection.SetSingle(index);
                _selectionAnchor = index;
                _selectedIndex = index;
                break;

            case ViewerSelectionMode.Toggle:
                var selectedAfterToggle =
                    _selection.Toggle(index);
                changed = true;
                _selectedIndex =
                    selectedAfterToggle
                        ? index
                        : _selection.IsEmpty
                            ? -1
                            : _selection.Max;
                _selectionAnchor = index;
                break;

            case ViewerSelectionMode.Range:
                var anchor =
                    (ulong)_selectionAnchor < (ulong)AssetCount
                        ? _selectionAnchor
                        : (ulong)_selectedIndex < (ulong)AssetCount
                            ? _selectedIndex
                            : index;
                var start = Math.Min(anchor, index);
                var end = Math.Max(anchor, index);

                changed =
                    _selection.SetRange(
                        start,
                        end);
                _selectedIndex = index;
                if (_selectionAnchor < 0)
                {
                    _selectionAnchor = anchor;
                }

                break;
        }

        if (previousPrimary != _selectedIndex)
        {
            SelectedAssetIndexChanged?.Invoke(
                this,
                _selectedIndex);
        }

        if (changed
            || previousPrimary != _selectedIndex)
        {
            PublishSelectionChanged();
        }

        if (scrollIntoView)
        {
            var row =
                checked((int)(index / _columns));
            _rows.ScrollIntoView(row);
        }
    }

    public void SelectAll()
    {
        if (AssetCount == 0)
        {
            ClearSelection();
            return;
        }

        _selection.SelectAll(
            AssetCount);

        _selectedIndex =
            _selectedIndex >= 0
                ? _selectedIndex
                : 0;
        _selectionAnchor =
            _selectionAnchor >= 0
                ? _selectionAnchor
                : 0;

        SelectedAssetIndexChanged?.Invoke(
            this,
            _selectedIndex);
        PublishSelectionChanged();
    }

    public void ClearSelection()
    {
        if (_selection.IsEmpty
            && _selectedIndex < 0)
        {
            return;
        }

        _selection.Clear();
        _selectedIndex = -1;
        _selectionAnchor = -1;
        SelectedAssetIndexChanged?.Invoke(
            this,
            -1);
        PublishSelectionChanged();
    }

    private void PublishSelectionChanged() =>
        SelectionChanged?.Invoke(
            this,
            new ViewerSelectionSnapshot(
                _selection.AsReadOnlyList(),
                _selectedIndex,
                _selectionAnchor));

    public void ScrollToAsset(long index)
    {
        if ((ulong)index >= (ulong)AssetCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        _rows.ScrollIntoView(checked((int)(index / _columns)));
    }

    public async Task DrainBitmapReleasesAsync()
    {
        while (true)
        {
            Task[] releases;
            Task[] loads;

            lock (_bitmapReleaseGate)
            {
                if (_bitmapReleaseFailure is not null)
                {
                    throw new InvalidOperationException(
                        "A thumbnail composition release failed.",
                        _bitmapReleaseFailure);
                }

                releases = [.. _pendingBitmapReleases];
            }

            lock (_tileLoadGate)
            {
                loads = [.. _pendingTileLoads];
            }

            if (releases.Length == 0
                && loads.Length == 0)
            {
                // A tile completion can schedule a composition-fenced release
                // while the first snapshots are being taken. Recheck both
                // sets before declaring the Viewer surface drained.
                lock (_bitmapReleaseGate)
                {
                    if (_pendingBitmapReleases.Count != 0)
                    {
                        continue;
                    }
                }

                lock (_tileLoadGate)
                {
                    if (_pendingTileLoads.Count == 0)
                    {
                        return;
                    }
                }

                continue;
            }

            await Task.WhenAll(
                    releases.Concat(loads))
                .ConfigureAwait(false);
        }
    }

    private void TrackTileLoad(Task load)
    {
        ArgumentNullException.ThrowIfNull(load);

        lock (_tileLoadGate)
        {
            _pendingTileLoads.Add(load);
        }

        _ = ObserveTileLoadAsync(load);
    }

    private async Task ObserveTileLoadAsync(Task load)
    {
        try
        {
            await load.ConfigureAwait(false);
        }
        catch
        {
            // LoadAsync reports expected tile failures through ViewerSession.
            // The observer owns lifecycle bookkeeping only.
        }
        finally
        {
            lock (_tileLoadGate)
            {
                _pendingTileLoads.Remove(load);
            }
        }
    }

    private void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        _compositor =
            ElementComposition.GetElementVisual(this)?.Compositor
            ?? _compositor;
    }

    private void RememberWarmPresentation(
        long index,
        ViewerAsset asset,
        ViewerThumbnail thumbnail)
    {
        if (_warmPresentations.Remove(
                index,
                out var previous))
        {
            _warmPresentationLru.Remove(
                previous.Node);
        }

        var node =
            _warmPresentationLru.AddLast(
                index);
        _warmPresentations[index] =
            new WarmPresentation(
                asset,
                thumbnail,
                node);

        while (_warmPresentations.Count
               > WarmPresentationLimit)
        {
            var oldest =
                _warmPresentationLru.First;
            if (oldest is null)
            {
                break;
            }

            _warmPresentationLru.RemoveFirst();
            _warmPresentations.Remove(
                oldest.Value);
        }
    }

    private void UpdateWarmPresentationAsset(
        long index,
        ViewerAsset asset)
    {
        if (!_warmPresentations.TryGetValue(
                index,
                out var existing))
        {
            return;
        }

        _warmPresentations[index] =
            existing with
            {
                Asset = asset
            };
    }

    private bool TryAcquireWarmPresentation(
        long index,
        out ViewerAsset asset,
        out DecodedBitmapLease lease)
    {
        asset = null!;
        lease = null!;

        if (!_warmPresentations.TryGetValue(
                index,
                out var existing))
        {
            return false;
        }

        if (!_session.BitmapCache.TryAcquireExisting(
                existing.Thumbnail,
                out var acquired)
            || acquired is null)
        {
            _warmPresentations.Remove(index);
            _warmPresentationLru.Remove(
                existing.Node);
            return false;
        }

        _warmPresentationLru.Remove(
            existing.Node);
        var node =
            _warmPresentationLru.AddLast(
                index);
        _warmPresentations[index] =
            existing with
            {
                Node = node
            };

        _warmPresentationHits++;
        asset = existing.Asset;
        lease = acquired;
        return true;
    }

    private void ReleaseBitmapLeaseAfterComposition(
        DecodedBitmapLease lease)
    {
        var compositor = _compositor;
        if (compositor is null)
        {
            lease.Dispose();
            return;
        }

        Task release;
        try
        {
            var batch =
                compositor.RequestCompositionBatchCommitAsync();
            release =
                DisposeBitmapLeaseAfterAsync(
                    lease,
                    batch.Rendered);
        }
        catch (Exception exception)
        {
            lock (_bitmapReleaseGate)
            {
                _bitmapReleaseFailure ??= exception;
                _unfencedBitmapLeases.Add(lease);
            }

            return;
        }

        lock (_bitmapReleaseGate)
        {
            _pendingBitmapReleases.Add(release);
        }

        _ = ObserveBitmapReleaseAsync(release);
    }

    private async Task ObserveBitmapReleaseAsync(Task release)
    {
        try
        {
            await release.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (_bitmapReleaseGate)
            {
                _bitmapReleaseFailure ??= exception;
            }
        }
        finally
        {
            lock (_bitmapReleaseGate)
            {
                _pendingBitmapReleases.Remove(release);
            }
        }
    }

    private async Task DisposeBitmapLeaseAfterAsync(
        DecodedBitmapLease lease,
        Task compositionRendered)
    {
        try
        {
            await compositionRendered.ConfigureAwait(false);

            // CompositionBatch.Rendered may complete synchronously on the
            // render loop. Avalonia/Skia bitmap disposal belongs on the UI
            // dispatcher, matching ViewerOriginalBitmap's release contract.
            await Dispatcher.UIThread.InvokeAsync(
                lease.Dispose);
        }
        catch (Exception exception)
        {
            lock (_bitmapReleaseGate)
            {
                _bitmapReleaseFailure ??= exception;
                _unfencedBitmapLeases.Add(lease);
            }

            throw;
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var width = Math.Max(1, e.NewSize.Width);
        var previousWidth = _viewportWidth;
        _viewportWidth = width;

        var columns =
            CalculateColumns(width);
        var listWidthChanged =
            _layoutMode == ViewerLayoutMode.List
            && Math.Abs(previousWidth - width) >= 1;

        if (columns == _columns
            && !listWidthChanged)
        {
            return;
        }

        var anchorAssetIndex =
            GetViewportAnchorAssetIndex();
        _columns = columns;
        RebuildRows(anchorAssetIndex);
    }

    private int CalculateColumns(double width)
    {
        if (_layoutMode == ViewerLayoutMode.List)
        {
            return 1;
        }

        var availableWidth =
            Math.Max(
                1,
                width - (GridOuterPadding * 2));
        var tileWidth =
            GetTileWidth();
        var cellWidth =
            tileWidth
            + _session.Options.TileSpacing;
        return Math.Max(
            1,
            (int)Math.Floor(
                (availableWidth + _session.Options.TileSpacing)
                / cellWidth));
    }

    private double GetTileWidth() =>
        _layoutMode == ViewerLayoutMode.List
            ? Math.Max(
                320,
                _viewportWidth - (GridOuterPadding * 2))
            : _densityLevel switch
            {
                0 => 120,
                1 => _session.Options.TileWidth,
                2 => 260,
                _ => throw new ArgumentOutOfRangeException()
            };

    private double GetTileHeight() =>
        _layoutMode == ViewerLayoutMode.List
            ? _densityLevel switch
            {
                0 => 60,
                1 => 68,
                2 => 82,
                _ => throw new ArgumentOutOfRangeException()
            }
            : _densityLevel switch
            {
                0 => 120,
                1 => _session.Options.TileHeight,
                2 => 260,
                _ => throw new ArgumentOutOfRangeException()
            };

    private long? GetViewportAnchorAssetIndex()
    {
        var firstVisibleRow = GetFirstVisibleRowIndex();
        if (firstVisibleRow >= 0)
        {
            return checked((long)firstVisibleRow * _columns);
        }

        var firstRealizedRow = GetFirstRealizedRowIndex();
        if (firstRealizedRow >= 0)
        {
            return checked((long)firstRealizedRow * _columns);
        }

        return _selectedIndex >= 0
            ? _selectedIndex
            : null;
    }

    private int GetFirstVisibleRowIndex()
    {
        var viewportHeight = _rows.Bounds.Height;
        var bestIndex = -1;
        var bestTop = double.PositiveInfinity;

        foreach (var container in _rows.GetRealizedContainers())
        {
            var index = _rows.IndexFromContainer(container);
            if (index < 0)
            {
                continue;
            }

            var origin = container.TranslatePoint(default, _rows);
            if (origin is not { } point)
            {
                continue;
            }

            var top = point.Y;
            var bottom = top + container.Bounds.Height;
            if (bottom <= 0 || top >= viewportHeight)
            {
                continue;
            }

            if (top < bestTop)
            {
                bestTop = top;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private int GetLastVisibleRowIndex()
    {
        var viewportHeight = _rows.Bounds.Height;
        var bestIndex = -1;
        var bestBottom = double.NegativeInfinity;

        foreach (var container in _rows.GetRealizedContainers())
        {
            var index = _rows.IndexFromContainer(container);
            if (index < 0)
            {
                continue;
            }

            var origin = container.TranslatePoint(default, _rows);
            if (origin is not { } point)
            {
                continue;
            }

            var top = point.Y;
            var bottom = top + container.Bounds.Height;
            if (bottom <= 0 || top >= viewportHeight)
            {
                continue;
            }

            if (bottom > bestBottom)
            {
                bestBottom = bottom;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private int GetFirstRealizedRowIndex() =>
        _rows.GetRealizedContainers()
            .Select(_rows.IndexFromContainer)
            .Where(static index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();

    private void RebuildRows(long? anchorAssetIndex = null)
    {
        _rows.ItemsSource = new VirtualRowIndexList(AssetCount, _columns);
        var tileWidth = GetTileWidth();
        var tileHeight = GetTileHeight();

        _rows.ItemTemplate = new FuncDataTemplate<long>(
            (rowIndex, _) => new ViewerRowControl(
                this,
                _session,
                rowIndex,
                _columns,
                _layoutMode,
                tileWidth,
                tileHeight),
            supportsRecycling: false);

        if (anchorAssetIndex is { } anchor
            && AssetCount > 0)
        {
            var clamped = Math.Clamp(anchor, 0, AssetCount - 1);
            var row = checked((int)(clamped / _columns));

            Dispatcher.UIThread.Post(
                () => _rows.ScrollIntoView(row),
                DispatcherPriority.Loaded);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape
            && e.KeyModifiers == KeyModifiers.None)
        {
            ClearSelection();
            e.Handled = true;
            return;
        }

        if (_selectedIndex >= 0
            && e.KeyModifiers == KeyModifiers.None
            && e.Key is Key.Enter or Key.Space)
        {
            AssetInvoked?.Invoke(
                this,
                _selectedIndex);
            e.Handled = true;
            return;
        }

        if (_selectedIndex >= 0
            && e.Key == Key.I
            && e.KeyModifiers == KeyModifiers.None)
        {
            AssetDetailRequested?.Invoke(
                this,
                _selectedIndex);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A
            && e.KeyModifiers == KeyModifiers.Control)
        {
            SelectAll();
            e.Handled = true;
            return;
        }

        if (AssetCount == 0)
        {
            return;
        }

        long next;
        if (_selectedIndex < 0)
        {
            next = e.Key switch
            {
                Key.Left or Key.Right or Key.Up or Key.Down or Key.Home => 0,
                Key.End => AssetCount - 1,
                _ => -1
            };

            if (next < 0)
            {
                return;
            }
        }
        else
        {
            next = e.Key switch
            {
                Key.Left => _selectedIndex - 1,
                Key.Right => _selectedIndex + 1,
                Key.Up => _selectedIndex - _columns,
                Key.Down => _selectedIndex + _columns,
                Key.Home => 0,
                Key.End => AssetCount - 1,
                _ => _selectedIndex
            };
        }

        next = Math.Clamp(
            next,
            0,
            AssetCount - 1);

        if (next == _selectedIndex)
        {
            return;
        }

        var mode =
            e.KeyModifiers.HasFlag(
                KeyModifiers.Shift)
                ? ViewerSelectionMode.Range
                : ViewerSelectionMode.Replace;

        SelectAsset(
            next,
            mode: mode);
        e.Handled = true;
    }

    internal int LookaheadDirectionForSmoke =>
        _lookaheadDirection;

    internal bool IsScrollTrackingAttachedForSmoke =>
        _galleryScrollViewer is not null;

    internal long LookaheadScheduleCountForSmoke =>
        _lookaheadScheduleCount;

    internal static long ResolveLookaheadRowForSmoke(
        long rowIndex,
        int direction) =>
        rowIndex + (direction < 0 ? -1L : 1L);

    private void OnGalleryWheel(
        object? sender,
        PointerWheelEventArgs e) =>
        SetLookaheadDirection(-e.Delta.Y);

    private void EnsureScrollTracking()
    {
        if (_galleryScrollViewer is not null)
        {
            return;
        }

        var scroller = _rows.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
        if (scroller is null)
        {
            return;
        }

        _galleryScrollViewer = scroller;
        _lastGalleryOffsetY = scroller.Offset.Y;
        scroller.PropertyChanged += OnGalleryOffsetChanged;
    }

    private void StopScrollTracking()
    {
        if (_galleryScrollViewer is { } scroller)
        {
            scroller.PropertyChanged -= OnGalleryOffsetChanged;
            _galleryScrollViewer = null;
        }
    }

    private void OnGalleryOffsetChanged(
        object? sender,
        AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ScrollViewer.OffsetProperty
            || sender is not ScrollViewer scroller)
        {
            return;
        }

        var next = scroller.Offset.Y;
        var delta = next - _lastGalleryOffsetY;
        _lastGalleryOffsetY = next;
        SetLookaheadDirection(delta);
    }

    private void SetLookaheadDirection(double deltaY)
    {
        var next = deltaY > 0.01
            ? 1
            : deltaY < -0.01
                ? -1
                : _lookaheadDirection;
        if (next == _lookaheadDirection)
        {
            return;
        }

        _lookaheadDirection = next;
        // A small reverse scroll can stay within the same set of
        // realized rows. A pure row-attached scheduler would never
        // reschedule upward warmup in that case.
        var nearestVisible = next < 0
            ? GetFirstVisibleRowIndex()
            : GetLastVisibleRowIndex();
        if (nearestVisible >= 0)
        {
            ScheduleLookahead(nearestVisible, _columns);
        }
    }

    private void CancelLookahead()
    {
        _lookaheadCancellation?.Cancel();
        _lookaheadCancellation?.Dispose();
        _lookaheadCancellation = null;
    }

    private void ScheduleLookahead(long rowIndex, int columns)
    {
        _lookaheadScheduleCount++;
        // Rows may attach rapidly while the user scrolls. Never enqueue
        // two independent before/after ranges for each attached row:
        // 100k/10k fast-scroll workloads otherwise amplify background
        // thumbnail requests and exceed the product's work budget.
        CancelLookahead();
        if (_session.Options.PrefetchRows <= 0)
        {
            return;
        }

        var session = _session;
        _lookaheadCancellation = new CancellationTokenSource();
        TrackTileLoad(
            PrefetchViewportLookaheadAsync(
                session,
                rowIndex,
                columns,
                _lookaheadDirection,
                _lookaheadCancellation.Token));
    }

    private async Task PrefetchViewportLookaheadAsync(
        ViewerSession session,
        long rowIndex,
        int columns,
        int direction,
        CancellationToken cancellationToken)
    {
        try
        {
            // A short quiet period lets the current Foreground requests
            // enqueue, while avoiding the previous gate that required
            // every visible tile to finish first.
            if (session.Options.PrefetchDelay > TimeSpan.Zero)
            {
                await Task.Delay(
                    session.Options.PrefetchDelay,
                    cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var rows = session.Options.PrefetchRows;
            var afterStartIndex = checked((rowIndex + 1) * columns);
            var afterCount = afterStartIndex < session.Count
                ? checked((int)Math.Min(
                    session.Count - afterStartIndex,
                    (long)rows * columns))
                : 0;
            var beforeStartRow = Math.Max(0, rowIndex - rows);
            var beforeStartIndex = checked(beforeStartRow * columns);
            var beforeCount = checked((int)(
                (rowIndex - beforeStartRow) * columns));

            // Reverse browsing is as important as forward browsing.
            // Start the probable next direction first, while keeping
            // the other side's lookahead within the same bounded task.
            if (direction < 0 && beforeCount > 0)
            {
                await session.PrefetchAsync(
                    beforeStartIndex,
                    beforeCount,
                    cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (afterCount > 0)
            {
                await session.PrefetchAsync(
                    afterStartIndex,
                    afterCount,
                    cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (direction >= 0 && beforeCount > 0)
            {
                await session.PrefetchAsync(
                    beforeStartIndex,
                    beforeCount,
                    cancellationToken).ConfigureAwait(false);
            }

            // Source prefetch alone is not enough: a tile still has to
            // decode an Avalonia Bitmap when the user reaches it.
            // Only decode one next row after current visible work is
            // ready, leaving foreground decode capacity untouched.
            var nextRowCount = Math.Min(
                columns,
                Math.Min(8, session.Options.DecodedBitmapEntryLimit / 8));
            var warmRow = ResolveLookaheadRowForSmoke(
                rowIndex,
                direction);
            var warmStartIndex = checked(warmRow * columns);
            if (nextRowCount > 0
                && warmStartIndex >= 0
                && warmStartIndex < session.Count)
            {
                for (var attempt = 0; attempt < 80; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var state = session.Diagnostics;
                    if (state.AttachedTiles > 0
                        && state.ReadyTiles >= state.AttachedTiles
                        && state.ActiveBitmapDecodes == 0)
                    {
                        await PredecodeNextRowAsync(
                            session,
                            warmStartIndex,
                            checked((int)Math.Min(
                                session.Count - warmStartIndex,
                                nextRowCount)),
                            cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    await Task.Delay(
                        TimeSpan.FromMilliseconds(25),
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PredecodeNextRowAsync(
        ViewerSession session,
        long startIndex,
        int count,
        CancellationToken cancellationToken)
    {
        for (var offset = 0; offset < count; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = startIndex + offset;
            DecodedBitmapLease? lease = null;
            try
            {
                var asset = await session.GetAssetAsync(
                    index, cancellationToken).ConfigureAwait(false);
                var thumbnail = await session.GetThumbnailAsync(
                    asset,
                    ViewerThumbnailPriority.Background,
                    cancellationToken).ConfigureAwait(false);
                lease = await session.BitmapCache.AcquireAsync(
                    thumbnail,
                    cancellationToken).ConfigureAwait(false);

                // Warm descriptors and decoded cache residency must be
                // updated together on UI thread. An unleased bitmap
                // remains in the bounded LRU until later eviction.
                var acquired = lease;
                await Dispatcher.UIThread.InvokeAsync(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (ReferenceEquals(_session, session))
                        {
                            RememberWarmPresentation(
                                index,
                                asset,
                                thumbnail);
                        }

                        acquired.Dispose();
                    });
                lease = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A failed offscreen warm-up must never mark a visible
                // tile as failed. The actual tile retains its retry
                // and user-facing error handling when it appears.
                System.Diagnostics.Trace.TraceWarning(
                    "Viewer lookahead bitmap preparation failed: {0}",
                    exception);
                break;
            }
            finally
            {
                if (lease is not null)
                {
                    var orphan = lease;
                    await Dispatcher.UIThread.InvokeAsync(
                        orphan.Dispose);
                }
            }
        }
    }

    private sealed class ViewerRowControl : StackPanel
    {
        private readonly long _rowIndex;
        private readonly int _columns;
        private readonly ThumbnailViewerControl _owner;

        public ViewerRowControl(
            ThumbnailViewerControl owner,
            ViewerSession session,
            long rowIndex,
            int columns,
            ViewerLayoutMode layoutMode,
            double tileWidth,
            double tileHeight)
        {
            _owner = owner;
            _rowIndex = rowIndex;
            _columns = columns;

            Orientation = Orientation.Horizontal;
            Spacing = session.Options.TileSpacing;
            Height =
                layoutMode == ViewerLayoutMode.Grid
                    ? tileHeight + session.Options.TileSpacing
                    : tileHeight;

            var start = checked(rowIndex * columns);
            for (var column = 0; column < columns; column++)
            {
                var index = start + column;
                if (index >= session.Count)
                {
                    break;
                }

                Children.Add(new ViewerTileControl(
                    _owner,
                    session,
                    index,
                    layoutMode,
                    tileWidth,
                    tileHeight));
            }

            AttachedToVisualTree += (_, _) =>
            {
                _owner.EnsureScrollTracking();
                _owner.ScheduleLookahead(_rowIndex, _columns);
            };
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1001:Types that own disposable fields should be disposable",
        Justification = "The tile cancellation source is cancelled and disposed on visual detach.")]
    private sealed class ViewerTileControl : Border
    {
        private readonly ThumbnailViewerControl _owner;
        private readonly ViewerSession _session;
        private readonly long _index;
        private readonly ViewerLayoutMode _layoutMode;
        private readonly Image _image;
        private readonly TextBlock? _label;
        private readonly TextBlock? _listSecondary;
        private readonly TileCaptionOverlay? _captionOverlay;
        private readonly Grid? _gridLayers;
        private readonly Grid? _listPanel;
        private Control? _actionOverlay;
        private Control? _selectionBadge;
        private Border? _failureOverlay;
        private Button? _retryButton;
        private CancellationTokenSource? _loadCancellation;
        private DecodedBitmapLease? _bitmapLease;
        private ViewerAsset? _asset;
        private string? _failureReason;
        private bool _isReady;
        private bool _isLoading;
        private long _loadStartedAt;
        private bool _hovered;
        private bool _pressed;

        private static readonly IBrush TileBackground =
            ViewerVisualTokens.Surface;
        private static readonly IBrush SelectedBorder =
            ViewerVisualTokens.Selection;
        private static readonly IBrush SelectedHoverBorder =
            ViewerVisualTokens.Focus;
        private static readonly IBrush HoverBorder =
            ViewerVisualTokens.BorderStrong;
        private static readonly IBrush PressedBorder =
            ViewerVisualTokens.Focus;
        private static readonly IBrush FocusBorder =
            ViewerVisualTokens.Focus;
        private static readonly IBrush OverlayBackground =
            ViewerVisualTokens.Overlay;
        private static readonly IBrush OverlayBorder =
            ViewerVisualTokens.BorderStrong;
        public ViewerTileControl(
            ThumbnailViewerControl owner,
            ViewerSession session,
            long index,
            ViewerLayoutMode layoutMode,
            double tileWidth,
            double tileHeight)
        {
            _owner = owner;
            _session = session;
            _index = index;
            _layoutMode = layoutMode;

            Width = tileWidth;
            Height = tileHeight;
            Focusable = true;
            Padding = new Thickness(0);
            BorderThickness = new Thickness(2);
            BorderBrush = Brushes.Transparent;
            CornerRadius =
                new CornerRadius(
                    layoutMode == ViewerLayoutMode.Grid
                        ? GridCornerRadius
                        : 7);
            ClipToBounds = true;
            Background = TileBackground;

            _image = new Image
            {
                Stretch =
                    layoutMode == ViewerLayoutMode.Grid
                        ? Stretch.UniformToFill
                        : Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            if (layoutMode == ViewerLayoutMode.List)
            {
                _label = new TextBlock
                {
                    Text = " ",
                    MaxLines = 1,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontSize = ViewerVisualTokens.BodyFontSize,
                    FontWeight = FontWeight.Medium,
                    Foreground = ViewerVisualTokens.Foreground
                };
                _listSecondary = new TextBlock
                {
                    Text = " ",
                    MaxLines = 1,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontSize = ViewerVisualTokens.CaptionFontSize,
                    Foreground = ViewerVisualTokens.MutedForeground
                };
                _captionOverlay = null;
            }
            else
            {
                _label = null;
                _listSecondary = null;
                _captionOverlay =
                    new TileCaptionOverlay(tileWidth);
            }

            if (layoutMode == ViewerLayoutMode.List)
            {
                _image.Width =
                    Math.Max(48, tileHeight - 10);
                _image.Height =
                    Math.Max(48, tileHeight - 10);
                _image.Margin = new Thickness(4);

                var panel = new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 10
                };
                _listPanel = panel;
                _gridLayers = null;
                panel.Children.Add(_image);

                var text =
                    new StackPanel
                    {
                        Spacing = 3,
                        VerticalAlignment =
                            VerticalAlignment.Center
                    };
                text.Children.Add(_label!);
                text.Children.Add(_listSecondary!);
                Grid.SetColumn(text, 1);
                panel.Children.Add(text);

                Child = panel;
            }
            else
            {
                var layers = new Grid();
                _gridLayers = layers;
                layers.Children.Add(_image);

                layers.Children.Add(_captionOverlay!);
                Child = layers;
                _listPanel = null;
            }

            PointerPressed += OnPointerPressed;
            PointerReleased += OnPointerReleased;
            PointerEntered += OnPointerEntered;
            PointerExited += OnPointerExited;
            GotFocus += OnFocusChanged;
            LostFocus += OnFocusChanged;
            AttachedToVisualTree += OnAttached;
            DetachedFromVisualTree += OnDetached;
        }

        private const string InfoOverlayIconPath =
            "M12 21a9 9 0 100-18 9 9 0 000 18z M12 10.5v6 M12 7.5h.01";
        private const string ExpandOverlayIconPath =
            "M8.25 3.75h-4.5v4.5 M15.75 3.75h4.5v4.5 M8.25 20.25h-4.5v-4.5 M15.75 20.25h4.5v-4.5";

        private static Button CreateOverlayButton(
            string pathData,
            string tooltip,
            string? acceleratorKey = null)
        {
            var icon =
                new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse(pathData),
                    Stroke = ViewerVisualTokens.Foreground,
                    StrokeThickness = 1.8,
                    Stretch = Stretch.Uniform,
                    Width = 15,
                    Height = 15,
                    HorizontalAlignment =
                        HorizontalAlignment.Center,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            var button =
                new Button
                {
                    Content = icon,
                    Width = 30,
                    Height = 30,
                    MinWidth = 30,
                    MinHeight = 30,
                    Padding = new Thickness(0),
                    CornerRadius = new CornerRadius(8),
                    Background = OverlayBackground,
                    Foreground = ViewerVisualTokens.Foreground,
                    BorderBrush = OverlayBorder,
                    BorderThickness = new Thickness(1),
                    HorizontalContentAlignment =
                        HorizontalAlignment.Center,
                    VerticalContentAlignment =
                        VerticalAlignment.Center
                };
            button.Resources["ButtonBackgroundPointerOver"] =
                ViewerVisualTokens.Hover;
            button.Resources["ButtonBorderBrushPointerOver"] =
                ViewerVisualTokens.Focus;
            button.Resources["ButtonForegroundPointerOver"] =
                ViewerVisualTokens.Foreground;
            button.Resources["ButtonBackgroundPressed"] =
                ViewerVisualTokens.Pressed;
            button.Resources["ButtonBorderBrushPressed"] =
                ViewerVisualTokens.Focus;
            button.Resources["ButtonForegroundPressed"] =
                ViewerVisualTokens.Foreground;
            ToolTip.SetTip(button, tooltip);
            ViewerVisualTokens.Name(
                button,
                tooltip,
                acceleratorKey: acceleratorKey);
            return button;
        }

        public long Index => _index;

        public bool IsReady => _isReady;

        internal bool IsLoadingForDiagnostics => _isLoading;


        public bool IsFailed =>
            _failureOverlay?.IsVisible == true;

        public string? FailureReason =>
            _failureReason;

        public bool IsSelected { get; private set; }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _owner.SelectionChanged += OnSelectionChanged;
            UpdateSelection();
            _owner.CompletePendingAssetFocus(this);
            _session.NotifyTileAttached();
            StartLoad();
        }

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _owner.SelectionChanged -= OnSelectionChanged;
            _session.NotifyTileDetached();
            CancelLoad();
        }

        private void OnPointerEntered(
            object? sender,
            PointerEventArgs e)
        {
            _hovered = true;
            UpdateVisualState();
        }

        private void OnPointerExited(
            object? sender,
            PointerEventArgs e)
        {
            _hovered = false;
            _pressed = false;
            UpdateVisualState();
        }

        private void OnPointerReleased(
            object? sender,
            PointerReleasedEventArgs e)
        {
            _pressed = false;
            UpdateVisualState();
        }

        private void OnFocusChanged(
            object? sender,
            RoutedEventArgs e) =>
            UpdateVisualState();

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _pressed = true;
            UpdateVisualState();
            if (e.GetCurrentPoint(this).Properties.PointerUpdateKind
                == PointerUpdateKind.RightButtonPressed)
            {
                if (!_owner.IsAssetSelected(_index))
                {
                    _owner.SelectAsset(
                        _index,
                        scrollIntoView: false,
                        ViewerSelectionMode.Replace);
                }

                Focus();

                _owner.AssetContextRequested?.Invoke(
                    _owner,
                    new ViewerAssetContextRequestedEventArgs(
                        _index,
                        this));
                e.Handled = true;
                return;
            }

            if (e.Source is Button
                || (e.Source as Visual)
                    ?.FindAncestorOfType<Button>() is not null)
            {
                return;
            }

            var mode =
                e.KeyModifiers.HasFlag(
                    KeyModifiers.Shift)
                    ? ViewerSelectionMode.Range
                    : e.KeyModifiers.HasFlag(
                        KeyModifiers.Control)
                        ? ViewerSelectionMode.Toggle
                        : ViewerSelectionMode.Replace;

            _owner.SelectAsset(
                _index,
                scrollIntoView: false,
                mode);
            Focus();

            if (e.ClickCount >= 2
                && mode == ViewerSelectionMode.Replace)
            {
                _owner.AssetInvoked?.Invoke(
                    _owner,
                    _index);
            }

            e.Handled = true;
        }

        private void OnSelectionChanged(
            object? sender,
            ViewerSelectionSnapshot selection) =>
            UpdateSelection();

        private void UpdateSelection()
        {
            IsSelected =
                _owner.IsAssetSelected(
                    _index);
            UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            var visualState =
                ResolveTileVisualState(
                    IsSelected,
                    _hovered,
                    _pressed,
                    IsKeyboardFocusWithin);

            BorderBrush =
                visualState switch
                {
                    ViewerTileVisualState.SelectedHover =>
                        SelectedHoverBorder,
                    ViewerTileVisualState.Selected =>
                        SelectedBorder,
                    ViewerTileVisualState.Pressed =>
                        PressedBorder,
                    ViewerTileVisualState.Hover =>
                        HoverBorder,
                    ViewerTileVisualState.Focus =>
                        FocusBorder,
                    _ => Brushes.Transparent
                };

            Background =
                visualState switch
                {
                    ViewerTileVisualState.SelectedHover =>
                        ViewerVisualTokens.SelectedHover,
                    ViewerTileVisualState.Selected =>
                        ViewerVisualTokens.SelectedSurface,
                    ViewerTileVisualState.Pressed =>
                        ViewerVisualTokens.Pressed,
                    ViewerTileVisualState.Hover =>
                        ViewerVisualTokens.Hover,
                    _ => TileBackground
                };

            var showActions =
                ShouldShowTileActions(
                    _layoutMode,
                    IsSelected,
                    _hovered,
                    IsKeyboardFocusWithin);
            if (showActions)
            {
                EnsureActionOverlay();
            }

            if (_actionOverlay is not null)
            {
                _actionOverlay.IsVisible =
                    showActions;
            }

            var showBadge =
                IsSelected
                && _layoutMode == ViewerLayoutMode.Grid;
            if (showBadge)
            {
                EnsureSelectionBadge();
            }

            if (_selectionBadge is not null)
            {
                _selectionBadge.IsVisible =
                    showBadge;
            }
        }

        private void EnsureActionOverlay()
        {
            if (_actionOverlay is not null)
            {
                return;
            }

            var actions =
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    HorizontalAlignment =
                        HorizontalAlignment.Right,
                    VerticalAlignment =
                        _layoutMode == ViewerLayoutMode.Grid
                            ? VerticalAlignment.Top
                            : VerticalAlignment.Center,
                    Margin =
                        _layoutMode == ViewerLayoutMode.Grid
                            ? new Thickness(0, 8, 8, 0)
                            : new Thickness(4)
                };

            var info =
                CreateOverlayButton(
                    InfoOverlayIconPath,
                    "詳細 (I)",
                    "I");
            info.Click +=
                (_, _) =>
                {
                    _owner.SelectAsset(
                        _index,
                        scrollIntoView: false);
                    _owner.AssetDetailRequested?.Invoke(
                        _owner,
                        _index);
                };
            actions.Children.Add(info);

            var open =
                CreateOverlayButton(
                    ExpandOverlayIconPath,
                    "大きく表示",
                    "Enter");
            open.Click +=
                (_, _) =>
                {
                    _owner.SelectAsset(
                        _index,
                        scrollIntoView: false);
                    _owner.AssetInvoked?.Invoke(
                        _owner,
                        _index);
                };
            actions.Children.Add(open);

            _actionOverlay = actions;

            if (_layoutMode == ViewerLayoutMode.Grid)
            {
                _gridLayers!.Children.Add(actions);
            }
            else
            {
                Grid.SetColumn(actions, 2);
                _listPanel!.Children.Add(actions);
            }
        }

        public bool IsActionOverlayVisibleForSmoke()
        {
            EnsureActionOverlay();
            UpdateVisualState();
            return _actionOverlay!.IsVisible;
        }

        public List<ViewerTileActionGeometry>
            GetActionGeometryForSmoke()
        {
            EnsureActionOverlay();
            _actionOverlay!.IsVisible = true;
            UpdateLayout();

            var result =
                new List<ViewerTileActionGeometry>();
            foreach (var button in _actionOverlay
                         .GetVisualDescendants()
                         .OfType<Button>())
            {
                var buttonOrigin =
                    button.TranslatePoint(
                        new Point(0, 0),
                        this)
                    ?? throw new InvalidOperationException(
                        "Unable to translate overlay button bounds.");
                var buttonRect =
                    new Rect(
                        buttonOrigin,
                        button.Bounds.Size);

                var icon =
                    button.GetVisualDescendants()
                        .OfType<Avalonia.Controls.Shapes.Path>()
                        .FirstOrDefault();
                if (icon is null)
                {
                    continue;
                }

                var iconOrigin =
                    icon.TranslatePoint(
                        new Point(0, 0),
                        this)
                    ?? throw new InvalidOperationException(
                        "Unable to translate overlay icon bounds.");
                result.Add(
                    new ViewerTileActionGeometry(
                        buttonRect,
                        new Rect(
                            iconOrigin,
                            icon.Bounds.Size)));
            }

            return result;
        }

        public ViewerTilePresentation GetPresentationForSmoke()
        {
            var actions =
                GetActionGeometryForSmoke();

            return _layoutMode == ViewerLayoutMode.List
                ? new ViewerTilePresentation(
                    _label?.Text ?? string.Empty,
                    _listSecondary?.Text ?? string.Empty,
                    string.Empty,
                    actions.Count)
                : new ViewerTilePresentation(
                    _captionOverlay?.NameText ?? string.Empty,
                    _captionOverlay?.SizeText ?? string.Empty,
                    _captionOverlay?.OrganizationText ?? string.Empty,
                    actions.Count);
        }

        private void EnsureSelectionBadge()
        {
            if (_selectionBadge is not null)
            {
                return;
            }

            var badge =
                new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = SelectedBorder,
                    HorizontalAlignment =
                        HorizontalAlignment.Left,
                    VerticalAlignment =
                        VerticalAlignment.Top,
                    Margin = new Thickness(8),
                    Child =
                        new Avalonia.Controls.Shapes.Path
                        {
                            Data =
                                Geometry.Parse(
                                    "M5 12.5l4 4L19 6.5"),
                            Stroke =
                                ViewerVisualTokens.Stage,
                            StrokeThickness = 2.2,
                            Stretch = Stretch.Uniform,
                            Width = 11,
                            Height = 11,
                            HorizontalAlignment =
                                HorizontalAlignment.Center,
                            VerticalAlignment =
                                VerticalAlignment.Center
                        }
                };

            _selectionBadge = badge;
            _gridLayers!.Children.Add(badge);
        }

        private void StartLoad()
        {
            CancelLoad();
            ClearFailure();
            _isLoading = true;
            _loadStartedAt = Stopwatch.GetTimestamp();
            _owner._tileLoadsStarted++;
            _loadCancellation =
                new CancellationTokenSource();
            var token =
                _loadCancellation.Token;

            if (_owner.TryAcquireWarmPresentation(
                    _index,
                    out var warmAsset,
                    out var warmLease))
            {
                _asset = warmAsset;
                ReplaceBitmapLease(
                    warmLease);
                ApplyPresentation(
                    warmAsset);
                MarkReady();
                _owner.RecordTileReady(
                    _loadStartedAt,
                    warm: true);
                _isLoading = false;

                var refresh =
                    RefreshWarmPresentationAsync(
                        token);
                _owner.TrackTileLoad(
                    refresh);
                return;
            }

            var load =
                LoadAsync(token);
            _owner.TrackTileLoad(load);
        }

        private void CancelLoad()
        {
            if (_isLoading && !_isReady)
            {
                _owner._tileLoadsCancelled++;
            }

            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;
            _isLoading = false;

            if (_isReady)
            {
                _isReady = false;
                _session.NotifyTileNotReady();
            }

            ReplaceBitmapLease(null);
        }

        private void EnsureFailureOverlay()
        {
            if (_failureOverlay is not null)
            {
                return;
            }

            var message =
                new TextBlock
                {
                    Text = "読み込めません",
                    Foreground =
                        ViewerVisualTokens.Foreground,
                    FontSize =
                        ViewerVisualTokens.CaptionFontSize,
                    FontWeight =
                        FontWeight.SemiBold,
                    TextAlignment =
                        TextAlignment.Center,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            var retry =
                new Button
                {
                    Content = "再試行",
                    MinWidth = 64,
                    MinHeight = 28,
                    Padding =
                        new Thickness(10, 4),
                    CornerRadius =
                        new CornerRadius(7),
                    Background =
                        ViewerVisualTokens.Surface,
                    Foreground =
                        ViewerVisualTokens.Foreground,
                    BorderBrush =
                        ViewerVisualTokens.BorderStrong,
                    BorderThickness =
                        new Thickness(1)
                };
            retry.Click +=
                (_, _) => RetryLoad();

            var panel =
                new StackPanel
                {
                    Orientation =
                        _layoutMode
                            == ViewerLayoutMode.List
                            ? Orientation.Horizontal
                            : Orientation.Vertical,
                    Spacing = 8,
                    HorizontalAlignment =
                        HorizontalAlignment.Center,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };
            panel.Children.Add(message);
            panel.Children.Add(retry);

            var overlay =
                new Border
                {
                    Background =
                        ViewerVisualTokens.Overlay,
                    Padding =
                        new Thickness(8),
                    Child = panel,
                    IsVisible = false,
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch,
                    VerticalAlignment =
                        VerticalAlignment.Stretch
                };

            _retryButton = retry;
            _failureOverlay = overlay;

            if (_layoutMode == ViewerLayoutMode.Grid)
            {
                _gridLayers!.Children.Add(overlay);
            }
            else
            {
                Grid.SetColumn(overlay, 1);
                _listPanel!.Children.Add(overlay);
            }
        }

        private void RetryLoad()
        {
            if (_isLoading)
            {
                return;
            }

            StartLoad();
        }

        private void ClearFailure()
        {
            _failureReason = null;
            ToolTip.SetTip(this, null);

            if (_failureOverlay is not null)
            {
                _failureOverlay.IsVisible = false;
            }

            if (_retryButton is not null)
            {
                _retryButton.IsEnabled = false;
            }
        }

        private void ShowFailure(
            Exception exception)
        {
            _isLoading = false;
            _failureReason =
                GetSafeFailureReason(exception);
            EnsureFailureOverlay();

            _failureOverlay!.IsVisible = true;
            _retryButton!.IsEnabled = true;
            ToolTip.SetTip(
                this,
                _failureReason);
            ToolTip.SetTip(
                _retryButton,
                $"{_failureReason} 再試行します。");

            if (_label is not null)
            {
                _label.Text =
                    _asset?.DisplayName
                    ?? "読み込めません";
                _listSecondary!.Text =
                    "読み込めません";
            }
            else
            {
                _captionOverlay!.SetText(
                    _asset?.DisplayName
                    ?? "読み込めません",
                    "読み込めません",
                    rating:
                        _asset?.Rating,
                    favorite:
                        _asset?.Favorite
                        ?? false);
            }
        }

        private static string GetSafeFailureReason(
            Exception exception) =>
            exception switch
            {
                FileNotFoundException
                    or DirectoryNotFoundException =>
                    "元画像が見つかりません。",
                UnauthorizedAccessException =>
                    "画像ファイルを読み取る権限がありません。",
                NotSupportedException =>
                    "この画像形式はサムネイル表示に対応していません。",
                InvalidDataException =>
                    "画像データをデコードできませんでした。",
                IOException =>
                    "画像ファイルを読み込めませんでした。",
                _ =>
                    "サムネイルの読み込みに失敗しました。"
            };

        internal void RetryForSmoke() =>
            RetryLoad();

        private void ReplaceBitmapLease(
            DecodedBitmapLease? next)
        {
            var previous = _bitmapLease;
            _bitmapLease = next;
            _image.Source = next?.Bitmap;

            if (previous is not null)
            {
                _owner.ReleaseBitmapLeaseAfterComposition(
                    previous);
            }
        }

        private void ApplyPresentation(
            ViewerAsset asset)
        {
            if (_label is not null)
            {
                _label.Text =
                    asset.DisplayName;
                _listSecondary!.Text =
                    FormatListSecondary(asset);
                return;
            }

            _captionOverlay!.SetText(
                asset.DisplayName,
                FormatFileSize(asset.FileSize),
                asset.Rating,
                asset.Favorite);
        }

        private void MarkReady()
        {
            if (_isReady)
            {
                return;
            }

            _isReady = true;
            _session.NotifyTileReady();
        }

        private async Task RefreshWarmPresentationAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                var asset =
                    await _session.GetAssetAsync(
                        _index,
                        cancellationToken)
                        .ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(
                    () =>
                    {
                        cancellationToken
                            .ThrowIfCancellationRequested();
                        _asset = asset;
                        _owner.UpdateWarmPresentationAsset(
                            _index,
                            asset);
                        ApplyPresentation(asset);
                    });
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _session.NotifyTileLoadFailed(
                    exception);
            }
        }

        private async Task LoadAsync(
            CancellationToken cancellationToken)
        {
            DecodedBitmapLease? lease = null;

            try
            {
                var asset =
                    await _session.GetAssetAsync(
                        _index,
                        cancellationToken)
                        .ConfigureAwait(false);
                var afterMetadata = Stopwatch.GetTimestamp();
                var thumbnail =
                    await _session.GetThumbnailAsync(
                        asset,
                        ViewerThumbnailPriority.Foreground,
                        cancellationToken)
                        .ConfigureAwait(false);
                var afterSource = Stopwatch.GetTimestamp();
                lease =
                    await _session.BitmapCache
                        .AcquireAsync(
                            thumbnail,
                            cancellationToken)
                        .ConfigureAwait(false);
                var afterBitmap = Stopwatch.GetTimestamp();

                await Dispatcher.UIThread.InvokeAsync(
                    () =>
                    {
                        cancellationToken
                            .ThrowIfCancellationRequested();

                        var next = lease;
                        lease = null;
                        _asset = asset;
                        ClearFailure();
                        _owner.RememberWarmPresentation(
                            _index,
                            asset,
                            thumbnail);
                        ReplaceBitmapLease(next);
                        ApplyPresentation(asset);
                        MarkReady();
                        _owner.RecordTileReady(
                            _loadStartedAt,
                            warm: false,
                            metadataMilliseconds: Stopwatch.GetElapsedTime(
                                _loadStartedAt,
                                afterMetadata).TotalMilliseconds,
                            thumbnailMilliseconds: Stopwatch.GetElapsedTime(
                                afterMetadata,
                                afterSource).TotalMilliseconds,
                            bitmapMilliseconds: Stopwatch.GetElapsedTime(
                                afterSource,
                                afterBitmap).TotalMilliseconds);
                        _isLoading = false;
                    });
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _session.NotifyTileLoadFailed(exception);
                await Dispatcher.UIThread.InvokeAsync(
                    () => ShowFailure(exception));
            }
            finally
            {
                lease?.Dispose();
            }
        }
    }
    internal static bool ShouldStartBackgroundPrefetchForSmoke(
        int attachedTiles,
        int readyTiles) =>
        ShouldStartBackgroundPrefetch(
            attachedTiles,
            readyTiles);

    private static bool ShouldStartBackgroundPrefetch(
        int attachedTiles,
        int readyTiles) =>
        attachedTiles > 0
        && readyTiles >= attachedTiles;

    internal static bool ResolveTileActionVisibilityForSmoke(
        ViewerLayoutMode layoutMode,
        bool selected,
        bool hovered,
        bool focused) =>
        ShouldShowTileActions(
            layoutMode,
            selected,
            hovered,
            focused);

    private static bool ShouldShowTileActions(
        ViewerLayoutMode layoutMode,
        bool selected,
        bool hovered,
        bool focused) =>
        // Selection must remain visible without permanently covering the
        // thumbnail with commands. Secondary actions are contextual: reveal
        // them on pointer hover or keyboard focus, and keep right-click as
        // the complete accelerator surface in both Grid and List modes.
        hovered
        || focused;

    internal static string ResolveTileVisualStateForSmoke(
        bool selected,
        bool hovered,
        bool pressed,
        bool focused) =>
        ResolveTileVisualState(
            selected,
            hovered,
            pressed,
            focused).ToString();

    private static ViewerTileVisualState ResolveTileVisualState(
        bool selected,
        bool hovered,
        bool pressed,
        bool focused) =>
        (selected, hovered, pressed, focused) switch
        {
            (true, true, _, _) =>
                ViewerTileVisualState.SelectedHover,
            (true, false, _, _) =>
                ViewerTileVisualState.Selected,
            (false, _, true, _) =>
                ViewerTileVisualState.Pressed,
            (false, true, false, _) =>
                ViewerTileVisualState.Hover,
            (false, false, false, true) =>
                ViewerTileVisualState.Focus,
            _ =>
                ViewerTileVisualState.Neutral
        };

    private enum ViewerTileVisualState
    {
        Neutral = 0,
        Hover = 1,
        Pressed = 2,
        Selected = 3,
        SelectedHover = 4,
        Focus = 5
    }

    private sealed record WarmPresentation(
        ViewerAsset Asset,
        ViewerThumbnail Thumbnail,
        LinkedListNode<long> Node);

    internal readonly record struct ViewerTilePresentation(
        string Primary,
        string Secondary,
        string Organization,
        int ActionCount);

    internal readonly record struct ViewerTileActionGeometry(
        Rect ButtonBounds,
        Rect IconBounds);

    public sealed class ViewerAssetContextRequestedEventArgs(
        long index,
        Control anchor)
        : EventArgs
    {
        public long Index { get; } = index;

        public Control Anchor { get; } =
            anchor
            ?? throw new ArgumentNullException(nameof(anchor));
    }

    private sealed class TileCaptionOverlay : Control
    {
        private static readonly IBrush CaptionGradient =
            ViewerVisualTokens.CaptionGradient;
        private static readonly IBrush MutedText =
            ViewerVisualTokens.MutedForeground;
        private static readonly IBrush BadgeBackground =
            ViewerVisualTokens.OverlaySoft;

        private readonly double _maxTextWidth;
        private FormattedText? _name;
        private FormattedText? _size;
        private FormattedText? _organization;

        public string NameText { get; private set; } =
            string.Empty;
        public string SizeText { get; private set; } =
            string.Empty;
        public string OrganizationText { get; private set; } =
            string.Empty;

        public TileCaptionOverlay(double tileWidth)
        {
            _maxTextWidth =
                Math.Max(1, tileWidth - 16);
            IsHitTestVisible = false;
        }

        public void SetText(
            string name,
            string size,
            int? rating,
            bool favorite)
        {
            NameText = name;
            SizeText = size;
            OrganizationText =
                FormatOrganizationCue(
                    rating,
                    favorite);

            _name =
                CreateText(
                    name,
                    ViewerVisualTokens.CaptionFontSize,
                    ViewerVisualTokens.Foreground,
                    FontWeight.Medium);
            _size =
                string.IsNullOrEmpty(size)
                    ? null
                    : CreateText(
                        size,
                        ViewerVisualTokens.CaptionFontSize,
                        MutedText,
                        FontWeight.Normal);

            _organization =
                string.IsNullOrEmpty(OrganizationText)
                    ? null
                    : CreateText(
                        OrganizationText,
                        ViewerVisualTokens.CaptionFontSize,
                        ViewerVisualTokens.Foreground,
                        FontWeight.SemiBold);
            InvalidateVisual();
        }

        private FormattedText CreateText(
            string text,
            double fontSize,
            IBrush foreground,
            FontWeight weight)
        {
            var formatted =
                new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    fontSize,
                    foreground)
                {
                    MaxTextWidth = _maxTextWidth,
                    MaxLineCount = 1,
                    Trimming = TextTrimming.CharacterEllipsis
                };
            formatted.SetFontWeight(weight);
            return formatted;
        }

        public override void Render(
            DrawingContext context)
        {
            base.Render(context);

            var height =
                Math.Min(74, Bounds.Height);
            var top =
                Math.Max(0, Bounds.Height - height);
            context.DrawRectangle(
                CaptionGradient,
                null,
                new Rect(
                    0,
                    top,
                    Bounds.Width,
                    height));

            if (_organization is not null)
            {
                var badgeWidth =
                    Math.Min(
                        Bounds.Width - 16,
                        _organization.Width + 12);
                context.DrawRectangle(
                    BadgeBackground,
                    null,
                    new Rect(
                        8,
                        8,
                        badgeWidth,
                        _organization.Height + 8));
                context.DrawText(
                    _organization,
                    new Point(14, 12));
            }

            if (_name is null)
            {
                return;
            }

            const double bottom = 8;
            const double lineGap = 1;
            var sizeHeight =
                _size?.Height
                ?? 0;
            var nameY =
                Math.Max(
                    top,
                    Bounds.Height
                    - bottom
                    - sizeHeight
                    - (sizeHeight > 0 ? lineGap : 0)
                    - _name.Height);
            context.DrawText(
                _name,
                new Point(8, nameY));

            if (_size is not null)
            {
                var sizeY =
                    Math.Max(
                        top,
                        Bounds.Height
                        - bottom
                        - _size.Height);
                context.DrawText(
                    _size,
                    new Point(8, sizeY));
            }
        }
    }

    private static string FormatListSecondary(
        ViewerAsset asset)
    {
        var slash =
            asset.RelativePath.LastIndexOf('/');
        var folder =
            slash > 0
                ? asset.RelativePath[..slash]
                : "ルート";
        var organization =
            FormatOrganizationCue(
                asset.Rating,
                asset.Favorite);

        return string.IsNullOrEmpty(organization)
            ? $"{folder}  ·  {FormatFileSize(asset.FileSize)}"
            : $"{folder}  ·  {FormatFileSize(asset.FileSize)}  ·  {organization}";
    }

    private static string FormatOrganizationCue(
        int? rating,
        bool favorite)
    {
        var parts = new List<string>(2);
        if (favorite)
        {
            parts.Add("♥");
        }

        if (rating is > 0)
        {
            parts.Add(
                new string(
                    '★',
                    Math.Clamp(
                        rating.Value,
                        1,
                        5)));
        }

        return string.Join(" ", parts);
    }

    private static string FormatFileSize(long bytes)
    {
        const double kib = 1024;
        const double mib = kib * 1024;
        const double gib = mib * 1024;

        return bytes switch
        {
            >= (long)gib =>
                $"{bytes / gib:F1} GB",
            >= (long)mib =>
                $"{bytes / mib:F1} MB",
            >= (long)kib =>
                $"{bytes / kib:F0} KB",
            _ =>
                $"{bytes} B"
        };
    }

}
