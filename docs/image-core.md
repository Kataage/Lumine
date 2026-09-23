# Lumine Image Core

Issue #288 establishes the persistent thumbnail pipeline used by the normal high-volume viewer.

## Cache contract

The thumbnail key is independent of the original absolute path. It is derived from:

- generator version
- stable asset id
- Library Core source revision
- source file size and persisted modified timestamp
- thumbnail profile id/version/dimensions/quality

A valid cache hit opens only the cached WebP file. It does not stat, open or decode the original. The original path is needed only for a cache miss or corrupt-cache recovery.

Old revisions remain harmless orphaned cache entries until bounded pruning removes them.

## Profiles

- grid-small: 256 x 256, quality 80
- grid-medium: 512 x 512, quality 82
- detail-preview: 1600 x 1600, quality 85

Profiles only downscale. They do not enlarge small originals.

## Decode and output

libvips `thumbnail` is used directly from the source filename so format loaders can use shrink-on-load paths. EXIF orientation is enabled. Resampling is performed in linear light, output pixels are normalized to sRGB, and WebP is the persistent cache format with alpha preservation and stripped metadata.

JPEG, PNG, WebP and GIF static preview support are mandatory in the bundled Windows runtime. HEIF/AVIF is capability-probed at runtime and exercised when both load/save operations are present.

## Crash and corruption behavior

Generation writes to a unique temporary WebP beside the final cache entry and moves it into place only after libvips completes the file. Interrupted `*.tmp.webp` files are removed when the cache is opened.

A cache entry that cannot be opened as a valid image is deleted and regenerated from the source rather than returned to the Viewer.

## Scheduling

`ThumbnailPipeline` owns a bounded queue and a bounded worker set. Foreground work is dequeued before background work. Backpressure is applied before enqueue when the total queue is full.

Cancellation is accepted before enqueue and while queued. Worker count defaults to half the logical processors, clamped to 1..4.

## Cache accounting

`ThumbnailCache.GetStatsAsync` reports persisted files and bytes. `PruneAsync(maxBytes)` deletes oldest generated entries until the requested disk budget is met. Temporary interrupted writes are not counted as usable cache.

## Diagnostics

The common benchmark contract records:

- single cold generation
- repeated warm cache hits with the original removed
- bounded parallel batch generation
- cache files/bytes
- cache hit/miss/generation/source-open counters
- generation peak working set

Performance budgets are added only after the Windows CI baseline is observed; CI green alone is not treated as acceptance.
