namespace Lumine.Viewer;

public readonly record struct ViewerFoundationDescriptor(bool UsesWebView, string RenderingPath)
{
    public static ViewerFoundationDescriptor Current =>
        new(false, "Avalonia native control tree / Skia rendering");
}
