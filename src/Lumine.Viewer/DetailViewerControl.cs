using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lumine.Viewer;

public sealed class DetailViewerControl : UserControl
{
    private static readonly Cursor PanAvailableCursor =
        new(StandardCursorType.Hand);
    private static readonly Cursor PanningCursor =
        new(StandardCursorType.SizeAll);

    private ViewerDetailSession _session;
    private readonly ScrollViewer _scroll;
    private readonly Image _image;
    private readonly TextBlock _status;
    private readonly TextBlock _metadata;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Button _fit;
    private readonly Button _actual;
    private readonly Button _zoomOut;
    private readonly Button _zoomIn;
    private readonly Button _fullScreen;
    private readonly Button _info;
    private readonly Button _close;
    private readonly Slider _zoomSlider;
    private readonly TextBlock _zoomText;
    private readonly Border _toolbarHost;
    private readonly Border _utilityHost;
    private readonly Border _statusHost;
    private readonly Border _metadataHost;
    private readonly Border _surface;
    private readonly DispatcherTimer _chromeTimer;
    private double _zoom = 1;
    private bool _fitMode = true;
    private bool _dragging;
    private bool _syncingZoomSlider;
    private Point _dragStart;
    private Vector _dragStartOffset;
    private ThumbnailViewerControl? _grid;
    private bool _syncingSelection;
    private bool _sessionEventsAttached;
    private bool _gridEventsAttached;
    private bool _visualAttached;
    private TopLevel? _topLevel;
    private Compositor? _compositor;
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

        _previous =
            CreateViewerButton(
                CreateViewerIcon(
                    "M15.75 5.25L9 12l6.75 6.75",
                    18),
                "前の画像",
                "viewer.previous",
                "Left Arrow");
        _next =
            CreateViewerButton(
                CreateViewerIcon(
                    "M8.25 5.25L15 12l-6.75 6.75",
                    18),
                "次の画像",
                "viewer.next",
                "Right Arrow");
        _zoomOut =
            CreateViewerButton(
                "−",
                "縮小 (-)",
                "viewer.zoom-out",
                "-");
        _zoomIn =
            CreateViewerButton(
                "+",
                "拡大 (+)",
                "viewer.zoom-in",
                "+");
        _zoomOut.FontSize = 20;
        _zoomIn.FontSize = 20;
        _zoomOut.FontWeight =
            FontWeight.SemiBold;
        _zoomIn.FontWeight =
            FontWeight.SemiBold;
        _fit =
            CreateViewerButton(
                "フィット",
                "ウィンドウに合わせる (0)",
                "viewer.fit",
                "0");
        _actual =
            CreateViewerButton(
                "1:1",
                "100%表示",
                "viewer.actual-size",
                "1");
        _fullScreen =
            CreateViewerButton(
                CreateViewerIcon(
                    "M4 9V4h5 M15 4h5v5 M20 15v5h-5 M9 20H4v-5",
                    16),
                "全画面表示 (F11)",
                "viewer.fullscreen",
                "F11");
        _info =
            CreateViewerButton(
                CreateViewerIcon(
                    "M12 21a9 9 0 100-18 9 9 0 000 18z M12 10.5v6 M12 7.5h.01",
                    16),
                "画像情報",
                "viewer.info",
                "I");
        _close =
            CreateViewerButton(
                CreateViewerIcon(
                    "M6 6l12 12 M18 6L6 18",
                    16),
                "閉じる (Esc)",
                "viewer.close",
                "Esc");

