using System.Buffers.Binary;
using Avalonia.Controls;
using Lumine.Library;
using Lumine.Viewer;

namespace Lumine.App;

internal static class ProductAcceptanceFunctionalScenario
{
    public static async Task RunAsync(
        MainWindow window,
        AppDataPaths dataPaths,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(dataPaths);
        ArgumentNullException.ThrowIfNull(metadata);

        // Cold and warm acceptance intentionally share the same product
        // database. Use a fresh source-library path per process so the
        // product workflow remains repeatable without colliding with
        // directed-relation uniqueness from the previous run.
        var scratchRoot =
            Path.Combine(
                dataPaths.RootPath,
                "product-acceptance-scratch",
                Guid.NewGuid().ToString("N"));
        var libraryRoot =
            Path.Combine(
                scratchRoot,
                "library");
        var emptyLibraryRoot =
            Path.Combine(
                scratchRoot,
                "empty-library");

        if (Directory.Exists(scratchRoot))
        {
            Directory.Delete(
                scratchRoot,
                recursive: true);
        }

        Directory.CreateDirectory(libraryRoot);
        Directory.CreateDirectory(emptyLibraryRoot);

        WriteBmp24(
            Path.Combine(
                libraryRoot,
                "product-first.bmp"),
            width: 1200,
            height: 900);
        WriteBmp24(
            Path.Combine(
                libraryRoot,
                "product-second.bmp"),
            width: 960,
            height: 720);

        try
        {
            VerifyIdentity(window);
            metadata["product.identity"] = "pass";

            await window.OpenLibraryAsync(
                libraryRoot,
                dataPaths,
                cancellationToken);

            Require(
                string.Equals(
                    window.ProductShellState,
                    "Workspace",
                    StringComparison.Ordinal),
                $"Product acceptance scratch library did not enter Workspace: {window.ProductShellState}.");

            var runtime =
                window.CurrentRuntime
                ?? throw new InvalidOperationException(
                    "Product acceptance scratch library did not create CoreViewerRuntime.");
            var shell =
                window.CurrentShell
                ?? throw new InvalidOperationException(
                    "Product acceptance scratch library did not create CoreViewerShell.");

            await VerifyNavigationAsync(
                window,
                cancellationToken);
            metadata["product.navigation"] = "pass";

            await VerifyBrowseAsync(
                window,
                shell,
                cancellationToken);
            metadata["product.browse"] = "pass";

            var first =
                await runtime.ViewerSession.GetAssetAsync(
                    0,
                    cancellationToken);
            var second =
                await runtime.ViewerSession.GetAssetAsync(
                    1,
                    cancellationToken);

            shell.GridViewer.SelectAsset(0);
            await shell.ShowContextDetailAsync();

            Require(
                shell.IsContextDetailVisible
                && shell.ContextDetail.AssetId == first.Id
                && shell.ContextDetail.TitleText
                    == first.DisplayName,
                "Contextual detail did not open for the selected product-acceptance asset.");

            shell.ContextDetail.SetEditorValuesForSmoke(
                rating: 3,
                favorite: false,
                statusLabel: "reviewed",
                colorLabel: "green",
                tags: "product-acceptance, edited",
                notes: "product-acceptance-search-token");

            var saved =
                await shell.ContextDetail.SaveEditorAsync(
                    notify: false,
                    cancellationToken:
                        cancellationToken);

            Require(
                saved is not null
                && saved.Rating == 3
                && !saved.Favorite
                && saved.StatusLabel == "reviewed"
                && saved.ColorLabel == "green"
                && saved.Tags.Contains("product-acceptance")
                && saved.Tags.Contains("edited")
                && saved.Notes
                    == "product-acceptance-search-token"
                && shell.ContextDetail.RatingText == "★3"
                && shell.ContextDetail.TagsText.Contains(
                    "edited",
                    StringComparison.Ordinal)
                && shell.ContextDetail.NotesText
                    == "product-acceptance-search-token",
                "Contextual metadata editor did not persist and render the complete user-owned metadata record.");

            shell.GridViewer.SelectAsset(
                1,
                scrollIntoView: false,
                mode: ViewerSelectionMode.Toggle);

            Require(
                shell.GridViewer.SelectedAssetCount == 2,
                "Ctrl/toggle-style multi-selection contract did not retain two selected assets.");

            metadata["product.organization"] = "pass";

            var work =
                await shell.CreateWorkFromSelectionAsync(
                    new CreativeWorkDialogResult(
                        "Product Acceptance Work",
                        "final product acceptance"));

            Require(
                work is not null
                && work.Assets.Select(
                        static asset => asset.Id)
                    .SequenceEqual(
                        new[]
                        {
                            first.Id,
                            second.Id
                        }),
                "Ordered Work creation failed during product acceptance.");

            var group =
                await shell.CreateGenerationGroupFromSelectionAsync(
                    new CreativeGroupDialogResult(
                        "Product Acceptance Group",
                        work!.Id,
                        "product acceptance prompt",
                        "product acceptance negative",
                        "product-acceptance-model",
                        "euler",
                        "normal",
                        24,
                        4.5,
                        "{\"productAcceptance\":true}",
                        "manual non-AI archive workflow"));

            Require(
                group is not null
                && group.WorkId == work.Id
                && group.Assets.Count == 2,
                "Generation Group creation failed during product acceptance.");

            var relation =
                await shell.CreateRelationFromSelectionAsync(
                    new CreativeRelationDialogResult(
                        ReverseDirection: false,
                        RelationType: "img2img",
                        Note: "product acceptance lineage"));

            Require(
                relation is not null
                && relation.Parent.Id == first.Id
                && relation.Child.Id == second.Id,
                "Directed lineage relation lost parent/child direction during product acceptance.");

            var publication =
                await shell.CreatePublicationFromSelectionAsync(
                    new CreativePublicationDialogResult(
                        work.Id,
                        "Pixiv",
                        "@product-acceptance",
                        "Product Acceptance Publication",
                        "snapshot body",
                        "product acceptance",
                        new DateTimeOffset(
                            2026,
                            10,
                            2,
                            7,
                            0,
                            0,
                            TimeSpan.Zero),
                        "product-acceptance",
                        "https://example.invalid/product-acceptance",
                        "{\"ageRestriction\":\"all\"}"));

            Require(
                publication is not null
                && publication.Assets.Count == 2
                && publication.Assets[0].AssetId
                    == first.Id
                && publication.Assets[1].AssetId
                    == second.Id,
                "Publication snapshot did not preserve ordered asset membership.");

            var creativeContext =
                await runtime.LibraryService
                    .GetAssetCreativeContextAsync(
                        runtime.Library.Id,
                        second.Id,
                        cancellationToken);

            Require(
                creativeContext.Works.Any(
                    item => item.Id == work.Id)
                && creativeContext.GenerationGroups.Any(
                    item => item.Id == group!.Id)
                && creativeContext.Relations.Any(
                    item => item.Id == relation!.Id)
                && creativeContext.Publications.Any(
                    item => item.Id == publication!.Id),
                "Creative archive context did not round-trip Work / Generation Group / Relation / Publication.");

            metadata["product.creative_archive"] = "pass";

            await VerifyViewerAsync(
                shell,
                cancellationToken);
            metadata["product.viewer"] = "pass";

            var shellBeforeNoMatch =
                window.CurrentShell
                ?? throw new InvalidOperationException(
                    "Product acceptance lost the Viewer shell before no-match coverage.");
            var gridBeforeNoMatch =
                shellBeforeNoMatch.GridViewer;
            gridBeforeNoMatch.SelectAsset(0);
            await shellBeforeNoMatch.ShowContextDetailAsync();

            await window.ApplyBrowseFilterForSmokeAsync(
                new BrowseFilterState(
                    SearchText:
                        "__lumine_product_acceptance_no_match__"));

            Require(
                string.Equals(
                    window.ProductShellState,
                    "Workspace",
                    StringComparison.Ordinal)
                && window.CurrentRuntime is not null
                && window.CurrentRuntime.AssetCount == 0
                && ReferenceEquals(
                    window.CurrentShell,
                    shellBeforeNoMatch)
                && ReferenceEquals(
                    window.CurrentShell.GridViewer,
                    gridBeforeNoMatch)
                && window.CurrentShell
                    .GridViewer
                    .SelectedAssetIndex == -1
                && !window.CurrentShell
                    .IsContextDetailVisible,
                "Filtered zero-result query rebuilt the Viewer surface or retained stale selection/Inspector state.");

            await window.ApplyBrowseFilterForSmokeAsync(
                new BrowseFilterState(
                    SortOrder:
                        window.SettingsSnapshot
                            .ViewerDefaults
                            .SortOrder));

            Require(
                string.Equals(
                    window.ProductShellState,
                    "Workspace",
                    StringComparison.Ordinal)
                && window.CurrentRuntime is not null
                && window.CurrentRuntime.AssetCount > 0
                && ReferenceEquals(
                    window.CurrentShell,
                    shellBeforeNoMatch)
                && ReferenceEquals(
                    window.CurrentShell.GridViewer,
                    gridBeforeNoMatch),
                "Clearing the product-acceptance filter rebuilt the Viewer surface instead of restoring data in place.");

            await window.OpenLibraryAsync(
                emptyLibraryRoot,
                dataPaths,
                cancellationToken);

            Require(
                string.Equals(
                    window.ProductShellState,
                    "EmptyLibrary",
                    StringComparison.Ordinal)
                && window.CurrentRuntime is not null
                && window.CurrentShell is null,
                "Truly empty scratch library did not use EmptyLibrary.");

            metadata["product.states"] = "pass";

            await window.OpenLibraryAsync(
                libraryRoot,
                dataPaths,
                cancellationToken);

            window.NavigateForSmoke("設定");

            Require(
                window.WorkspacePageForSmoke
                    is Border
                && window.IsWorkspacePageVisibleForSmoke
                && !window.IsNavigationPaneVisibleForSmoke
                && window.SettingsSnapshot.DataPaths.RootPath
                    == dataPaths.RootPath
                && window.SettingsSnapshot
                    .EncodedThumbnailMemoryByteLimit > 0,
                "Product Settings did not expose the active storage/resource state in the main workspace.");

            var portable =
                AppDataPaths.CreatePortable();

            Require(
                portable.IsPortable
                && string.Equals(
                    Path.GetFileName(
                        portable.RootPath),
                    "data",
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    Path.GetDirectoryName(
                        portable.RootPath),
                    Path.TrimEndingDirectorySeparator(
                        AppContext.BaseDirectory),
                    StringComparison.OrdinalIgnoreCase),
                "Portable data-root contract did not resolve executable-local data/.");

            metadata["product.settings_portable"] = "pass";

            var queryResult =
                await window.CurrentRuntime!
                    .LibraryService
                    .GetAssetPageAsync(
                        window.CurrentRuntime.Library.Id,
                        new AssetQuery(
                            SearchText:
                                "product-acceptance-search-token",
                            RequiredTags:
                                ["edited"],
                            MinRating: 3,
                            MaxRating: 3,
                            Favorite: false,
                            StatusLabel: "reviewed",
                            ColorLabel: "green"),
                        limit: 10,
                        cancellationToken:
                            cancellationToken);

            Require(
                queryResult.Items.Count == 1
                && queryResult.Items[0].Id == first.Id,
                "Product metadata was not immediately searchable/filterable after editing.");

            metadata["product.search_metadata_roundtrip"] =
                "pass";
            metadata["product.automated_result"] =
                "pass";
        }
        catch
        {
            metadata["product.automated_result"] =
                "fail";
            throw;
        }
    }

