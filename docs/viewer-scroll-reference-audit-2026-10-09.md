# Issue #634 — Avalonia reference audit and controlled viewport-buffer experiment (2026-10-09)

Status: RESEARCH / A-B EXPERIMENT; product behavior unchanged by default. Do not close Issue #634 until real Windows owner acceptance succeeds.

## The user-observed defect and what our prior measurements actually prove

The user sees a conspicuous unloaded thumbnail strip on ordinary small scrolls. PRs #642–#647 introduced source and decoded-Bitmap prefetch, diagnostics, and scheduling refinements. The new PR #647 diagnostic separated each warm-tile lookup into (a) descriptor absent, (b) descriptor present / decoded Bitmap unavailable, or (c) a decoded warm hit. Synthetic Windows headless tests with 200ms dwell followed by three 198px forward offsets recorded 16–18 descriptor-absent lookups out of 21, with **zero** descriptor-present/Bitmap-unavailable lookups in the checked fixtures. These numbers do **not** prove whether the source lookup is the bottleneck, whether the row only becomes realized upon entering view, or whether the compositor actually painted a frame.

PR #648 tried decoding an extra directional prefetch row under the same 32 MiB cap. Its explicit admission telemetry from Windows run #37941115510 shows 0 extra-row attempts / 0 decoded during 200ms dwell for all three 2-row size fixtures, and 1 attempt / 0 decoded during subsequent scrolling for each. The experiment does not demonstrate any benefit and **was closed unmerged**; no further speculative depth/capacity expansions should be stacked onto it.

## External authoritative references examined

