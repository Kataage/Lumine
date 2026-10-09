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


## 2026-10-10 — CI #37967740260: observed stale first tick and updated methodology

The corrected public routed-event approach **compiled** and delivered one wheel event, +200px scroll and forward (+1) direction. Its single non-dispatched timer tick reported **unchanged PNG SHA**, 7/7 blue samples, 0 visible unready, and the settled image was 7/7 blue. The fail-closed gate rejected `raw_wheel_first_tick_raster_changed=False`. This is **inconclusive**, not proof of an actual blank or proof that the compositor failed: the fixture uses uniform blue thumbnails and a scroll by approximately one tile row can be pixel-identical even after repaint. Also Avalonia's official headless documentation notes that layout and compositor dispatcher work may still be queued when only `ForceRenderTimerTick` is called.

The opt-in diagnostic now overlays a **16×16 top-right, non-hit-test witness pixel** from initial window creation. It starts lime, then switches to magenta **only after the routed wheel event is accepted**; this lies outside the existing seven bottom-row tile samples. On the saved *pre-input* PNG, the lime witness must be visible. The **one no-dispatch timer tick** is still captured separately, with unchanged raster/witness explicitly permitted and reported as stale/inconclusive. The probe then performs **at most three bounded pairs** of `Dispatcher.UIThread.RunJobs()` followed by exactly one forced render tick, recording the first PNG whose witness turns magenta. A changed PNG and magenta pixel, and valid seven-column painted/dark classification, are required. The normal fully flushed frame is saved last. If three controlled passes cannot paint the witness, it fails closed and preserves artifacts. It is not allowed to pretend the new post-dispatch frame is the first physical GPU/compositor-present frame.

The marker establishes that a **new headless Skia raster** was generated after the input; it does **not by itself prove the scroll contents were updated**, nor does it demonstrate no transient blank strip on actual Windows GPU. Black-tile counts at each checkpoint are observation data, **not a diagnostic gate requiring zero black**, to avoid hiding the defect. The original 10k/50k/100k performance acceptance, 10k <=1600 source requests, decoded <=32MiB, native virtualization, NativeAOT and portable default remain unchanged. No production Viewer/cache/scheduler behavior was modified.

If this bounded marker experiment succeeds, future product tests should compare identical input traces against actual changed paint timestamps and move beyond monochrome fixtures toward distinct per-row imagery. The real Windows product-owner scroll acceptance in #634 remains **FAILED/OPEN**.
