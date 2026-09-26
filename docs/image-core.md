# Lumine Image Core

Issue #288 establishes the persistent thumbnail pipeline used by the normal high-volume viewer.

## Cache contract

The thumbnail key is independent of the original absolute path. It is derived from:

- generator version
- stable asset id
- Library Core source revision
- source file size and persisted modified timestamp
- persisted source identity
- thumbnail profile id/version/dimensions/quality

A valid cache hit with persisted technical metadata opens only the cached WebP file. It does not stat, hash, open or decode the original. The original path is needed only for a first metadata derivation, cache miss or corrupt-cache recovery.

Issue #308 moves the cache-key contract to generator version 3. The first cold/miss path opens a stable source snapshot and derives a source identity before capturing raw/oriented dimensions, alpha and actual loader format. On Windows/NTFS the primary identity is the file's current USN obtained with `FSCTL_READ_FILE_USN_DATA`, so the common path does not read the whole file merely to hash it. If that fast identity is unavailable, Image Core falls back to SHA-256. App composition persists the identity and metadata against the exact Library asset id/source_revision/file stat. Subsequent warm hits reuse the DB metadata and content-bound key without touching the source. While a Viewer page still holds a pre-enrichment DTO, Image Core also keeps a bounded 4,096-entry in-process metadata LRU keyed by asset/source_revision/stat.

Old revisions and pre-#308 cache keys remain harmless orphaned cache entries until bounded pruning removes them.

## Profiles

- grid-small: 256 x 256, quality 80
- grid-medium: 512 x 512, quality 82
- detail-preview: 1600 x 1600, quality 85

Profiles only downscale. They do not enlarge small originals.

## Decode and output

libvips `thumbnail` is used directly from the source filename so format loaders can use shrink-on-load paths. EXIF orientation is enabled. Resampling is performed in linear light and `output_profile=srgb` performs ICC-aware normalization before metadata is stripped from the cached WebP. Alpha is preserved.

JPEG, PNG, WebP and GIF static preview support are mandatory in the bundled Windows runtime. HEIF/AVIF is capability-probed at runtime and exercised when both load/save operations are present.

## Crash and corruption behavior

Generation writes to a unique temporary WebP beside the final cache entry and moves it into place only after libvips completes the file. Cache construction performs no recursive disk walk. The shard touched by a cache miss receives local stale-temp cleanup, while full interrupted-write recovery is an explicit asynchronous maintenance operation. Temporary files are never deleted merely because another cache instance or prune pass sees them: only files older than the interrupted-write grace period are treated as stale and recovered. This prevents cleanup from racing an active atomic write without turning app startup into a cache-wide scan.

A cache entry that cannot be opened as a valid image is deleted and regenerated from the source rather than returned to the Viewer.

## Scheduling

`ThumbnailPipeline` owns a bounded queue and a bounded worker set. Foreground work is preferred, but a bounded foreground burst prevents permanent starvation of queued background work. Backpressure is applied before enqueue when the total queue is full.

Cancellation is accepted before enqueue and while queued. Once libvips evaluation has started, the cancellation token drives NetVips `SetKill(true)` so obsolete foreground/background work can stop inside the native pipeline rather than occupying a worker until full decode/save completes. Worker count defaults to half the logical processors, clamped to 1..4. The default foreground burst is eight items per worker when background work is waiting.

## Cache accounting

`ThumbnailCache.GetStatsAsync` reports persisted files and bytes. `PruneAsync(maxBytes)` deletes oldest generated entries until the requested disk budget is met. Pruning never materializes the whole cache in RAM: it keeps a bounded 4,096-entry oldest-candidate window and rescans only when a large eviction requires another window. A zero-byte prune streams deletions directly. Temporary interrupted writes are not counted as usable cache.

## Diagnostics

Lumine owns the persistent cache, so the libvips process-global operation cache is disabled for the thumbnail path. This follows libvips guidance for proxy-style workloads that process many different images and avoids duplicate hidden caching. libvips internal concurrency is also capped relative to the default Lumine worker count so application-level workers do not multiply into an oversized native thread pool.

The common benchmark contract records:

- single cold generation
- repeated warm cache hits with the original removed
- bounded parallel batch generation
- cache files/bytes
- cache hit/miss/generation/source-open counters
- source metadata probe count, fast-identity hits, hash fallbacks, in-process metadata-memory hits and bytes hashed
- a 10,000-distinct-file source metadata probe workload with measured latency/logical file bytes and actual hashed bytes
- generation and batch peak working set
- libvips tracked-memory high-water mark, open-file count, and operation-cache size

The Windows CI benchmark is enforced by `build/Test-ImagePerformance.ps1`. It gates cold generation, 1,000 persistent cache hits, bounded 64-request batch throughput, source-open/cache-hit invariants, and both absolute and incremental peak working set during batch generation.


## Source technical metadata contract

Image Core owns derivation, not persistence. It returns `SourceTechnicalMetadata` containing oriented dimensions, raw dimensions, alpha, actual loader-derived format and source identity. `Lumine.App` is the only layer allowed to bridge that result into Library Core.

A persisted source identity is validated on a thumbnail cache miss and on full-resolution decode. NTFS USN catches same-size/mtime rewrites without a whole-file hash; non-NTFS/unavailable-USN environments use SHA-256 fallback. Reconciliation also compares persisted identity for same-stat files so watcher/USN gaps cannot silently preserve stale metadata. Detail preview carries the metadata through Viewer contracts, so selecting a warm preview can show dimensions/format without probing the original. Warm preview uses persisted metadata without touching the original. When the user explicitly requests full resolution, Image Core prepares one stable source snapshot and validates the persisted source identity before the App allocates the full-size bitmap; decode then consumes that same held snapshot rather than reopening or re-hashing the source. This preserves the #291 stale-allocation safety boundary while keeping ordinary Detail selection source-free.
