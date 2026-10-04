using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
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
    private readonly Border _statusSurface;
    private readonly ContentControl _viewerHost;
    private readonly ContentControl _browseHost;
    private readonly Grid _workspaceContent;
    private readonly Grid _workspaceHost;
    private readonly ContentControl _workspacePageHost;
    private readonly ContentControl _lightboxHost;
    private readonly Grid _appShell;
    private readonly LibraryService _navigationLibraryService;
    private readonly Task _navigationInitialization;
    private readonly ContentControl _navigationRailHost;
    private readonly Border _navigationPane;
    private readonly Button _navigationPin;
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
    private WindowState? _lightboxPreviousWindowState;
    private bool _lightboxFullScreen;
    private bool _compactNavigationLayout;
    private bool _navigationPinned;
    private CoreViewerRuntime? _runtime;
    private CoreViewerShell? _shell;
    private bool _closeStarted;
    private bool _closeCompleted;
    private string _productShellState = "Welcome";
    private string _navigationDestination = "ライブラリ";
    private string? _failedLibraryRoot;
    private AppDataPaths? _failedLibraryDataPaths;
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
    private readonly HashSet<string> _expandedFolderPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<LibraryTagInfo> _tags =
        Array.Empty<LibraryTagInfo>();
    private IReadOnlyList<PublicationInfo> _publications =
        Array.Empty<PublicationInfo>();
    private PublicationCursor? _publicationNextCursor;
    private long _publicationTotalCount;
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

        _status = new TextBlock
        {
            Text = recovered
                ? "前回の終了を検出しました。安全な状態から復旧しています。"
                : string.Empty,
            FontSize = LumineDesign.CaptionFontSize,
            Foreground = recovered
                ? LumineDesign.Warning
                : LumineDesign.MutedForeground,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        _statusSurface =
            new Border
            {
                Background =
                    LumineDesign.SurfaceRaised,
                BorderBrush =
                    LumineDesign.BorderStrong,
                BorderThickness =
                    new Thickness(1),
                CornerRadius =
                    new CornerRadius(
                        LumineDesign.PanelRadius),
                Padding =
                    new Thickness(
                        LumineDesign.Space12,
                        LumineDesign.Space6),
                Margin =
                    new Thickness(
                        LumineDesign.Space12),
                MaxWidth = 720,
                HorizontalAlignment =
                    HorizontalAlignment.Center,
                VerticalAlignment =
                    VerticalAlignment.Top,
                IsHitTestVisible = false,
                IsVisible =
                    !string.IsNullOrWhiteSpace(
                        _status.Text),
                Child = _status
            };
        _status.PropertyChanged +=
            (_, args) =>
            {
                if (args.Property
                    == TextBlock.TextProperty)
                {
                    _statusSurface.IsVisible =
                        !string.IsNullOrWhiteSpace(
                            _status.Text);
                }
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

        _workspaceContent =
            new Grid
            {
                Background =
                    LumineDesign.Background,
                RowDefinitions =
                    new RowDefinitions("Auto,*")
            };
        _workspaceContent.Children.Add(
            _browseHost);
        Grid.SetRow(_viewerHost, 1);
        _workspaceContent.Children.Add(
            _viewerHost);

        _workspacePageHost =
            new ContentControl
            {
                IsVisible = false,
                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,
                VerticalContentAlignment =
                    VerticalAlignment.Stretch
            };

        _workspaceHost =
            new Grid
            {
                Background =
                    LumineDesign.Background
            };
        _workspaceHost.Children.Add(
            _workspaceContent);
        _workspaceHost.Children.Add(
            _workspacePageHost);
        _workspaceHost.Children.Add(
            _statusSurface);

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
                FontSize = LumineDesign.BodyFontSize,
                FontWeight = FontWeight.Bold,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        _navigationPin =
            LumineDesign.ConfigureIconButton(
                new Button
                {
                    Content =
                        LumineDesign.CreateStrokeIcon(
                            LumineDesign.PinIconPath,
                            16)
                },
                "サイドに固定");

        var collapseNavigation =
            LumineDesign.ConfigureIconButton(
                new Button
                {
                    Content =
                        LumineDesign.CreateStrokeIcon(
                            LumineDesign.CloseIconPath,
                            15)
                },
                "ナビゲーションを閉じる");

        var navigationHeader =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto,Auto"),
                ColumnSpacing =
                    LumineDesign.Space4,
                Margin = new Thickness(14, 12, 10, 8)
            };
        navigationHeader.Children.Add(
            _navigationTitle);
        Grid.SetColumn(_navigationPin, 1);
        navigationHeader.Children.Add(
            _navigationPin);
        Grid.SetColumn(collapseNavigation, 2);
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
                MinWidth = 250,
                MaxWidth = 320,
                Background = LumineDesign.SurfaceRaised,
                BorderBrush = LumineDesign.BorderStrong,
                BorderThickness =
                    new Thickness(1),
                CornerRadius =
                    new CornerRadius(
                        LumineDesign.PanelRadius),
                ClipToBounds = true,
                Child = navigationLayout
            };
        collapseNavigation.Click +=
            (_, _) =>
            {
                _navigationPane.IsVisible = false;
                ApplyNavigationLayout(
                    ResolveLayoutWidth());
            };
        _navigationPin.Click +=
            (_, _) =>
            {
                _navigationPinned =
                    !_navigationPinned;
                UpdateNavigationPinVisual();
                ApplyNavigationLayout(
                    ResolveLayoutWidth());
            };

        _appShell = new Grid
        {
            Background = LumineDesign.Background,
            ColumnDefinitions =
                new ColumnDefinitions(
                    $"{LumineDesign.NavigationWidth},*")
        };
        _appShell.Children.Add(
            _navigationRailHost);
        Grid.SetColumn(_workspaceHost, 1);
        _appShell.Children.Add(
            _workspaceHost);
        Grid.SetColumn(_navigationPane, 1);
        _navigationPane.ZIndex = 20;
        _appShell.Children.Add(
            _navigationPane);

        SizeChanged +=
            (_, e) =>
                ApplyNavigationLayout(
                    e.NewSize.Width);
        UpdateNavigationPinVisual();
        ApplyNavigationLayout(Width);

        _lightboxHost =
            new ContentControl
            {
                IsVisible = false,
                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,
                VerticalContentAlignment =
                    VerticalAlignment.Stretch
            };

        var rootLayer =
            new Grid
            {
                Background = LumineDesign.Background
            };
        rootLayer.Children.Add(_appShell);
        rootLayer.Children.Add(_lightboxHost);

        Content = rootLayer;

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

    internal ProductSettingsSnapshot SettingsSnapshot =>
        _settingsSnapshot;

    internal Control? NavigationContentForSmoke =>
        _navigationContent.Content
            as Control;

    internal Control? WorkspacePageForSmoke =>
        _workspacePageHost.Content
            as Control;

    internal bool IsWorkspacePageVisibleForSmoke =>
        _workspacePageHost.IsVisible;

    internal Rect WorkspacePageBoundsForSmoke =>
        _workspacePageHost.Bounds;

    internal BrowseWorkspaceControls? BrowseControlsForSmoke =>
        _browseControls;

    internal string StatusTextForSmoke =>
        _status.Text
        ?? string.Empty;

    internal bool HasRetryableOpenFailureForSmoke =>
        !string.IsNullOrWhiteSpace(
            _failedLibraryRoot);

    internal bool OpenFolderCommandEnabledForSmoke =>
        _openFolder.IsEnabled;

    internal Task RetryFailedLibraryForSmokeAsync() =>
        RetryFailedLibraryAsync();

    internal Task ApplyBrowseFilterForSmokeAsync(
        BrowseFilterState state) =>
        OnBrowseFiltersChangedAsync(
            state);

    internal void PresentLoadingStateForSmoke(
        string progressText =
            "画像一覧を準備しています…")
    {
        _productShellState =
            "Loading";
        _status.Text =
            string.Empty;
        _viewerHost.Content =
            CreateLoadingLibraryState(
                out var progress);
        progress.Text =
            progressText;
    }

    internal void PresentWelcomeStateForSmoke()
    {
        _productShellState =
            "Welcome";
        _status.Text =
            string.Empty;
        _viewerHost.Content =
            CreateWelcomeState(
                recovered: false);
    }

    internal void NavigateForSmoke(
        string destination) =>
        OnNavigationRequested(
            destination);

    internal static IReadOnlyList<string> ProductNavigationLabels =>
        LumineDesign.NavigationLabels;

    internal void ShowLightbox(
        Control content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // MainWindow owns only modal presentation. Keep the browse visual
        // tree enabled so its virtualized rows remain realized while the
        // lightbox is open. The full-client lightbox blocks pointer input and
        // owns keyboard focus; hit testing is disabled on the background as
        // an explicit modal boundary without tearing down virtualization.
        _appShell.IsHitTestVisible = false;
        _lightboxHost.Content = content;
        _lightboxHost.IsVisible = true;
    }

    internal void HideLightbox(
        Control? content = null)
    {
        if (content is not null
            && !ReferenceEquals(
                _lightboxHost.Content,
                content))
        {
            return;
        }

        ExitLightboxFullScreen();
        _lightboxHost.Content = null;
        _lightboxHost.IsVisible = false;
        _appShell.IsHitTestVisible = true;
    }

    internal void ToggleLightboxFullScreen()
    {
        if (!_lightboxHost.IsVisible)
        {
            return;
        }

        if (_lightboxFullScreen)
        {
            ExitLightboxFullScreen();
            return;
        }

        _lightboxPreviousWindowState =
            WindowState;
        WindowState =
            WindowState.FullScreen;
        _lightboxFullScreen = true;
    }

    private void ExitLightboxFullScreen()
    {
        if (!_lightboxFullScreen)
        {
            return;
        }

        var restore =
            _lightboxPreviousWindowState
            ?? WindowState.Normal;
        _lightboxPreviousWindowState = null;
        _lightboxFullScreen = false;
        WindowState =
            restore == WindowState.FullScreen
                ? WindowState.Normal
                : restore;
    }

    internal bool IsLightboxFullScreen =>
        _lightboxFullScreen;

    internal bool IsLightboxVisible =>
        _lightboxHost.IsVisible;

    internal bool IsFocusInsideLightboxForSmoke
    {
        get
        {
            var focused =
                FocusManager?.GetFocusedElement()
                as Visual;
            return focused is not null
                && (ReferenceEquals(
                        focused,
                        _lightboxHost)
                    || focused.GetVisualAncestors()
                        .Any(
                            ancestor =>
                                ReferenceEquals(
                                    ancestor,
                                    _lightboxHost)));
        }
    }


    internal bool IsCompactNavigationLayout =>
        _compactNavigationLayout;

    internal bool IsNavigationPaneVisibleForSmoke =>
        _navigationPane.IsVisible;

    internal bool IsNavigationPinnedForSmoke =>
        _navigationPinned;

    internal void SetNavigationPaneVisibleForSmoke(
        bool visible)
    {
        _navigationPane.IsVisible = visible;
        ApplyNavigationLayout(
            ResolveLayoutWidth());
    }

    internal void SetNavigationPinnedForSmoke(
        bool pinned)
    {
        _navigationPinned = pinned;
        UpdateNavigationPinVisual();
        ApplyNavigationLayout(
            ResolveLayoutWidth());
    }

    internal Rect NavigationPaneBounds =>
        _navigationPane.Bounds;

    internal bool IsNavigationPaneOverlayForSmoke =>
        _navigationPane.IsVisible
        && _navigationPane.ZIndex > 0
        && Grid.GetColumn(
            _workspaceHost)
            == 1;

    internal bool NavigationPinVisibleForSmoke =>
        _navigationPin.IsVisible;

    internal Rect LightboxBounds =>
        _lightboxHost.Bounds;

    internal bool IsWorkspaceInteractionEnabled =>
        _appShell.IsHitTestVisible
        && !_lightboxHost.IsVisible;

    private double ResolveLayoutWidth() =>
        ClientSize.Width > 0
            ? ClientSize.Width
            : Width;

    private void ApplyNavigationLayout(
        double width)
    {
        // Keep contextual navigation off the permanent canvas until there is
        // enough desktop width for an explicit pinned layout.
        _compactNavigationLayout =
            width < 1200;

        var paneCanDock =
            !_compactNavigationLayout
            && _navigationPinned
            && _navigationPane.IsVisible
            && !IsMainWorkspaceDestination(
                _navigationDestination);

        _navigationPin.IsEnabled =
            !_compactNavigationLayout;
        _navigationPin.IsVisible =
            !_compactNavigationLayout;

        if (paneCanDock)
        {
            _appShell.ColumnDefinitions =
                new ColumnDefinitions(
                    $"{LumineDesign.NavigationWidth},Auto,*");
            Grid.SetColumn(
                _navigationPane,
                1);
            Grid.SetColumn(
                _workspaceHost,
                2);
            _navigationPane.HorizontalAlignment =
                HorizontalAlignment.Stretch;
            _navigationPane.VerticalAlignment =
                VerticalAlignment.Stretch;
            _navigationPane.Margin =
                new Thickness(0);
            _navigationPane.Width = 280;
            _navigationPane.CornerRadius =
                new CornerRadius(0);
            _navigationPane.BorderBrush =
                LumineDesign.Border;
            _navigationPane.BorderThickness =
                new Thickness(0, 0, 1, 0);
            _navigationPane.ZIndex = 0;
            return;
        }

        _appShell.ColumnDefinitions =
            new ColumnDefinitions(
                $"{LumineDesign.NavigationWidth},*");
        Grid.SetColumn(
            _workspaceHost,
            1);
        Grid.SetColumn(
            _navigationPane,
            1);
        _navigationPane.HorizontalAlignment =
            HorizontalAlignment.Left;
        _navigationPane.VerticalAlignment =
            VerticalAlignment.Stretch;
        _navigationPane.Margin =
            new Thickness(
                LumineDesign.Space12);
        _navigationPane.Width =
            _compactNavigationLayout
                ? Math.Clamp(
                    width
                    - LumineDesign.NavigationWidth
                    - 64,
                    250,
                    300)
                : 280;
        _navigationPane.CornerRadius =
            new CornerRadius(
                LumineDesign.PanelRadius);
        _navigationPane.BorderBrush =
            LumineDesign.BorderStrong;
        _navigationPane.BorderThickness =
            new Thickness(1);
        _navigationPane.ZIndex = 20;
    }

    private void UpdateNavigationPinVisual()
    {
        _navigationPin.Background =
            _navigationPinned
                ? LumineDesign.AccentMuted
                : Brushes.Transparent;
        _navigationPin.BorderBrush =
            _navigationPinned
                ? LumineDesign.BorderStrong
                : LumineDesign.Border;
        ToolTip.SetTip(
            _navigationPin,
            _navigationPinned
                ? "固定を解除"
                : "サイドに固定");
        AutomationProperties.SetName(
            _navigationPin,
            _navigationPinned
                ? "ナビゲーションの固定を解除"
                : "ナビゲーションをサイドに固定");
    }

    private void OnNavigationRequested(
        string destination)
    {
        if (_closeStarted)
        {
            return;
        }

        _navigationDestination = destination;
        _navigationPane.IsVisible =
            !IsMainWorkspaceDestination(
                destination);
        _navigationTitle.Text = destination;
        ApplyNavigationLayout(
            ResolveLayoutWidth());
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
        PublicationPage? publicationPage =
            null;
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
                    limit: LibraryRepository.MaxTagListLimit,
                    cancellationToken: cancellationToken);
            facets =
                await _navigationLibraryService.GetBrowseFacetsAsync(
                    runtime.Library.Id,
                    cancellationToken);
            if (string.Equals(
                    _navigationDestination,
                    "公開履歴",
                    StringComparison.Ordinal))
            {
                publicationPage =
                    await _navigationLibraryService
                        .ListPublicationsPageAsync(
                            runtime.Library.Id,
                            LibraryRepository.PublicationPageSize,
                            cursor: null,
                            cancellationToken:
                                cancellationToken);
            }
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
        _publications =
            publicationPage?.Items
            ?? Array.Empty<PublicationInfo>();
        _publicationNextCursor =
            publicationPage?.NextCursor;
        _publicationTotalCount =
            publicationPage?.TotalCount
            ?? 0;
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

        if (IsMainWorkspaceDestination(
                _navigationDestination))
        {
            _navigationContent.Content = null;
            _workspacePageHost.Content =
                ProductSettingsView.Create(
                    _settingsSnapshot,
                    SaveViewerDefaultsFromSettingsAsync,
                    SaveThumbnailModeFromSettingsAsync,
                    SaveDiskCacheBudgetFromSettingsAsync,
                    SaveMemoryBudgetFromSettingsAsync,
                    ClearThumbnailCacheFromSettingsAsync,
                    ShowDiagnosticsFromNavigationAsync);
            _workspacePageHost.IsVisible = true;
            _workspaceContent.IsHitTestVisible = false;
            return;
        }

        _workspacePageHost.Content = null;
        _workspacePageHost.IsVisible = false;
        _workspaceContent.IsHitTestVisible = true;

        _navigationContent.Content =
            _navigationDestination switch
            {
                "ライブラリ" =>
                    ProductNavigationViews.CreateLibraries(
                        _libraries,
                        _runtime?.Library.Id,
                        ChooseAndOpenLibraryAsync,
                        OpenCatalogLibraryAsync,
                        RescanLibraryAsync,
                        ToggleLibraryEnabledAsync,
                        RemoveLibraryAsync,
                        ReportNavigationError),
                "フォルダー" =>
                    _runtime is null
                        ? ProductNavigationViews.CreateNoLibrary(
                            "フォルダー")
                        : ProductNavigationViews.CreateFolders(
                            _folders,
                            _browseFilterState.FolderPath,
                            _expandedFolderPaths,
                            ApplyFolderScopeAsync,
                            ReportNavigationError),
                "タグ" =>
                    _runtime is null
                        ? ProductNavigationViews.CreateNoLibrary(
                            "タグ")
                        : ProductNavigationViews.CreateTags(
                            _tags,
                            _browseFilterState.TagNames,
                            ApplyTagScopeAsync,
                            CreateTagFromNavigationAsync,
                            UpdateTagFromNavigationAsync,
                            DeleteTagFromNavigationAsync,
                            ReportNavigationError),
                "公開履歴" =>
                    _runtime is null
                        ? ProductNavigationViews.CreateNoLibrary(
                            "公開履歴")
                        : ProductNavigationViews.CreatePublicationEntry(
                            _publications,
                            _publicationTotalCount,
                            _publicationNextCursor is not null,
                            LoadMorePublicationHistoryAsync,
                            ReportNavigationError),
                _ =>
                    ProductNavigationViews.CreateNoLibrary(
                        _navigationDestination)
            };
    }

    private async Task<PublicationPage>
        LoadMorePublicationHistoryAsync()
    {
        var runtime =
            _runtime
            ?? throw new InvalidOperationException(
                "公開履歴を読み込むにはライブラリを開いてください。");
        var cursor =
            _publicationNextCursor;
        if (cursor is null)
        {
            return new PublicationPage(
                Array.Empty<PublicationInfo>(),
                null,
                _publicationTotalCount);
        }

        var page =
            await _navigationLibraryService
                .ListPublicationsPageAsync(
                    runtime.Library.Id,
                    LibraryRepository.PublicationPageSize,
                    cursor);

        if (!ReferenceEquals(
                _runtime,
                runtime))
        {
            return new PublicationPage(
                Array.Empty<PublicationInfo>(),
                null,
                0);
        }

        var knownIds =
            new HashSet<long>(
                _publications.Select(
                    static item =>
                        item.Id));
        _publications =
            _publications
                .Concat(
                    page.Items.Where(
                        item =>
                            knownIds.Add(
                                item.Id)))
                .ToArray();
        _publicationNextCursor =
            page.NextCursor;
        _publicationTotalCount =
            page.TotalCount;
        return page;
    }

    private static bool IsMainWorkspaceDestination(
        string destination) =>
        string.Equals(
            destination,
            "設定",
            StringComparison.Ordinal);

    private void ReportNavigationError(
        string message)
    {
        _status.Foreground =
            LumineDesign.Warning;
        _status.Text = message;
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

    private async Task RescanLibraryAsync(
        LibraryCatalogItem library)
    {
        var runtime = _runtime;
        if (runtime is null
            || runtime.Library.Id != library.Id)
        {
            _status.Foreground =
                LumineDesign.Warning;
            _status.Text =
                "表示中のライブラリだけ再スキャンできます。";
            return;
        }

        var runtimeBefore = runtime;
        var acceptProgress = true;
        _status.Foreground =
            LumineDesign.MutedForeground;
        _status.Text =
            "ライブラリを再スキャンしています…";

        var progress =
            new Progress<LibraryScanProgress>(
                scan =>
                {
                    if (!acceptProgress
                        || !ReferenceEquals(
                            _runtime,
                            runtimeBefore))
                    {
                        return;
                    }

                    _status.Text =
                        FormatLibraryScanProgress(
                            "再スキャン中",
                            scan);
                });

        try
        {
            var result =
                await runtime.SyncSession
                    .ReconcileNowAsync(
                        progress: progress);
            acceptProgress = false;

            if (!ReferenceEquals(
                    _runtime,
                    runtimeBefore))
            {
                return;
            }

            await ApplyBrowseQueryAsync();
            StartNavigationRefresh();

            _status.Foreground =
                LumineDesign.MutedForeground;
            var completionStatus =
                $"再スキャン完了 · {result.Discovered:N0}件確認";
            _status.Text =
                completionStatus;
            _ = ClearTransientStatusAsync(
                completionStatus);
        }
        catch (Exception exception)
        {
            acceptProgress = false;
            if (ReferenceEquals(
                    _runtime,
                    runtimeBefore))
            {
                _status.Foreground =
                    LumineDesign.Warning;
                _status.Text =
                    $"再スキャンできませんでした: {exception.Message}";
            }

            throw;
        }
    }

    private async Task ClearTransientStatusAsync(
        string expectedText)
    {
        await Task.Delay(
                TimeSpan.FromMilliseconds(1200))
            .ConfigureAwait(false);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                if (string.Equals(
                        _status.Text,
                        expectedText,
                        StringComparison.Ordinal))
                {
                    _status.Text =
                        string.Empty;
                }
            });
    }

    internal Task RescanActiveLibraryForSmokeAsync()
    {
        var runtime =
            _runtime
            ?? throw new InvalidOperationException(
                "No active library.");
        var library =
            _libraries.FirstOrDefault(
                item => item.Id == runtime.Library.Id)
            ?? new LibraryCatalogItem(
                runtime.Library.Id,
                runtime.Library.Name,
                runtime.LibraryRoot,
                true,
                LibraryScanState.Complete,
                runtime.AssetCount,
                null,
                null);
        return RescanLibraryAsync(
            library);
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

        var confirmed =
            await ProductDialogs.ConfirmAsync(
                this,
                "ライブラリの登録を解除しますか？",
                $"「{library.Name}」をLumineのライブラリ一覧から外します。",
                "元画像ファイルは削除しません。このライブラリに紐づくLumine側の登録情報は解除されます。",
                confirmLabel: "登録解除");
        if (!confirmed)
        {
            return;
        }

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

        if (removed)
        {
            _ = ClearTransientStatusAsync(
                _status.Text);
        }

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
        _productShellState = "Welcome";
        _status.Foreground =
            LumineDesign.MutedForeground;
        _status.Text = status;
        _viewerHost.Content =
            CreateWelcomeState(recovered: false);
        _folders = Array.Empty<LibraryFolderInfo>();
        _expandedFolderPaths.Clear();
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
            return tag is null
                ? _browseControls.ClearTagScopesAsync()
                : _browseControls.ToggleTagScopeAsync(
                    tag);
        }

        if (tag is null)
        {
            _browseFilterState =
                _browseFilterState with
                {
                    RequiredTags =
                        Array.Empty<string>()
                };
            return ApplyBrowseQueryAsync();
        }

        var tags =
            _browseFilterState.TagNames
                .ToList();
        var existing =
            tags.FindIndex(
                value =>
                    string.Equals(
                        value,
                        tag,
                        StringComparison.Ordinal));
        if (existing >= 0)
        {
            tags.RemoveAt(existing);
        }
        else
        {
            tags.Add(tag);
        }

        tags.Sort(StringComparer.Ordinal);
        _browseFilterState =
            _browseFilterState with
            {
                RequiredTags = tags
            };
        return ApplyBrowseQueryAsync();
    }

    private async Task CreateTagFromNavigationAsync(
        string name,
        string color)
    {
        var runtime =
            _runtime
            ?? throw new InvalidOperationException(
                "タグを作成するにはライブラリを開いてください。");

        await _navigationLibraryService.CreateTagAsync(
            runtime.Library.Id,
            name,
            color);
        await RefreshNavigationAsync(
            CancellationToken.None);
    }

    private async Task UpdateTagFromNavigationAsync(
        LibraryTagInfo tag,
        string name,
        string color)
    {
        var runtime =
            _runtime
            ?? throw new InvalidOperationException(
                "タグを編集するにはライブラリを開いてください。");

        var updated =
            await _navigationLibraryService.UpdateTagAsync(
                runtime.Library.Id,
                tag.Id,
                name,
                color);

        if (_browseControls is not null)
        {
            await _browseControls.ReplaceTagScopeAsync(
                tag.Name,
                updated.Name);
        }
        else
        {
            var activeTags =
                _browseFilterState.TagNames
                    .ToList();
            var activeIndex =
                activeTags.FindIndex(
                    value =>
                        string.Equals(
                            value,
                            tag.Name,
                            StringComparison.OrdinalIgnoreCase));
            if (activeIndex >= 0)
            {
                activeTags[activeIndex] =
                    updated.Name;
                _browseFilterState =
                    _browseFilterState with
                    {
                        RequiredTags =
                            activeTags
                                .Distinct(
                                    StringComparer.OrdinalIgnoreCase)
                                .OrderBy(
                                    static value => value,
                                    StringComparer.OrdinalIgnoreCase)
                                .ToArray()
                    };
                await ApplyBrowseQueryAsync();
            }
        }

        await RefreshNavigationAsync(
            CancellationToken.None);

        if (_shell is not null)
        {
            await _shell.SyncTagConsumersAsync();
        }
    }

    private async Task DeleteTagFromNavigationAsync(
        LibraryTagInfo tag)
    {
        var runtime =
            _runtime
            ?? throw new InvalidOperationException(
                "タグを削除するにはライブラリを開いてください。");

        var confirmed =
            await ProductDialogs.ConfirmAsync(
                this,
                "タグを削除しますか？",
                $"「{tag.Name}」を削除します。",
                tag.AssetCount > 0
                    ? $"{tag.AssetCount:N0}件の画像からもこのタグが外れます。画像ファイル自体は変更しません。"
                    : "このタグはまだ画像へ付与されていません。",
                confirmLabel: "タグを削除",
                tone: ProductDialogTone.Danger);
        if (!confirmed)
        {
            return;
        }

        if (!await _navigationLibraryService.DeleteTagAsync(
                runtime.Library.Id,
                tag.Id))
        {
            throw new InvalidOperationException(
                "タグを削除できませんでした。");
        }

        var remaining =
            _browseFilterState.TagNames
                .Where(value =>
                    !string.Equals(
                        value,
                        tag.Name,
                        StringComparison.Ordinal))
                .ToArray();
        if (remaining.Length
            != _browseFilterState.TagNames.Count)
        {
            _browseFilterState =
                _browseFilterState with
                {
                    RequiredTags =
                        remaining
                };
            await ApplyBrowseQueryAsync();
        }

        await RefreshNavigationAsync(
            CancellationToken.None);
    }

    private async Task OnBrowseFiltersChangedAsync(
        BrowseFilterState state)
    {
        _browseFilterState =
            state
            ?? throw new ArgumentNullException(nameof(state));

        await ApplyBrowseQueryAsync();

        // The tag manager owns transient search/manage state. Rebuilding the
        // entire navigation surface after every tag toggle would erase that
        // state and recreate the v2 regression where tagging feels jumpy.
        if (!string.Equals(
                _navigationDestination,
                "タグ",
                StringComparison.Ordinal))
        {
            RenderNavigationDestination();
        }
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

    private async Task SaveDiskCacheBudgetFromSettingsAsync(
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
                ThumbnailCacheByteLimit =
                    bytes
            };

        await _host.SaveSettingsAsync(
            next);
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

        var result =
            await PruneThumbnailCacheAsync();

        _status.Foreground =
            LumineDesign.MutedForeground;
        _status.Text =
            result.FilesDeleted == 0
                ? "削除する表示用cacheはありませんでした。"
                : $"{result.FilesDeleted:N0}ファイル / {FormatBytes(result.BytesDeleted)} の表示用cacheを削除しました。";

        StartNavigationRefresh();
    }

    internal Task<ThumbnailPruneResult>
        PruneThumbnailCacheForSmokeAsync() =>
        PruneThumbnailCacheAsync();

    private async Task<ThumbnailPruneResult>
        PruneThumbnailCacheAsync()
    {
        if (_runtime is not null)
        {
            return await _runtime.ThumbnailCache.PruneAsync(
                0);
        }

        var cache =
            new ThumbnailCache(
                _defaultDataPaths.ThumbnailCachePath,
                _resourcePolicy);
        return await cache.PruneAsync(
            0);
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
        var diskLimit =
            _host?.Settings.ResourcePolicy
                ?.ThumbnailCacheByteLimit
            ?? _resourcePolicy
                .ThumbnailCacheByteLimit;
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
            diskLimit,
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
                state.TagNames.Count == 0
                    ? null
                    : state.TagNames,
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
        const string publicationStatus =
            "Publicationを公開履歴へ保存しました。";
        _status.Text =
            publicationStatus;
        _ = ClearTransientStatusAsync(
            publicationStatus);
        StartNavigationRefresh();
    }

    private async Task ApplyBrowseQueryAsync()
    {
        var runtime = _runtime;
        if (runtime is null)
        {
            return;
        }

        var query =
            BuildBrowseQuery();
        var existingShell =
            _shell;
        CoreViewerQueryUiState? queryUiState =
            null;

        try
        {
            if (existingShell is not null)
            {
                _status.Foreground =
                    LumineDesign.MutedForeground;
                _status.Text =
                    "表示を更新しています…";
                queryUiState =
                    await existingShell
                        .PrepareForQueryChangeAsync();
            }

            await runtime.ApplyQueryAsync(
                query);

            if (existingShell is not null
                && queryUiState is not null)
            {
                var assetIndices =
                    await ResolvePreservedAssetIndicesAsync(
                        runtime,
                        query,
                        queryUiState);
                await existingShell
                    .CompleteQueryChangeAsync(
                        queryUiState,
                        assetIndices);

                _shell =
                    existingShell;
                _productShellState =
                    "Workspace";
                existingShell.SetNoMatchState(
                    runtime.AssetCount == 0,
                    ClearBrowseFiltersAsync,
                    OpenBrowseFilterPanel);
                _status.Foreground =
                    LumineDesign.MutedForeground;
                _status.Text =
                    string.Empty;
            }
            else if (runtime.AssetCount == 0)
            {
                var totalAssetCount =
                    await runtime.LibraryService
                        .CountAssetsAsync(
                            runtime.Library.Id);
                _shell = null;
                _productShellState =
                    totalAssetCount == 0
                        ? "EmptyLibrary"
                        : "NoMatch";
                _viewerHost.Content =
                    totalAssetCount == 0
                        ? CreateEmptyLibraryState()
                        : CreateNoMatchState();
            }
            else
            {
                var nextShell =
                    CreateCoreViewerShell(runtime);
                _shell = nextShell;
                _viewerHost.Content = nextShell;
                _productShellState = "Workspace";
                nextShell.SelectInitialAsset();
            }
        }
        catch (Exception exception)
        {
            if (existingShell is not null
                && queryUiState is not null)
            {
                try
                {
                    var restoredIndices =
                        await ResolvePreservedAssetIndicesAsync(
                            runtime,
                            runtime.CurrentQuery,
                            queryUiState);
                    await existingShell
                        .CompleteQueryChangeAsync(
                            queryUiState,
                            restoredIndices);
                    _shell =
                        existingShell;
                    existingShell.SetNoMatchState(false);
                    _productShellState =
                        "Workspace";
                }
                catch
                {
                    // Keep the original failure as the user-facing cause.
                    // Terminal teardown remains owned by the normal library
                    // close path if even the in-place restore cannot complete.
                }
            }
            else
            {
                var restored =
                    CreateCoreViewerShell(runtime);
                _shell = restored;
                _viewerHost.Content = restored;
                _productShellState =
                    runtime.AssetCount == 0
                        ? "EmptyLibrary"
                        : "Workspace";

                if (runtime.AssetCount > 0)
                {
                    restored.SelectInitialAsset();
                }
            }

            _status.Foreground =
                LumineDesign.Warning;
            _status.Text =
                $"検索・フィルターを適用できませんでした: {exception.Message}";
            return;
        }

        UpdateScopeDisplay();
    }

    private static Task<IReadOnlyDictionary<long, long>>
        ResolvePreservedAssetIndicesAsync(
            CoreViewerRuntime runtime,
            AssetQuery? query,
            CoreViewerQueryUiState state)
    {
        if (state.SelectedAssetIds.Count == 0)
        {
            IReadOnlyDictionary<long, long> empty =
                new Dictionary<long, long>();
            return Task.FromResult(empty);
        }

        return runtime.LibraryService
            .GetAssetIndicesAsync(
                runtime.Library.Id,
                query ?? new AssetQuery(),
                state.SelectedAssetIds);
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
        if (_runtime is null)
        {
            return;
        }

        _status.Foreground =
            LumineDesign.MutedForeground;
        _status.Text = string.Empty;
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
        _failedLibraryRoot = null;
        _failedLibraryDataPaths = null;
        _productShellState = "Loading";
        _status.Foreground =
            LumineDesign.MutedForeground;
        _status.Text =
            string.Empty;

        await DisposeCurrentRuntimeAsync()
            .ConfigureAwait(true);
        _expandedFolderPaths.Clear();

        _viewerHost.Content =
            CreateLoadingLibraryState(
                out var loadingProgress);

        var acceptOpenProgress = true;
        var progress =
            new Progress<CoreViewerOpenProgress>(
                update =>
                {
                    if (acceptOpenProgress)
                    {
                        loadingProgress.Text =
                            FormatOpenProgress(update);
                    }
                });

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
            acceptOpenProgress = false;

            _runtime = runtime;
            runtime = null;
            _failedLibraryRoot = null;
            _failedLibraryDataPaths = null;

            if (_runtime.AssetCount == 0)
            {
                _shell = null;
                _viewerHost.Content =
                    CreateEmptyLibraryState();
                _productShellState =
                    "EmptyLibrary";
            }
            else
            {
                var shell =
                    CreateCoreViewerShell(
                        _runtime);
                _shell = shell;
                _viewerHost.Content = shell;
                _productShellState =
                    "Workspace";
                shell.SelectInitialAsset();
            }

            EnsureBrowseControls();
            UpdateScopeDisplay();
            StartNavigationRefresh();

            _status.Foreground =
                LumineDesign.MutedForeground;
            if (_runtime.AssetCount == 0)
            {
                _status.Text =
                    string.Empty;
            }
            else
            {
                var openedStatus =
                    $"{_runtime.AssetCount:N0}件の画像を表示しています。";
                _status.Text =
                    openedStatus;
                _ = ClearTransientStatusAsync(
                    openedStatus);
            }

            _host?.Log.Write(
                "library",
                $"Opened {_runtime.LibraryRoot} with {_runtime.AssetCount:N0} assets.");
        }
        catch (OperationCanceledException)
            when (operationToken.IsCancellationRequested)
        {
            acceptOpenProgress = false;
            if (!_closeStarted)
            {
                _productShellState = "Welcome";
                var cancelledStatus =
                    "ライブラリの読み込みをキャンセルしました。";
                _status.Text =
                    cancelledStatus;
                _ = ClearTransientStatusAsync(
                    cancelledStatus);
                _viewerHost.Content =
                    CreateWelcomeState(recovered: false);
            }
        }
        catch (Exception exception)
        {
            acceptOpenProgress = false;
            _host?.Log.Write(
                "library",
                $"Open failed: {exception.Message}");

            _failedLibraryRoot =
                libraryRoot;
            _failedLibraryDataPaths =
                dataPaths;
            _productShellState = "Error";
            _status.Foreground =
                LumineDesign.Danger;
            _status.Text =
                string.Empty;
            _viewerHost.Content =
                CreateLibraryOpenFailureState(
                    exception);
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

    private static string FormatOpenProgress(
        CoreViewerOpenProgress progress) =>
        progress.Stage switch
        {
            CoreViewerOpenStage.InitializingDatabase =>
                "ライブラリ情報を確認しています…",
            CoreViewerOpenStage.RegisteringLibrary =>
                "ライブラリを登録しています…",
            CoreViewerOpenStage.SynchronizingLibrary
                when progress.ScanProgress
                    is { } scan =>
                FormatLibraryScanProgress(
                    "ライブラリを走査中",
                    scan),
            CoreViewerOpenStage.SynchronizingLibrary =>
                "ライブラリの変更を同期しています…",
            CoreViewerOpenStage.CreatingViewer =>
                "画像一覧を準備しています…",
            CoreViewerOpenStage.Ready =>
                "画像一覧を準備できました。",
            _ =>
                "ライブラリを準備しています…"
        };

    private static string FormatLibraryScanProgress(
        string prefix,
        LibraryScanProgress progress)
    {
        var skipped =
            progress.Skipped > 0
                ? $" · スキップ {progress.Skipped:N0}"
                : string.Empty;
        return
            $"{prefix}… 検出 {progress.Discovered:N0} · 登録 {progress.Persisted:N0}{skipped}";
    }

    private Control CreateLibraryOpenFailureState(
        Exception exception)
    {
        var retry =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "もう一度開く"
                });
        retry.Click +=
            async (_, _) =>
            {
                retry.IsEnabled = false;
                try
                {
                    await RetryFailedLibraryAsync();
                }
                finally
                {
                    if (string.Equals(
                            _productShellState,
                            "Error",
                            StringComparison.Ordinal))
                    {
                        retry.IsEnabled = true;
                    }
                }
            };

        var choose =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content =
                        "別の画像フォルダーを選ぶ"
                });
        choose.Click +=
            async (_, _) =>
            {
                choose.IsEnabled = false;
                try
                {
                    await ChooseAndOpenLibraryAsync();
                }
                finally
                {
                    if (string.Equals(
                            _productShellState,
                            "Error",
                            StringComparison.Ordinal))
                    {
                        choose.IsEnabled = true;
                    }
                }
            };

        var detail =
            new Expander
            {
                Header = "エラー詳細",
                IsExpanded = false,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                Content =
                    new TextBlock
                    {
                        Text = exception.Message,
                        Foreground =
                            LumineDesign.MutedForeground,
                        FontSize =
                            LumineDesign.CaptionFontSize,
                        TextWrapping =
                            TextWrapping.Wrap
                    }
            };

        var actions =
            new StackPanel
            {
                Spacing =
                    LumineDesign.Space8
            };
        actions.Children.Add(retry);
        actions.Children.Add(choose);
        actions.Children.Add(detail);

        return LumineDesign.CreateProductState(
            "ライブラリを開けませんでした",
            "元画像は変更していません。フォルダーの状態を確認して再試行するか、別のフォルダーを選んでください。",
            actions);
    }

    private Task RetryFailedLibraryAsync()
    {
        var root =
            _failedLibraryRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            return Task.CompletedTask;
        }

        return OpenLibraryAsync(
            root,
            _failedLibraryDataPaths);
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

    private Control CreateEmptyLibraryState()
    {
        var add =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "別の画像フォルダーを追加"
                });
        add.Click += OnOpenFolderClicked;

        return LumineDesign.CreateProductState(
            "画像がありません",
            "このライブラリには、まだ表示できる画像がありません。別の画像フォルダーを追加できます。",
            add);
    }

    private async Task ClearBrowseFiltersAsync()
    {
        _browseFilterState =
            new BrowseFilterState(
                SortOrder:
                    _browsePreferences.SortOrder);
        EnsureBrowseControls();
        await ApplyBrowseQueryAsync();
        RenderNavigationDestination();
    }

    private void OpenBrowseFilterPanel()
    {
        _browseControls?.OpenFilterPanel();
    }

    private Control CreateNoMatchState()
    {
        var clear =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "条件をすべて解除"
                });
        clear.Click +=
            async (_, _) =>
                await ClearBrowseFiltersAsync();

        var edit =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "フィルターを見直す"
                });
        edit.Click +=
            (_, _) => OpenBrowseFilterPanel();

        var actions =
            new WrapPanel
            {
                HorizontalAlignment =
                    HorizontalAlignment.Center
            };
        clear.Margin =
            new Thickness(
                0,
                0,
                LumineDesign.Space8,
                LumineDesign.Space8);
        edit.Margin =
            new Thickness(
                0,
                0,
                0,
                LumineDesign.Space8);
        actions.Children.Add(clear);
        actions.Children.Add(edit);

        return LumineDesign.CreateProductState(
            "一致する画像がありません",
            "条件を解除するか、フィルターを見直してください。",
            new Border
            {
                Child = actions
            });
    }

    private Control CreateLoadingLibraryState(
        out TextBlock progressText)
    {
        progressText =
            new TextBlock
            {
                Text = "ライブラリを準備しています…",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                TextAlignment =
                    TextAlignment.Center,
                TextWrapping =
                    TextWrapping.Wrap
            };

        var progress =
            new ProgressBar
            {
                IsIndeterminate = true,
                MinHeight = 4,
                MaxHeight = 4
            };

        var content =
            new StackPanel
            {
                Spacing =
                    LumineDesign.Space8
            };
        content.Children.Add(progress);
        content.Children.Add(progressText);

        return LumineDesign.CreateProductState(
            "ライブラリを開いています",
            "画像一覧を安全に準備しています。",
            content);
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
                ? "前回の状態から安全に復旧しました。画像フォルダーを開いて作業を続けられます。"
                : "画像フォルダーを追加すると、Lumineで整理・閲覧を始められます。";

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