    private static void VerifyIdentity(
        MainWindow window)
    {
        Require(
            string.Equals(
                window.Title,
                "Lumine",
                StringComparison.Ordinal)
            && window.Icon is not null,
            "Native product chrome did not expose Lumine branding.");

        Require(
            MainWindow.ProductNavigationLabels
                .SequenceEqual(
                [
                    "ライブラリ",
                    "フォルダー",
                    "タグ",
                    "公開履歴",
                    "設定"
                ]),
            "Product navigation hierarchy drifted from the approved Lumine information architecture.");
    }

    private static Task VerifyNavigationAsync(
        MainWindow window,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var destination
                 in MainWindow.ProductNavigationLabels)
        {
            window.NavigateForSmoke(destination);

            var rendered =
                string.Equals(
                    destination,
                    "設定",
                    StringComparison.Ordinal)
                    ? window.WorkspacePageForSmoke is not null
                        && window.IsWorkspacePageVisibleForSmoke
                        && !window.IsNavigationPaneVisibleForSmoke
                    : window.NavigationContentForSmoke is not null
                        && !window.IsWorkspacePageVisibleForSmoke;

            Require(
                rendered,
                $"Navigation destination '{destination}' did not render the expected product surface.");
        }

        return Task.CompletedTask;
    }

    private static async Task VerifyBrowseAsync(
        MainWindow window,
        CoreViewerShell shell,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        shell.SetBrowseLayout(
            new BrowsePreferences(
                BrowseViewMode.List,
                2,
                AssetSortOrder.ModifiedNewest));

        Require(
            shell.GridViewer.LayoutMode
                == ViewerLayoutMode.List
            && shell.GridViewer.Columns == 1,
            "List mode did not retain single-column virtualization.");

        shell.SetBrowseLayout(
            new BrowsePreferences(
                BrowseViewMode.Grid,
                0,
                AssetSortOrder.ModifiedNewest));

        Require(
            shell.GridViewer.LayoutMode
                == ViewerLayoutMode.Grid
            && shell.GridViewer.Columns >= 1,
            "Grid/density mode did not remain usable.");

        await Task.CompletedTask;
    }

    private static async Task VerifyViewerAsync(
        CoreViewerShell shell,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        shell.HideContextDetail();

        await shell.OpenFocusedViewAsync(0);

        Require(
            shell.IsFocusedViewVisible
            && shell.DetailViewer.SelectedAssetIndex == 0
            && shell.DetailViewer.LoadState
                == ViewerDetailLoadState.PreviewReady,
            "Focused viewer did not reuse the production Detail path.");

        shell.CloseFocusedView();

        await shell.DetailViewer.SelectAsync(
            0,
            cancellationToken);

        await shell.DetailViewer.ActualSizeAsync(
            cancellationToken);

        Require(
            shell.DetailViewer.IsOriginal,
            "1:1 did not promote the selected image to original resolution.");

        await shell.DetailViewer.ZoomByAsync(
            1.25,
            cancellationToken);
        shell.DetailViewer.PanBy(
            24,
            16);
        shell.DetailViewer.Fit();

        Require(
            shell.DetailViewer.SelectedAssetIndex == 0,
            "Fit/zoom/pan lost the selected asset.");

        await shell.DetailViewer.SelectAsync(
            1,
            cancellationToken);

        Require(
            shell.DetailViewer.SelectedAssetIndex == 1,
            "Next-image navigation failed.");

        await shell.DetailViewer.SelectAsync(
            0,
            cancellationToken);

        Require(
            shell.DetailViewer.SelectedAssetIndex == 0,
            "Previous-image navigation failed.");
    }

    private static void Require(
        bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }

    private static void WriteBmp24(
        string path,
        int width,
        int height)
    {
        var rowStride =
            checked(
                ((width * 3 + 3) / 4) * 4);
        const int pixelOffset = 54;
        var bytes =
            new byte[
                checked(
                    pixelOffset
                    + rowStride * height)];

        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(2, 4),
            checked((uint)bytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(10, 4),
            pixelOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(14, 4),
            40);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(18, 4),
            width);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(22, 4),
            height);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(26, 2),
            1);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(28, 2),
            24);

        for (var y = 0;
             y < height;
             y++)
        {
            var storedY =
                height - 1 - y;
            var row =
                pixelOffset
                + storedY * rowStride;

            for (var x = 0;
                 x < width;
                 x++)
            {
                var offset =
                    row + x * 3;
                bytes[offset] =
                    (byte)(
                        56
                        + (x + y) % 160);
                bytes[offset + 1] =
                    (byte)(
                        34
                        + y % 180);
                bytes[offset + 2] =
                    (byte)(
                        12
                        + x % 200);
            }
        }

        File.WriteAllBytes(
            path,
            bytes);
    }
}
