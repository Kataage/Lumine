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
