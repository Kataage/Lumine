using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lumine.Viewer;

public sealed class DetailViewerControl : UserControl
{
    private readonly ViewerDetailSession _session;
    private readonly ScrollViewer _scroll;
    private readonly Image _image;
    private readonly TextBlock _status;
    private readonly TextBlock _metadata;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Button _fit;
    private readonly Button _actual;
    private double _zoom = 1;
    private bool _fitMode = true;
    private bool _dragging;
    private Point _dragStart;
    private Vector _dragStartOffset;
    private ThumbnailViewerControl? _grid;
    private bool _syncingSelection;
    private bool _sessionEventsAttached;
    private bool _gridEventsAttached;
    private bool _visualAttached;
    private TopLevel? _topLevel;
    private readonly object _zoomGate = new();
    private double _requestedZoom = 1;
    private PixelSize _requestedZoomBasis;
    private PixelSize _displayedZoomBasis;
    private long _zoomCommandVersion;
    private long _pendingZoomCommandVersion;
    private long _observedSelectionVersion;

    public DetailViewerControl(ViewerDetailSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _observedSelectionVersion = _session.Snapshot.SelectionVersion;
        Focusable = true;

        _previous = new Button { Content = "◀" };
        _next = new Button { Content = "▶" };
        _fit = new Button { Content = "Fit" };
        _actual = new Button { Content = "1:1" };

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        toolbar.Children.Add(_previous);
        toolbar.Children.Add(_next);
        toolbar.Children.Add(_fit);
        toolbar.Children.Add(_actual);

        _status = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        toolbar.Children.Add(_status);

        _image = new Image
        {
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        var surface = new Border
        {
            Background = Brushes.Black,
            Child = _image
        };

        _scroll = new ScrollViewer
        {
            Content = surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        _metadata = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8
        };
        layout.Children.Add(toolbar);
        Grid.SetRow(_scroll, 1);
        layout.Children.Add(_scroll);
        Grid.SetRow(_metadata, 2);
        layout.Children.Add(_metadata);

        Content = layout;

        _previous.Click += async (_, _) => await MoveAsync(-1);
        _next.Click += async (_, _) => await MoveAsync(1);
        _fit.Click += (_, _) => Fit();
        _actual.Click += async (_, _) => await ActualSizeAsync();

        _scroll.SizeChanged += (_, _) =>
        {
            if (_fitMode && !HasPendingZoomCommand())
            {
                ApplyFit();
            }
        };

        _scroll.PointerWheelChanged += OnPointerWheelChanged;
        surface.PointerPressed += OnPointerPressed;
        surface.PointerMoved += OnPointerMoved;
        surface.PointerReleased += OnPointerReleased;
        KeyDown += OnKeyDown;

        AttachSessionEvents();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

        ApplySnapshot(_session.Snapshot);
    }

    public double Zoom => _zoom;

    public long SelectedAssetIndex => _session.Snapshot.SelectedIndex;

    public ViewerDetailLoadState LoadState => _session.Snapshot.State;

    public bool IsOriginal => _session.Snapshot.IsOriginal;

    public Vector PanOffset => _scroll.Offset;

    public string MetadataText => _metadata.Text ?? string.Empty;

    public event EventHandler<long>? SelectedAssetIndexChanged;

    public Task SelectAsync(
        long index,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var before = _session.Snapshot;
        var shouldReset =
            (ulong)index < (ulong)_session.Count
            && (before.SelectedIndex != index
                || before.State is ViewerDetailLoadState.Empty
                    or ViewerDetailLoadState.Error);

        var task = _session.SelectAsync(index, cancellationToken);

        if (shouldReset)
        {
            var current = _session.Snapshot;
            if (current.SelectionVersion != before.SelectionVersion
                && current.SelectedIndex == index)
            {
                PrepareForSelectionChange();
                _observedSelectionVersion =
                    current.SelectionVersion;
            }
        }

        return task;
    }

    public void BindGrid(ThumbnailViewerControl grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        UnbindGrid();

        _grid = grid;

        if (_visualAttached)
        {
            AttachGridEvents();
            SynchronizeFromGrid();
        }
    }

    public void UnbindGrid()
    {
        DetachGridEvents();
        _grid = null;
    }

    private void AttachGridEvents()
    {
        if (_grid is null || _gridEventsAttached)
        {
            return;
        }

        _grid.SelectedAssetIndexChanged += OnGridSelectionChanged;
        _gridEventsAttached = true;
    }

    private void DetachGridEvents()
    {
        if (_grid is null || !_gridEventsAttached)
        {
            return;
        }

        _grid.SelectedAssetIndexChanged -= OnGridSelectionChanged;
        _gridEventsAttached = false;
    }

    private void SynchronizeFromGrid()
    {
        if (_grid is null || _grid.SelectedAssetIndex < 0)
        {
            return;
        }

        var selected = _grid.SelectedAssetIndex;
        var snapshot = _session.Snapshot;
        if (snapshot.SelectedIndex == selected
            && snapshot.State is not ViewerDetailLoadState.Empty
                and not ViewerDetailLoadState.Error)
        {
            return;
        }

        _ = SelectAsync(selected);
    }

    public void Fit()
    {
        CancelPendingZoomCommands();
        ApplyFit();
    }

    private void ApplyFit()
    {
        var snapshot = _session.Snapshot;
        if (snapshot.Bitmap is null)
        {
            return;
        }

        var viewport = _scroll.Viewport;
        var sourceSize = GetSourcePixelSize(snapshot);
        if (viewport.Width <= 0
            || viewport.Height <= 0
            || sourceSize.Width <= 0
            || sourceSize.Height <= 0)
        {
            return;
        }

        var renderScaling = GetRenderScaling();
        var widthScale =
            viewport.Width * renderScaling / sourceSize.Width;
        var heightScale =
            viewport.Height * renderScaling / sourceSize.Height;
        var zoom = Math.Clamp(
            Math.Min(widthScale, heightScale),
            _session.Options.MinZoom,
            _session.Options.MaxZoom);

        _fitMode = true;
        SetZoom(zoom);
        SynchronizeRequestedZoomIfIdle(zoom);
        _scroll.Offset = default;
    }

    public async Task ActualSizeAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = _session.Snapshot;
        if (snapshot.Asset is null || snapshot.Bitmap is null)
        {
            return;
        }

        var commandVersion = BeginZoomCommand(1);
        var selectionVersion = snapshot.SelectionVersion;

        try
        {
            await _session.EnsureOriginalAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            ResetRequestedZoomIfCurrent(commandVersion);
            throw;
        }

        var current = _session.Snapshot;
        if (!IsCurrentZoomCommand(commandVersion)
            || current.SelectionVersion != selectionVersion
            || !current.IsOriginal)
        {
            ResetRequestedZoomIfCurrent(commandVersion);
            return;
        }

        _fitMode = false;
        SetZoom(1);
        SynchronizeRequestedZoom(1);
    }