        _zoomSlider =
            new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Width = 104,
                MinWidth = 84,
                VerticalAlignment =
                    VerticalAlignment.Center,
                SmallChange = 1,
                LargeChange = 8
            };
        ToolTip.SetTip(
            _zoomSlider,
            "ズーム");
        ViewerVisualTokens.Name(
            _zoomSlider,
            "ズーム",
            "viewer.zoom-slider");

        _zoomText = new TextBlock
        {
            Text = "100%",
            MinWidth = 52,
            Foreground = ViewerVisualTokens.Foreground,
            FontSize = ViewerVisualTokens.CaptionFontSize,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(6, 4),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        toolbar.Children.Add(_zoomOut);
        toolbar.Children.Add(_zoomSlider);
        toolbar.Children.Add(_zoomText);
        toolbar.Children.Add(_zoomIn);
        toolbar.Children.Add(
            new Border
            {
                Width = 1,
                Height = 18,
                Margin = new Thickness(3, 0),
                Background = ViewerVisualTokens.Border,
                VerticalAlignment = VerticalAlignment.Center
            });
        toolbar.Children.Add(_fit);
        toolbar.Children.Add(_actual);

        var utilityBar =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(4)
            };
        utilityBar.Children.Add(_info);
        utilityBar.Children.Add(_fullScreen);
        utilityBar.Children.Add(
            new Border
            {
                Width = 1,
                Height = 18,
                Margin = new Thickness(2, 0),
                Background = ViewerVisualTokens.Border,
                VerticalAlignment = VerticalAlignment.Center
            });
        utilityBar.Children.Add(_close);

        _status = new TextBlock
        {
            Foreground = ViewerVisualTokens.Foreground,
            FontSize = ViewerVisualTokens.CaptionFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            IsVisible = false
        };
        _statusHost =
            new Border
            {
                Background = ViewerVisualTokens.Overlay,
                BorderBrush = ViewerVisualTokens.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 6),
                Margin = new Thickness(12, 12, 12, 16),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsVisible = false,
                IsHitTestVisible = false,
                Child = _status
            };

        _image = new Image
        {
            Stretch = Stretch.Fill,
            // Fit-mode images can be smaller than the ScrollViewer viewport
            // on one axis. Center the image within the stage instead of
            // pinning it to the top-left; when zoomed beyond the viewport the
            // content still expands normally and ScrollViewer offset owns pan.
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _session.SetOriginalReleaseHandler(
            ReleaseOriginalAfterCompositionAsync);

        _surface = new Border
        {
            Background = ViewerVisualTokens.Stage,
            Child = _image,
            Cursor = Cursor.Default
        };

        _scroll = new ScrollViewer
        {
            Content = _surface,
            HorizontalContentAlignment =
                HorizontalAlignment.Center,
            VerticalContentAlignment =
                VerticalAlignment.Center,
            HorizontalScrollBarVisibility =
                ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility =
                ScrollBarVisibility.Hidden
        };

        _metadata = new TextBlock
        {
            Foreground = ViewerVisualTokens.Foreground,
            FontSize = ViewerVisualTokens.CaptionFontSize,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 720
        };
        _metadataHost =
            new Border
            {
                Background =
                    ViewerVisualTokens.Overlay,
                BorderBrush =
                    ViewerVisualTokens.Border,
                BorderThickness =
                    new Thickness(1),
                CornerRadius =
                    new CornerRadius(9),
                Padding =
                    new Thickness(10, 6),
                Margin =
                    new Thickness(12, 12, 12, 16),
                HorizontalAlignment =
                    HorizontalAlignment.Center,
                VerticalAlignment =
                    VerticalAlignment.Bottom,
                IsVisible = false,
                IsHitTestVisible = false,
                Child = _metadata
            };

        var stage = new Grid
        {
            Background = ViewerVisualTokens.Stage
        };
        stage.Children.Add(_scroll);

        _previous.Width = 44;
        _previous.Height = 56;
        _previous.MinWidth = 44;
        _previous.MinHeight = 56;
        _previous.Padding = new Thickness(0);
        _previous.HorizontalAlignment =
            HorizontalAlignment.Left;
        _previous.VerticalAlignment =
            VerticalAlignment.Center;
        _previous.Margin = new Thickness(14, 0);
        stage.Children.Add(_previous);

        _next.Width = 44;
        _next.Height = 56;
        _next.MinWidth = 44;
        _next.MinHeight = 56;
        _next.Padding = new Thickness(0);
        _next.HorizontalAlignment =
            HorizontalAlignment.Right;
        _next.VerticalAlignment =
            VerticalAlignment.Center;
        _next.Margin = new Thickness(14, 0);
        stage.Children.Add(_next);

        _toolbarHost =
            new Border
            {
                Background = ViewerVisualTokens.Overlay,
                BorderBrush = ViewerVisualTokens.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(2),
                Margin = new Thickness(10),
                HorizontalAlignment =
                    HorizontalAlignment.Center,
                VerticalAlignment =
                    VerticalAlignment.Top,
                Child = toolbar
            };
        _utilityHost =
            new Border
            {
                Background = ViewerVisualTokens.Overlay,
                BorderBrush = ViewerVisualTokens.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(2),
                Margin = new Thickness(10),
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                VerticalAlignment =
                    VerticalAlignment.Top,
                Child = utilityBar
            };
        stage.Children.Add(_toolbarHost);
        stage.Children.Add(_utilityHost);
        stage.Children.Add(_statusHost);
        stage.Children.Add(_metadataHost);

        _chromeTimer =
            new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.4)
            };
        _chromeTimer.Tick +=
            (_, _) => FadeChrome();

        stage.PointerEntered +=
            (_, _) => RevealChrome();
        stage.PointerMoved +=
            (_, _) => RevealChrome();

        foreach (var control in new Control[]
                 {
                     _previous,
                     _next,
                     _zoomOut,
                     _zoomIn,
                     _fit,
                     _actual,
                     _info,
                     _fullScreen,
                     _close,
                     _zoomSlider
                 })
        {
            control.GotFocus +=
                (_, _) => RevealChrome(
                    autoHide: false);
            control.LostFocus +=
                (_, _) => RevealChrome();
            control.PointerEntered +=
                (_, _) => RevealChrome(
                    autoHide: false);
            control.PointerExited +=
                (_, _) => RevealChrome();
        }

        RevealChrome();

        Content = stage;

        _previous.Click += async (_, _) => await MoveAsync(-1);
        _next.Click += async (_, _) => await MoveAsync(1);
        _zoomOut.Click +=
            async (_, _) =>
                await ZoomByAsync(
                    1 / _session.Options.ZoomStep);
        _zoomIn.Click +=
            async (_, _) =>
                await ZoomByAsync(
                    _session.Options.ZoomStep);
        _fit.Click += (_, _) => Fit();
        _actual.Click += async (_, _) => await ActualSizeAsync();
        _info.Click +=
            (_, _) =>
            {
                _metadataHost.IsVisible =
                    !_metadataHost.IsVisible;
                _info.Background =
                    _metadataHost.IsVisible
                        ? ViewerVisualTokens.SelectedSurface
                        : ViewerVisualTokens.Overlay;
                RevealChrome(
                    autoHide: false);
            };
        _fullScreen.Click +=
            (_, _) =>
                FullScreenToggleRequested?.Invoke(
                    this,
                    EventArgs.Empty);
        _close.Click +=
            (_, _) =>
                CloseRequested?.Invoke(
                    this,
                    EventArgs.Empty);

        _zoomSlider.PropertyChanged +=
            async (_, args) =>
            {
                if (_syncingZoomSlider
                    || args.Property
                        != RangeBase.ValueProperty)
                {
                    return;
                }

                await SetZoomAsync(
                    ZoomFromSlider(
                        _zoomSlider.Value));
            };

        _scroll.SizeChanged += (_, _) =>
        {
            if (_fitMode && !HasPendingZoomCommand())
            {
                ApplyFit();
            }

            Dispatcher.UIThread.Post(
                UpdatePanAffordance,
                DispatcherPriority.Render);
        };

        // Own wheel input before ScrollViewer's built-in scrolling sees it.
        // In focused viewing the interaction contract is deliberately simple:
        // wheel = zoom, left drag = pan.
        stage.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnPointerWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _surface.PointerPressed += OnPointerPressed;
        _surface.PointerMoved += OnPointerMoved;
        _surface.PointerReleased += OnPointerReleased;
        KeyDown += OnKeyDown;

        AttachSessionEvents();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

        ApplySnapshot(_session.Snapshot);
    }

    private static Avalonia.Controls.Shapes.Path CreateViewerIcon(
        string pathData,
        double size) =>
        new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(pathData),
            Stroke = ViewerVisualTokens.Foreground,
            StrokeThickness = 1.9,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            HorizontalAlignment =
                HorizontalAlignment.Center,
            VerticalAlignment =
                VerticalAlignment.Center
        };

    private static Button CreateViewerButton(
        object content,
        string tooltip,
        string? automationId = null,
        string? acceleratorKey = null)
    {
        var button =
            new Button
            {
                Content = content,
                MinWidth = 36,
                MinHeight = 32,
                Padding = new Thickness(9, 5),
                CornerRadius = new CornerRadius(7),
                Background = ViewerVisualTokens.Overlay,
                Foreground = ViewerVisualTokens.Foreground,
                BorderBrush = ViewerVisualTokens.Border,
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment =
                    HorizontalAlignment.Center,
                VerticalContentAlignment =
                    VerticalAlignment.Center
            };
        ToolTip.SetTip(button, tooltip);
        ViewerVisualTokens.Name(
            button,
            tooltip,
            automationId,
            acceleratorKey);
        return button;
    }

    public double Zoom => _zoom;

    public long SelectedAssetIndex => _session.Snapshot.SelectedIndex;

    public ViewerDetailLoadState LoadState => _session.Snapshot.State;

    public bool IsOriginal => _session.Snapshot.IsOriginal;

    public Vector PanOffset => _scroll.Offset;

    public string MetadataText => _metadata.Text ?? string.Empty;

    internal bool IsMetadataVisibleForSmoke =>
        _metadataHost.IsVisible;

    internal bool IsMetadataInVisualTreeForSmoke =>
        _metadataHost.GetVisualParent() is not null;

    internal double ZoomSliderValueForSmoke =>
        _zoomSlider.Value;

    internal Task SetZoomSliderForSmokeAsync(
        double value)
    {
        var clamped =
            Math.Clamp(
                value,
                _zoomSlider.Minimum,
                _zoomSlider.Maximum);
        _syncingZoomSlider = true;
        try
        {
            _zoomSlider.Value = clamped;
        }
        finally
        {
            _syncingZoomSlider = false;
        }

        return SetZoomAsync(
            ZoomFromSlider(clamped));
    }

    internal void ToggleMetadataForSmoke()
    {
        _metadataHost.IsVisible =
            !_metadataHost.IsVisible;
    }

    internal Rect MetadataBoundsInControlForSmoke =>
        GetControlBoundsForSmoke(
            _metadataHost,
            "metadata host");

    internal Rect ZoomSliderBoundsInControlForSmoke =>
        GetControlBoundsForSmoke(
            _zoomSlider,
            "zoom slider");


    internal Rect ImageBoundsInControlForSmoke
    {
        get
        {
            var origin =
                _image.TranslatePoint(
                    new Point(0, 0),
                    this)
                ?? throw new InvalidOperationException(
                    "Unable to map detail image bounds.");
            return new Rect(
                origin,
                _image.Bounds.Size);
        }
    }

    internal Rect ViewportBoundsInControlForSmoke
    {
        get
        {
            var origin =
                _scroll.TranslatePoint(
                    new Point(0, 0),
                    this)
                ?? throw new InvalidOperationException(
                    "Unable to map detail viewport bounds.");
            return new Rect(
                origin,
                _scroll.Bounds.Size);
        }
    }


    internal Rect ToolbarBoundsInControlForSmoke =>
        GetControlBoundsForSmoke(
            _toolbarHost,
            "toolbar");

    internal Rect UtilityBoundsInControlForSmoke =>
        GetControlBoundsForSmoke(
            _utilityHost,
            "utility toolbar");

    internal Rect PreviousBoundsInControlForSmoke =>
        GetControlBoundsForSmoke(
            _previous,
            "previous button");

    internal Rect NextBoundsInControlForSmoke =>
        GetControlBoundsForSmoke(
            _next,
            "next button");


    private Rect GetControlBoundsForSmoke(
        Control control,
        string label)
    {
        var origin =
            control.TranslatePoint(
                new Point(0, 0),
                this)
            ?? throw new InvalidOperationException(
                $"Unable to map detail {label} bounds.");
        return new Rect(
            origin,
            control.Bounds.Size);
    }

    public event EventHandler<long>? SelectedAssetIndexChanged;

    public event EventHandler? FullScreenToggleRequested;

    public event EventHandler? CloseRequested;

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
        Dispatcher.UIThread.Post(
            CenterViewport,
            DispatcherPriority.Render);
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

        var anchor =
            CaptureViewportAnchor();
        _fitMode = false;
        SetZoom(1);
        SynchronizeRequestedZoom(1);
        Dispatcher.UIThread.Post(
            () => RestoreViewportAnchor(anchor),
            DispatcherPriority.Render);
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

        var anchor =
            _fitMode
                ? (0.5, 0.5)
                : CaptureViewportAnchor();
        _fitMode = false;
        SetZoom(committedTarget);
        SynchronizeRequestedZoom(committedTarget);
        Dispatcher.UIThread.Post(
            () => RestoreViewportAnchor(anchor),
            DispatcherPriority.Render);
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
        _zoomText.Text =
            $"{Math.Round(zoom * 100):N0}%";
        SynchronizeZoomSlider(zoom);

        Dispatcher.UIThread.Post(
            UpdatePanAffordance,
            DispatcherPriority.Render);
    }

    private double SliderFromZoom(
        double zoom)
    {
        var minimum =
            _session.Options.MinZoom;
        var maximum =
            _session.Options.MaxZoom;
        var clamped =
            Math.Clamp(
                zoom,
                minimum,
                maximum);

        if (maximum <= minimum
            || minimum <= 0)
        {
            return 0;
        }

        return 100
            * Math.Log(clamped / minimum)
            / Math.Log(maximum / minimum);
    }

    private double ZoomFromSlider(
        double value)
    {
        var minimum =
            _session.Options.MinZoom;
        var maximum =
            _session.Options.MaxZoom;

        if (maximum <= minimum
            || minimum <= 0)
        {
            return minimum;
        }

        var normalized =
            Math.Clamp(value, 0, 100)
            / 100;
        return minimum
            * Math.Pow(
                maximum / minimum,
                normalized);
    }

    private void SynchronizeZoomSlider(
        double zoom)
    {
        _syncingZoomSlider = true;
        try
        {
            _zoomSlider.Value =
                SliderFromZoom(zoom);
        }
        finally
        {
            _syncingZoomSlider = false;
        }
    }

    private void CenterViewport()
    {
        var extent =
            _scroll.Extent;
        var viewport =
            _scroll.Viewport;

        var horizontal =
            Math.Max(
                0,
                (extent.Width - viewport.Width)
                / 2);
        var vertical =
            Math.Max(
                0,
                (extent.Height - viewport.Height)
                / 2);

        _scroll.Offset =
            new Vector(
                horizontal,
                vertical);
    }

    private (double X, double Y) CaptureViewportAnchor()
    {
        var viewport =
            _scroll.Viewport;
        var width =
            double.IsFinite(_image.Width)
                ? _image.Width
                : _image.Bounds.Width;
        var height =
            double.IsFinite(_image.Height)
                ? _image.Height
                : _image.Bounds.Height;

        static double Axis(
            double content,
            double viewportSize,
            double offset)
        {
            if (content <= 0
                || content <= viewportSize)
            {
                return 0.5;
            }

            return Math.Clamp(
                (offset + (viewportSize / 2))
                / content,
                0,
                1);
        }

        return (
            Axis(
                width,
                viewport.Width,
                _scroll.Offset.X),
            Axis(
                height,
                viewport.Height,
                _scroll.Offset.Y));
    }

    private void RestoreViewportAnchor(
        (double X, double Y) anchor)
    {
        var viewport =
            _scroll.Viewport;
        var width =
            double.IsFinite(_image.Width)
                ? _image.Width
                : _image.Bounds.Width;
        var height =
            double.IsFinite(_image.Height)
                ? _image.Height
                : _image.Bounds.Height;

        static double Axis(
            double content,
            double viewportSize,
            double fraction)
        {
            if (content <= viewportSize)
            {
                return 0;
            }

            return Math.Clamp(
                (content * fraction)
                - (viewportSize / 2),
                0,
                Math.Max(0, content - viewportSize));
        }

        _scroll.Offset =
            new Vector(
                Axis(
                    width,
                    viewport.Width,
                    anchor.X),
                Axis(
                    height,
                    viewport.Height,
                    anchor.Y));
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
        Dispatcher.UIThread.Post(
            CenterViewport,
            DispatcherPriority.Render);
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

    public void PrepareForSessionRebind()
    {
        DetachGridEvents();
        DetachSessionEvents();
        CancelPendingZoomCommands();
        _image.Source = null;
        _status.Text = string.Empty;
        _metadata.Text = string.Empty;
    }

    public void RebindSession(
        ViewerDetailSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        DetachSessionEvents();
        _session = session;
        _observedSelectionVersion =
            _session.Snapshot.SelectionVersion;
        _session.SetOriginalReleaseHandler(
            ReleaseOriginalAfterCompositionAsync);

        if (_visualAttached)
        {
            AttachSessionEvents();
        }

        ApplySnapshot(_session.Snapshot);
    }

    private void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        _visualAttached = true;
        _compositor =
            ElementComposition.GetElementVisual(
                _image)?.Compositor
            ?? _compositor;
        AttachSessionEvents();
        AttachTopLevelScaling();
        AttachGridEvents();
        ApplySnapshot(_session.Snapshot);
        SynchronizeFromGrid();
    }

    public void PrepareForDetach()
    {
        _chromeTimer.Stop();
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

    private void OnDetachedFromVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e) =>
        PrepareForDetach();

    private Task ReleaseOriginalAfterCompositionAsync(
        ViewerOriginalBitmap original)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(
            () =>
            {
                try
                {
                    var bitmap = original.Bitmap;

                    if (ReferenceEquals(
                            _image.Source,
                            bitmap))
                    {
                        _image.Source = null;
                    }

                    var compositor =
                        ElementComposition.GetElementVisual(
                            _image)?.Compositor
                        ?? _compositor;

                    Task disposal;
                    if (compositor is null)
                    {
                        // No visual was ever attached, so there is no
                        // compositor reference to drain.
                        disposal = original.BeginDispose();
                    }
                    else
                    {
                        _compositor = compositor;

                        // Setting Image.Source above invalidates the visual.
                        // The compositor batch fence completes only after the
                        // resulting composition batch has rendered on the
                        // render thread, which is the point at which the old
                        // image can be released safely.
                        var batch =
                            compositor
                                .RequestCompositionBatchCommitAsync();

                        disposal =
                            original.DisposeAfterAsync(
                                batch.Rendered);
                    }

                    _ = CompleteOriginalReleaseAsync(
                        disposal,
                        completion);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(
                        exception);
                }
            },
            DispatcherPriority.Render);

        return completion.Task;
    }

    private static async Task CompleteOriginalReleaseAsync(
        Task disposal,
        TaskCompletionSource completion)
    {
        try
        {
            await disposal.ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
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
            ViewerDetailLoadState.Empty =>
                "画像を選択してください",
            ViewerDetailLoadState.LoadingPreview =>
                "プレビューを読み込んでいます…",
            ViewerDetailLoadState.PreviewReady =>
                snapshot.ErrorMessage is null
                    ? string.Empty
                    : snapshot.ErrorMessage,
            ViewerDetailLoadState.LoadingOriginal =>
                "読み込み中…",
            ViewerDetailLoadState.OriginalReady =>
                string.Empty,
            ViewerDetailLoadState.Error =>
                string.IsNullOrWhiteSpace(
                    snapshot.ErrorMessage)
                    ? "この画像を表示できません"
                    : "この画像を表示できません"
                      + $" · {snapshot.ErrorMessage}",
            _ => "画像の状態を確認しています…"
        };
        _status.IsVisible =
            !string.IsNullOrWhiteSpace(
                _status.Text);
        _statusHost.IsVisible =
            _status.IsVisible;

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
        // Consume the routed wheel gesture even when a device reports only a
        // horizontal delta. Letting any wheel event reach ScrollViewer makes
        // zoom and pan feel coupled.
        e.Handled = true;
        await ApplyWheelZoomAsync(e.Delta.Y);
    }

    private Task ApplyWheelZoomAsync(double deltaY)
    {
        if (deltaY == 0)
        {
            return Task.CompletedTask;
        }

        var factor = deltaY > 0
            ? _session.Options.ZoomStep
            : 1 / _session.Options.ZoomStep;
        return ZoomByAsync(factor);
    }

    internal Task ApplyWheelZoomForSmokeAsync(double deltaY) =>
        ApplyWheelZoomAsync(deltaY);

    internal bool CanPanForSmoke =>
        CanPan();

    private bool CanPan() =>
        _scroll.Extent.Width
            > _scroll.Viewport.Width + 0.5
        || _scroll.Extent.Height
            > _scroll.Viewport.Height + 0.5;

    private void UpdatePanAffordance()
    {
        _surface.Cursor =
            _dragging
                ? PanningCursor
                : CanPan()
                    ? PanAvailableCursor
                    : Cursor.Default;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_scroll).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            e.Handled = true;
            if (_fitMode)
            {
                _ = ActualSizeAsync();
            }
            else
            {
                Fit();
            }

            return;
        }

        if (!CanPan())
        {
            return;
        }

        _dragging = true;
        UpdatePanAffordance();
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

    private void RevealChrome(
        bool autoHide = true)
    {
        _toolbarHost.Opacity = 1;
        _utilityHost.Opacity = 1;
        _previous.Opacity = 1;
        _next.Opacity = 1;
        _toolbarHost.IsHitTestVisible = true;
        _utilityHost.IsHitTestVisible = true;
        _previous.IsHitTestVisible = true;
        _next.IsHitTestVisible = true;
        _chromeTimer.Stop();
        if (autoHide)
        {
            _chromeTimer.Start();
        }
    }

    private bool IsChromeInteractionActive() =>
        _dragging
        || _toolbarHost.IsPointerOver
        || _utilityHost.IsPointerOver
        || _previous.IsPointerOver
        || _next.IsPointerOver
        || _close.IsFocused
        || _fullScreen.IsFocused
        || _actual.IsFocused
        || _info.IsFocused
        || _zoomSlider.IsFocused
        || _fit.IsFocused
        || _zoomIn.IsFocused
        || _zoomOut.IsFocused
        || _previous.IsFocused
        || _next.IsFocused;

    private void FadeChrome()
    {
        _chromeTimer.Stop();

        if (IsChromeInteractionActive())
        {
            RevealChrome(
                autoHide: false);
            return;
        }

        // Idle Viewer chrome must not obscure or intercept the image.
        _toolbarHost.Opacity = 0;
        _utilityHost.Opacity = 0;
        _previous.Opacity = 0;
        _next.Opacity = 0;
        _toolbarHost.IsHitTestVisible = false;
        _utilityHost.IsHitTestVisible = false;
        _previous.IsHitTestVisible = false;
        _next.IsHitTestVisible = false;
    }

    internal Rect CloseButtonBoundsInControlForSmoke =>
        GetControlBoundsForSmoke(
            _close,
            "close button");

    internal bool HasPanCursorForSmoke =>
        !ReferenceEquals(
            _surface.Cursor,
            Cursor.Default);

    internal void RequestCloseForSmoke() =>
        CloseRequested?.Invoke(
            this,
            EventArgs.Empty);

    internal void FocusCloseForSmoke() =>
        _close.Focus();

    internal bool ZoomButtonsUseConventionalGlyphsForSmoke =>
        string.Equals(
            _zoomOut.Content as string,
            "−",
            StringComparison.Ordinal)
        && string.Equals(
            _zoomIn.Content as string,
            "+",
            StringComparison.Ordinal);

    internal bool IsChromeVisibleForSmoke =>
        _toolbarHost.Opacity > 0.9
        && _utilityHost.Opacity > 0.9
        && _previous.Opacity > 0.9
        && _next.Opacity > 0.9;

    internal bool IsChromeNonBlockingForSmoke =>
        _toolbarHost.Opacity <= 0.001
        && _utilityHost.Opacity <= 0.001
        && _previous.Opacity <= 0.001
        && _next.Opacity <= 0.001
        && !_toolbarHost.IsHitTestVisible
        && !_utilityHost.IsHitTestVisible
        && !_previous.IsHitTestVisible
        && !_next.IsHitTestVisible;

    internal void FadeChromeForSmoke() =>
        FadeChrome();

    internal void RevealChromeForSmoke() =>
        RevealChrome(
            autoHide: false);

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        UpdatePanAffordance();
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
            case Key.OemMinus:
            case Key.Subtract:
                await ZoomByAsync(
                    1 / _session.Options.ZoomStep);
                e.Handled = true;
                break;
            case Key.OemPlus:
            case Key.Add:
                await ZoomByAsync(
                    _session.Options.ZoomStep);
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                Fit();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                await ActualSizeAsync();
                e.Handled = true;
                break;
            case Key.I:
                _metadataHost.IsVisible =
                    !_metadataHost.IsVisible;
                _info.Background =
                    _metadataHost.IsVisible
                        ? ViewerVisualTokens.SelectedSurface
                        : ViewerVisualTokens.Overlay;
                RevealChrome(
                    autoHide: false);
                e.Handled = true;
                break;
            case Key.F11:
                FullScreenToggleRequested?.Invoke(
                    this,
                    EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }
}
