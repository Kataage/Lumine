# Lumine v2 real-library Core acceptance

Issue #295 is the final Core gate before any v2 AI product work begins.

CI remains necessary, but CI green alone does not close #295. The final run must use a representative image library on a real Windows x64 machine and the published NativeAOT `Lumine.App.exe`.

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

Use the wrapper so cold and warm runs share the same v2 database and persistent thumbnail cache:

```powershell
pwsh -File .\build\Run-RealLibraryAcceptance.ps1 \
  -Exe "C:\path\to\Lumine.App.exe" \
  -Library "D:\path\to\representative-library" \
  -OutputDirectory "D:\Lumine-acceptance"
```

Defaults:

- minimum assets: 1,000
- scripted browse: 60 seconds per run
- idle observation: 10 seconds per run
- fast-scroll gate: 1,500 ms, matching the existing Viewer acceptance gate

The wrapper deletes only its isolated acceptance data root before the cold run. It does not delete or modify image files in the supplied representative library.

It then launches:

1. cold run with a fresh Lumine data/cache root
2. warm run against the same Lumine data/cache root

After each process exits, the wrapper requires:

- acceptance process exit code 0
- raw JSON present
- `runtime.unclean` absent
- SQLite WAL absent or empty
- post-shutdown thumbnail cache bytes <= the configured disk budget
- post-shutdown interrupted thumbnail writes = 0

Across the results it requires:

- automated result = pass
- representative asset count >= configured minimum
- visible tile failures = 0
- thumbnail failures = 0
- interrupted thumbnail writes = 0
- filesystem reconcile failures = 0
- persistent thumbnail cache bytes <= the effective configured disk-cache limit
- max scripted fast-scroll refresh <= configured gate
- warm persistent thumbnail cache hits > 0
- warm thumbnail-generation source opens = 0

A warm filesystem bootstrap other than `UsnDelta` is reported as a warning rather than silently treated as equivalent. A reconcile fallback can be legitimate when USN replay is unavailable, but it must be reviewed for the actual target volume.

## Output

The output directory contains:

- `cold.json` — raw #286 `BenchmarkResult`
- `warm.json` — raw #286 `BenchmarkResult`
- `summary.json` — #295 decision support

`summary.json` contains:

- automated decision
- hardware ID
- hashed representative-library path instead of the raw private path
- asset count
- cold/warm max fast-scroll latency
- cold/warm peak working set
- cold/warm idle working set from the end of the explicit idle-settle measurement
- pre-shutdown and post-shutdown thumbnail-cache bytes versus the configured budget
- warm source-open/cache-hit evidence
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

Do not close #295 from CI or the automated JSON alone.

## Resource interpretation

The common #286 resource snapshots provide process working set, managed heap, total allocations, GC counts, and CPU time around measurements.

The acceptance result also records Core-owned thumbnail cache/pipeline diagnostics and cache bounds.

Two limitations are explicit rather than hidden:

- OS-level per-process disk-read bytes are not currently collected; thumbnail source-open and metadata-hash byte counters are the Core-owned I/O signal.
- dedicated GPU-memory usage is not collected because Core owns no AI/GPU model runtime. Avalonia compositor residency is platform-owned.

If either limitation becomes material to a real bottleneck, create a focused follow-up Issue rather than silently inventing a measurement.
