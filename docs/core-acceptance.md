# Lumine v2 real-library Core acceptance

Issue #295 established and passed the technical Core foundation gate. This acceptance harness remains the performance/correctness baseline for subsequent Lumine product work under #385/#397.

CI remains necessary, but product acceptance still requires representative-library evidence on a real Windows x64 machine using the published NativeAOT `Lumine.App.exe`.

## What the product acceptance mode does

The published app supports:

```text
--core-acceptance
--library-dir=<representative library>
--data-dir=<isolated v2 acceptance data root>
--output=<raw #286 BenchmarkResult JSON>
--mode=<cold|warm>
--min-assets=<minimum representative asset count>
--browse-seconds=<scripted browse duration>
--idle-seconds=<idle observation duration>
```

This is not a separate mock application. The mode starts the real Avalonia desktop lifetime and real `MainWindow`, then runs the production `CoreViewerRuntime`, `ThumbnailViewerControl`, and `DetailViewerControl` paths.

The representative-library UI path is read-only with respect to source image files.

The scripted UI path covers:

- startup -> real window ready
- library open / synchronization
- initial viewport ready
- forward fast scroll across the library
- direction reversal
- Detail preview
- next / previous
- zoom / pan / Fit
- 1:1/original request
- repeated browsing for a configurable duration
- idle settle
- coordinated real-window shutdown

The same real Windows process then runs an isolated scratch-library functional phase, outside the representative source library, covering:

- live add
- modify
- rename with stable asset identity
- delete
- no periodic idle reconciliation
- app-stopped/offline file creation and restart recovery
- metadata edit round-trip
- filename search
- two-character Japanese search
- tag/notes/favorite/rating/status/color composed filtering
- filename sort

The raw result uses the #286 `BenchmarkResult` schema and records hardware/OS/runtime/revision plus resource snapshots.

## Cold + warm wrapper

Use the wrapper so cold and warm runs share the same v2 database. The product default is `MemoryOnly`, which deliberately does not persist display thumbnails across processes. `PersistentDisk` can be selected explicitly for diagnostics/comparison:

```powershell
# PowerShell 7
pwsh -File .\build\Run-RealLibraryAcceptance.ps1 -Exe ".\artifacts\native-aot\Lumine.App.exe" -Library "D:\path\to\representative-library" -OutputDirectory "D:\Lumine-acceptance"

# Windows PowerShell 5.1
powershell.exe -ExecutionPolicy Bypass -File .\build\Run-RealLibraryAcceptance.ps1 -Exe ".\artifacts\native-aot\Lumine.App.exe" -Library "D:\path\to\representative-library" -OutputDirectory "D:\Lumine-acceptance"
```
Defaults:

- minimum assets: 1,000
- scripted browse: 60 seconds per run
- idle observation: 10 seconds per run
- fast-scroll gate: 1,500 ms, matching the existing Viewer acceptance gate
- thumbnail storage: `MemoryOnly` by default; pass `-ThumbnailStorageMode PersistentDisk` only for explicit persistent-cache diagnostics/comparison

The wrapper deletes its isolated acceptance data root and any prior `cold.json`, `warm.json`, and `summary.json` before the cold run. It does not delete or modify image files in the supplied representative library.

The representative library must remain stable for the full cold+warm sequence. Cold and warm asset counts are required to match.

The acceptance data/results directory must not overlap the representative source library. The wrapper rejects overlapping paths before creating or deleting any output.

Before launch, the wrapper resolves the exact build revision from `revision.txt` next to `Lumine.App.exe` (or from an explicit `-Revision`). Unknown or malformed revisions are rejected.

It then launches:

1. cold run with a fresh Lumine data/cache root
2. warm run against the same Lumine data/cache root
3. for `PersistentDisk` only, when the first warm run successfully commits additional persistent thumbnails, one additional steady-warm run against the now-populated cache

For `MemoryOnly`, each process starts without display-thumbnail persistence by design. The wrapper therefore does not manufacture a cross-process steady-warm convergence requirement. Instead it requires zero persistent thumbnail files/bytes, bounded encoded-thumbnail memory, and otherwise runs the same real Viewer interaction sequence and correctness gates.

After each process exits, the wrapper requires:

