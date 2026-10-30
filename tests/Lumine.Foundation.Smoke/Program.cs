using Lumine.Core;
using Lumine.Image;
using Lumine.Library;
using Lumine.Viewer;

Console.WriteLine($"{FoundationInfo.ProductName} architecture v{FoundationInfo.ArchitectureVersion}");
Console.WriteLine(FoundationInfo.RuntimeDescription);

// These are byte/entry ceilings, not preallocated RAM. The product
// defaults must be genuinely GiB-scale and must be internally usable:
// increasing only the byte budget while keeping the old 96-entry cap
// would leave most of the native Bitmap cache unreachable.
var defaultPolicy = CoreResourcePolicy.Resolve(processorCount: 8);
const long gib = 1024L * 1024 * 1024;
if (defaultPolicy.DecodedThumbnailByteLimit < gib
    || defaultPolicy.EncodedThumbnailMemoryByteLimit < gib
    || defaultPolicy.DecodedThumbnailEntryLimit < 2048
    || defaultPolicy.DecodedThumbnailEntryLimit
        > CoreResourcePolicy.MaxDecodedThumbnailEntryLimit
    || defaultPolicy.DecodedThumbnailByteLimit
        > CoreResourcePolicy.MaxDecodedThumbnailByteLimit
    || defaultPolicy.EncodedThumbnailMemoryByteLimit
        > CoreResourcePolicy.MaxEncodedThumbnailMemoryByteLimit)
{
    throw new InvalidOperationException(
        "Default thumbnail byte/entry budgets are not bounded GiB-scale.");
}

var overridden = CoreResourcePolicy.Resolve(
    new ResourcePolicySettings
    {
        DecodedThumbnailByteLimit = 128L * 1024 * 1024,
        DecodedThumbnailEntryLimit = 48,
        EncodedThumbnailMemoryByteLimit = 256L * 1024 * 1024
    },
    processorCount: 8);
if (overridden.DecodedThumbnailByteLimit != 128L * 1024 * 1024
    || overridden.DecodedThumbnailEntryLimit != 48
    || overridden.EncodedThumbnailMemoryByteLimit != 256L * 1024 * 1024)
{
    throw new InvalidOperationException(
        "Explicit user cache limits were overwritten by new defaults.");
}

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
