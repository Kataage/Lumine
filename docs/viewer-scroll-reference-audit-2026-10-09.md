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
