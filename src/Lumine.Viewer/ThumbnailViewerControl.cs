using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Selection;

namespace Lumine.Viewer;

public sealed class ThumbnailViewerControl : UserControl
{
    private readonly ViewerSession _session;
    private readonly ListBox _rows;
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

        RebuildRows();
    }

    public long AssetCount => _session.Count;

    public long SelectedAssetIndex => _selectedIndex;

    public int Columns => _columns;

    public int RealizedRowCount => _rows.GetRealizedContainers().Count();

    public ViewerRuntimeDiagnostics Diagnostics => _session.Diagnostics;

    public event EventHandler<long>? SelectedAssetIndexChanged;

    public void SelectAsset(long index, bool scrollIntoView = true)
    {
        if ((ulong)index >= (ulong)AssetCount)
        {
            return;
        }

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

    public void ScrollToAsset(long index)
    {
        if ((ulong)index >= (ulong)AssetCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        _rows.ScrollIntoView(checked((int)(index / _columns)));
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

        _columns = columns;
        RebuildRows();
    }

    private void RebuildRows()
    {
        _rows.ItemsSource = new VirtualRowIndexList(AssetCount, _columns);
        _rows.ItemTemplate = new FuncDataTemplate<long>(
            (rowIndex, _) => new ViewerRowControl(
                _session,
                rowIndex,
                _columns,
                SelectAsset),
            supportsRecycling: false);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (AssetCount == 0)
        {
            return;
        }

        var current = _selectedIndex < 0 ? 0 : _selectedIndex;
        var next = e.Key switch
        {
            Key.Left => current - 1,
            Key.Right => current + 1,
            Key.Up => current - _columns,
            Key.Down => current + _columns,
            Key.Home => 0,
            Key.End => AssetCount - 1,
            _ => current
        };

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
        private readonly Action<long, bool> _select;
        private CancellationTokenSource? _prefetchCancellation;

        public ViewerRowControl(
            ViewerSession session,
            long rowIndex,
            int columns,
            Action<long, bool> select)
        {
            _session = session;
            _rowIndex = rowIndex;
            _columns = columns;
            _select = select;

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
                    session,
                    index,
                    () => _select(index, false)));
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
        private readonly ViewerSession _session;
        private readonly long _index;
        private readonly Action _select;
        private readonly Image _image;
        private readonly TextBlock _label;
        private CancellationTokenSource? _loadCancellation;
        private DecodedBitmapLease? _bitmapLease;

        public ViewerTileControl(
            ViewerSession session,
            long index,
            Action select)
        {
            _session = session;
            _index = index;
            _select = select;

            Width = session.Options.TileWidth;
            Height = session.Options.TileHeight;
            Padding = new Thickness(4);
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

            PointerPressed += (_, _) => _select();
            AttachedToVisualTree += OnAttached;
            DetachedFromVisualTree += OnDetached;
        }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _session.NotifyTileAttached();
            StartLoad();
        }

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _session.NotifyTileDetached();
            CancelLoad();
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

            _image.Source = null;
            _bitmapLease?.Dispose();
            _bitmapLease = null;
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

                    _bitmapLease?.Dispose();
                    _bitmapLease = lease;
                    lease = null;
                    _image.Source = _bitmapLease.Bitmap;
                    _label.Text = asset.DisplayName;
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch
            {
                await Dispatcher.UIThread.InvokeAsync(() => _label.Text = "!");
            }
            finally
            {
                lease?.Dispose();
            }
        }
    }
}
