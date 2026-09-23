using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lumine.Core;
using Lumine.Viewer;

namespace Lumine.App;

public sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = "Lumine v2";
        Width = 1200;
        Height = 800;
        MinWidth = 800;
        MinHeight = 560;

        var content = new StackPanel
        {
            Margin = new Thickness(32),
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        content.Children.Add(new TextBlock
        {
            Text = "Lumine v2",
            FontSize = 34,
            FontWeight = FontWeight.SemiBold
        });

        content.Children.Add(new TextBlock
        {
            Text = "Greenfield foundation",
            FontSize = 18
        });

        content.Children.Add(new TextBlock
        {
            Text = FoundationInfo.RuntimeDescription,
            TextWrapping = TextWrapping.Wrap
        });

        var viewer = ViewerFoundationDescriptor.Current;
        content.Children.Add(new TextBlock
        {
            Text = $"Viewer path: {viewer.RenderingPath}; WebView: {viewer.UsesWebView}",
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(new TextBlock
        {
            Text = "Image, library and AI features are intentionally introduced in later milestones.",
            MaxWidth = 720,
            TextWrapping = TextWrapping.Wrap
        });

        Content = content;
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Program.Diagnostics.MarkWindowReady();
        _ = Program.Diagnostics.FlushRequestedAsync();
    }
}