    public void PanBy(double horizontal, double vertical)
    {
        _scroll.Offset = new Vector(
            Math.Max(0, _scroll.Offset.X + horizontal),
            Math.Max(0, _scroll.Offset.Y + vertical));
    }

    public async Task SetZoomAsync(
        double zoom,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = _session.Snapshot;
        if (snapshot.Bitmap is null)
        {
            return;
        }

        var clamped = Math.Clamp(
            zoom,
            _session.Options.MinZoom,
            _session.Options.MaxZoom);
        var commandVersion = BeginZoomCommand(clamped);

        await ApplyZoomCommandAsync(
            snapshot,
            clamped,
            commandVersion,
            requestedBasis: default,
            cancellationToken);
    }

    public async Task ZoomByAsync(
        double factor,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(factor, 0);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = _session.Snapshot;
        if (snapshot.Bitmap is null)
        {
            return;
        }

        var (commandVersion, target, requestedBasis) =
            BeginRelativeZoomCommand(factor);

        await ApplyZoomCommandAsync(
            snapshot,
            target,
            commandVersion,
            requestedBasis,
            cancellationToken);
    }

    private async Task ApplyZoomCommandAsync(
        ViewerDetailSnapshot snapshot,
        double target,
        long commandVersion,
        PixelSize requestedBasis,
        CancellationToken cancellationToken)
    {
        var committedTarget = target;

        if (!snapshot.IsOriginal
            && ShouldUseOriginal(snapshot, target))
        {
            var selectionVersion = snapshot.SelectionVersion;

            try
            {
                await _session.EnsureOriginalAsync(cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                ResetRequestedZoomIfCurrent(commandVersion);
                throw;
            }

            var current = _session.Snapshot;

            if (!IsCurrentZoomCommand(commandVersion)
                || current.SelectionVersion != selectionVersion
                || !current.IsOriginal
                || current.Bitmap is null)
            {
                ResetRequestedZoomIfCurrent(commandVersion);
                return;
            }

        }

        if (!IsCurrentZoomCommand(commandVersion))
        {
            return;
        }

        var latest = _session.Snapshot;
        if (latest.SelectionVersion != snapshot.SelectionVersion
            || latest.Bitmap is null)
        {
            ResetRequestedZoomIfCurrent(commandVersion);
            return;
        }

        if (requestedBasis.Width > 0 && requestedBasis.Height > 0)
        {
            committedTarget = Math.Clamp(
                CalculatePromotedZoom(
                    requestedBasis,
                    GetSourcePixelSize(latest),
                    target),
                _session.Options.MinZoom,
                _session.Options.MaxZoom);
        }

        _fitMode = false;
        SetZoom(committedTarget);
        SynchronizeRequestedZoom(committedTarget);
    }

    private async Task MoveAsync(long delta)
    {
        if (_session.Count == 0)
        {
            return;
        }

        var current = _session.Snapshot.SelectedIndex;
        var target = Math.Clamp(
            current < 0 ? 0 : current + delta,
            0,
            _session.Count - 1);

        if (target == current)
        {
            return;
        }

        PrepareForSelectionChange();
        await _session.SelectAsync(target);
    }

    private void SetZoom(double zoom)
    {
        _zoom = zoom;

        var snapshot = _session.Snapshot;
        if (snapshot.Bitmap is null)
        {
            return;
        }

        var sourceSize = GetSourcePixelSize(snapshot);
        var displaySize = CalculateDisplaySize(
            sourceSize,
            zoom,
            GetRenderScaling());

        _displayedZoomBasis = sourceSize;
        _image.Width = displaySize.Width;
        _image.Height = displaySize.Height;
    }

    internal static Size CalculateDisplaySize(
        PixelSize sourceSize,
        double zoom,
        double renderScaling)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(zoom, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(renderScaling, 0);

        return new Size(
            sourceSize.Width * zoom / renderScaling,
            sourceSize.Height * zoom / renderScaling);
    }

    internal static double CalculatePromotedZoom(
        PixelSize previewSize,
        PixelSize originalSize,
        double previewZoom)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(previewZoom, 0);

        if (previewSize.Width <= 0
            || previewSize.Height <= 0
            || originalSize.Width <= 0
            || originalSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(previewSize),
                "Preview and original pixel sizes must be positive.");
        }

