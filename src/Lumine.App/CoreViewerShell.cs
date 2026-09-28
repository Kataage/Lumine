using Avalonia.Controls;
using Lumine.Viewer;

namespace Lumine.App;

internal sealed class CoreViewerShell : UserControl
{
    private readonly ThumbnailViewerControl _grid;
    private readonly DetailViewerControl _detail;
    private bool _detached;

    public CoreViewerShell(CoreViewerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        _grid = new ThumbnailViewerControl(
            runtime.ViewerSession);
        _detail = new DetailViewerControl(
            runtime.DetailSession);
        _detail.BindGrid(_grid);

        if (runtime.AssetCount == 0)
        {
            Content = new Border
            {
                Padding = new Avalonia.Thickness(32),
                Child = new TextBlock
                {
                    Text = "No supported images were found in this library.",
                    FontSize = 18,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    HorizontalAlignment =
                        Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment =
                        Avalonia.Layout.VerticalAlignment.Center
                }
            };
            return;
        }

        var layout = new Grid
        {
            ColumnDefinitions =
                new ColumnDefinitions("2*,3*")
        };

        layout.Children.Add(_grid);

        Grid.SetColumn(_detail, 1);
        layout.Children.Add(_detail);

        Content = layout;
    }

    internal ThumbnailViewerControl GridViewer => _grid;

    internal DetailViewerControl DetailViewer => _detail;

    public void SelectInitialAsset()
    {
        if (_grid.AssetCount <= 0)
        {
            return;
        }

        _grid.SelectAsset(0);
        _grid.Focus();
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        _detail.UnbindGrid();
    }
}
