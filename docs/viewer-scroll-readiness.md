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

The benchmark now exercises a real mounted inner `ScrollViewer` via its `Offset`: three successive ~198 DIP row-sized forward movements, followed by three reverse movements after the first full viewport becomes UI-ready. After each offset update, it samples **actually realized bitmap tiles intersecting the mounted inner ScrollViewer's pixel viewport** whose decoded `Image.Source` is not yet ready, then measures how long the remaining UI assignments take. A zero-realized-tile viewport is treated as not ready rather than successful. This explicit geometry-based check replaced the original coarse first/last asset-index enumeration after CI [#37917618371](https://github.com/Kataage/Lumine/actions/runs/37917618371) failed `Small-scroll benchmark left unloaded visible tiles after the readiness window`: the index-range approach could include cells that had been virtualized/recycled and were not actually visible, so its result could not distinguish a true blank from a measurement artifact. The replacement retains a strict readiness failure for tiles that genuinely intersect the viewport but remain unready, with offset/cache/decode counts in the error diagnostics. Do not classify the original CI failure as definitively a false positive until the revised metrics run. The benchmark exposes separately for forward and reverse directions:

- `small_scroll_*_steps` (3 each)
- `small_scroll_*_missing_on_first_frame` (sum of first-sampled not-ready image tiles; lower is better, but not automatically guaranteed zero)
- `small_scroll_*_max_ui_ready_wait_ms` (worst per-step wait for all sampled visible tiles)
- `small_scroll_*_warm_bitmap_hits` (decoded presentation reuse during each phase)

The Windows Viewer performance gate now requires these fields and enforces a bounded ready wait without relaxing the 1,600-request/10k fast-scroll, 32 MiB decoded cache, 100k virtualization or peak memory limits. **Do not invent a zero-blank release threshold before collecting a real baseline.** Review the cold/warm and forward/reverse results against subsequent changes.

**Important limits:** this test sets the real ScrollViewer offset, which exercises virtual row attachment and scroll-direction callbacks but is **not injected physical wheel input**. It counts UI-ready bitmap assignments, **not actual GPU composition/presented pixels**. It uses generated 512px image fixtures and cannot substitute for performance measurement with a representative real Windows library, actual wheel gestures and human product-owner acceptance. These remain open requirements of #634.

## #640 CI failure: decoded-cache admission starvation (2026-10-09)