1. **Avalonia official performance guidance** (https://docs.avaloniaui.net/docs/app-development/performance) explicitly describes collection virtualization, the requirement to constrain the viewport height, a realized-elements **buffer** for smooth scroll, and UI element / layout overhead tradeoffs. The prose calls the buffer `BufferFactor`.
2. **Avalonia VirtualizingStackPanel API reference** (https://api-docs.avaloniaui.net/docs/P_Avalonia_Controls_VirtualizingStackPanel_CacheLength) gives the public CLR property `CacheLength`; 0.5 means 0.5 viewport retained on *each side*. The source at https://github.com/AvaloniaUI/Avalonia/blob/main/src/Avalonia.Controls/VirtualizingStackPanel.cs shows the default registered value **0.0**, with a validation range 0–2.
3. **Avalonia ListBox source** (https://github.com/AvaloniaUI/Avalonia/blob/main/src/Avalonia.Controls/ListBox.cs) configures a `VirtualizingStackPanel` as its default `ItemsPanel`, so leaving `ThumbnailViewerControl._rows.ItemsPanel` unspecified inherits the default no-explicit-buffer behavior.
4. **Avalonia in-code template sample / upstream Q&A** (https://github.com/AvaloniaUI/Avalonia/discussions/13556) demonstrates assigning `ItemsPanel = new FuncTemplate<Panel>(...)` with a `VirtualizingStackPanel`. The production project pins Avalonia **12.1.3** in `Directory.Packages.props`. Use the package compiler and pinned API as the final arbiter where online docs and version names differ.
5. **Avalonia image performance guidance** (https://docs.avaloniaui.net/troubleshooting/app-performance-issues) recommends using reduced-size thumbnail images and avoiding synchronous decode on the UI thread. Lumine already uses bounded thumbnail decode, so merely scaling new `Image` controls is not the right fix.
6. **Avalonia custom rendering docs** (https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering) distinguish UI-thread drawing and compositor/render-thread work. A tile being assigned `Image.Source` is not evidence it has been presented on the physical display.

## Code audit: why the earlier strategy is insufficient

- `ThumbnailViewerControl` constructs `new ListBox` with no explicit `ItemsPanel`/realization buffer; each `ViewerRowControl` constructs its tile controls during creation and schedules lookahead in `AttachedToVisualTree`. Each `ViewerTileControl.StartLoad` first tries the warm presentation dictionary. Once the next row is attached **only at the viewport boundary**, it can be too late for an unprepared tile to become visible during that frame.
- `GetFirstVisibleRowIndex` / `GetLastVisibleRowIndex` use container coordinates relative to the outer ListBox; `ViewportReadiness` uses the *inner ScrollViewer* viewport. These are **not guaranteed to be the same coordinate system**, especially with padding, scrollbars and item templates. A layout-aligned test should compare them; do not assume they are equivalent without measurement.
- The benchmark's `BenchmarkApplication.BuildAvaloniaApp` uses `UseHeadlessDrawing = false`; its `small_scroll_*_missing_on_first_frame` captures currently unready **UI tile state** after `Dispatcher.UIThread.RunJobs()`, **not a rendered bitmap/screenshot, present fence, or hardware GPU vsync**. So CI green cannot close the actual user-visible defect, nor justify claims of “zero blanks”.
- Two distinct caches must not be conflated: (`VirtualizingStackPanel.CacheLength` controls *realized UI elements*, while `DecodedBitmapCache` controls *native decoded Bitmap memory*). A larger virtualization buffer can itself generate more thumbnail requests/UI work and cause memory pressure: it is not a free solution.

## Controlled A/B design

Run exactly the same mounted headless benchmarks at 10k, 50k, 100k, `PrefetchRows=2`, identical provider (12ms simulated async source latency), 384px max Bitmap dimension, 32MiB decoded cache and unchanged limits.

- **Control**: `ViewerOptions.RealizationBufferFactor = 0`. This leaves the existing ListBox default `ItemsPanel` intact.
- **Trial**: `RealizationBufferFactor = 0.25`; explicit `FuncTemplate<Panel?>` producing `VirtualizingStackPanel { CacheLength = 0.25 }`. Only the native virtualized-element realization buffer changes. No additional source/bitmap prefetch depth.
- Preserve both prior one-row and two-row suites, and require full original Viewer acceptance gates for the trial. Compare `max_realized_rows`, `max_attached_tiles`, `thumbnail_requests`, `thumbnail_requests_cancelled`, `max_decoded_bitmap_bytes`, `peak_additional_working_set_bytes`, `small_scroll_settled_forward_missing_on_first_frame`, `small_scroll_settled_forward_no_descriptor`, `tile_attach_to_ui_ready_max_ms` and teardown/NativeAOT/portable. The JSON is retained separately; do not compare mismatched prefetch depths.
- **Fail closed** if requests exceed 1,600 at 10k, decoded cache exceeds 32MiB, virtualization/working-set bounds fail, foreground stalls or teardown fails. Do not weaken thresholds to force adoption. A single synthetic run showing fewer misses is **not enough**: repeat on a fixed baseline with cold/warm cases, plus real Windows physical-wheel + composited screenshot/present timestamps under the same representative library before changing the default from zero.
- If buffer A/B fails or worsens latency, investigate a **viewport-geometry-driven tiled virtualization approach** with explicit pre-realized destination rows and foreground-first bitmap retention, not another generic prefetch increase.

## Decision at this change

This PR supplies **a reproducible, reference-driven A/B test and an explicit audit**, not a product performance claim. Keep the production default at zero until the evidence supports a strictly bounded change. Record CI results in Issue #634 immediately; keep the Issue open for real Windows owner signoff.


## Final controlled A/B result — do not adopt 0.25 overscan

The final Windows [CI #37944166876](https://github.com/Kataage/Lumine/actions/runs/37944166876) uploaded all baseline and trial JSON despite strict trial failure. With the same 2-row source prefetch, 10k / 50k / 100k photos and 200ms-rested 3×198px forward offsets:

| Image count | First-frame unready, buffer 0 → .25 | Thumbnail requests, buffer 0 → .25 | Max realized rows | Max attached tiles |
|---:|---:|---:|---:|---:|
| 10,000 | 16 → 7 | 1,344 → **1,871** | 5 → 7 | 35 → 49 |
| 50,000 | 17 → 7 | 1,396 → **1,837** | 5 → 7 | 35 → 49 |
| 100,000 | 16 → 7 | 1,297 → **1,826** | 5 → 7 | 35 → 49 |

Bitmap peak stays 33,030,144 bytes (31.5 MiB), below 32 MiB, but **1,871 requests exceeds the unchanged 1,600 request ceiling**. The trial also has zero warm lookup hits, but only **14 newly attached warm lookups vs the control's 21**: overscanned rows remain attached, so no new warm dictionary lookup may be needed on scroll. Do not misinterpret zero warm lookup hits as proof of no UI readiness benefit; the alternative fails due to **I/O work explosion**, not because first-frame UI readiness did not improve.

Interpretation: **Early UI realization helps**, but an eager `OnAttached→StartLoad` policy turns extra offscreen controls into additional thumbnail requests during the fast-jump workload. A production implementation must decouple **cheap UI realization** from **unnecessary source I/O** with a bounded intentional foreground/neighbor priority scheme, without raising decoded memory or source request caps. Any change also needs a metric closer to *actual rendered pixels* than the current `IsReady` flag. [PR #649](https://github.com/Kataage/Lumine/pull/649) was therefore closed **without merge**.

## Separate Skia-rendered-frame evidence

Avalonia's [official headless testing guide](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform) and [HeadlessWindowExtensions source](https://github.com/AvaloniaUI/Avalonia/blob/main/src/Headless/Avalonia.Headless/HeadlessWindowExtensions.cs) document `CaptureRenderedFrame()` when the test uses `UseSkia()` with `UseHeadlessDrawing = false`. Lumine's benchmark already uses those settings, but did not previously capture a frame. The separate opt-in `--frame-output <path.png>` diagnostic now captures the Skia raster on the first offset of the 200ms-dwell forward small-scroll probe. CI runs a separate 10k instance **after standard performance acceptance** and uploads the PNG plus its JSON.

The screenshot demonstrates what Skia **rasterized in headless mode** at that specific observation point. Because snapshot rendering can advance timers/frame queues, the opt-in screenshot run must **not** replace the unchanged timing/requests acceptance suites. A Skia headless PNG is **not evidence of actual Windows hardware GPU/display present**, so Issue #634 still needs owner physical-wheel/compositor verification. This removes the prior assumption that `Image.Source` assignment alone proves the next frame rendered correctly.

Next research milestones: inspect captured frames; instrument viewport visible-vs-pre-realized tile stages / per-request direction and cancellation only where missing; then prototype separation of control realization from speculative source I/O under the same bounded budgets. No changes to product default overscan until a repeatable improvement is shown.



## Deferred-overscan experiment rejected and pixel-level acceptance baseline

The follow-up **PR #651** decoupled attached native 0.25-viewport overscan controls from foreground thumbnail I/O until they entered the viewport. [Windows CI #37947868054](https://github.com/Kataage/Lumine/actions/runs/37947868054) generated the matched 10k/50k/100k A/B JSON and headless Skia PNGs, but **failed** the unchanged 100k fast-scroll gate: **1,630.7939ms >1,500ms**. UI-unready after 200ms dwell and 3 forward offsets was control→trial **18→17 / 18→18 / 13→14** while requests were **1,525→1,561 / 1,568→1,515 / 1,486→1,533**. There was no consistent first-frame benefit despite requests staying under the 10k 1600 cap. The trial was **closed without merge**.

Crucially, the first-offset Skia PNGs (artifact `lumine-deferred-overscan-frames-37947868054`) show **seven of seven** blue thumbnail center pixels in the control versus **two blue / five dark placeholders** in the trial on the newly visible bottom row (1200×800 screenshot, y=710, seven evenly spaced thumbnail centers). The difference is visible in actual Skia rasterization; the aggregate three-step `small_scroll_settled_forward_missing_on_first_frame` metric does not capture this exact first-offset snapshot. The deferred trial recorded 1,780 deferred control registrations, 1,212 activations and 554 explicit discards across its full 10k benchmark, confirming that deferred loading really occurred. It is not correct to mark this experiment successful from its request count alone.

### Guardrail: measure rendered tile placeholders automatically

A separate, opt-in screenshot path in the unchanged `Lumine.Viewer.Benchmarks` now decodes its saved first-offset Skia PNG with SkiaSharp and samples **one fixed center pixel per gallery column** in the deterministic synthetic fixture's newly appearing bottom row, at y=0.8875 × image height (710 at 800px). It distinguishes the known plain **blue WebP** synthetic tile (approximately R48/G112/B197), the **dark** unrendered placeholder (approximately R21/G21/B24), and unexpected/other pixels. Each sample is counted exactly once. It records the image dimensions/column sample summary in the *independent* frame-diagnostic JSON, prints the counts, and CI rejects missing/inconsistent categories. This complements, but does not change or loosen, the established timing/source/memory performance suite. The rendering diagnostic must not be silently included in timed measurements.

This classifier is deliberately **fixture-specific**, not a universal computer-vision test for arbitrary real images or user photographs. It does not equate a headless Skia screenshot with physical Windows display/GPU-present behavior, and a single screenshot is insufficient to promote a product default. Future viewport-driven scheduling trials must report both the existing bounded request/memory/latency metrics and the **rasterized black-placeholder count**, ideally across multiple samples. A cleanly rendered first-offset frame without excessive I/O is the minimum condition to pursue real-owner validation.

### Architectural next step (do not prematurely merge another heuristic)

Both tested extremes fail:

1. Eagerly loading every offscreen realized tile: the native 0.25 buffer reduced synthetic 3-step UI-unready values, but grew total source requests beyond the 1,600/10k cap.
2. Deferring thumbnail I/O until a tile actually intersects the viewport: request count stayed bounded, but the next visible row could still be black in the **first rasterized frame** and 100k fast-scroll exceeded 1.5s.

A candidate next architecture should use a single **viewport-derived image request coordinator**. It should know the currently visible row range and the nearest imminent row *before* attachment, prioritize foreground and one directional neighbor with explicit per-scroll-revision cancellation, and suppress far-away row requests after large seeking. Avalonia's virtualized controls would remain cheap reusable presentation surfaces, and may be realized ahead of time, **without binding request scheduling to `AttachedToVisualTree`**. Replace per-row event-driven lookahead with one coordinator in a dedicated, separately testable Issue #634 PR; do not layer more ad-hoc prefetch flags onto the failed experiments. Existing 32MiB decoded cache, 1,600/10k source requests, 100k virtualized-row cap, portable NativeAOT and teardown boundaries are invariant.
