using Lumine.Core;
using Lumine.Image;
using Lumine.Library;
using Lumine.Viewer;

Console.WriteLine($"{FoundationInfo.ProductName} architecture v{FoundationInfo.ArchitectureVersion}");
Console.WriteLine(FoundationInfo.RuntimeDescription);

var resourcePolicy = CoreResourcePolicy.Resolve(
    new ResourcePolicySettings
    {
        ThumbnailWorkerCount = 2,
        ThumbnailQueueCapacity = 64,
        ThumbnailForegroundBurst = 4,
        DecodedThumbnailEntryLimit = 48,
        DecodedThumbnailByteLimit = 64L * 1024 * 1024,
        DetailPreviewEntryLimit = 3,
        DetailPreviewByteLimit = 32L * 1024 * 1024,
        DetailOriginalByteLimit = 192L * 1024 * 1024,
        ThumbnailCacheByteLimit = 2L * 1024 * 1024 * 1024,
        VipsTrackedMemoryLimitBytes = 48L * 1024 * 1024
    },
    processorCount: 8);

if (resourcePolicy.ThumbnailWorkerCount != 2
    || resourcePolicy.ThumbnailQueueCapacity != 64
    || resourcePolicy.DecodedThumbnailEntryLimit != 48
    || resourcePolicy.DetailOriginalByteLimit != 192L * 1024 * 1024
    || resourcePolicy.VipsConcurrency != 4)
{
    throw new InvalidOperationException(
        "Core resource policy did not resolve the expected bounded values.");
}

try
{
    _ = CoreResourcePolicy.Resolve(
        new ResourcePolicySettings
        {
            ThumbnailWorkerCount =
                CoreResourcePolicy.MaxThumbnailWorkerCount + 1
        },
        processorCount: 8);
    throw new InvalidOperationException(
        "Out-of-range resource policy was accepted.");
}
catch (ArgumentOutOfRangeException)
{
}

var sqlite = SqliteRuntimeProbe.Probe();
if (sqlite.Scalar != 1)
{
    throw new InvalidOperationException($"SQLite smoke query returned {sqlite.Scalar}, expected 1.");
}
if (!sqlite.Fts5TrigramContentlessDelete)
{
    throw new InvalidOperationException(
        "SQLite runtime lacks the FTS5 trigram/contentless-delete contract required by local search. " +
        (sqlite.SearchRuntimeError ?? "No SQLite error was reported."));
}
Console.WriteLine($"SQLite {sqlite.Version}: FTS5 trigram/contentless-delete OK");

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
