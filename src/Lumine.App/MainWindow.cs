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
    private readonly LibraryService _navigationLibraryService;
    private readonly Task _navigationInitialization;
    private readonly ContentControl _navigationRailHost;
    private readonly Border _navigationPane;
    private readonly TextBlock _navigationTitle;
    private readonly ContentControl _navigationContent;
    private CancellationTokenSource? _openCancellation;
    private CancellationTokenSource? _diagnosticsCancellation;
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
    private string? _folderScope;
    private string? _tagScope;
    private IReadOnlyList<LibraryCatalogItem> _libraries =
        Array.Empty<LibraryCatalogItem>();
    private IReadOnlyList<LibraryFolderInfo> _folders =
        Array.Empty<LibraryFolderInfo>();
    private IReadOnlyList<LibraryTagInfo> _tags =
        Array.Empty<LibraryTagInfo>();

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

        var workspace = new Grid
        {
            Background = LumineDesign.Background,
            RowDefinitions =
                new RowDefinitions("Auto,Auto,*")
        };
        workspace.Children.Add(header);

        Grid.SetRow(_libraryPath, 1);
        _libraryPath.Margin =
            new Thickness(16, 6, 16, 6);
        workspace.Children.Add(_libraryPath);

        Grid.SetRow(_viewerHost, 2);
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
                _thumbnailStorageMode);

            operationToken.ThrowIfCancellationRequested();

            var shell =
                new CoreViewerShell(runtime);

            _runtime = runtime;
            _shell = shell;
            runtime = null;

            _viewerHost.Content = shell;
            _productShellState =
                _runtime.AssetCount == 0
                    ? "EmptyLibrary"
                    : "Workspace";
            _status.Text =
                $"{_runtime.AssetCount:N0} 件 · {_runtime.Library.Name}";

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
            _status.Text =
                "ローカルの画像フォルダーを選択してください。";
            return;
        }

        await OpenLibraryAsync(path);
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

        _openCancellation?.Cancel();
        _diagnosticsCancellation?.Cancel();
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
