# Viewer idle progressive prefetch — Issue #805 (2026-11-08)

## User-facing behavior

The Viewer exposes the entire asset result as a virtualized scrolling collection. There is **no UI page limit** or "load next page" action. `MetadataPageSize=256` and a bounded page cache are internal cursor-pagination resources; they must not be replaced with unbounded asset metadata lists.

The user requested preparing images ahead of scrolling, while an abrupt jump should prioritize the **new visible viewport** rather than intermediate pages.

## v2 implementation stage 1: bounded idle expansion

- The existing visible viewport (foreground) and directional near-lookahead (background) remain unchanged.
- After the existing source lookahead completes, the current viewport may spend idle time preparing additional decoded bitmaps *without waiting for a scroll event*. This includes a far seek **after** its foreground target is ready; intermediate destinations are never prefetched.
- Admission is deferred by 300ms to give normal interaction an opportunity to supersede stale work. The existing coalesced lookahead cancellation token cancels the work on changed viewport or rebind; its 5-second timeout remains intact.
- `ResolveIdleProgressiveRowsForSmoke` computes additional rows from **both** free decoded-bitmap cache entries and free decoded byte budget, reserving at least one visible viewport's worth. Each batch reevaluates foreground readiness and remaining space.
- Source and decoded bitmap work uses existing background-priority `PredecodeNextRowAsync` and capped worker concurrency. The next likely row is promoted as MRU after idle expansion.
- The original two-viewport ceiling is **not** a total limit. Idle work advances in two-row batches, re-evaluating decoded cache headroom and foreground readiness between rows, and yielding 25ms between batches. It stops at the dataset edge, resource exhaustion, viewport change, or the inherited five-second lookahead cancellation deadline. These are runtime/resource safeguards, **not** user-visible pages.
- Disabled prefetch (`PrefetchRows < 2`) does not trigger idle expansion. Far seeks first prioritize rendering the new foreground target; only afterward can the idle preparation resume from that new viewport.

## Why not precompute the entire library in MemoryOnly mode?

The normal portable product uses `ThumbnailStorageMode.MemoryOnly`. Eagerly decoding all files would evict decoded images needed soon, consume excessive CPU/VRAM/RAM and might violate #788's strict idle quiescence gate. The capacity limit remains mandatory even when the virtual list includes 100k images.

Persistent background thumbnail generation for all assets would be a separate, explicitly user-enabled storage policy with a disk budget, pause/resume controls, invalidation, and cleanup. No automatic disk usage is introduced by #805.

## Acceptance and outstanding verification

Stage 2 also adds mounted Viewer tests that require idle preparation to cross the old two-viewport ceiling and resume after a far jump at its destination. Full Windows CI, NativeAOT, real-library cold/warm timing, 10s idle zero-inflight, and physical review of this stage are not yet established. Do not claim a performance win or close #805/#634 before those checks.

Confirm that changing the visible row, direction, filter, dataset, or closing the viewer cancels obsolete idle work, and the new viewport remains higher priority. Compare actual visible paint timing and background worker counts against develop.

Related: #634 (short-scroll visible blank), #635 (product UX), #788 (strict idle worker completion), #289 (bounded 100k virtual Viewer).
