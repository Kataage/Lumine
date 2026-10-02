using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Lumine.Core;
using Lumine.Image;
using Lumine.Library;

namespace Lumine.App;

public sealed class MainWindow : Window
{
    private readonly AppDataPaths _defaultDataPaths;
    private readonly CoreResourcePolicy _resourcePolicy;
    private readonly ThumbnailStorageMode _thumbnailStorageMode;
    private readonly AppHost? _host;
    private readonly Button _openFolder;
    private readonly Button _diagnostics;
    private readonly TextBlock _status;
    private readonly TextBlock _libraryPath;
    private readonly TextBlock _sectionTitle;
    private readonly ContentControl _viewerHost;
    private readonly ContentControl _browseHost;
    private readonly LibraryService _navigationLibraryService;
    private readonly Task _navigationInitialization;
    private readonly ContentControl _navigationRailHost;
    private readonly Border _navigationPane;
    private readonly TextBlock _navigationTitle;
    private readonly ContentControl _navigationContent;
    private CancellationTokenSource? _openCancellation;
    private CancellationTokenSource? _diagnosticsCancellation;
    private CancellationTokenSource? _navigationCancellation;
    private Task _openOperation = Task.CompletedTask;
    private Task _runtimeDiagnosticsOperation = Task.CompletedTask;
    private Task _diagnosticFlushOperation = Task.CompletedTask;
    private Task _navigationOperation = Task.CompletedTask;
    private Window? _diagnosticsWindow;
    private CoreViewerRuntime? _runtime;
    private CoreViewerShell? _shell;
    private bool _closeStarted;
    private bool _closeCompleted;
    private string _productShellState = "Welcome";
    private string _navigationDestination = "ライブラリ";
    private BrowsePreferences _browsePreferences;
    private BrowseFilterState _browseFilterState;
    private BrowseWorkspaceControls? _browseControls;
    private LibraryBrowseFacets _browseFacets =
        new(
            Array.Empty<string>(),
            Array.Empty<string>());
    private IReadOnlyList<LibraryCatalogItem> _libraries =
        Array.Empty<LibraryCatalogItem>();
    private IReadOnlyList<LibraryFolderInfo> _folders =
        Array.Empty<LibraryFolderInfo>();
    private IReadOnlyList<LibraryTagInfo> _tags =
        Array.Empty<LibraryTagInfo>();
    private IReadOnlyList<PublicationInfo> _publications =
        Array.Empty<PublicationInfo>();
    private ProductSettingsSnapshot _settingsSnapshot;

    public MainWindow()
        : this(
            Program.Host?.DataPaths
                ?? AppDataPaths.ResolveDefault(),
            Program.ResourcePolicy,
            Program.Host)
    {
    }

