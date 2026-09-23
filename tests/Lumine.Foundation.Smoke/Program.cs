using Lumine.Core;
using Lumine.Image;
using Lumine.Library;
using Lumine.Viewer;

Console.WriteLine($"{FoundationInfo.ProductName} architecture v{FoundationInfo.ArchitectureVersion}");
Console.WriteLine(FoundationInfo.RuntimeDescription);

var sqlite = SqliteRuntimeProbe.Probe();
if (sqlite.Scalar != 1)
{
    throw new InvalidOperationException($"SQLite smoke query returned {sqlite.Scalar}, expected 1.");
}
Console.WriteLine($"SQLite {sqlite.Version}: OK");

var vips = VipsRuntimeProbe.Probe();
if (!vips.Initialized)
{
    throw new InvalidOperationException("libvips is not initialized.");
}
Console.WriteLine($"libvips {vips.Version}: OK");

var viewer = ViewerFoundationDescriptor.Current;
if (viewer.UsesWebView)
{
    throw new InvalidOperationException("Lumine v2 viewer foundation must not use WebView.");
}
Console.WriteLine($"Viewer foundation: {viewer.RenderingPath}");

Console.WriteLine("Lumine v2 foundation smoke test passed.");
