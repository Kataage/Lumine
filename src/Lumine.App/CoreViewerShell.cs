using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Lumine.Viewer;

namespace Lumine.App;

internal sealed class CoreViewerShell : UserControl
{
    private readonly ThumbnailViewerControl _grid;
    private readonly DetailViewerControl _detail;
    private bool _detached;

    public CoreViewerShell(
        CoreViewerRuntime runtime,
        BrowsePreferences? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);

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
        _detail.BindGrid(_grid);

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

        var detailSurface =
            new Border
            {
                Background = LumineDesign.Surface,
                BorderBrush = LumineDesign.Border,
                BorderThickness =
                    new Thickness(1, 0, 0, 0),
                Child = _detail
            };

        var layout = new Grid
        {
            Background = LumineDesign.Background,
            ColumnDefinitions =
                new ColumnDefinitions("3*,2*")
        };

        layout.Children.Add(gridSurface);

        Grid.SetColumn(detailSurface, 1);
        layout.Children.Add(detailSurface);

        Content = layout;
    }

    internal ThumbnailViewerControl GridViewer => _grid;

    internal DetailViewerControl DetailViewer => _detail;

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
