using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lumine.Viewer;

public sealed class ThumbnailViewerControl : UserControl
{
    private readonly ViewerSession _session;
    private readonly ListBox _rows;
    private readonly object _bitmapReleaseGate = new();
    private readonly HashSet<Task> _pendingBitmapReleases = [];
    private readonly List<DecodedBitmapLease> _unfencedBitmapLeases = [];
    private Exception? _bitmapReleaseFailure;

    // Tile decode tasks extend beyond ViewerSession.GetThumbnailAsync:
    // they also acquire/decode Avalonia bitmaps. Dynamic Grid/List rebuilds
    // can detach a tile while that final stage is still completing, so shell
    // shutdown must drain these tasks as well as composition-fenced leases.
    private readonly object _tileLoadGate = new();
    private readonly HashSet<Task> _pendingTileLoads = [];
    private Compositor? _compositor;
    private int _columns = 1;
    private readonly ViewerRangeSelection _selection = new();
    private long _selectedIndex = -1;
    private long _selectionAnchor = -1;
    private ViewerLayoutMode _layoutMode;
    private int _densityLevel;
    private double _viewportWidth = 1;

    private const double GridOuterPadding = 12;
    private const double GridCornerRadius = 10;

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

        RebuildRows();
    }

    public long AssetCount => _session.Count;

    public long SelectedAssetIndex => _selectedIndex;

    public int SelectedAssetCount => _selection.Count;

    public IReadOnlyList<long> SelectedAssetIndices =>
        _selection.AsReadOnlyList();

    public int Columns => _columns;

    public ViewerLayoutMode LayoutMode =>
        _layoutMode;

    public int DensityLevel =>
        _densityLevel;

    public void PrepareForDetach()
    {
        // Terminal shell teardown must detach realized rows synchronously.
        // Relying only on visual-tree event delivery leaves a timing window
        // where tile decode/file work can outlive the owning window.
        _rows.ItemsSource = null;
        ClearSelection();
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

    public event EventHandler<long>? SelectedAssetIndexChanged;

    public event EventHandler<ViewerSelectionSnapshot>? SelectionChanged;

    public event EventHandler<long>? AssetInvoked;

    public event EventHandler<long>? AssetDetailRequested;

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
        if (e.Key == Key.Escape)
        {
            ClearSelection();
            e.Handled = true;
            return;
        }

        if (_selectedIndex >= 0
            && e.Key is Key.Enter or Key.Space)
        {
            AssetInvoked?.Invoke(
                this,
                _selectedIndex);
            e.Handled = true;
            return;
        }

        if (_selectedIndex >= 0
            && e.Key == Key.I)
        {
            AssetDetailRequested?.Invoke(
                this,
                _selectedIndex);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A
            && e.KeyModifiers.HasFlag(
                KeyModifiers.Control))
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1001:Types that own disposable fields should be disposable",
        Justification = "The row cancellation source is cancelled and disposed on visual detach.")]
    private sealed class ViewerRowControl : StackPanel
    {
        private readonly ViewerSession _session;
        private readonly long _rowIndex;
        private readonly int _columns;
        private readonly ThumbnailViewerControl _owner;
        private CancellationTokenSource? _prefetchCancellation;

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
            _session = session;
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

            AttachedToVisualTree += OnAttached;
            DetachedFromVisualTree += OnDetached;
        }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _prefetchCancellation?.Cancel();
            _prefetchCancellation?.Dispose();
            _prefetchCancellation = new CancellationTokenSource();

            if (_session.Options.PrefetchRows > 0)
            {
                _ = PrefetchAfterDelayAsync(_prefetchCancellation.Token);
            }
        }

        private async Task PrefetchAfterDelayAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_session.Options.PrefetchDelay > TimeSpan.Zero)
                {
                    await Task.Delay(
                        _session.Options.PrefetchDelay,
                        cancellationToken).ConfigureAwait(false);
                }

                var rows = _session.Options.PrefetchRows;

                var beforeStartRow = Math.Max(0, _rowIndex - rows);
                var beforeRowCount = _rowIndex - beforeStartRow;
                if (beforeRowCount > 0)
                {
                    await _session.PrefetchAsync(
                        checked(beforeStartRow * _columns),
                        checked((int)(beforeRowCount * _columns)),
                        cancellationToken).ConfigureAwait(false);
                }

                var afterStartRow = _rowIndex + 1;
                var afterStartIndex = checked(afterStartRow * _columns);
                if (afterStartIndex < _session.Count)
                {
                    var afterCount = checked((int)Math.Min(
                        _session.Count - afterStartIndex,
                        (long)rows * _columns));

                    await _session.PrefetchAsync(
                        afterStartIndex,
                        afterCount,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _prefetchCancellation?.Cancel();
            _prefetchCancellation?.Dispose();
            _prefetchCancellation = null;
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
        private CancellationTokenSource? _loadCancellation;
        private DecodedBitmapLease? _bitmapLease;
        private bool _isReady;
        private bool _hovered;

        private static readonly IBrush TileBackground =
            ViewerVisualTokens.Surface;
        private static readonly IBrush SelectedBorder =
            ViewerVisualTokens.Selection;
        private static readonly IBrush HoverBorder =
            ViewerVisualTokens.BorderStrong;
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
            PointerEntered += OnPointerEntered;
            PointerExited += OnPointerExited;
            AttachedToVisualTree += OnAttached;
            DetachedFromVisualTree += OnDetached;
        }

        private const string InfoOverlayIconPath =
            "M12 21a9 9 0 100-18 9 9 0 000 18z M12 10.5v6 M12 7.5h.01";
        private const string ExpandOverlayIconPath =
            "M8.25 3.75h-4.5v4.5 M15.75 3.75h4.5v4.5 M8.25 20.25h-4.5v-4.5 M15.75 20.25h4.5v-4.5";

        private static Button CreateOverlayButton(
            string pathData,
            string tooltip)
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
            ToolTip.SetTip(button, tooltip);
            ViewerVisualTokens.Name(
                button,
                tooltip);
            return button;
        }

        public long Index => _index;

        public bool IsReady => _isReady;

        public bool IsSelected { get; private set; }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _owner.SelectionChanged += OnSelectionChanged;
            UpdateSelection();
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
            UpdateVisualState();
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
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
            BorderBrush =
                IsSelected
                    ? SelectedBorder
                    : _hovered
                        ? HoverBorder
                        : Brushes.Transparent;

            var showActions =
                _hovered;
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
                    "詳細 (I)");
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
                    "大きく表示");
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
            _loadCancellation = new CancellationTokenSource();
            var load =
                LoadAsync(_loadCancellation.Token);
            _owner.TrackTileLoad(load);
        }

        private void CancelLoad()
        {
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;

            if (_isReady)
            {
                _isReady = false;
                _session.NotifyTileNotReady();
            }

            ReplaceBitmapLease(null);
        }

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

        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            DecodedBitmapLease? lease = null;

            try
            {
                var asset = await _session.GetAssetAsync(
                    _index,
                    cancellationToken).ConfigureAwait(false);
                var thumbnail = await _session.GetThumbnailAsync(
                    asset,
                    ViewerThumbnailPriority.Foreground,
                    cancellationToken).ConfigureAwait(false);
                lease = await _session.BitmapCache.AcquireAsync(
                    thumbnail,
                    cancellationToken).ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var next = lease;
                    lease = null;
                    ReplaceBitmapLease(next);
                    if (_label is not null)
                    {
                        _label.Text = asset.DisplayName;
                        _listSecondary!.Text =
                            FormatListSecondary(asset);
                    }
                    else
                    {
                        _captionOverlay!.SetText(
                            asset.DisplayName,
                            FormatFileSize(asset.FileSize),
                            asset.Rating,
                            asset.Favorite);
                    }

                    if (!_isReady)
                    {
                        _isReady = true;
                        _session.NotifyTileReady();
                    }
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _session.NotifyTileLoadFailed(exception);
                await Dispatcher.UIThread.InvokeAsync(
                    () =>
                    {
                        if (_label is not null)
                        {
                            _label.Text = "!";
                        }
                        else
                        {
                            _captionOverlay!.SetText(
                                "!",
                                string.Empty,
                                rating: null,
                                favorite: false);
                        }
                    });
            }
            finally
            {
                lease?.Dispose();
            }
        }
    }
    private sealed class TileCaptionOverlay : Control
    {
        private static readonly IBrush CaptionGradient =
            new LinearGradientBrush
            {
                StartPoint =
                    new RelativePoint(
                        0,
                        0,
                        RelativeUnit.Relative),
                EndPoint =
                    new RelativePoint(
                        0,
                        1,
                        RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(
                        Color.FromArgb(
                            0,
                            0,
                            0,
                            0),
                        0),
                    new GradientStop(
                        Color.FromArgb(
                            70,
                            0,
                            0,
                            0),
                        0.35),
                    new GradientStop(
                        Color.FromArgb(
                            220,
                            0,
                            0,
                            0),
                        1)
                ]
            };

        private static readonly IBrush MutedText =
            ViewerVisualTokens.MutedForeground;
        private static readonly IBrush BadgeBackground =
            ViewerVisualTokens.OverlaySoft;

        private readonly double _maxTextWidth;
        private FormattedText? _name;
        private FormattedText? _size;
        private FormattedText? _organization;

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

            var organization =
                FormatOrganizationCue(
                    rating,
                    favorite);
            _organization =
                string.IsNullOrEmpty(organization)
                    ? null
                    : CreateText(
                        organization,
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
