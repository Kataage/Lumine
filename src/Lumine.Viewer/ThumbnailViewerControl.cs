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
    private Compositor? _compositor;
    private int _columns = 1;
    private long _selectedIndex = -1;

    public ThumbnailViewerControl(ViewerSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        Focusable = true;
        ClipToBounds = true;

        _rows = new ListBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectionMode = SelectionMode.Single
        };

        Content = _rows;
        KeyDown += OnKeyDown;
        SizeChanged += OnSizeChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;

        RebuildRows();
    }

    public long AssetCount => _session.Count;

    public long SelectedAssetIndex => _selectedIndex;

    public int Columns => _columns;

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

    public void SelectAsset(long index, bool scrollIntoView = true)
    {
        if ((ulong)index >= (ulong)AssetCount)
        {
            return;
        }

        Focus();

        if (_selectedIndex == index)
        {
            return;
        }

        _selectedIndex = index;
        SelectedAssetIndexChanged?.Invoke(this, index);

        if (scrollIntoView)
        {
            var row = checked((int)(index / _columns));
            _rows.ScrollIntoView(row);
        }
    }

    public void ClearSelection()
    {
        if (_selectedIndex < 0)
        {
            return;
        }

        _selectedIndex = -1;
        SelectedAssetIndexChanged?.Invoke(this, -1);
    }

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
            Task[] pending;

            lock (_bitmapReleaseGate)
            {
                if (_bitmapReleaseFailure is not null)
                {
                    throw new InvalidOperationException(
                        "A thumbnail composition release failed.",
                        _bitmapReleaseFailure);
                }

                if (_pendingBitmapReleases.Count == 0)
                {
                    return;
                }

                pending = [.. _pendingBitmapReleases];
            }

            await Task.WhenAll(pending).ConfigureAwait(false);
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
            lease.Dispose();
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
        var cellWidth = _session.Options.TileWidth + _session.Options.TileSpacing;
        var columns = Math.Max(1, (int)Math.Floor(width / cellWidth));

        if (columns == _columns)
        {
            return;
        }

        var anchorAssetIndex = GetViewportAnchorAssetIndex();
        _columns = columns;
        RebuildRows(anchorAssetIndex);
    }

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
        _rows.ItemTemplate = new FuncDataTemplate<long>(
            (rowIndex, _) => new ViewerRowControl(
                this,
                _session,
                rowIndex,
                _columns),
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

        next = Math.Clamp(next, 0, AssetCount - 1);
        if (next != _selectedIndex)
        {
            SelectAsset(next);
            e.Handled = true;
        }
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
            int columns)
        {
            _owner = owner;
            _session = session;
            _rowIndex = rowIndex;
            _columns = columns;

            Orientation = Orientation.Horizontal;
            Spacing = session.Options.TileSpacing;
            Height = session.Options.TileHeight;

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
                    index));
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
        private readonly Image _image;
        private readonly TextBlock _label;
        private CancellationTokenSource? _loadCancellation;
        private DecodedBitmapLease? _bitmapLease;
        private bool _isReady;

        public ViewerTileControl(
            ThumbnailViewerControl owner,
            ViewerSession session,
            long index)
        {
            _owner = owner;
            _session = session;
            _index = index;

            Width = session.Options.TileWidth;
            Height = session.Options.TileHeight;
            Padding = new Thickness(4);
            BorderThickness = new Thickness(2);
            BorderBrush = Brushes.Transparent;
            CornerRadius = new CornerRadius(4);

            _image = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            _label = new TextBlock
            {
                Text = " ",
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var panel = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto")
            };
            panel.Children.Add(_image);
            Grid.SetRow(_label, 1);
            panel.Children.Add(_label);
            Child = panel;

            PointerPressed += OnPointerPressed;
            AttachedToVisualTree += OnAttached;
            DetachedFromVisualTree += OnDetached;
        }

        public long Index => _index;

        public bool IsReady => _isReady;

        public bool IsSelected { get; private set; }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _owner.SelectedAssetIndexChanged += OnSelectedAssetChanged;
            UpdateSelection(_owner.SelectedAssetIndex);
            _session.NotifyTileAttached();
            StartLoad();
        }

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _owner.SelectedAssetIndexChanged -= OnSelectedAssetChanged;
            _session.NotifyTileDetached();
            CancelLoad();
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _owner.SelectAsset(_index, scrollIntoView: false);
            e.Handled = true;
        }

        private void OnSelectedAssetChanged(object? sender, long index) =>
            UpdateSelection(index);

        private void UpdateSelection(long selectedIndex)
        {
            IsSelected = selectedIndex == _index;
            BorderBrush = IsSelected
                ? Brushes.DodgerBlue
                : Brushes.Transparent;
        }

        private void StartLoad()
        {
            CancelLoad();
            _loadCancellation = new CancellationTokenSource();
            _ = LoadAsync(_loadCancellation.Token);
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
                    thumbnail.CachePath,
                    cancellationToken).ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var next = lease;
                    lease = null;
                    ReplaceBitmapLease(next);
                    _label.Text = asset.DisplayName;

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
                await Dispatcher.UIThread.InvokeAsync(() => _label.Text = "!");
            }
            finally
            {
                lease?.Dispose();
            }
        }
    }
}
