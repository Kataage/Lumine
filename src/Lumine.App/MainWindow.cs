using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Lumine.App;

public sealed class MainWindow : Window
{
    private readonly Button _openFolder;
    private readonly TextBlock _status;
    private readonly TextBlock _libraryPath;
    private readonly ContentControl _viewerHost;
    private CancellationTokenSource? _openCancellation;
    private CoreViewerRuntime? _runtime;
    private CoreViewerShell? _shell;
    private bool _closeStarted;
    private bool _closeCompleted;

    public MainWindow()
    {
        Title = "Lumine v2";
        Width = 1440;
        Height = 900;
        MinWidth = 900;
        MinHeight = 600;

        _openFolder = new Button
        {
            Content = "Open library folder…"
        };
        _openFolder.Click += OnOpenFolderClicked;

        _status = new TextBlock
        {
            Text = "Choose an image library folder to begin.",
            VerticalAlignment = VerticalAlignment.Center
        };

        _libraryPath = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        };

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(16, 12)
        };
        toolbar.Children.Add(_openFolder);
        toolbar.Children.Add(_status);

        _viewerHost = new ContentControl
        {
            HorizontalContentAlignment =
                HorizontalAlignment.Stretch,
            VerticalContentAlignment =
                VerticalAlignment.Stretch
        };

        var empty = new Border
        {
            Padding = new Thickness(32),
            Child = new TextBlock
            {
                Text = "Lumine v2 Core Viewer\n\nOpen a folder to index and browse images.",
                FontSize = 20,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment =
                    HorizontalAlignment.Center,
                VerticalAlignment =
                    VerticalAlignment.Center
            }
        };
        _viewerHost.Content = empty;

        var layout = new Grid
        {
            RowDefinitions =
                new RowDefinitions("Auto,Auto,*")
        };
        layout.Children.Add(toolbar);

        Grid.SetRow(_libraryPath, 1);
        _libraryPath.Margin =
            new Thickness(16, 0, 16, 8);
        layout.Children.Add(_libraryPath);

        Grid.SetRow(_viewerHost, 2);
        layout.Children.Add(_viewerHost);

        Content = layout;

        Opened += OnOpened;
        Closing += OnClosing;
    }

    internal CoreViewerRuntime? CurrentRuntime => _runtime;

    internal CoreViewerShell? CurrentShell => _shell;

    internal async Task OpenLibraryAsync(
        string libraryRoot,
        AppDataPaths? dataPaths = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            libraryRoot);

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

        await DisposeCurrentRuntimeAsync()
            .ConfigureAwait(true);

        var progress =
            new Progress<CoreViewerOpenProgress>(
                update => _status.Text = update.Message);

        CoreViewerRuntime? runtime = null;

        try
        {
            runtime = await CoreViewerRuntime.OpenAsync(
                libraryRoot,
                dataPaths ?? AppDataPaths.ResolveDefault(),
                Program.ResourcePolicy,
                progress,
                operationToken);

            operationToken.ThrowIfCancellationRequested();

            var shell =
                new CoreViewerShell(runtime);

            _runtime = runtime;
            _shell = shell;
            runtime = null;

            _viewerHost.Content = shell;
            _status.Text =
                $"{_runtime.AssetCount:N0} assets · {_runtime.Library.Name}";

            shell.SelectInitialAsset();
        }
        catch (OperationCanceledException)
            when (operationToken.IsCancellationRequested)
        {
            if (!_closeStarted)
            {
                _status.Text =
                    "Library opening cancelled.";
            }
        }
        catch (Exception exception)
        {
            _status.Text =
                $"Unable to open library: {exception.Message}";
            _libraryPath.Text = string.Empty;
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
                    Title = "Open Lumine image library",
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
                "The selected folder is not a local filesystem path.";
            return;
        }

        await OpenLibraryAsync(path);
    }

    private void OnOpened(
        object? sender,
        EventArgs e)
    {
        Program.Diagnostics.MarkWindowReady();
        _ = Program.Diagnostics.FlushRequestedAsync(
            Program.ResourcePolicy.ToDiagnosticMetadata());
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
        _status.Text = "Closing Lumine…";

        _openCancellation?.Cancel();

        try
        {
            await DisposeCurrentRuntimeAsync();

            await Program.Diagnostics.FlushRequestedAsync(
                Program.ResourcePolicy.ToDiagnosticMetadata());
        }
        catch (Exception exception)
        {
            _status.Text =
                $"Shutdown error: {exception.Message}";
        }
        finally
        {
            _openCancellation?.Dispose();
            _openCancellation = null;
            _closeCompleted = true;
            Close();
        }
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
            shell.Detach();
        }

        if (runtime is not null)
        {
            await runtime.DisposeAsync();
        }
    }
}