- acceptance process exit code 0
- raw JSON present and reporting the requested cold/warm mode
- raw schema version = 1
- raw build revision exactly matches the packaged/explicit revision
- hardware ID matches the wrapper invocation
- process and OS architecture are X64
- `runtime.unclean` absent
- SQLite WAL absent or empty
- post-shutdown thumbnail cache bytes <= the configured disk budget
- post-shutdown interrupted thumbnail writes = 0

Across the results it requires:

- automated result = pass
- cold and warm refer to the same representative-library path
- cold and warm asset counts are identical
- cold persistent thumbnail cache started empty
- representative asset count >= configured minimum
- visible tile failures = 0
- Viewer thumbnail request failures = 0
- thumbnail generation failures = 0
- no in-flight thumbnail request remains after idle settle
- no active bitmap decode remains after idle settle
- thumbnail cache maintenance failures = 0
- interrupted thumbnail writes = 0
- representative-library filesystem queue depth = 0 after idle settle
- representative-library watcher produced no tracked asset mutation (upsert/delete/tracked rename) during the run
- raw watcher notifications that are filtered as unsupported/no-op paths remain diagnostic evidence and do not by themselves fail acceptance
- watcher overflows = 0
- runtime filesystem reconciliations = 0 on the stable representative library
- filesystem reconcile failures = 0
- persistent thumbnail cache bytes <= the effective configured disk-cache limit
- max scripted fast-scroll refresh <= configured gate
- for `PersistentDisk`, final warm persistent thumbnail cache hits > 0
- for `MemoryOnly`, persistent thumbnail file/byte counts remain zero before and after shutdown, and encoded-thumbnail memory stays within its configured bound
- every thumbnail source-open attempt is accounted as exactly one successful generation, post-open cancellation, or failure
- cache misses may exceed source opens because cancellation can occur after a miss is claimed but before source generation starts
- persistent cache file growth must match successful generations after accounting for maintenance deletions
- if the first warm run successfully commits any additional thumbnails, a second steady-warm run must prove zero successful generations and zero persistent-cache file growth
- source-open attempts that are explicitly recorded as post-open cancellations are diagnostic performance evidence, not cache-convergence failures; issue #380 owns reducing that wasted work
- the Cold <= 1,500 ms fast-scroll gate is checked before an optional steady-warm run because later cache convergence cannot repair a Cold responsiveness failure

A warm filesystem bootstrap other than `UsnDelta` is reported as a warning rather than silently treated as equivalent. A reconcile fallback can be legitimate when USN replay is unavailable, but it must be reviewed for the actual target volume.

## Thumbnail storage policy comparison (#388)

Use the shipped comparison runner when deciding between the current persistent-display-thumbnail behavior and the v1-style memory-only philosophy:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\build\Compare-ThumbnailStoragePolicies.ps1 `
  -Exe ".\artifacts\native-aot\Lumine.App.exe" `
  -Library "D:\path\to\representative-library" `
  -OutputDirectory "D:\Lumine-thumbnail-policy-comparison"
```

The runner executes two isolated acceptance sequences against the same executable, hardware and representative source library:

1. `PersistentDisk`
2. `MemoryOnly`

