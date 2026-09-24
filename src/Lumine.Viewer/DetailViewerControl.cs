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

    public DetailViewerControl(ViewerDetailSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
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
            if (_fitMode)
            {
                Fit();
            }
        };

        _scroll.PointerWheelChanged += OnPointerWheelChanged;
        surface.PointerPressed += OnPointerPressed;
        surface.PointerMoved += OnPointerMoved;
        surface.PointerReleased += OnPointerReleased;
        KeyDown += OnKeyDown;

        _session.StateChanged += OnStateChanged;
        _session.SelectedIndexChanged += OnSessionSelectionChanged;
        DetachedFromVisualTree += (_, _) => UnbindGrid();

        ApplySnapshot(_session.Snapshot);
    }

    public double Zoom => _zoom;

    public long SelectedAssetIndex => _session.Snapshot.SelectedIndex;

    public ViewerDetailLoadState LoadState => _session.Snapshot.State;

    public bool IsOriginal => _session.Snapshot.IsOriginal;

    public event EventHandler<long>? SelectedAssetIndexChanged;

    public Task SelectAsync(
        long index,
        CancellationToken cancellationToken = default) =>
        _session.SelectAsync(index, cancellationToken);

    public void BindGrid(ThumbnailViewerControl grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        UnbindGrid();

        _grid = grid;
        _grid.SelectedAssetIndexChanged += OnGridSelectionChanged;

        if (_grid.SelectedAssetIndex >= 0)
        {
            _ = SelectAsync(_grid.SelectedAssetIndex);
        }
    }

    public void UnbindGrid()
    {
        if (_grid is null)
        {
            return;
        }

        _grid.SelectedAssetIndexChanged -= OnGridSelectionChanged;
        _grid = null;
    }

    public void Fit()
    {
        var snapshot = _session.Snapshot;
        if (snapshot.Bitmap is null)
        {
            return;
        }

        var viewport = _scroll.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        var widthScale = viewport.Width / snapshot.Bitmap.PixelSize.Width;
        var heightScale = viewport.Height / snapshot.Bitmap.PixelSize.Height;

        _fitMode = true;
        SetZoom(
            Math.Clamp(
                Math.Min(widthScale, heightScale),
                _session.Options.MinZoom,
                _session.Options.MaxZoom));
        _scroll.Offset = default;
    }

    public async Task ActualSizeAsync(
        CancellationToken cancellationToken = default)
    {
        _fitMode = false;
        await _session.EnsureOriginalAsync(cancellationToken);
        SetZoom(1);
    }

    public async Task SetZoomAsync(
        double zoom,
        CancellationToken cancellationToken = default)
    {
        _fitMode = false;
        var clamped = Math.Clamp(
            zoom,
            _session.Options.MinZoom,
            _session.Options.MaxZoom);

        if (clamped >= 1 && !_session.Snapshot.IsOriginal)
        {
            await _session.EnsureOriginalAsync(cancellationToken);
        }

        SetZoom(clamped);
    }

    private async Task MoveAsync(long delta)
    {
        await _session.MoveAsync(delta);
        _fitMode = true;
    }

    private void SetZoom(double zoom)
    {
        _zoom = zoom;

        var bitmap = _session.Snapshot.Bitmap;
        if (bitmap is null)
        {
            return;
        }

        _image.Width = bitmap.PixelSize.Width * zoom;
        _image.Height = bitmap.PixelSize.Height * zoom;
    }

    private void OnStateChanged(
        object? sender,
        ViewerDetailSnapshot snapshot) =>
        Dispatcher.UIThread.Post(
            () => ApplySnapshot(snapshot),
            DispatcherPriority.Render);

    private void ApplySnapshot(ViewerDetailSnapshot snapshot)
    {
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
            : metadata is null
                ? $"{snapshot.Asset.DisplayName} · {snapshot.Asset.FileSize:N0} bytes"
                : $"{snapshot.Asset.DisplayName} · {metadata.Width}×{metadata.Height} · {metadata.Format ?? snapshot.Asset.Format ?? "unknown"} · {metadata.FileSize:N0} bytes";

        _previous.IsEnabled = snapshot.SelectedIndex > 0;
        _next.IsEnabled = snapshot.SelectedIndex >= 0
            && snapshot.SelectedIndex + 1 < _session.Count;

        if (snapshot.Bitmap is not null)
        {
            if (_fitMode)
            {
                Fit();
            }
            else
            {
                SetZoom(_zoom);
            }
        }
    }

    private void OnSessionSelectionChanged(object? sender, long index)
    {
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
    }

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

        await SetZoomAsync(_zoom * factor);
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
