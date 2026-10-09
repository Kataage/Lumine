# Issue #634 — unflushed raw-wheel first-tick raster experiment

**Status:** separate headless diagnostic only; no production changes and no claim of physical Windows GPU-present validation.

## Why the prior green input test is not first-frame proof

[PR #656](https://github.com/Kataage/Lumine/pull/656) passed **all 60 Windows-core CI steps** and was merged to `develop` as `47981d69`. In its synthetic 10k screenshot run, four *genuine routed headless wheel events* advanced actual offset 50px after the first and 200px after the fourth; the live input direction changed from -1 after a programmatic distant seek to +1 after a forward wheel. The first and fourth saved Skia PNGs both contained 7 blue thumbnails / 0 dark placeholders on the sampled lower row, source requests 1513 <=1600, decoded Bitmap peak 31.5MiB <=32MiB. The old `MeasureSmallScrollAsync` directly changed `ScrollViewer.Offset` by 198px/step and never sent wheel events.

**Upstream source review found a methodological caveat:** in pinned Avalonia **12.1.3**, `HeadlessWindowExtensions.MouseWheel` calls `RunJobsOnImpl`, which runs `RunJobsAndRender` **before and after** the wheel input. That helper calls `Dispatcher.UIThread.RunJobs` and `AvaloniaHeadlessPlatform.ForceRenderTimerTick` up to 10 times until stable; `CaptureRenderedFrame` also calls that helper. Thus the 7/7 blue result is **post-flush**, not proven to be the first frame immediately after an actual wheel event. See exact [source](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Headless/Avalonia.Headless/HeadlessWindowExtensions.cs).

Pinned [ScrollContentPresenter source](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Controls/Presenters/ScrollContentPresenter.cs) uses a **50px** fallback per wheel delta unit when logical scrolling is inactive, explaining the observed 50px/tick versus the earlier synthetic +198px per step.

## Diagnostic design based on upstream internals, WITHOUT linking an internal interface

Avalonia's **internal** `IHeadlessWindow.MouseWheel` ultimately invokes the top-level `Input` callback with `RawMouseWheelEventArgs(MouseDevice, Timestamp, InputRoot, Point, Delta, Modifiers)`. We verified the exact [12.1.3 HeadlessWindowImpl implementation](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Headless/Avalonia.Headless/HeadlessWindowImpl.cs). We must not compile Lumine against the **internal** interface.

Instead the **benchmark-only** `RawWheelFirstFrameProbe` uses the public (but explicitly *unstable/private API*) `ITopLevelImpl.Input` callback and a separately constructed `MouseDevice` to route one synthetic raw wheel event `delta=(0,-4)`. This reproduces one 200px equivalent input event **without the convenience helper's hidden dispatcher/render stabilization loop**. It does not change the Viewer or rely on this unstable API in production. The test asserts the actual Viewer routed-wheel handler saw one event, the offset moved forward and the current input direction became +1.

Immediately **after the synchronous input callback**, the test captures visible UI unready count. Then it calls `AvaloniaHeadlessPlatform.ForceRenderTimerTick()` **exactly once** and reads `window.GetLastRenderedFrame()` **without triggering additional job-flush passes**. The candidate first explicit renderer-tick PNG is classified by the already-merged fixture-specific blue/dark raster checker. It also saves the prior raster and checks a SHA256 digest to establish that the first-tick framebuffer **actually changed** (otherwise one might mislabel a stale frame as a new presented frame). Finally it runs the official `CaptureRenderedFrame()` convenience helper to get a stabilized frame after automatic dispatcher flush and classifies those same seven pixels. The first-tick and fully flushed images are kept side by side.

This is a **headless Skia first explicit tick**, *not* an actual physical Windows compositor present timestamp. If one forced tick never yields a new buffer under pinned platform behavior, the trial **fails closed and reports the limitation**, rather than claiming zero blanks. No current product behavior depends on these internals or on the result.

## CI, budgets, and admission

The experiment is strictly opt-in: `--unflushed-wheel-evidence-dir` on a **separate 10k benchmark run** using the same 2-row source prefetch. Its three PNGs and JSON are uploaded with `if: always()` ahead of assertion. The normal 10k/50k/100k Viewer performance suites remain byte-for-byte unchanged. CI checks one routed raw event, offset delta >0, direction +1, a **changed first-tick raster**, the seven blue/dark sample accounting for both first-tick and settled raster, <=1600 source thumbnail requests and <=32MiB decoded cache.

A new finding is useful only if it discriminates **first-tick blank pixels versus post-flush ready pixels**. We must not treat a synchronous raw input test with a 4-unit delta as equivalent to either four distinct physical detents or a real Windows GPU present fence. The next production change may only be proposed after comparing those separate input models and the original strict Viewer resource limits. Issue #634 remains OPEN until real Windows owner mousewheel/display verification succeeds.
