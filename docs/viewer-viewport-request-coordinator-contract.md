# Lumine Viewer — viewport-driven image request coordinator contract

**Issue:** [#634](https://github.com/Kataage/Lumine/issues/634)
**Status:** Stage 1 architectural primitive + smoke tests. **NOT YET connected to production I/O.** Product defaults and current Viewer behavior remain unchanged until stage 2 passes the complete acceptance suite and actual Skia raster comparison.

## Verified motivation

- Avalonia officially documents [virtualization and viewport buffers](https://docs.avaloniaui.net/docs/app-development/performance) and the [native VirtualizingStackPanel CacheLength](https://api-docs.avaloniaui.net/docs/P_Avalonia_Controls_VirtualizingStackPanel_CacheLength). UI realization and thumbnail decoding are **different resource lifecycles**.
- [PR #649](https://github.com/Kataage/Lumine/pull/649), closed without merge: native 0.25-viewport overscan improved synthetic UI-ready first offset counts at 10k/50k/100k **16→7 / 17→7 / 16→7**, but fast-jump workload requests ballooned **1,344→1,871 / 1,396→1,837 / 1,297→1,826**, violating 10k cap 1,600. Attaching extra controls must not automatically create extra I/O.
- [PR #651](https://github.com/Kataage/Lumine/pull/651), closed without merge: deferring *every* offscreen control request until visible avoided the huge request increase but left Skia raster placeholders on first offset (7 blue controls vs trial 2 blue/5 dark in the 1200×800 controlled capture) and 100k fast scroll 1,630.8ms vs max 1,500ms. Waiting until visible can start work **too late**.
- [PR #652](https://github.com/Kataage/Lumine/pull/652), separate telemetry: fixed synthetic-frame pixel classifications distinguish painted thumbnails from dark placeholders, rather than relying only on `Image.Source` readiness. A headless PNG is still **not** real GPU/display-present evidence.

These experiments and their CI artifacts are documented in [the source audit](viewer-scroll-reference-audit-2026-10-09.md). The evidence supports neither an unconditional overscan nor a blanket defer-until-visible heuristic.

## Stage 1: pure, bounded planning, independently tested

`ViewerViewportRequestPlanner.Build` accepts **only** asset count, columns, first/last **visible** row, direction and an explicit near-future I/O *asset* cap. It is intentionally independent of:
- the Avalonia virtualizing panel's extra attached controls / `CacheLength`;
- the 10k, 50k or 100k library's entire item collection;
- view-specific present/fence logic; and
- the existing mutable per-row source request queue.

Outputs are two contiguous, separately prioritized ranges:
1. all **currently visible assets** (priority foreground once attached); and
2. at most **one immediate directional neighbor row**, further capped by `maxImminentAssets <= columns` (speculative background / decoder work).

No opposite second/third row, no unbounded far source prefetch, no room to increase the request window as more virtualized UI containers are realized. Before a viewport is valid, the plan is **empty**, rather than requesting a stale range. First and last partial rows are clipped; huge-count arithmetic is checked and does not compute `assetCount + columns - 1`.

`ViewerViewportRequestCoordinator` is a **single UI-thread-owned** revision state machine; identical plans are coalesced, while scroll direction reversals or fast distant seeks cancel the previous revision's token. An empty plan cancels everything speculative. Teardown cancels the last token and prevents new work. This class does **not** perform source requests or decoding in stage 1. The smoke suite explicitly tests bounded forward/backward ranges, partial library tail, zero/invalid geometry, huge Int64 asset counts, repeated offsets, reversal, far seek and shutdown.

## Stage 2: integration rules (not part of the present PR)

The next dedicated PR under **the same Issue #634** should connect exactly one coordinator instance to the **inner ScrollViewer's actual visible geometry** and one worker/batch execution path in `ViewerSession`. Do not silently take a realized row index as a visible row, nor use outer `ListBox.Bounds` as the inner viewport without rechecking geometry. The current `ScheduleLookahead` has calls from `ViewerRowControl.AttachedToVisualTree`; those must be removed or bridged rather than running a **second** speculative source path concurrently.

Direction intent must come from routed wheel / small scroll displacement, not from programmatic far jumps. On each actual viewport revision:
1. foreground visible tiles retain priority and existing normal retry/error handling;
2. a **single small directional imminent row** can be prefetched/decoded before entering view, within existing 32MiB decoded bytes, entry limits and max source requests;
3. no requests for the rest of the virtualized overscan controls;
4. cancellation and deduplication occur by revision, reusing shared in-flight thumbnail requests where possible;
5. the bounded speculative row must never delay ready visible pixels or interactive selected detail work.

Snapshot the visible geometry and revision once per coalesced update; do not have a background task enumerate UI containers. No custom scroll calculations that bypass Avalonia's documented virtualization geometry. Prevent stale or canceled worker completions from making offscreen guesses into authoritative current plans.

## Nonnegotiable accept/reject gates

- 10k source requests <= **1,600**, decoded Bitmap cache <= **32 MiB**; existing 50k/100k, <=16 realized rows, <=128 attached tiles, <=128MiB additional working set and startup/fast-scroll/shutdown/NativeAOT/portable thresholds unchanged.
- Measure repeatable **cold, 200ms rested, forward, reversal and far seek** trials; compare actual first-offset **Skia raster black tile samples**, not only `IsReady` UI flags. Do not reuse a screenshot-perturbed run for timing acceptance.
- Do not raise the prefetch depth or cache size to hide a failure. If the coordinator does not beat control in raster quality and simultaneously retain budget, revert/close unmerged.
- Physical Windows owner mouse wheel/GPU compositor validation remains mandatory before Issue #634 can be closed.

**Stage 1 is a contract and unit-tested cancellation primitive, not a completed performance fix.**


## Stage 2 A/B verdict — PR #654 rejected, not a production fix

[PR #654](https://github.com/Kataage/Lumine/pull/654) attempted to connect the stage-1 planner to the runtime, with the new route **opt-in** and the existing legacy lookahead disabled in that route. Its measured [Windows CI #37952664978](https://github.com/Kataage/Lumine/actions/runs/37952664978) **failed** at the original settled-forward requirement: `10000 Viewer settled forward scrolling still reused no decoded next-row bitmaps.` The test is **not** a no-op: each 10k/50k/100k trial reported 105 plan revisions, 58 activated plans and 7 predecoded Bitmaps. The 200ms-dwell **three-step** unready UI counts were, control → experimental route, **17→21 / 18→21 / 19→14**, while thumbnail source requests were **1400→1456 / 1368→1484 / 1366→1451**. The cache peak stayed 33,030,144 bytes, within 32 MiB.

The independent, screenshot-perturbed **first-offset Skia raster** A/B artifact `lumine-viewport-coordinator-frames-37952664978` was substantially worse: **7 blue painted thumbnail centers, 0 dark placeholders** in the existing control; **0 blue, 7 dark placeholders** in the new scheduler (1200×800, y=710). The trial's warm-lookups were **0 hits, 21 descriptor-absent** at 10k. This is a direct **rendered-pixel** regression even though the source-request and Bitmap byte limits passed.

The logged pre-scroll `LastScheduledDirection=-1` after a distant programmatic jump is **opposite** the subsequent synthetic forward scrollbar motion. It explains *which neighbor was speculatively decoded*, but cannot alone prove all causes of the first-offset black raster. A single-sided neighbor selection that infers browsing intent from a previous large seek is not a valid universal replacement for the legacy two-sided warmup. The trial was **closed unmerged**; stage-1 planner/cancellation primitives remain in `develop` without runtime activation.

### Non-negotiable follow-up gates

1. Compare **actual rendered first-offset pixels** for matched 10k synthetic control/trial; must never have **more black placeholders or fewer painted thumbnails** than the control. When the control has black placeholders, demand a **strict raster improvement**, not mere resource-limit compliance. Current fixture uses `RenderedFrameTileAudit`, a known blue/dark per-column classifier; it is **not** a generic photograph classifier or actual GPU-present fence.
2. Compare against the original **full** 10k/50k/100k first paint, settled/cold forward/reverse, 100k fast scroll, memory, lifecycle and NativeAOT/portable policies; no performance threshold, request allowance or decoded Bitmap cap may be increased.
3. Capture **browsing intent before layout/offset mutation**, and explicitly distinguish wheel/trackpad, scrollbar, programmatic seek and direction-unknown cases. Do not treat a large seek's displacement sign as the next scroll direction.
4. Instrument **time at which a specific soon-visible index was selected for prefetch, its source completion, decoded cache residency and first visible raster**. Counts alone are inadequate: 7 speculative Bitmaps from the wrong row do not constitute 7 useful first-frame warmups.
5. Do not add a second speculative queue atop the old one to inflate warm-hit statistics. One logical request authority plus deadline-aware foreground priority is required.

The strict raster comparison guard now has a reusable PowerShell implementation and ten deterministic acceptance/rejection cases in `build/Compare-ViewerSkiaRaster.ps1` and `build/Test-ViewerSkiaRasterComparison.ps1`. This guard protects future architecture trials; it does **not** claim Issue #634 itself is fixed. Real Windows mousewheel/compositor owner acceptance remains **OPEN/FAILED**.
