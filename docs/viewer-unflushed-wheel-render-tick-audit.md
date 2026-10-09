# Issue #634 — unflushed routed-wheel event vs post-flush Skia raster

**Status:** optional, diagnostic-only architecture research, **no product change**; not a physical Windows GPU-present test.

## Grounded prior evidence

[PR #656](https://github.com/Kataage/Lumine/pull/656) passed all **60** Windows-core steps and merged as `47981d69`. Its 10k headless fixture recorded four actual Avalonia-routed `MouseWheel` events; +50px after the first, +200px after the fourth; wheel intent -1 after programmatic seek → +1 after actual forward wheel. The lower-row first/fourth Skia snapshots each had **7 blue rendered thumbnails, zero dark placeholders**. Requests 1513 (<=1600), decoded cache 31.5MiB (<=32MiB). In contrast the older `MeasureSmallScrollAsync` did a **single direct +198px offset mutation per step** without sending any wheel event.

**Critical upstream reference constraint:** pinned Avalonia 12.1.3 [`HeadlessWindowExtensions.MouseWheel`](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Headless/Avalonia.Headless/HeadlessWindowExtensions.cs) invokes dispatcher jobs and `ForceRenderTimerTick` repeatedly **before and after** input (up to ten stabilization passes). The public `CaptureRenderedFrame()` helper also flushes. Consequently 7/7 blue in those PNGs is **a post-flush screenshot**; it cannot establish the absence of dark placeholders on an earlier headless frame. The [`ScrollContentPresenter` implementation](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Controls/Presenters/ScrollContentPresenter.cs) uses 50px per wheel unit when logical scrolling is disabled, matching the actual 50px/tick measurement.

## A failed private-API approach is explicitly rejected

Initial [PR #657](https://github.com/Kataage/Lumine/pull/657) CI [#37967072509](https://github.com/Kataage/Lumine/actions/runs/37967072509) failed at C# compilation:

- `ITopLevelImpl.Input` is **inaccessible** (`CS0122`)
- `MouseDevice` public no-argument constructor is **not available** (`CS1729`)
- `RawMouseWheelEventArgs` six-argument constructor is **not available** (`CS1729`)

These results demonstrate that reading a symbol in upstream implementation sources does **not** imply that it is part of the external, accessible NuGet API contract. We will **not** bypass these restrictions with reflection, `InternalsVisibleTo`, patched Avalonia assemblies or production links to unstable input internals.

## Revised approach with routed-event public API

Avalonia 12.1.3's [`PointerWheelEventArgs` public constructor](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Base/Input/PointerWheelEventArgs.cs) and [`Pointer` public constructor](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Base/Input/Pointer.cs) allow a headless **routed** wheel event to be constructed and raised on a hit-tested `InputElement` with `InputElement.RaiseEvent`. Unlike the `MouseWheel` helper, this avoids the helper's automatic pre/post dispatcher-render loop. It is **not raw platform or USB input**; it is an actual Avalonia routed `PointerWheelChanged` event delivered to both the viewer's tunnel handler and the scroll presenter's normal handlers. The opt-in test uses one aggregated wheel event with delta `(0,-4)`, nominally 200px. It must assert the viewer's routed event count advanced once, the real inner ScrollViewer moved forward, and the intent became +1. Any failure is reported, never silently treated as normal behavior.

The benchmark saves the pre-input Skia framebuffer; immediately after the synchronous `RaiseEvent` it records `ViewportReadiness.UnreadyTiles`, forces **one explicit** `AvaloniaHeadlessPlatform.ForceRenderTimerTick` (no convenience helper or extra dispatcher/render passes), then calls non-flushing `GetLastRenderedFrame`. It classifies that PNG using the merged seven-column blue/dark pixel analyzer. To guard against mistakenly calling the **old framebuffer** a new visual result, the saved pre-input and first-tick PNGs are SHA256 compared; the CI fails if they are identical. Finally a normal `CaptureRenderedFrame` flushes rendering and saves a stabilized PNG for contrast. The test reports both samples and their immediate UI-readiness metadata.

A forced render timer tick can be distinct from an actual completed compositor frame, especially if layout/visual state invalidations have pending dispatcher work. Therefore an unchanged PNG is a **failed/inconclusive** first-tick experiment, not evidence of no blanks; it may require a separately controlled single layout pass in future trials. Under no circumstances is this a physical Windows GPU present-timestamp claim.

## Strict A/B evidence safeguards

`--unflushed-wheel-evidence-dir` runs this as a **separate opt-in 10k synthetic benchmark**, after the original 10k/50k/100k direct-offset performance tests. It writes three distinct raster PNGs and JSON; CI uses artifact upload with `if: always()` **before** the strict gate. It requires one actually routed wheel event, positive delta, forward intent, a *changed* first-tick framebuffer and 7 accounted fixture pixels on both first-tick and post-flush images, requests <=1600 at 10k, decoded cache <=32MiB. All existing Viewer source/cancel/memory/first-paint/fast-scroll/shutdown/NativeAOT/portable criteria remain **unchanged**.

This is a research prerequisite before another product scheduler change, **not** a claim that the blank-row problem is fixed. Issue #634 remains OPEN pending real Windows owner mouse wheel / GPU compositor validation.