This was a real blocked-viewport finding, **not** only a metric implementation bug. After revising the visible-tile measurement to count mounted intersecting controls, Windows CI [#37918681548](https://github.com/Kataage/Lumine/actions/runs/37918681548) still failed the first 198 DIP forward movement with **35 visible / 32 ready / 3 unready**; source in-flight and active bitmap decodes were both zero after the 1.5s window. Diagnostic CI [#37919703549](https://github.com/Kataage/Lumine/actions/runs/37919703549) isolated indices **28, 29, 30**, all still `loading`, with no provider failure and only one earlier cancellation.

The synthetic provider returned **512×512** WebP images. In the decoded cache a 512×512 RGBA Bitmap is charged **1 MiB**. With a strict **32 MiB** budget, at most 32 such distinct leased images fit. `DecodedBitmapCache.TryAcquire` waits on a capacity signal when existing bitmap leases prevent eviction, so a 35-tile mounted viewport can become stuck rather than merely slow. This explains the exact stalled state; it also highlights that decoded **presentation size**, not encoded thumbnail file dimensions, must participate in the memory budget.

Correction inside the same PR:
- The ViewerSession passes a dedicated `DecodedThumbnailMaxDimension` option (default **384 physical pixels**) to its thumbnail bitmap cache. The cache uses Avalonia's `Bitmap.DecodeToWidth` / `DecodeToHeight`, respecting thumbnail aspect ratio, and does **not upscale** already smaller thumbnails. The file/memory original and focused/full-resolution Detail path are unchanged. A raw decoded-cache caller retains its previous full-resolution behavior.
- Scaled-file cache keys are distinct from full-resolution file keys. The existing entry/byte limits, pinned lease lifecycle and cancellation behavior are unchanged.
- A deterministic headless regression constructs 35 independently keyed 512px presentation images with all leases held under the **32 MiB** cap, and requires each to be admitted within a bound at **384px** decoded size. The normal mounted 10k/50k/100k forward/reverse offset probes must also pass, as must existing fast-jump and NativeAOT gates.

**Verification limitation:** implementation and regression have been committed, but the final Windows CI and product owner acceptance must be checked before saying the defect is fixed. At very wide/large virtualized viewports, a hard entry/byte cap can still make a fully leased working set larger than the budget; this must be covered by viewport-size/scaling tests and an admission-pressure policy rather than loosening memory caps. No claim is made that GPU-composited physical wheel scrolling is blank-free.

## Settled forward scrolling versus immediate scroll (sixth #634 step)

[PR #640](https://github.com/Kataage/Lumine/pull/640) passed all 50 Windows CI checks and merged, but its immediate post-first-viewport-ready probes still show **20–21 not-ready first-frame tiles across 3 forward 198 DIP offsets**, **zero forward decoded warm-presentation hits**, while the reverse phase reports **zero first-frame misses and 14 bitmap reuse hits**. The previous 32 MiB / 512px fully pinned cache stall was corrected by bounding decoded Viewer presentation size; remaining forward blanks now require a distinct diagnosis, not an arbitrary increase in the cache budget.

The next diagnostic keeps the original immediate forward + reverse probe completely intact and adds a second forward probe after jumping to a **different, previously unvisited middle-library viewport**. It first waits for that viewport to be UI-ready, then allows a fixed **200ms natural browsing dwell**, and finally uses three one-row mounted ScrollViewer.Offset moves. It records `small_scroll_settled_forward_{steps,missing_on_first_frame,max_ui_ready_wait_ms,warm_bitmap_hits}` separately for 10k/50k/100k fixtures.

Interpretation:
- If settled forward lookahead reuses decoded bitmaps and reduces first-frame blank tiles, the scheduling mechanism works after idle but is late for immediate scrolling; optimize its lead time and cancellation behavior while measuring first-paint regressions.
- If it is still cold after the dwell, inspect the coalesced lookahead anchor, source fetch timing, predecode eligibility and cancellation/supersession before adjusting the scheduler.
- Neither 200ms artificially waiting nor UI `Image.Source` readiness establishes a zero-blank claim for real Windows wheel input. Keep the 32 MiB / 1600 request / virtualization / NativeAOT gates intact, and collect owner real-library interaction evidence before closing #634.

## Diagnose why 200ms rested forward scroll is still cold (seventh #634 step)

The [PR #641](https://github.com/Kataage/Lumine/pull/641) mounted Windows benchmark passed full CI and confirmed **21 first-frame misses in 3 settled forward scrolls and zero decoded warm bitmap reuse** at **all 10k/50k/100k fixture sizes**, even after a 200ms dwell at an unvisited middle-library region. This eliminates the simple explanation that users scroll too soon after the initial viewport is loaded; it does **not** prove that lookahead never runs, because the prior metric counted successful bitmap reuse on tile attachment only.

In the next bounded diagnostic, the Viewer publishes inexpensive monotonic lookahead-stage counters and the last scheduled row/direction + attempted decoded row. The 200ms dwell benchmark snapshots the counters *before and after the dwell*, separately from new decodes after the ensuing three small scroll steps. The Windows gate validates that the fields exist and are nonnegative without inventing a pass threshold. This discriminates:

- **Scheduled but source prefetch never completed**: inspect CTS cancellation/supersession and background source work.
- **Source completed, decode never eligible**: inspect the foreground-ready/active-decode gate (and any attached-vs-ready mismatch).
- **Decoded some tiles but warm reuse remains zero**: inspect whether the *actual next visible row* matches the last attempted warm index, whether its bitmap is still resident under pinned cache pressure, and whether cancelled tasks evict the expected row.
- **Decoded hits only after movement**: predecode scheduling is too late for small gestures; confirm actual user-relevant lead time before changing priorities.

This instrumentation does not change decode, prefetch, cache or attachment scheduling semantics. Do not construe headless UI readiness as actual GPU compositing; keep Issue #634 open until real Windows-wheel owner acceptance.

## Measured reverse-direction bias after a jump (eighth #634 step)

Windows CI [#37922983535](https://github.com/Kataage/Lumine/actions/runs/37922983535) on PR #642 passed 50/50 steps. The new 200ms-dwell snapshot made the root cause concrete: all 10k/50k/100k cases completed **7 offscreen Bitmap decodes** during the dwell, but the last lookahead direction remained **-1** (upward), inherited from the preceding jump from the library end into its middle. The last attempted warm Bitmap index fell **one row before the last visible row** (for example 10k: warm index 5145 versus last visible 5158). Three immediately following **forward** 198-DIP steps therefore each exposed a cold row: 21 first-frame misses and **zero Bitmap reuse hits** in every fixture.

This is not a generalized prefetch-execution failure, nor a reason to loosen the 32 MiB cache. The existing direction-aware warmup prepares only one row based on the **last observed movement direction**, which a programmatic jump cannot reliably use to predict the user's next gesture.

The follow-up code keeps the same bounded source lookahead and foreground-first decode gate, prepares the previously preferred adjacent row **first**, and then opportunistically pre-decodes the opposite one-row neighbor **only if the worst-case full attached viewport plus both adjacent rows fit within the existing bitmap entry/byte limits**. At standard 35-tile/7-column, 384px-decoded, 32 MiB / 64-entry benchmarks, there is sufficient headroom; the extra row is skipped for larger/pinned viewports or tighter policies. The extra work shares the same cancellable coalesced lookahead task; rapid jumps still cancel speculation. No original/detail decode or cache limit is modified.

Regression requirements: the settled 200ms-forward probe must demonstrate **at least one actual decoded warm Bitmap reuse hit** and at least eight bitmap preparation events across the two directions, while existing immediate forward/reverse, 10k fast-scroll **1,600 requests**, 32 MiB, 100k UI virtualization, memory, NativeAOT, Windows Portable and lifecycle gates all remain enforced. Headless UI assignment remains distinct from physical Windows wheel/composition; owner acceptance remains a separate blocker until verified.

## Prepare actual outside-viewport neighbors (ninth #634 step)

[PR #643](https://github.com/Kataage/Lumine/pull/643) passed Windows CI 50/50 and merged as `f8342d97`. The 200ms settled-forward 3-row probe now has 3 / 5 / 6 Bitmap cache hits at 10k / 50k / 100k assets, versus zero before; missing first-frame tile counts are 18 / 16 / 15 versus 21 previously. This is measurable improvement without relaxing 32 MiB decoded bitmaps or other acceptance constraints. The immediate cold forward probe remains at 21 missed tiles and zero warm reuse at every size, however.

A follow-up audit found that `ScheduleLookahead` receives a **newly attached virtualized row**, and the old preferred `rowIndex - 1` can still lie *inside the visible viewport*, especially after a programmatic upward jump. Decoding such an already-visible row consumes the 200ms dwell and bounded-cache capacity without warming the *true previous offscreen row*. Likewise the opposite direction should start after the **actual last visible row**, not the attached row index.

The correction derives nearest offscreen rows `first - 1` and `last + 1` from `GetFirstVisibleRowIndex` and `GetLastVisibleRowIndex`, using these boundaries for both source lookahead and Bitmap preparation. **Important CI correction:** the initial implementation sampled visible geometry immediately inside `ScheduleLookahead` during `AttachedToVisualTree`, before virtualized layout was stable; the Windows smoke failed to warm the first genuinely offscreen row (`target=24`). The fixed implementation resolves both boundary indices via the UI dispatcher **after the coalescing delay**, once visual layout has had an opportunity to settle. Failure diagnostics now print observed viewport boundaries and the lookahead pipeline's counters and last warm index. Before initial layout, fall back to the attached row. At the first/last library row, skip nonexistent neighbors and keep the valid side. Preserve the same coalesced cancellation task, one neighbor per side, foreground-first gate and worst-case 32 MiB / entry-capacity checks. Unit/smoke tests assert the offscreen indices for normal, first-row and prelayout cases, and attempt actual cached reuse on a neighboring offscreen visible row.

CI-derived headless UI assignment metrics, even if improved, still do not prove physical Windows wheel/GPU presentation or end-user experience. Maintain Issue #634 as OPEN until this evidence and owner acceptance.

## Start decoded adjacent rows before distant source lookahead (tenth #634 step)

[PR #644](https://github.com/Kataage/Lumine/pull/644) merged to `develop` as `12f6ec58` after all 50 Windows CI steps passed, including the repaired virtualization smoke and NativeAOT/portable checks. The settled three-step forward benchmark showed 17 / 18 / 14 first-frame misses at 10k / 50k / 100k versus 18 / 16 / 15 on PR #643; warm bitmap cache hits were 4 / 3 / 4 versus 3 / 5 / 6. This is **mixed sampling, not proven generalized latency improvement**, though the last decoded target now correctly lies just outside the visible viewport.

The next opportunity is a separate scheduling stage: the Viewer has ordinarily `PrefetchRows=2`, but its old lookahead awaited the source fetch for *all* rows before beginning even one adjacent decoded Bitmap. Real local libraries can take much longer to fetch/generate thumbnails for a distant lookahead row, unnecessarily delaying the neighbor a user is about to expose.

The scheduler now splits each already-bounded per-direction source range into **nearest one-row** and **remaining distant rows**. It requests the nearest row on the preferred side, then the nearest row on the opposite side; once those sources are available it waits for the existing foreground-idle gate and warms adjacent Bitmap rows under the existing 32 MiB/entry budget. Only afterward does it request more distant sources, with original directional ordering and the exact same total unique request range. If the user jumps away, cancellation can now avoid distant speculative work rather than issuing extra requests. `SourcePrefetchCompleted` continues to mean all planned source ranges (near and far) finished, not just the first two rows.

For `PrefetchRows=1` (the original three-size benchmark), no far rows exist and this is intentionally a no-op. The product/default `PrefetchRows=2` path is now measured **independently in the Windows CI**, using the same mounted 10k/50k/100k benchmark with `--prefetch-rows 2` and writing `viewer-prefetch2/viewer-N.json`. Both suites enforce the **same full Viewer performance acceptance limits**; the verifier checks an explicit `prefetch_rows` metadata field to prevent accidentally treating the legacy one-row test as a default two-row test. CI additionally prints the headless 200ms-settled 3-step first-frame-missing and warm Bitmap hit values **side by side** for both depths at each library size, and uploads the two-row `viewer-prefetch2/*.json` alongside the previous one-row baseline artifacts for independent review. Smoke coverage verifies disjoint correct source ranges, including reverse nearest-row positioning. Results must be compared before making any quantitative speedup claim; the current tests use synthetic thumbnails with a 12ms per-request provider delay, not a real filesystem library. All first-paint, fast-jump/1,600-request, 32 MiB, virtualization, NativeAOT/portable gates remain unchanged, and no real physical-wheel/GPU-present acceptance is implied.

## Refresh prefetch on same-direction visible-edge advancement (eleventh #634 step)

Source inspection after adding the default two-row performance suite found a further bounded-work blind spot: `SetLookaheadDirection` only schedules if the sign flips, and `ViewerRowControl` only schedules when a virtualized row **attaches**. A one-wheel movement can advance the *actual visible edge* into a row already realized in the virtualization buffer, without either event. Prefetch can stay anchored at the old viewport edge until another attachment. **First CI regression:** [run #37934895734](https://github.com/Kataage/Lumine/actions/runs/37934895734) failed at Viewer smoke because a 1-pixel scroll was followed by an additional edge-triggered schedule without any advance of the measured last visible asset. On a virtualized surface, the scheduled edge can temporarily be *ahead of* the instantaneous current edge after recycling/layout. Treating any *difference* as progress is incorrect. The revised guard requires **strict advancement in the current direction relative to the already-scheduled edge**; reverse/stale edge jitter is ignored. Regression tests explicitly cover forward/reverse progression, equality and retrograde edges; the mounted 1-pixel no-churn assertion applies when the last scheduled edge has caught up to the visible boundary. **Second CI failure:** [run #37935713088](https://github.com/Kataage/Lumine/actions/runs/37935713088) reached the same mounted Viewer smoke but failed `Lookahead diagnostics did not track a completed, eligible bitmap predecode.` This assertion coupled the *nearest-row Bitmap-warmed stage* to **full completion of distant source prefetch**, though PR #645 deliberately moved nearest Bitmap readiness **before** fetching distant rows. The test now checks the adjacent Bitmap stage as soon as ready, then separately waits for and asserts distant source completion under a stable idle viewport (with detailed per-stage diagnostic counters). This retains coverage of both stages without requiring the wrong execution order.

The follow-up change listens to the **actual inner ScrollViewer OffsetProperty** (already subscribed for scrollbar, keyboard and touch). On same-direction nontrivial offset motion it measures the current first/last visible row. It reschedules only if that directional edge differs from the edge most recently scheduled; existing direction-change events and row-attachment scheduling also update that edge marker, so small pixel deltas in one row do not repeatedly cancel/warm the same work. Rebuild or detach resets the marker to avoid stale keys. A smoke test advances the mounted scroll in the same direction across pre-realized rows and asserts at least one offset-specific refresh, then tests within-edge jitter does not trigger another.

The explicit objective is earlier next-row warming for **continued** forward browsing, not raising source work. This must pass **both** one-row and default two-row 10k/50k/100k performance acceptance, particularly the close-to-limit 10k 2-row request budget: the previous single Windows run recorded 1,587 requests against max 1,600. If offset-driven updates cause request churn or violate memory/shutdown limits, revise before merge. Headless first-frame samples and real Windows wheel/compositor acceptance remain separate; #634 stays open.

## Why decoded lookahead is not reused (twelfth #634 step)

[PR #646](https://github.com/Kataage/Lumine/pull/646) passed **53/53** Windows CI steps and merged into `develop` as `0c2a20a9`. Its single headless 200ms-settled three-small-scroll probe still showed **first-frame missing** tiles 18 / 19 / 8 at 10k / 50k / 100k with one source prefetch row, and 19 / 18 / 17 with two rows; neither setting consistently eliminates blank new strips. Two-row 10k had **1,372 source requests**, under the non-negotiable 1,600 limit. The 32 MiB decoded Bitmap peak remained ~31.5 MiB. These are one sample per fixture; they are **not physical Windows/compositor evidence**.

The next diagnostic distinguishes two root-cause classes at the precise **newly attached tile lookup** before visible loading:
- **No warm descriptor** for that asset index. Possible causes: the scheduler has not yet predecoded the target, that row was not selected, or its descriptor was trimmed.
- **Descriptor present, decoded Bitmap unavailable** in `BitmapCache.TryAcquireExisting`. Possible cause: bounded cache LRU evicted the Bitmap before the row entered view. Do not automatically classify every miss as LRU eviction without confirming why it was absent.
- **Warm hit** (existing semantics). The three mutually exclusive outcomes must exactly partition all warm lookup attempts.

The Viewer now publishes `ViewerWarmPresentationDiagnostics` counters without changing rendering, request priorities, cache limits or object lifetime. Headless small-scroll probes record **separate per-probe deltas** (cold forward, reverse, 200ms-settled forward), both for `PrefetchRows=1` and product-default `2`. CI rejects absent/negative counters or miss categories exceeding lookup attempts; mounted smoke verifies the full accounting identity. This is intentionally **diagnostic**, not a speculative performance fix. Follow-on cache retention/prefetch tuning must be guided by whether lack of predecode or lost Bitmap residency dominates, with full native/managed limits unchanged.

## Promoting one further directional source row to a decoded Bitmap (#634 follow-up)

[PR #647](https://github.com/Kataage/Lumine/pull/647) passed the 53-step Windows CI and merged as `68de8d65`. Its first 1-row and product-default 2-row CI measurements at 10k, 50k and 100k show **0** lookups where a speculative descriptor existed but its decoded Bitmap could not be acquired. Across 21 settled-forward tile-attachment lookups per fixture, there were **16–18 absent warm descriptors** (only 3–5 warm hits). Immediate cold forwards lacked descriptors for all 21 attachments. These are synthetic mounted UI findings, not proof of physical Windows-wheel presentation.

Rather than raise the 32 MiB Bitmap cache (which would not fix the measured missing-descriptor category), the next scheduler adjustment uses the existing *second* source-prefetch row of the **preferred scroll direction** as an opportunistically decoded Bitmap row. It retains the first preferred and nearest opposite Bitmap rows for reverse fairness. This third speculative row is admitted **only** if the worst-case sum of attached foreground tiles plus three 384×384 RGBA rows fits **both** the existing decoded entry and 32 MiB byte limits, the foreground is ready/idle, the grid columns can be completely decoded without violating the prior `nextRowCount` throttle, and `PrefetchRows >= 2`. For 35 attached tiles and 7 columns, 35 + 3×7 = 56 entries = 33,030,144 bytes (~31.5 MiB), under 32 MiB; 36 attached tiles exceed the byte cap and must skip this warmup.

To preserve the source request range, `PredecodeNextRowAsync` now returns its count of successfully predecoded indices. If all indices of that exact next directional row complete, the later far-range source-only prefetch excludes that row; if incomplete, the original far range remains eligible. No new background worker, deeper prefetch range, decode cache size, or foreground priority policy is introduced. A smoke test covers the 35-versus-36 attached-tile threshold and tighter entry/byte budgets. Both one-row and actual two-row suites still run at 10k/50k/100k with strict 1,600 request / 32 MiB / 100k virtualization and NativeAOT/portable gates.

**Potential downside:** preparing an extra Bitmap can consume native decode time and may add pressure to the LRU. This is a trial bounded by existing constraints, not a proven performance win. Compare first-frame missing counts and missing-descriptor proportions with PR #647 using multiple runs before accepting a latency improvement. Keep #634 OPEN until the real Windows owner test passes.

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