It writes `comparison.json` and keeps the full raw results beneath `persistent-disk\` and `memory-only\`. The comparison records:

- first viewport latency
- every scripted fast-scroll latency
- the direction-reversal tail
- whether each run meets the normal 1,500 ms target
- measured process CPU time across the acceptance measurements
- peak working set
- thumbnail source opens and post-open cancellations
- generated/hit counts
- encoded-memory usage
- persistent thumbnail files/bytes
- metadata hash bytes

The comparison runner uses a permissive collection ceiling (30,000 ms by default) so a slower policy can still finish and produce evidence. `TargetMaxFastScrollMs` remains 1,500 ms by default and is reported independently as the product-performance target. The comparison run is evidence collection. #388 used this runner on the 3,651-asset representative library and selected MemoryOnly as the product default; see ADR 0001.

`PersistentDisk` Warm and `MemoryOnly` Warm do not mean the same cache state. PersistentDisk measures cross-process display-thumbnail reuse. MemoryOnly intentionally begins each process without persisted display thumbnails and measures a fresh bounded in-process memory-cache lifecycle on a warm library database. The runner records this distinction rather than treating the two semantics as equivalent.

Image Core smoke also requires the two storage policies to produce byte-identical WebP output for the same source/profile. Therefore storage-policy comparison does not trade image quality for cache behavior.

## Native crash diagnostics

If `Lumine.App.exe` terminates with a non-zero native process exit code, the wrapper writes `<mode>-crash-diagnostics.txt` beside the acceptance outputs. The diagnostic includes the requested app revision, Windows composition mode, GPU/driver information, recent Windows Application Error / WER events for `Lumine.App.exe`, and the Lumine runtime log when available.

For Windows compositor / renderer A/B diagnosis, the same NativeAOT build can be run with:

```powershell
# Keep GPU rendering but bypass WinUI/DirectComposition.
powershell.exe -ExecutionPolicy Bypass -File .\Run-RealLibraryAcceptance.ps1 -Exe ".\Lumine.App.exe" -Library "D:\path\to\library" -OutputDirectory ".\acceptance-redirection" -Win32CompositionMode RedirectionSurface

# Keep the default composition order but force software rendering.
powershell.exe -ExecutionPolicy Bypass -File .\Run-RealLibraryAcceptance.ps1 -Exe ".\Lumine.App.exe" -Library "D:\path\to\library" -OutputDirectory ".\acceptance-software" -Win32RenderingMode Software
```

`Default` keeps Avalonia's normal Windows backend order. `RedirectionSurface` and `Software` are diagnostic overrides until physical evidence justifies changing a product default.

## Output

The output directory contains:

- `cold.json` — raw #286 `BenchmarkResult`
- `warm.json` — first warm raw #286 `BenchmarkResult`
- `warm-steady.json` — emitted only when a bounded cold-cancellation cache hole requires a convergence proof
- `summary.json` — #295 decision support

`summary.json` contains:

- automated decision
- hardware ID
- exact app Git revision
- explicit acceptance criteria used for the run
- SHA-256 of the exact `cold.json` and `warm.json` summarized
- hashed representative-library path instead of the raw private path
- asset count
- cold/warm max fast-scroll latency
- cold/warm peak working set
- cold/warm idle working set from the end of the explicit idle-settle measurement
- pre-shutdown and post-shutdown thumbnail-cache bytes versus the configured budget
- final warm source-open/post-open-cancellation/generation/cache-miss/cache-hit evidence
- whether warm convergence was required, including first-warm generation/cache-growth accounting and final steady-warm persistence evidence
- filesystem bootstrap mode
- measured bottlenecks
- known limitations
- any warnings
- placeholder for additional fix Issues
- manual observation checklist

## Manual observation is intentionally still required

Automation can measure stalls and catch failures, but the #295 completion criterion says browsing must not feel unpleasant in real use. The visible NativeAOT run therefore still requires confirmation that:

- initial grid appearance is not unpleasant
- continuous scroll and direction reversal remain visually responsive
- Detail next/previous/zoom/1:1 feel usable
- no periodic whole-library stall appears during the browse/idle window
- no unresolved P0/P1 Core behavior is observed

Do not close a later product acceptance gate from CI or automated JSON alone.

## Resource interpretation

The common #286 resource snapshots provide process working set, managed heap, total allocations, GC counts, and CPU time around measurements.

The acceptance result also records Core-owned thumbnail cache/pipeline diagnostics and cache bounds. The explicit idle-settle phase is a quiescence gate: background thumbnail/decode work and filesystem queues must drain to zero, and a stable representative source library must not trigger tracked asset mutations, watcher overflow, or runtime reconciliation. Raw ReadDirectoryChangesW notifications are retained in diagnostics because Windows can report unsupported/no-op paths; only changes that affect Lumine's tracked library state are treated as source mutations.

Two limitations are explicit rather than hidden:

- OS-level per-process disk-read bytes are not currently collected; thumbnail source-open and metadata-hash byte counters are the Core-owned I/O signal.
- dedicated GPU-memory usage is not collected because Core owns no AI/GPU model runtime. Avalonia compositor residency is platform-owned.

If either limitation becomes material to a real bottleneck, create a focused follow-up Issue rather than silently inventing a measurement.
