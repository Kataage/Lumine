# Issue #634 — actual wheel input versus direct ScrollViewer.Offset

**Status:** isolated synthetic headless input/raster diagnostic, not a claim of real physical Windows GPU/compositor acceptance.

## What the previous benchmark did (source-based audit)

`tools/Lumine.Viewer.Benchmarks/Program.cs` `MeasureSmallScrollAsync` advances `ScrollViewer.Offset.Y` by **198 pixels per step** for three steps. Despite the local variable names `forwardWheel` / `reverseWheel`, these calls **do not send pointer wheel events** and cannot prove the routed input pipeline was exercised. The previous `PR #654` first-offset Skia A/B evidence demonstrates *direct-offset scrolling* raster behavior, not precisely physical wheel input.

In the actual Viewer, `OnGalleryWheel` is registered for `InputElement.PointerWheelChangedEvent` with `RoutingStrategies.Tunnel` so it can record direction **before** Avalonia scroll containers handle the event. `OnGalleryOffsetChanged` is a separate `ScrollViewer.OffsetProperty` observer. These are related but not equivalent signals. Direction mismatches after programmatic `ScrollToAsset` jumps are not resolved by merely tuning source prefetch.

## Authoritative references used

- [Avalonia.Headless.HeadlessWindowExtensions.MouseWheel implementation](https://github.com/AvaloniaUI/Avalonia/blob/main/src/Headless/Avalonia.Headless/HeadlessWindowExtensions.cs): the official `TopLevel.MouseWheel(Point, Vector, RawInputModifiers)` drives the headless input implementation and dispatcher/render-job flushing. Unlike setting `ScrollViewer.Offset`, this exercises Avalonia's real routed mouse-wheel event flow.
- [Avalonia headless platform guide](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform): documents headless input and rendered-frame testing, and why `UseSkia` with drawing enabled is necessary to inspect actual raster pixels.
- [Avalonia ScrollContentPresenter source](https://github.com/AvaloniaUI/Avalonia/blob/main/src/Avalonia.Controls/Presenters/ScrollContentPresenter.cs): wheel input is converted to scroll offset by the framework, with scale/logic depending on logical scrolling and modifier state; therefore 198 pixels of direct offset must not be labeled equivalent to one physical wheel tick.
- [Avalonia routing discussion](https://github.com/AvaloniaUI/Avalonia/discussions/18269): tunnel event handlers see routed input before normal child Bubble handlers. The existing Viewer already listens in this phase. We must still confirm event delivery, not infer it from changed offset.

## New independent measurement

The Viewer now exposes a monotonic `RoutedWheelEventCount` incremented **only** by `OnGalleryWheel`, not by `ScrollViewer.Offset` changes. This counter introduces no additional source requests and does not change the scroll direction algorithm.

A separate opt-in benchmark argument `--wheel-evidence-dir artifacts/viewer-wheel-input` runs **after all existing accepted direct-offset performance probes** on a fresh 10k synthetic viewport, with a 200ms dwell. It takes a point in the **actual inner ScrollViewer** mapped into window coordinates and calls `window.MouseWheel(point, new Vector(0, -1))` via Avalonia.Headless. It captures and audits two real Skia snapshots: **after one routed wheel tick** and **after four consecutive routed wheel ticks**. The wheel input event count must increase by **at least four** and the actual scroll offset must advance both after the first and after the fourth ticks; otherwise the diagnostic **fails closed** rather than pretending the test exercised user input. The images use the same seven-column deterministic blue/dark raster sample classifier from PR #652. JSON records the actual routed wheel count, measured pixel displacement for one/four ticks, blue/dark pixel samples for each frame, and `small_scroll_input_kind=direct-scrollviewer-offset` to prevent comparing unlike scenarios.

CI archives both wheel raster PNGs and diagnostic JSON separately from normal 1-row/2-row 10k/50k/100k acceptance. The earlier timing, source ≤1600, decoded Bitmap ≤32MiB, virtualization, NativeAOT and portable gates are unchanged. **No GPU-present assertion:** `MouseWheel` creates routed input in a headless environment, but this is not a physical USB/Windows input event or a real display present fence.

## Next acceptance path

Use this event-authentic **wheel** fixture as a separate baseline alongside existing direct-offset probes. Only after a known wheel/input/raster behavior is collected should another scheduler integration be proposed. A later controlled A/B would have to use the same wheel input source, same viewport geometry, repeatable first and fourth raster capture timings, and the unchanged source/memory/NativeAOT limits, followed by physical Windows owner signoff. Issue #634 remains OPEN.