    internal MainWindow(
        AppDataPaths defaultDataPaths,
        CoreResourcePolicy resourcePolicy,
        AppHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(
            defaultDataPaths);
        ArgumentNullException.ThrowIfNull(
            resourcePolicy);

        _defaultDataPaths = defaultDataPaths;
        _resourcePolicy = resourcePolicy;
        _thumbnailStorageMode =
            host?.ThumbnailStorageMode
            ?? Program.ThumbnailStorageMode;
        _host = host;
        _browsePreferences =
            BrowsePreferenceResolver.Resolve(
                host?.Settings
                    ?? new AppSettingsDocument(),
                out _);
        _browseFilterState =
            new BrowseFilterState(
                SortOrder:
                    _browsePreferences.SortOrder);
        _settingsSnapshot =
            CreateSettingsSnapshot(
                new ThumbnailCacheStats(
                    0,
                    0,
                    0));
        _navigationLibraryService =
            new LibraryService(
                _defaultDataPaths.DatabasePath);
        _navigationInitialization =
            _navigationLibraryService.InitializeAsync();

        Title = "Lumine";
        Icon = LumineDesign.CreateWindowIcon();
        Width = 1440;
        Height = 900;
        MinWidth = 900;
        MinHeight = 600;
        Background = LumineDesign.Background;
        Foreground = LumineDesign.Foreground;
        FontFamily = LumineDesign.UiFont;

        _openFolder =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "画像フォルダーを追加"
                });
        _openFolder.Click += OnOpenFolderClicked;

        _diagnostics =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "診断情報"
                });
        _diagnostics.Click += OnDiagnosticsClicked;

        var recovered =
            _host?.PreviousShutdownWasUnclean == true;
        _productShellState =
            recovered
                ? "Recovery"
                : "Welcome";

        _sectionTitle = new TextBlock
        {
            Text = "ライブラリ",
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = LumineDesign.Foreground,
            VerticalAlignment = VerticalAlignment.Center
        };

        _status = new TextBlock
        {
            Text = recovered
                ? "前回の終了を検出しました。安全な状態から復旧しています。"
                : "画像を見る準備ができています。",
            FontSize = 11,
            Foreground = recovered
                ? LumineDesign.Warning
                : LumineDesign.MutedForeground,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        _libraryPath = new TextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = LumineDesign.MutedForeground,
            FontSize = 10.5
        };

        var heading =
            new StackPanel
            {
                Spacing = 2,
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        heading.Children.Add(_sectionTitle);
        heading.Children.Add(_status);

        var actions =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        actions.Children.Add(_diagnostics);
        actions.Children.Add(_openFolder);

        var headerGrid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto"),
                MinHeight = LumineDesign.HeaderHeight
            };
        heading.Margin =
            new Thickness(16, 8, 12, 8);
        headerGrid.Children.Add(heading);
        Grid.SetColumn(actions, 1);
        actions.Margin =
            new Thickness(8, 8, 14, 8);
        headerGrid.Children.Add(actions);

        var header =
            new Border
            {
                Background = LumineDesign.Surface,
                BorderBrush = LumineDesign.Border,
                BorderThickness =
                    new Thickness(0, 0, 0, 1),
                Child = headerGrid
            };

        _viewerHost = new ContentControl
        {
            Background = LumineDesign.Background,
            HorizontalContentAlignment =
                HorizontalAlignment.Stretch,
            VerticalContentAlignment =
                VerticalAlignment.Stretch
        };

        _viewerHost.Content =
            CreateWelcomeState(recovered);

        _browseHost =
            new ContentControl
            {
                IsVisible = false,
                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch
            };

        var workspace = new Grid
        {
            Background = LumineDesign.Background,
            RowDefinitions =
                new RowDefinitions("Auto,Auto,Auto,*")
        };
        workspace.Children.Add(header);

        Grid.SetRow(_browseHost, 1);
        workspace.Children.Add(_browseHost);

        Grid.SetRow(_libraryPath, 2);
        _libraryPath.Margin =
            new Thickness(16, 6, 16, 6);
        workspace.Children.Add(_libraryPath);

        Grid.SetRow(_viewerHost, 3);
        workspace.Children.Add(_viewerHost);

        _navigationRailHost =
            new ContentControl
            {
                Width = LumineDesign.NavigationWidth,
                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,
                VerticalContentAlignment =
                    VerticalAlignment.Stretch
            };
        _navigationRailHost.Content =
            LumineDesign.CreateNavigationRail(
                _navigationDestination,
                OnNavigationRequested);

        _navigationTitle =
            new TextBlock
            {
                Text = _navigationDestination,
                Foreground = LumineDesign.Foreground,
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        var collapseNavigation =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "‹",
                    Width = 34,
                    Padding = new Thickness(0)
                });
        ToolTip.SetTip(
            collapseNavigation,
            "ナビゲーションを閉じる");

        var navigationHeader =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(12, 10, 8, 8)
            };
        navigationHeader.Children.Add(
            _navigationTitle);
        Grid.SetColumn(collapseNavigation, 1);
        navigationHeader.Children.Add(
            collapseNavigation);

        _navigationContent =
            new ContentControl
            {
                Margin = new Thickness(10, 0, 10, 10),
                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,
                VerticalContentAlignment =
                    VerticalAlignment.Stretch
            };

        var navigationLayout =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions("Auto,*")
            };
        navigationLayout.Children.Add(
            navigationHeader);
        Grid.SetRow(_navigationContent, 1);
        navigationLayout.Children.Add(
            _navigationContent);

        _navigationPane =
            new Border
            {
                Width = 280,
                Background = LumineDesign.Surface,
                BorderBrush = LumineDesign.Border,
                BorderThickness =
                    new Thickness(0, 0, 1, 0),
                Child = navigationLayout
            };
        collapseNavigation.Click +=
            (_, _) =>
                _navigationPane.IsVisible = false;

        var appShell = new Grid
        {
            Background = LumineDesign.Background,
            ColumnDefinitions =
                new ColumnDefinitions(
                    $"{LumineDesign.NavigationWidth},Auto,*")
        };
        appShell.Children.Add(
            _navigationRailHost);
        Grid.SetColumn(_navigationPane, 1);
        appShell.Children.Add(
            _navigationPane);
        Grid.SetColumn(workspace, 2);
        appShell.Children.Add(workspace);

        Content = appShell;

        RenderNavigationDestination();

        Opened += OnOpened;
        Closing += OnClosing;
    }

    internal CoreViewerRuntime? CurrentRuntime =>
        _runtime;

    internal CoreViewerShell? CurrentShell =>
        _shell;

    internal string ProductShellState =>
        _productShellState;

    internal static IReadOnlyList<string> ProductNavigationLabels =>
        LumineDesign.NavigationLabels;

    private void OnNavigationRequested(
        string destination)
    {
        if (_closeStarted)
        {
            return;
        }

        _navigationDestination = destination;
        _navigationPane.IsVisible = true;
        _navigationTitle.Text = destination;
        _navigationRailHost.Content =
            LumineDesign.CreateNavigationRail(
                destination,
                OnNavigationRequested);
        RenderNavigationDestination();
        StartNavigationRefresh();
    }

    private void StartNavigationRefresh()
    {
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation =
            new CancellationTokenSource();

        var operation =
            RefreshNavigationAsync(
                _navigationCancellation.Token);
        _navigationOperation = operation;
        _ = ObserveNavigationOperationAsync(operation);
    }

    private async Task ObserveNavigationOperationAsync(
        Task operation)
    {
        try
        {
            await operation;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!_closeStarted)
            {
                _status.Foreground =
                    LumineDesign.Warning;
                _status.Text =
                    $"ナビゲーションを更新できませんでした: {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(
                    _navigationOperation,
                    operation))
            {
                _navigationOperation =
                    Task.CompletedTask;
            }
        }
    }

    private async Task RefreshNavigationAsync(
        CancellationToken cancellationToken)
    {
        await _navigationInitialization
            .WaitAsync(cancellationToken);

        var libraries =
            await _navigationLibraryService.ListLibrariesAsync(
                includeDisabled: true,
                cancellationToken);

        IReadOnlyList<LibraryFolderInfo> folders =
            Array.Empty<LibraryFolderInfo>();
        IReadOnlyList<LibraryTagInfo> tags =
            Array.Empty<LibraryTagInfo>();
        IReadOnlyList<PublicationInfo> publications =
            Array.Empty<PublicationInfo>();
        var facets =
            new LibraryBrowseFacets(
                Array.Empty<string>(),
                Array.Empty<string>());

        var runtime = _runtime;
        if (runtime is not null)
        {
            folders =
                await _navigationLibraryService.ListFoldersAsync(
                    runtime.Library.Id,
                    cancellationToken);
            tags =
                await _navigationLibraryService.ListTagsAsync(
                    runtime.Library.Id,
                    cancellationToken: cancellationToken);
            facets =
                await _navigationLibraryService.GetBrowseFacetsAsync(
                    runtime.Library.Id,
                    cancellationToken);
            publications =
                await _navigationLibraryService.ListPublicationsAsync(
                    runtime.Library.Id,
                    limit: 100,
                    cancellationToken);
        }

        var cacheStats =
            await GetThumbnailCacheStatsAsync(
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        _settingsSnapshot =
            CreateSettingsSnapshot(
                cacheStats);
        _libraries = libraries;
        _folders = folders;
        _tags = tags;
        _publications = publications;
        _browseFacets = facets;
        _browseControls?.UpdateFacetData(
            _tags,
            _browseFacets);
        RenderNavigationDestination();
    }

    private void RenderNavigationDestination()
    {
        if (_navigationContent is null)
        {
            return;
        }

        _navigationTitle.Text =
            _navigationDestination;

        _navigationContent.Content =
            _navigationDestination switch
            {
                "ライブラリ" =>
                    ProductNavigationViews.CreateLibraries(
                        _libraries,
                        _runtime?.Library.Id,
                        ChooseAndOpenLibraryAsync,
                        OpenCatalogLibraryAsync,
                        ToggleLibraryEnabledAsync,
                        RemoveLibraryAsync),
                "フォルダー" =>
                    _runtime is null
                        ? ProductNavigationViews.CreateNoLibrary(
                            "フォルダー")
                        : ProductNavigationViews.CreateFolders(
                            _folders,
                            _browseFilterState.FolderPath,
                            ApplyFolderScopeAsync),
                "タグ" =>
                    _runtime is null
                        ? ProductNavigationViews.CreateNoLibrary(
                            "タグ")
                        : ProductNavigationViews.CreateTags(
                            _tags,
                            _browseFilterState.Tag,
                            ApplyTagScopeAsync),
                "公開履歴" =>
                    _runtime is null
                        ? ProductNavigationViews.CreateNoLibrary(
                            "公開履歴")
                        : ProductNavigationViews.CreatePublicationEntry(
                            _publications),
                "設定" =>
                    ProductSettingsView.Create(
                        _settingsSnapshot,
                        SaveViewerDefaultsFromSettingsAsync,
                        SaveThumbnailModeFromSettingsAsync,
                        SaveMemoryBudgetFromSettingsAsync,
                        ClearThumbnailCacheFromSettingsAsync,
                        ShowDiagnosticsFromNavigationAsync),
                _ =>
                    ProductNavigationViews.CreateNoLibrary(
                        _navigationDestination)
            };
    }

    private async Task ChooseAndOpenLibraryAsync()
    {
        var folders =
            await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Lumine に画像フォルダーを追加",
                    AllowMultiple = false
                });

        if (folders.Count == 0)
        {
            return;
        }

        var path = folders[0].Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            _status.Foreground =
                LumineDesign.Warning;
            _status.Text =
                "ローカルの画像フォルダーを選択してください。";
            return;
        }

        await OpenLibraryAsync(path);
    }

    private Task OpenCatalogLibraryAsync(
        LibraryCatalogItem library)
    {
        if (!library.IsEnabled)
        {
            _status.Foreground =
                LumineDesign.Warning;
            _status.Text =
                "無効なライブラリです。先に有効化してください。";
            return Task.CompletedTask;
        }

        if (!Directory.Exists(library.RootPath))
        {
            _status.Foreground =
                LumineDesign.Warning;
            _status.Text =
                "ライブラリのフォルダーが見つかりません。";
            return Task.CompletedTask;
        }

        return OpenLibraryAsync(
            library.RootPath);
    }

    private async Task ToggleLibraryEnabledAsync(
        LibraryCatalogItem library)
    {
        await _navigationInitialization;

        if (library.IsEnabled
            && _runtime?.Library.Id == library.Id)
        {
            await CloseActiveLibraryAsync(
                "ライブラリを無効化しました。");
        }

        var changed =
            await _navigationLibraryService.SetLibraryEnabledAsync(
                library.Id,
                !library.IsEnabled);

        if (!changed)
        {
            _status.Foreground =
                LumineDesign.Warning;
            _status.Text =
                "ライブラリの状態を変更できませんでした。";
        }

        StartNavigationRefresh();
    }

    private async Task RemoveLibraryAsync(
        LibraryCatalogItem library)
    {
        await _navigationInitialization;

        if (_runtime?.Library.Id == library.Id)
        {
            await CloseActiveLibraryAsync(
                "ライブラリの登録を解除しました。");
        }

        var removed =
            await _navigationLibraryService
                .RemoveLibraryRegistrationAsync(
                    library.Id);

        _status.Foreground =
            removed
                ? LumineDesign.MutedForeground
                : LumineDesign.Warning;
        _status.Text =
            removed
                ? "Lumineの登録情報を削除しました。元画像は変更していません。"
                : "ライブラリの登録を解除できませんでした。";

        StartNavigationRefresh();
    }

    private async Task CloseActiveLibraryAsync(
        string status)
    {
        await DisposeCurrentRuntimeAsync();

        _browseControls?.DisposeTransientWork();
        _browseControls = null;
        _browseHost.Content = null;
        _browseHost.IsVisible = false;
        _browseFilterState =
            new BrowseFilterState(
                SortOrder:
                    _browsePreferences.SortOrder);
        _libraryPath.Text = string.Empty;
        _productShellState = "Welcome";
        _status.Foreground =
            LumineDesign.MutedForeground;
        _status.Text = status;
        _viewerHost.Content =
            CreateWelcomeState(recovered: false);
        _folders = Array.Empty<LibraryFolderInfo>();
        _tags = Array.Empty<LibraryTagInfo>();
        _publications = Array.Empty<PublicationInfo>();
        _browseFacets =
            new LibraryBrowseFacets(
                Array.Empty<string>(),
                Array.Empty<string>());
        RenderNavigationDestination();
    }

    private Task ApplyFolderScopeAsync(
        string? folderPath)
    {
        if (_browseControls is not null)
        {
            return _browseControls.SetFolderScopeAsync(
                folderPath);
        }

        _browseFilterState =
            _browseFilterState with
            {
                FolderPath =
                    string.IsNullOrWhiteSpace(folderPath)
                        ? null
                        : folderPath
            };
        return ApplyBrowseQueryAsync();
    }

    private Task ApplyTagScopeAsync(
        string? tag)
    {
        if (_browseControls is not null)
        {
            return _browseControls.SetTagScopeAsync(
                tag);
        }

        _browseFilterState =
            _browseFilterState with
            {
                Tag =
                    string.IsNullOrWhiteSpace(tag)
                        ? null
                        : tag
            };
        return ApplyBrowseQueryAsync();
    }

    private async Task OnBrowseFiltersChangedAsync(
        BrowseFilterState state)
    {
        _browseFilterState =
            state
            ?? throw new ArgumentNullException(nameof(state));

        await ApplyBrowseQueryAsync();
        RenderNavigationDestination();
    }

    private async Task OnBrowsePreferencesChangedAsync(
        BrowsePreferences preferences)
    {
        _browsePreferences =
            preferences
            ?? throw new ArgumentNullException(nameof(preferences));

        _shell?.SetBrowseLayout(
            _browsePreferences);

        if (_host is not null)
        {
            try
            {
                await _host.SaveBrowsePreferencesAsync(
                    _browsePreferences);
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException)
            {
                _host.Log.Write(
                    "settings",
                    $"Browse preference save failed: {exception.Message}");
                _status.Foreground =
                    LumineDesign.Warning;
                _status.Text =
                    "表示設定を保存できませんでした。現在の表示には反映されています。";
            }
        }
    }

    private async Task SaveViewerDefaultsFromSettingsAsync(
        BrowsePreferences preferences)
    {
        await OnBrowsePreferencesChangedAsync(
            preferences);

        _browseFilterState =
            _browseFilterState with
            {
                SortOrder =
                    preferences.SortOrder
            };

        if (_runtime is not null)
        {
            await ApplyBrowseQueryAsync();
            EnsureBrowseControls();
        }

        StartNavigationRefresh();
    }

    private async Task SaveThumbnailModeFromSettingsAsync(
        ThumbnailStorageMode mode)
    {
        if (_host is null)
        {
            throw new InvalidOperationException(
                "設定保存を利用できません。");
        }

        if (mode == ThumbnailStorageMode.PersistentDisk
            && _settingsSnapshot.PersistedThumbnailStorageMode
                != ThumbnailStorageMode.PersistentDisk)
        {
            var confirmed =
                await ProductDialogs.ConfirmAsync(
                    this,
                    "永続サムネイルcacheを有効にしますか？",
                    "表示用サムネイルをLumineのデータフォルダーへ保存します。初回表示後の再利用は速くなりますが、ディスク使用量が増えます。",
                    "元画像やユーザーメタデータはcacheとは別に管理されます。cacheはいつでも安全に削除できます。",
                    confirmLabel: "有効にする");
            if (!confirmed)
            {
                StartNavigationRefresh();
                return;
            }
        }

        await _host.SaveThumbnailStorageModeAsync(
            mode);
        StartNavigationRefresh();
    }

    private async Task SaveMemoryBudgetFromSettingsAsync(
        long bytes)
    {
        if (_host is null)
        {
            throw new InvalidOperationException(
                "設定保存を利用できません。");
        }

        var current =
            _host.Settings.ResourcePolicy
            ?? new ResourcePolicySettings();
        var next =
            current with
            {
                EncodedThumbnailMemoryByteLimit =
                    bytes
            };

        await _host.SaveSettingsAsync(
            next);
        StartNavigationRefresh();
    }

    private async Task ClearThumbnailCacheFromSettingsAsync()
    {
        var confirmed =
            await ProductDialogs.ConfirmAsync(
                this,
                "表示用cacheを削除しますか？",
                "Lumineが生成した表示用サムネイルだけを削除します。",
                "元画像、ライブラリ登録、評価、お気に入り、タグ、ノート、Work、Generation Group、Lineage、Publicationは削除しません。",
                confirmLabel: "cacheを削除");
        if (!confirmed)
        {
            return;
        }

        ThumbnailPruneResult result;
        if (_runtime is not null)
        {
            result =
                await _runtime.ThumbnailCache.PruneAsync(
                    0);
        }
        else
        {
            var cache =
                new ThumbnailCache(
                    _defaultDataPaths.ThumbnailCachePath,
                    _resourcePolicy);
            result =
                await cache.PruneAsync(
                    0);
        }

        _status.Foreground =
            LumineDesign.MutedForeground;
        _status.Text =
            result.FilesDeleted == 0
                ? "削除する表示用cacheはありませんでした。"
                : $"{result.FilesDeleted:N0}ファイル / {FormatBytes(result.BytesDeleted)} の表示用cacheを削除しました。";

        StartNavigationRefresh();
    }

    private async Task<ThumbnailCacheStats>
        GetThumbnailCacheStatsAsync(
            CancellationToken cancellationToken)
    {
        if (_runtime is not null)
        {
            return await _runtime.ThumbnailCache
                .GetStatsAsync(
                    cancellationToken);
        }

        var cache =
            new ThumbnailCache(
                _defaultDataPaths.ThumbnailCachePath,
                _resourcePolicy);
        return await cache.GetStatsAsync(
            cancellationToken);
    }

    private ProductSettingsSnapshot CreateSettingsSnapshot(
        ThumbnailCacheStats cacheStats)
    {
        var persistedMode =
            _host is null
                ? ThumbnailStorageMode.MemoryOnly
                : ThumbnailStoragePreference
                    .ResolvePersisted(
                        _host.Settings,
                        out _);
        var memoryLimit =
            _host?.Settings.ResourcePolicy
                ?.EncodedThumbnailMemoryByteLimit
            ?? _resourcePolicy
                .EncodedThumbnailMemoryByteLimit;

        return new ProductSettingsSnapshot(
            _browsePreferences,
            persistedMode,
            _host?.ThumbnailStorageMode
                ?? _thumbnailStorageMode,
            memoryLimit,
            _defaultDataPaths,
            cacheStats,
            _host?.SettingsWarning,
            !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(
                    ThumbnailStoragePreference
                        .EnvironmentVariable)));
    }

    private AssetQuery? BuildBrowseQuery()
    {
        var state =
            _browseFilterState;

        if (!state.HasFilters
            && state.SortOrder
                == AssetSortOrder.ModifiedNewest)
        {
            return null;
        }

        return new AssetQuery(
            SearchText:
                string.IsNullOrWhiteSpace(
                    state.SearchText)
                    ? null
                    : state.SearchText,
            RequiredTags:
                string.IsNullOrWhiteSpace(
                    state.Tag)
                    ? null
                    : [state.Tag],
            MinRating:
                state.MinRating,
            Favorite:
                state.FavoriteOnly
                    ? true
                    : null,
            StatusLabel:
                state.StatusLabel,
            ColorLabel:
                state.ColorLabel,
            SortOrder:
                state.SortOrder,
            FolderPathPrefix:
                state.FolderPath);
    }

    private CoreViewerShell CreateCoreViewerShell(
        CoreViewerRuntime runtime) =>
        new(
            runtime,
            _browsePreferences,
            RefreshAfterBulkMutationAsync,
            OnBulkEntryRequested);

    private async Task RefreshAfterBulkMutationAsync()
    {
        await ApplyBrowseQueryAsync();
        StartNavigationRefresh();
    }

    private void OnBulkEntryRequested(
        string destination)
    {
        if (!string.Equals(
                destination,
                "publication",
                StringComparison.Ordinal))
        {
            return;
        }

        OnNavigationRequested(
            "公開履歴");
        _status.Foreground =
            LumineDesign.MutedForeground;
        _status.Text =
            "Publicationを公開履歴へ保存しました。";
        StartNavigationRefresh();
    }

    private async Task ApplyBrowseQueryAsync()
    {
        var runtime = _runtime;
        if (runtime is null)
        {
            return;
        }

        var previousShell = _shell;
        if (previousShell is not null)
        {
            _viewerHost.Content = null;
            _shell = null;
            await previousShell.DetachAsync();
        }

        _productShellState = "Loading";
        _viewerHost.Content =
            LumineDesign.CreateProductState(
                "表示を更新しています",
                "検索・並び順・フィルターを反映しています。");

        try
        {
            await runtime.ApplyQueryAsync(
                BuildBrowseQuery());

            var nextShell =
                CreateCoreViewerShell(runtime);
            _shell = nextShell;
            _viewerHost.Content = nextShell;
            _productShellState =
                runtime.AssetCount == 0
                    ? "EmptyLibrary"
                    : "Workspace";
            UpdateScopeDisplay();

            if (runtime.AssetCount > 0)
            {
                nextShell.SelectInitialAsset();
            }
        }
        catch (Exception exception)
        {
            var restored =
                CreateCoreViewerShell(runtime);
            _shell = restored;
            _viewerHost.Content = restored;
            _productShellState =
                runtime.AssetCount == 0
                    ? "EmptyLibrary"
                    : "Workspace";
            _status.Foreground =
                LumineDesign.Warning;
            _status.Text =
                $"検索・フィルターを適用できませんでした: {exception.Message}";

            if (runtime.AssetCount > 0)
            {
                restored.SelectInitialAsset();
            }
        }

        UpdateScopeDisplay();
    }

    private void EnsureBrowseControls()
    {
        _browseControls?.DisposeTransientWork();

        _browseControls =
            new BrowseWorkspaceControls(
                _browseFilterState,
                _browsePreferences,
                _tags,
                _browseFacets,
                OnBrowseFiltersChangedAsync,
                OnBrowsePreferencesChangedAsync);
        _browseHost.Content =
            _browseControls;
        _browseHost.IsVisible = true;
    }

    private void UpdateScopeDisplay()
    {
        var runtime = _runtime;
        if (runtime is null)
        {
            _libraryPath.Text = string.Empty;
            return;
        }

        var scopes =
            new List<string>();
        if (_browseFilterState.FolderPath
            is { } folder)
        {
            scopes.Add(
                $"フォルダー: {folder}");
        }

        if (_browseFilterState.Tag
            is { } tag)
        {
            scopes.Add(
                $"タグ: {tag}");
        }

        _libraryPath.Text =
            scopes.Count == 0
                ? runtime.LibraryRoot
                : runtime.LibraryRoot
                  + "  ·  "
                  + string.Join(
                      "  ·  ",
                      scopes);
        _status.Foreground =
            LumineDesign.MutedForeground;
        _status.Text =
            $"{runtime.AssetCount:N0} 件 · {runtime.Library.Name}";
    }

    private async Task ShowDiagnosticsFromNavigationAsync()
    {
        if (!_runtimeDiagnosticsOperation.IsCompleted)
        {
            return;
        }

        _diagnosticsCancellation?.Dispose();
        _diagnosticsCancellation =
            new CancellationTokenSource();

        var operation =
            ShowRuntimeDiagnosticsAsync(
                _diagnosticsCancellation.Token);
        _runtimeDiagnosticsOperation =
            operation;

        try
        {
            await operation;
        }
        finally
        {
            if (ReferenceEquals(
                    _runtimeDiagnosticsOperation,
                    operation))
            {
                _runtimeDiagnosticsOperation =
                    Task.CompletedTask;
            }

            _diagnosticsCancellation?.Dispose();
            _diagnosticsCancellation = null;

            if (!_closeStarted)
            {
                _diagnostics.IsEnabled = true;
            }
        }
    }

    internal Task OpenLibraryAsync(
        string libraryRoot,
        AppDataPaths? dataPaths = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            libraryRoot);

        var operation = OpenLibraryCoreAsync(
            libraryRoot,
            dataPaths,
            cancellationToken);
        _openOperation = operation;
        return ObserveOpenOperationAsync(operation);
    }

    internal async Task<string> BuildRuntimeDiagnosticsTextAsync(
        CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Lumine v2 runtime diagnostics");
        builder.AppendLine(
            $"Data root: {_defaultDataPaths.RootPath}");
        builder.AppendLine(
            $"Database: {_defaultDataPaths.DatabasePath}");
        builder.AppendLine(
            $"Thumbnail cache: {_defaultDataPaths.ThumbnailCachePath}");
        builder.AppendLine(
            $"Settings: {_defaultDataPaths.SettingsPath}");
        builder.AppendLine(
            $"Runtime log: {_defaultDataPaths.RuntimeLogPath}");
        builder.AppendLine(
            $"Configured thumbnail storage: {_thumbnailStorageMode}");

        if (_host is not null)
        {
            builder.AppendLine(
                $"Previous shutdown: {(_host.PreviousShutdownWasUnclean ? "unclean (recovery path)" : "clean / first launch")}");

            if (!string.IsNullOrWhiteSpace(
                    _host.SettingsWarning))
            {
                builder.AppendLine(
                    $"Settings warning: {_host.SettingsWarning}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Effective bounded resource policy:");

        foreach (var pair in
                 _resourcePolicy
                     .ToDiagnosticMetadata()
                     .OrderBy(
                         static pair => pair.Key,
                         StringComparer.Ordinal))
        {
            builder.AppendLine(
                $"  {pair.Key} = {pair.Value}");
        }

        var runtime = _runtime;
        if (runtime is null)
        {
            builder.AppendLine();
            builder.AppendLine(
                "Active library: none");
            return builder.ToString();
        }

        builder.AppendLine();
        builder.AppendLine(
            $"Active library: {runtime.LibraryRoot}");
        builder.AppendLine(
            $"Assets: {runtime.AssetCount.ToString("N0", CultureInfo.InvariantCulture)}");

        var cacheStats =
            await runtime.ThumbnailCache.GetStatsAsync(
                cancellationToken).ConfigureAwait(true);

        builder.AppendLine(
            $"Thumbnail cache files: {cacheStats.FileCount.ToString("N0", CultureInfo.InvariantCulture)}");
        builder.AppendLine(
            $"Thumbnail cache bytes: {FormatBytes(cacheStats.TotalBytes)}");
        builder.AppendLine(
            $"Interrupted thumbnail writes: {cacheStats.InterruptedWriteCount.ToString("N0", CultureInfo.InvariantCulture)}");

        var memoryCache =
            runtime.ThumbnailMemoryCacheStats;
        builder.AppendLine(
            $"Thumbnail storage mode: {runtime.ThumbnailStorageMode}");
        builder.AppendLine(
            $"Encoded thumbnail memory cache: {memoryCache.EntryCount.ToString("N0", CultureInfo.InvariantCulture)} entries / {FormatBytes(memoryCache.EncodedBytes)} of {FormatBytes(memoryCache.ByteLimit)}; hits={memoryCache.HitCount.ToString("N0", CultureInfo.InvariantCulture)}");

        var maintenance =
            runtime.ThumbnailCacheMaintenanceDiagnostics;

        builder.AppendLine(
            $"Cache maintenance: completed={maintenance.RunsCompleted.ToString("N0", CultureInfo.InvariantCulture)}, cancelled={maintenance.RunsCancelled.ToString("N0", CultureInfo.InvariantCulture)}, failed={maintenance.RunsFailed.ToString("N0", CultureInfo.InvariantCulture)}");
        builder.AppendLine(
            $"Cache maintenance deleted: {maintenance.FilesDeleted.ToString("N0", CultureInfo.InvariantCulture)} files / {FormatBytes(maintenance.BytesDeleted)}");
        builder.AppendLine(
            $"Cache maintenance foreground preemptions: {maintenance.ForegroundPreemptions.ToString("N0", CultureInfo.InvariantCulture)}");

        if (!string.IsNullOrWhiteSpace(
                maintenance.LastError))
        {
            builder.AppendLine(
                $"Cache maintenance last error: {maintenance.LastError}");
        }

        return builder.ToString();
    }

    private async Task ObserveOpenOperationAsync(
        Task operation)
    {
        try
        {
            await operation;
        }
        finally
        {
            if (ReferenceEquals(
                    _openOperation,
                    operation))
            {
                _openOperation =
                    Task.CompletedTask;
            }
        }
    }

    private async Task OpenLibraryCoreAsync(
        string libraryRoot,
        AppDataPaths? dataPaths,
        CancellationToken cancellationToken)
    {
        _openCancellation?.Cancel();
        _openCancellation?.Dispose();
        _openCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        var operationToken =
            _openCancellation.Token;

        await _navigationInitialization
            .WaitAsync(operationToken);

        _browseFilterState =
            new BrowseFilterState(
                SortOrder:
                    _browsePreferences.SortOrder);
        _browseControls?.DisposeTransientWork();
        _browseControls = null;
        _browseHost.Content = null;
        _browseHost.IsVisible = false;
        _openFolder.IsEnabled = false;
        _libraryPath.Text =
            Path.GetFullPath(libraryRoot);
        _productShellState = "Loading";
        _status.Foreground =
            LumineDesign.MutedForeground;
        _status.Text =
            "ライブラリを準備しています…";

        await DisposeCurrentRuntimeAsync()
            .ConfigureAwait(true);

        _viewerHost.Content =
            LumineDesign.CreateProductState(
                "ライブラリを開いています",
                "画像を確認し、表示の準備をしています。");

        var progress =
            new Progress<CoreViewerOpenProgress>(
                _ => _status.Text =
                    "ライブラリを準備しています…");

        CoreViewerRuntime? runtime = null;

        try
        {
            runtime = await CoreViewerRuntime.OpenAsync(
                libraryRoot,
                dataPaths ?? _defaultDataPaths,
                _resourcePolicy,
                progress,
                operationToken,
                _thumbnailStorageMode,
                BuildBrowseQuery());

            operationToken.ThrowIfCancellationRequested();

            var shell =
                CreateCoreViewerShell(runtime);

            _runtime = runtime;
            _shell = shell;
            runtime = null;

            _viewerHost.Content = shell;
            _productShellState =
                _runtime.AssetCount == 0
                    ? "EmptyLibrary"
                    : "Workspace";
            EnsureBrowseControls();
            UpdateScopeDisplay();
            StartNavigationRefresh();

            _host?.Log.Write(
                "library",
                $"Opened {_runtime.LibraryRoot} with {_runtime.AssetCount:N0} assets.");

            shell.SelectInitialAsset();
        }
        catch (OperationCanceledException)
            when (operationToken.IsCancellationRequested)
        {
            if (!_closeStarted)
            {
                _productShellState = "Welcome";
                _status.Text =
                    "ライブラリの読み込みをキャンセルしました。";
                _viewerHost.Content =
                    CreateWelcomeState(recovered: false);
            }
        }
        catch (Exception exception)
        {
            _host?.Log.Write(
                "library",
                $"Open failed: {exception.Message}");

            _productShellState = "Error";
            _status.Foreground =
                LumineDesign.Danger;
            _status.Text =
                "ライブラリを開けませんでした。";
            _libraryPath.Text = string.Empty;
            _viewerHost.Content =
                LumineDesign.CreateProductState(
                    "ライブラリを開けませんでした",
                    "元画像は変更していません。フォルダーの状態を確認して、もう一度追加してください。\n\n"
                    + exception.Message);
        }
        finally
        {
            if (runtime is not null)
            {
                await runtime.DisposeAsync();
            }

            if (!_closeStarted)
            {
                _openFolder.IsEnabled = true;
            }
        }
    }

    private async void OnOpenFolderClicked(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ChooseAndOpenLibraryAsync();
    }

    private async void OnDiagnosticsClicked(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_runtimeDiagnosticsOperation.IsCompleted)
        {
            return;
        }

        _diagnosticsCancellation?.Dispose();
        _diagnosticsCancellation =
            new CancellationTokenSource();

        var operation =
            ShowRuntimeDiagnosticsAsync(
                _diagnosticsCancellation.Token);
        _runtimeDiagnosticsOperation =
            operation;

        try
        {
            await operation;
        }
        catch (OperationCanceledException)
            when (_diagnosticsCancellation?.IsCancellationRequested == true)
        {
        }
        catch (Exception exception)
        {
            _status.Text =
                $"診断情報を読み込めませんでした: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(
                    _runtimeDiagnosticsOperation,
                    operation))
            {
                _runtimeDiagnosticsOperation =
                    Task.CompletedTask;
            }

            _diagnosticsCancellation?.Dispose();
            _diagnosticsCancellation = null;

            if (!_closeStarted)
            {
                _diagnostics.IsEnabled = true;
            }
        }
    }

    private async Task ShowRuntimeDiagnosticsAsync(
        CancellationToken cancellationToken)
    {
        _diagnostics.IsEnabled = false;

        var diagnostics =
            await BuildRuntimeDiagnosticsTextAsync(
                cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var text = new TextBox
        {
            Text = diagnostics,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(16)
        };

        var dialog = new Window
        {
            Title = "Lumine 診断情報",
            Icon = LumineDesign.CreateWindowIcon(),
            Background = LumineDesign.Background,
            Foreground = LumineDesign.Foreground,
            FontFamily = LumineDesign.UiFont,
            Width = 820,
            Height = 620,
            MinWidth = 640,
            MinHeight = 420,
            Content = text
        };

        _diagnosticsWindow = dialog;

        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            if (ReferenceEquals(
                    _diagnosticsWindow,
                    dialog))
            {
                _diagnosticsWindow = null;
            }
        }
    }

    private void OnOpened(
        object? sender,
        EventArgs e)
    {
        Program.Diagnostics.MarkWindowReady();
        Program.Acceptance?.MarkWindowReady();
        _diagnosticFlushOperation =
            Program.Diagnostics.FlushRequestedAsync(
                _resourcePolicy.ToDiagnosticMetadata());
        StartNavigationRefresh();
    }

    private async void OnClosing(
        object? sender,
        WindowClosingEventArgs e)
    {
        if (_closeCompleted)
        {
            return;
        }

        e.Cancel = true;

        if (_closeStarted)
        {
            return;
        }

        _closeStarted = true;
        _openFolder.IsEnabled = false;
        _diagnostics.IsEnabled = false;
        _status.Text = "Lumine を終了しています…";

        _browseControls?.DisposeTransientWork();
        _openCancellation?.Cancel();
        _diagnosticsCancellation?.Cancel();
        _navigationCancellation?.Cancel();
        _diagnosticsWindow?.Close();

        Exception? shutdownFailure = null;

        try
        {
            try
            {
                await _runtimeDiagnosticsOperation;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _host?.Log.Write(
                    "diagnostics",
                    $"Runtime diagnostics close-drain failed: {exception.Message}");
            }

            try
            {
                await _openOperation;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                shutdownFailure = exception;
            }

            try
            {
                await _navigationOperation;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _host?.Log.Write(
                    "navigation",
                    $"Navigation shutdown drain failed: {exception.Message}");
            }

            try
            {
                await DisposeCurrentRuntimeAsync();
            }
            catch (Exception exception)
            {
                shutdownFailure ??= exception;
            }

            try
            {
                await _diagnosticFlushOperation;
            }
            catch (Exception exception)
            {
                _host?.Log.Write(
                    "diagnostics",
                    $"Window-ready diagnostics flush failed: {exception.Message}");
            }

            try
            {
                await Program.Diagnostics.FlushRequestedAsync(
                    _resourcePolicy.ToDiagnosticMetadata());
            }
            catch (Exception exception)
            {
                _host?.Log.Write(
                    "diagnostics",
                    $"Shutdown diagnostics flush failed: {exception.Message}");
            }

            if (shutdownFailure is null)
            {
                _host?.Log.Write(
                    "window",
                    "MainWindow ownership graph drained.");
                Program.MarkWindowShutdownDrained();
            }
            else
            {
                _host?.Log.Write(
                    "window",
                    $"Shutdown error: {shutdownFailure.Message}");
                _status.Text =
                    $"Shutdown error: {shutdownFailure.Message}";
            }
        }
        finally
        {
            _openCancellation?.Dispose();
            _openCancellation = null;
            _diagnosticsCancellation?.Dispose();
            _diagnosticsCancellation = null;
            _navigationCancellation?.Dispose();
            _navigationCancellation = null;
            _diagnosticsWindow = null;
            _closeCompleted = true;
            Close();
        }
    }

    private static string FormatBytes(
        long bytes)
    {
        const double kib = 1024;
        const double mib = kib * 1024;
        const double gib = mib * 1024;

        return bytes switch
        {
            >= (long)gib =>
                $"{bytes / gib:F2} GiB",
            >= (long)mib =>
                $"{bytes / mib:F2} MiB",
            >= (long)kib =>
                $"{bytes / kib:F2} KiB",
            _ => $"{bytes} B"
        };
    }

    private Control CreateWelcomeState(
        bool recovered)
    {
        var action =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "画像フォルダーを追加"
                });
        action.Click += OnOpenFolderClicked;

        var description =
            recovered
                ? "前回の終了状態から復旧しました。画像そのものには変更を加えず、ライブラリを開くまで待機しています。"
                : "最初に画像フォルダーを追加してください。画像そのものをコピーせず、通常閲覧では表示用サムネイルもディスクへ保存しません。";

        return LumineDesign.CreateProductState(
            "Lumine",
            description,
            action,
            showBrand: true);
    }

    private async Task DisposeCurrentRuntimeAsync()
    {
        var shell = _shell;
        var runtime = _runtime;

        _shell = null;
        _runtime = null;

        if (shell is not null)
        {
            _viewerHost.Content = null;
            await shell.DetachAsync()
                .ConfigureAwait(true);
        }

        if (runtime is not null)
        {
            await runtime.DisposeAsync();
        }
    }
}
