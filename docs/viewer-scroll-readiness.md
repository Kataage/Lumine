# Lumine v2 — thumbnail scroll readiness (P1)

Tracking: [#634](https://github.com/Kataage/Lumine/issues/634)  
Owner acceptance: **FAILED** on 2026-10-09  
Related product-quality redesign: [#635](https://github.com/Kataage/Lumine/issues/635)

## Observed user failure (real Windows)

A small amount of scrolling exposes unloaded thumbnail tiles; the user must wait for them to appear. Core cold/warm benchmark success is not proof that the image gallery is ready for normal browsing. Normal sequential scroll should show images without a conspicuous blank strip.

Do not replace this requirement with loading animations or cached placeholders. They can communicate exceptional long jumps, but must not hide lack of readiness for typical small scrolls.

## Current implementation findings

From `src/Lumine.Viewer/ThumbnailViewerControl.cs` and `ViewerSession.cs`:

- Each realized `ViewerRowControl` schedules background thumbnail lookahead on visual attachment. Detaching cancels its work.
- Previously each row waited for `AttachedTiles == ReadyTiles` before also waiting `PrefetchDelay`. With any pending visible tile, lookahead could be postponed until *after* the user's first scroll.
- Its default lookahead was one row in both directions; backward work was submitted before forward work.
- `ViewerSession.PrefetchAsync` warms the metadata/thumbnail-source path with Background priority. It does **not** necessarily decode the Avalonia presentation bitmap; the visible `ViewerTileControl.LoadAsync` still calls `BitmapCache.AcquireAsync`.
- `DecodedBitmapCache` is explicitly bounded and ref-counted; any subsequent bitmap prewarm must maintain its byte/entry limits, composition-release ownership and shutdown drain.
- The app's product default is MemoryOnly; do not silently switch to permanent on-disk thumbnails.
- These findings establish a plausible scheduler latency, **not proof that it is the only user-visible bottleneck**. Instrumentation and representative-library measurements are still needed.

## First bounded intervention (PR for #634)

1. Start opportunistic background lookahead after only a short foreground-submission grace period, without waiting for every visible tile to finish.
2. Request the forward two rows before reverse rows. Keep low-priority background work coalesced with eventual foreground tile requests.
3. **Coalesce speculative work at the owning Viewer level** instead of starting separate before/after prefetch queues from *every* realized row. New row attachments cancel and supersede the old speculative range; detach/rebind cancels outstanding work. This requirement was added after CI #37910288282 measured **1,775 thumbnail requests** in the 10,000-image fast-scroll workload, exceeding the **1,600** request budget. The very first eager per-row attempt failed the existing performance gate even though functional Viewer smoke passed. Never weaken the benchmark limit to hide that regression.
4. Keep `ViewerOptions.PrefetchRows` small and explicit (2) and `PrefetchDelay` short (8 ms) so the virtualized row population remains bounded; **do not** introduce permanent full-library UI objects or unbounded queue allocations.
4. Add deterministic Viewer smoke with *foreground deliberately blocked*, asserting background lookahead begins while the viewport is still loading, followed by normal visible readiness. Keep existing 100k virtualization, bitmap-resource and shutdown tests.

This is a **first correction only**. It primarily warms source thumbnails, not necessarily final decoded presentations, and does not guarantee eliminating all short-scroll blanking.

## UI-ready stage instrumentation (second #634 step)

The Viewer currently tracks foreground thumbnail requests and decoded-cache occupancy, but the product owner's visible blank-tile complaint requires a **separate UI-ready measurement**. A tile's `ViewerTileControl.StartLoad` timestamp now begins when it is attached/retried, and the successful `Image.Source = Bitmap` assignment ends the interval. Reported as **UI source assignment**, *not a GPU composition or physically painted frame measurement*.

The new `ViewerTileReadinessDiagnostics` counters include:

- `Started`, `Ready`, `CancelledBeforeReady`, `ReadyFromBitmapCache` (return without source/decode work)
- Mean and maximum attach-to-UI-ready time
- Mean time spent fetching asset metadata, acquiring the encoded/source thumbnail, and acquiring/decoding its Avalonia bitmap on the non-warm path

The 10k/100k `Lumine.Viewer.Benchmarks` JSON now records these independently of `viewer.first_viewport_ready` and `viewer.fast_scroll_refresh`, so CI can expose an apparently fast scan that still spends time preparing each tile. The existing performance budget stays enforced; no new memory allocations proportional to library size or full-resolution image decode are introduced.

**Interpretation limits:** the measurements represent all attached/realized tiles, not just those inside the strict pixel viewport. A small nonzero UI assignment delay does not establish a physically blank frame, and the current generated fixtures are synthetic. The next targeted change should introduce a viewport-visibility timestamp and cold/warm representative-library small-wheel interaction benchmark before setting quantitative targets and considering low-priority decoded-bitmap prewarm. This issue cannot close without real Windows owner scroll acceptance.

## First bounded decoded-bitmap lookahead (third #634 step)

The preceding CI telemetry in `lumine-core-baselines-37912236670` showed that the 10k synthetic fast-jump workload started 1,118 tile loads but cancelled 1,030 before completion. It measured about 72 ms mean attach-to-UI-ready for the loads that finished, about 22 ms mean source wait and 4 ms mean bitmap acquisition. The workload is aggressively jumping and **must not be called a measurement of normal mouse-wheel scroll**.

The next implementation step therefore opportunistically **predecodes one adjacent forward row after all currently attached tiles are ready**, rather than just prefetching the encoded source. It is deliberately bounded by `min(columns, 8, decodedEntryLimit / 8)`, at most one row and never ahead of active foreground decodes. It uses the existing byte/entry-limited `DecodedBitmapCache` to hold the unleased bitmap and the viewer's existing warm-presentation descriptor so `StartLoad()` can attach the decoded bitmap immediately. All work shares the viewer-owned cancellable lookahead task: a new scroll, rebind or detach cancels stale work, and teardown drains the task.

Offscreen warm-up failures are logged but not counted as failures of any visible tile. The real visible request still retains its existing error and retry UX. Neither the cache budget nor the existing fast-scroll request-count/performance acceptance limits change.

The headless test exercises a **small forward scroll**: hold visible foreground loads pending, verify low-priority source lookahead starts, allow foreground to become ready, wait for an immediately following virtualized row's bitmap to be prepared, scroll into that row, and assert the decoded warm bitmap is reused on attachment without requesting/re-decoding it.

Remaining release work: measure actual real Windows natural wheel-scroll visual gaps across varied image formats and low/high density; adapt range to scroll direction/velocity while respecting cache pressure. The test validates `Image.Source` readiness, not a physical GPU frame-present fence. The final #634 acceptance must be verified by the owner.

## Direction-aware adjacent-row warmup (fourth #634 step)

After PR #638 made one next-row bitmap reusable on small forward scroll and passed Windows CI 50/50, lookahead still always treated "next" as **down**, even when the user reverses scrolling direction. This wastes speculative source/decode work while returning to previously unseen rows.

The owning `ThumbnailViewerControl` now captures the actual scroll direction:
- Tunneling `PointerWheelChanged` captures wheel intent *before* the virtualized ListBox reattaches rows.
- The viewer subscribes directly to its mounted inner `ScrollViewer.OffsetProperty` to track actual vertical offset changes, including keyboard, scrollbar and touch/inertia. A parent-only routed ScrollChanged listener failed the headless reverse-scroll test and is deliberately not used.
- Zero/tiny offsets and layout-only extent/viewport changes do not change direction. An actual direction reversal reschedules the coalesced lookahead immediately from the nearest visible row, even when the same set of rows remains realized, so the old-direction queue is not left active until a future row attach.
- For downward movement, start source-thumbnail lookahead below the newly attached row; for upward movement, start above it. Decode at most one bounded nearest **directional** row only after the current visible rows are ready and active bitmap decoding is idle.
- Reverse at row zero skips negative-index preparation. Existing cancellation and coalescing, limits, and shutdown ownership remain unchanged; the Viewer does not intercept scroll gestures or set event `Handled`.

Headless Viewer smoke now uses an actual mounted internal `ScrollViewer.Offset` change down and back up (not only calling `ScrollToAsset`), and checks that lookahead direction updates, plus bounded next-row target selection. This is a deterministic simulation of an offset change; **real Windows wheel perceived latency is still not measured**. Preserve existing Viewer fast-scroll performance gate rather than increasing it.

## Mounted small-offset scroll benchmark (fifth #634 step)

The existing 10k/50k/100k Viewer benchmark exercised 40 scattered `ScrollToAsset` seeks: it stressed cancellation and virtualization, but did not measure the user's actual complaint that **one small movement exposes unloaded images**. A green jump benchmark did not tell us what appeared during normal browsing.

The benchmark now exercises a real mounted inner `ScrollViewer` via its `Offset`: three successive ~198 DIP row-sized forward movements, followed by three reverse movements after the first full viewport becomes UI-ready. After each offset update, it samples the first realized viewport for visible tile entries whose decoded `Image.Source` is not yet ready, then measures how long the remaining UI assignments take. The benchmark exposes separately for forward and reverse directions:

- `small_scroll_*_steps` (3 each)
- `small_scroll_*_missing_on_first_frame` (sum of first-sampled not-ready image tiles; lower is better, but not automatically guaranteed zero)
- `small_scroll_*_max_ui_ready_wait_ms` (worst per-step wait for all sampled visible tiles)
- `small_scroll_*_warm_bitmap_hits` (decoded presentation reuse during each phase)

The Windows Viewer performance gate now requires these fields and enforces a bounded ready wait without relaxing the 1,600-request/10k fast-scroll, 32 MiB decoded cache, 100k virtualization or peak memory limits. **Do not invent a zero-blank release threshold before collecting a real baseline.** Review the cold/warm and forward/reverse results against subsequent changes.

**Important limits:** this test sets the real ScrollViewer offset, which exercises virtual row attachment and scroll-direction callbacks but is **not injected physical wheel input**. It counts UI-ready bitmap assignments, **not actual GPU composition/presented pixels**. It uses generated 512px image fixtures and cannot substitute for performance measurement with a representative real Windows library, actual wheel gestures and human product-owner acceptance. These remain open requirements of #634.

## Required follow-up work before closing #634

1. Record a scroll-into-view timing trace with at least: metadata pagination latency, decode/cache source latency, decoded bitmap cache acquisition, tile `Attached`→`Ready` latency, viewport direction/velocity, prefetch queue age/cancellation and cache hit rates.
2. Benchmark cold and warm 1-wheel-notch, two-wheel-notch, sustained forward scrolling, immediate direction reversal, long jumps and Grid/List; use real representative images, not only synthetic tiny fixtures. Include MemoryOnly, bounded RAM/decodes, 900/1440 layout and 225% scale.
3. Use the trace to implement a bounded viewport-level, direction-aware scheduler; prefer next likely rows, coalesce duplicate work, allow already visible foreground to preempt speculative decode and cancel stale work on jump/rebind.
4. If source warming is insufficient, explicitly **pre-decode** a few likely next tile presentations via the bounded `DecodedBitmapCache` with safe lease lifetime, without starving foreground/native decode or causing shutdown leaks. Measure how many warm bitmap hits result.
5. Review real Windows before/after behavior with the product owner. One PR/Issue step at a time; CI green is necessary but insufficient.

## Do not regress

- bounded memory/cache, default MemoryOnly, asynchronous cancellation/exception observation, native/composition ownership
- 100k-library virtualized row/UI count, smooth interaction when rapidly reversing or jumping
- main viewport priority and selected image interactivity
- NativeAOT / portable / clean shutdown / cold/warm resource regression gates

## External reference

Avalonia's `VirtualizingStackPanel.CacheLength` can retain extra realized elements around the viewport, but larger UI buffers consume memory and may increase GC/decode pressure; it must not be increased blindly as a substitute for measured prefetch: https://api-docs.avaloniaui.net/docs/P_Avalonia_Controls_VirtualizingStackPanel_CacheLength
