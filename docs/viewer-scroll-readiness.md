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
