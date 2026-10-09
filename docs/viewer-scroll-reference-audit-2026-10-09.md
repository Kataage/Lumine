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


## Controlled follow-up: offscreen realization without offscreen foreground I/O (PR #651 candidate)

The failed 0.25-buffer A/B proved two separable behaviors: (1) mounted Avalonia controls available earlier can reduce the *UI unready tile count* at the first offset, but (2) eagerly starting each tile's source request in `ViewerTileControl.OnAttached` increases source requests from **1,344 to 1,871** at 10k, violating the unchanged **1,600** cap. We should therefore avoid both unbounded overscan and the assumption that attached controls must immediately request thumbnails.

New experimental options are **default OFF**: `RealizationBufferFactor=0` and `DeferOverscanTileLoads=false`. Only a test configuration with `0 < RealizationBufferFactor <= 0.5` and defer=true creates Avalonia's built-in `VirtualizingStackPanel { CacheLength = buffer }` and delays **foreground** `StartLoad()` for visual-attached tiles whose actual coordinates do not intersect the inner ScrollViewer viewport. All attached tile bookkeeping stays truthful, and deferred tile controls can begin foreground loading when they become visible in a subsequent scroll offset or `LayoutUpdated` pass. Unattached/detached tiles are removed from the small deferred set; no global library scan. Lookahead's foreground-idle condition in this opt-in branch uses the **visible** `ViewportReadiness` instead of requiring all deliberately-unloaded overscan controls to become ready.

This is not the same as suppressing all speculative source work: the existing **2-row bounded background source prefetch** and warm descriptor lookup remain present. Only the *additional eager foreground requests created by offscreen control attachment* are gated. In particular, the extra row and source priority are not made larger. The main production branch keeps the original Avalonia default and eager behavior.

The existing production benchmark waits for **all attached tiles** to be ready before timing its first viewport; that is correct for the stock zero-buffer path, but would never terminate when the experimental path deliberately leaves attached **offscreen** tiles unloaded. The A/B harness therefore preserves the old control readiness requirement and, **only for the deferred trial**, requires every actually visible tile (`ViewportReadiness.UnreadyTiles == 0`) to be ready, with positive visible tile count and no active Bitmap decode. This is the intended product definition of viewport readiness, not an exemption for blank visible tiles.

CI A/B runs 10k/50k/100k with identical `PrefetchRows=2`, simulated provider, layout, UI size, source/decode limits and same strict existing policy at 0 vs 0.25-with-deferred-I/O. Compare **first-frame-unready**, **missing warm descriptor count**, **thumbnail request count**, **realized rows / attached tiles**, **decoded bytes**, first paint/viewport time, fast-scroll peak memory/virtualization and shutdown. The full JSON is uploaded even on failure. An independent 10k capture produces actual headless **Skia raster PNGs** for each variant without contaminating the acceptance benchmark runs. This is still not an actual Windows GPU-present fence.

### Acceptance / rejection

- **Reject** if 10k requests >1,600, decoded cache >32MiB, 100k virtualization cap >16 rows, attached tiles >128, incremental process working set >128MiB, or existing first-paint/viewport/shutdown/portable/NativeAOT gates fail. Do not relax original policy to justify a trial.
- The UI-ready first-frame misses and **Skia-rendered black slots** must improve on repeatable same-run controls without 10k request violations. A single run, even if green, is not enough for a product-default change.
- If this experimental variant fails, do not leave a growing chain of UI flags. Close without merge and document whether the blocker is geometry-driven load activation, layout/present timing, or background prefetch requests. Consider a cohesive viewport-driven tile scheduler separate from the `ListBox` container lifecycle.
- Issue #634 must remain **OPEN** until the user's physical Windows wheel/compositor display test passes.
