# Lumine Viewer — viewport request-coordinator A/B trial (Issue #634)

Status: **opt-in research; product default OFF; not an accepted speed fix**.

## Reference-driven motivation

The [Avalonia virtualized-items performance guide](https://docs.avaloniaui.net/docs/app-development/performance) and native [VirtualizingStackPanel CacheLength API](https://api-docs.avaloniaui.net/docs/P_Avalonia_Controls_VirtualizingStackPanel_CacheLength) distinguish realized UI controls from image I/O and decoding. In Lumine's prior Windows CI, native `CacheLength=0.25` produced earlier UI tile readiness but too many source requests (10k: **1344→1871**, cap **1600**), while deferring until visually intersecting the viewport produced black tiles in a rendered Skia frame and exceeded 100k fast-scroll time (**1630.8ms**, cap **1500ms**). Both PR #649 and #651 were closed unmerged, and the accepted [PR #652](https://github.com/Kataage/Lumine/pull/652) Skia pixel audit demonstrated actual black rasterized thumbnail cells.

[PR #653](https://github.com/Kataage/Lumine/pull/653) introduced a unit-tested, UI-free, bounded request planner and revision coordinator, merged after 55/55 Windows checks. The contract in [viewer-viewport-request-coordinator-contract.md](viewer-viewport-request-coordinator-contract.md) requires a **single** speculative owner rather than another per-row workload.

## Stage 2 experimental integration

`ViewerOptions.UseViewportRequestCoordinator` is **false by default**, so existing production behavior and all original 1-row and 2-row benchmark paths are untouched. The separate trial enables `--viewport-coordinator` at `PrefetchRows=2`.

In trial mode, the `ThumbnailViewerControl` routes **every** prior row-attach/offset/direction lookahead trigger to `RefreshViewportRequestPlan()`; it does not run the legacy `ScheduleLookahead` speculative worker. `RefreshViewportRequestPlan` uses **mounted inner ScrollViewer viewport coordinates** to determine the first and last actually visible virtualized row, then computes the next single directional neighbor from the planner. Increasing virtualized or realized offscreen UI elements cannot increase planned I/O. A `LayoutUpdated` callback rechecks geometry after Avalonia layout/recycling; identical plans are coalesced by the coordinator.

Each changed valid viewport revision cancels the previous speculative token, then executes **at most one** near-row `ViewerSession.PrefetchAsync` batch on the existing shared source request coordinator. The candidate decoded-Bitmap warmup starts **only once all foreground attached tiles are ready** and native decoding is idle; its known warm-descriptor dictionary and existing bounded Bitmap cache are reused. `PrefetchRows=0` or invalid viewport geometry generates no speculative I/O. A direction reversal, jump, visual detach or Viewer rebind cancels stale speculative tasks. This experiment deliberately does **not** add Avalonia `CacheLength`, new workers, cache sizes or a new source backend.

There is still a technical concern: if the nearest new visible row is not fully realized before first rasterization, even warmed Bitmap entries may be attached too late for the exact first displayed frame. This experiment will be rejected if rasterized black cell counts do not improve or if performance budgets regress.

## Acceptance

CI performs a control/trial comparison at **10k/50k/100k assets**, the same 2-row user setting, the same thumbnail fixtures/source latency, unchanged 32MiB decoded Bitmap cache, 10k <=1600 requests, 100k virtualization and first-paint/fast-scroll/shutdown/NativeAOT/portable gates. The trial's diagnostic metadata must show **more than 0 actual viewport plans**; otherwise the experiment did not run.

The CI runs a **separate** 10k control/trial headless Skia first-offset PNG capture, which is not used for timing acceptance. Each capture uses the merged **rendered-pixel audit** to count blue tile centers and dark unloaded placeholders. A screenshot's rendered Skia pixels are stronger than the `IsReady` flag but **still not physical Windows GPU present-fence evidence**.

The CI uploads JSON and screenshots **even on strict performance-gate failure**. No fixture, policy threshold or source-request budget is relaxed. If the trial fails or only produces a one-off synthetic improvement, keep the PR unmerged; the failed hypotheses and evidence belong in Issue #634. Only repeatable visual and performance wins justify a separate production-default adoption change, followed by owner physical-wheel acceptance. Issue #634 remains **OPEN**.