        var widthScale =
            previewSize.Width / (double)originalSize.Width;
        var heightScale =
            previewSize.Height / (double)originalSize.Height;

        return previewZoom * Math.Min(widthScale, heightScale);
    }

    internal bool IsFitMode => _fitMode;

    private long BeginZoomCommand(double target)
    {
        lock (_zoomGate)
        {
            _requestedZoom = target;
            // Explicit commands such as 1:1 are source-pixel zoom requests,
            // even while the original dimensions are not known yet.
            _requestedZoomBasis = default;
            var version = ++_zoomCommandVersion;
            _pendingZoomCommandVersion = version;
            return version;
        }
    }

    private (long Version, double Target, PixelSize Basis)
        BeginRelativeZoomCommand(double factor)
    {
        lock (_zoomGate)
        {
            _requestedZoom = Math.Clamp(
                _requestedZoom * factor,
                _session.Options.MinZoom,
                _session.Options.MaxZoom);
            var version = ++_zoomCommandVersion;
            _pendingZoomCommandVersion = version;
            return (
                version,
                _requestedZoom,
                _requestedZoomBasis);
        }
    }

    private bool IsCurrentZoomCommand(long version)
    {
        lock (_zoomGate)
        {
            return _zoomCommandVersion == version;
        }
    }

    private void SynchronizeRequestedZoom(double zoom)
    {
        var basis = GetCurrentZoomBasis();

        lock (_zoomGate)
        {
            _requestedZoom = zoom;
            _requestedZoomBasis = basis;
            _pendingZoomCommandVersion = 0;
        }
    }

    private void SynchronizeRequestedZoomIfIdle(double zoom)
    {
        var basis = GetCurrentZoomBasis();

        lock (_zoomGate)
        {
            if (_pendingZoomCommandVersion != 0)
            {
                return;
            }

            _requestedZoom = zoom;
            _requestedZoomBasis = basis;
        }
    }

    private bool HasPendingZoomCommand()
    {
        lock (_zoomGate)
        {
            return _pendingZoomCommandVersion != 0;
        }
    }

    private void ResetRequestedZoomIfCurrent(long version)
    {
        var basis = GetCurrentZoomBasis();

        lock (_zoomGate)
        {
            if (_zoomCommandVersion == version)
            {
                _requestedZoom = _zoom;
                _requestedZoomBasis = basis;
                _pendingZoomCommandVersion = 0;
            }
        }
    }

    private void CancelPendingZoomCommands()
    {
        var basis = GetCurrentZoomBasis();

        lock (_zoomGate)
        {
            _zoomCommandVersion++;
            _requestedZoom = _zoom;
            _requestedZoomBasis = basis;
            _pendingZoomCommandVersion = 0;
        }
    }

    private PixelSize GetCurrentZoomBasis()
    {
        var snapshot = _session.Snapshot;
        return snapshot.Bitmap is null
            ? default
            : GetSourcePixelSize(snapshot);
    }

    private void ApplyZoomAcrossBasisChange(
        ViewerDetailSnapshot snapshot)
    {
        var targetBasis = GetSourcePixelSize(snapshot);
        var zoom = _zoom;

        if (_displayedZoomBasis.Width > 0
            && _displayedZoomBasis.Height > 0
            && targetBasis.Width > 0
            && targetBasis.Height > 0
            && (_displayedZoomBasis.Width != targetBasis.Width
                || _displayedZoomBasis.Height != targetBasis.Height))
        {
            zoom = Math.Clamp(
                CalculatePromotedZoom(
                    _displayedZoomBasis,
                    targetBasis,
                    _zoom),
                _session.Options.MinZoom,
                _session.Options.MaxZoom);
        }

        SetZoom(zoom);
        SynchronizeRequestedZoomIfIdle(zoom);
    }

    private void PrepareForSelectionChange()
    {
        lock (_zoomGate)
        {
            _zoomCommandVersion++;
            _requestedZoom = 1;
            _requestedZoomBasis = default;
            _displayedZoomBasis = default;
            _pendingZoomCommandVersion = 0;
        }

        _fitMode = true;
        _scroll.Offset = default;
    }

    private static bool ShouldUseOriginal(
        ViewerDetailSnapshot snapshot,
        double zoom)
    {
        if (snapshot.IsOriginal || snapshot.Bitmap is null)
        {
            return false;
        }

        if (!HasKnownSourcePixelSize(snapshot))
        {
            // Without persisted source dimensions, preview pixel coordinates
            // are the only safe scale until the original is probed.
            return zoom >= 1;
        }

        var sourceSize = GetSourcePixelSize(snapshot);
        var previewSize = snapshot.Bitmap.PixelSize;

        return sourceSize.Width * zoom > previewSize.Width
            || sourceSize.Height * zoom > previewSize.Height;
    }

    private static bool HasKnownSourcePixelSize(
        ViewerDetailSnapshot snapshot) =>
        snapshot.Metadata is { Width: > 0, Height: > 0 }
        || ((snapshot.Asset?.Width ?? 0) > 0
            && (snapshot.Asset?.Height ?? 0) > 0);

    private static PixelSize GetSourcePixelSize(
        ViewerDetailSnapshot snapshot)
    {
        if (snapshot.Metadata is { Width: > 0, Height: > 0 } metadata)
        {
            return new PixelSize(metadata.Width, metadata.Height);
        }

        var assetWidth = snapshot.Asset?.Width ?? 0;
        var assetHeight = snapshot.Asset?.Height ?? 0;
        if (assetWidth > 0 && assetHeight > 0)
        {
            return new PixelSize(assetWidth, assetHeight);
        }

        return snapshot.Bitmap?.PixelSize ?? default;
    }

    private double GetRenderScaling()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
        return double.IsFinite(scaling) && scaling > 0
            ? scaling
            : 1d;
    }

    private void AttachSessionEvents()
    {
        if (_sessionEventsAttached)
        {
            return;
        }

        _session.StateChanged += OnStateChanged;
        _session.SelectedIndexChanged += OnSessionSelectionChanged;
        _sessionEventsAttached = true;
    }

    private void DetachSessionEvents()
    {
        if (!_sessionEventsAttached)
        {
            return;
        }

        _session.StateChanged -= OnStateChanged;
        _session.SelectedIndexChanged -= OnSessionSelectionChanged;
        _sessionEventsAttached = false;
    }

    private void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        _visualAttached = true;
        AttachSessionEvents();
        AttachTopLevelScaling();
        AttachGridEvents();
        ApplySnapshot(_session.Snapshot);
        SynchronizeFromGrid();
    }

    private void OnDetachedFromVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        _visualAttached = false;
        _image.Source = null;
        DetachGridEvents();
        DetachSessionEvents();
        DetachTopLevelScaling();
        CancelPendingZoomCommands();

        if (_session.Snapshot.State != ViewerDetailLoadState.Empty)
        {
            _session.Clear();
        }
    }

    private void AttachTopLevelScaling()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (ReferenceEquals(_topLevel, topLevel))
        {
            return;
        }

        DetachTopLevelScaling();
        _topLevel = topLevel;

        if (_topLevel is not null)
        {
            _topLevel.ScalingChanged += OnTopLevelScalingChanged;
        }
    }

    private void DetachTopLevelScaling()
    {
        if (_topLevel is null)
        {
            return;
        }

        _topLevel.ScalingChanged -= OnTopLevelScalingChanged;
        _topLevel = null;
    }

    private void OnTopLevelScalingChanged(object? sender, EventArgs e)
    {
        if (_session.Snapshot.Bitmap is null)
        {
            return;
        }

        if (_fitMode && !HasPendingZoomCommand())
        {
            ApplyFit();
        }
        else
        {
            SetZoom(_zoom);
        }
    }

    private void OnStateChanged(
        object? sender,
        ViewerDetailSnapshot snapshot) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_sessionEventsAttached)
                {
                    ApplySnapshot(_session.Snapshot);
                }
            },
            DispatcherPriority.Render);

    private void ApplySnapshot(ViewerDetailSnapshot snapshot)
    {
        if (snapshot.SelectionVersion != _observedSelectionVersion)
        {
            PrepareForSelectionChange();
            _observedSelectionVersion = snapshot.SelectionVersion;
        }

        _image.Source = snapshot.Bitmap;

        _status.Text = snapshot.State switch
        {
            ViewerDetailLoadState.Empty => "No selection",
            ViewerDetailLoadState.LoadingPreview => "Loading preview…",
            ViewerDetailLoadState.PreviewReady => snapshot.ErrorMessage is null
                ? "Preview"
                : $"Preview · {snapshot.ErrorMessage}",
            ViewerDetailLoadState.LoadingOriginal => "Loading original…",
            ViewerDetailLoadState.OriginalReady => "Original",
            ViewerDetailLoadState.Error => $"Error: {snapshot.ErrorMessage}",
            _ => snapshot.State.ToString()
        };

        var metadata = snapshot.Metadata;
        _metadata.Text = snapshot.Asset is null
            ? string.Empty
            : metadata is not null
                ? $"{snapshot.Asset.DisplayName} · {metadata.Width}×{metadata.Height} · {metadata.Format ?? snapshot.Asset.Format ?? "unknown"} · {metadata.FileSize:N0} bytes"
                : FormatAssetMetadata(snapshot.Asset);

        _previous.IsEnabled = snapshot.SelectedIndex > 0;
        _next.IsEnabled = snapshot.SelectedIndex >= 0
            && snapshot.SelectedIndex + 1 < _session.Count;
        _fit.IsEnabled = snapshot.Bitmap is not null;
        _actual.IsEnabled =
            snapshot.Asset is not null
            && snapshot.Bitmap is not null;

        if (snapshot.Bitmap is not null)
        {
            if (_fitMode && !HasPendingZoomCommand())
            {
                ApplyFit();
            }
            else
            {
                ApplyZoomAcrossBasisChange(snapshot);
            }
        }
    }

    private static string FormatAssetMetadata(ViewerAsset asset)
    {
        var dimensions = asset.Width is > 0 && asset.Height is > 0
            ? $" · {asset.Width}×{asset.Height}"
            : string.Empty;
        var format = string.IsNullOrWhiteSpace(asset.Format)
            ? string.Empty
            : $" · {asset.Format}";

        return $"{asset.DisplayName}{dimensions}{format} · {asset.FileSize:N0} bytes";
    }

    private void OnSessionSelectionChanged(
        object? sender,
        long index) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!_sessionEventsAttached
                    || _session.Snapshot.SelectedIndex != index)
                {
                    return;
                }

                SelectedAssetIndexChanged?.Invoke(this, index);

                if (_grid is null || _syncingSelection)
                {
                    return;
                }

                _syncingSelection = true;
                try
                {
                    _grid.SelectAsset(index);
                }
                finally
                {
                    _syncingSelection = false;
                }
            },
            DispatcherPriority.Render);

    private void OnGridSelectionChanged(object? sender, long index)
    {
        if (_syncingSelection || index < 0)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            _ = SelectAsync(index);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private async void OnPointerWheelChanged(
        object? sender,
        PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == 0)
        {
            return;
        }

        e.Handled = true;
        var factor = e.Delta.Y > 0
            ? _session.Options.ZoomStep
            : 1 / _session.Options.ZoomStep;

        await ZoomByAsync(factor);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_scroll).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        _dragStart = e.GetPosition(_scroll);
        _dragStartOffset = _scroll.Offset;
        e.Pointer.Capture((IInputElement?)sender);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var position = e.GetPosition(_scroll);
        var delta = position - _dragStart;
        _scroll.Offset = new Vector(
            Math.Max(0, _dragStartOffset.X - delta.X),
            Math.Max(0, _dragStartOffset.Y - delta.Y));
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                await MoveAsync(-1);
                e.Handled = true;
                break;
            case Key.Right:
                await MoveAsync(1);
                e.Handled = true;
                break;
            case Key.D0 when (e.KeyModifiers & KeyModifiers.Control) != 0:
            case Key.NumPad0 when (e.KeyModifiers & KeyModifiers.Control) != 0:
                Fit();
                e.Handled = true;
                break;
            case Key.D1 when (e.KeyModifiers & KeyModifiers.Control) != 0:
            case Key.NumPad1 when (e.KeyModifiers & KeyModifiers.Control) != 0:
                await ActualSizeAsync();
                e.Handled = true;
                break;
        }
    }
}
