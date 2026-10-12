# Viewer background technical metadata coalescing — Issue #714

Date: 2026-10-12
Scope: `Lumine.App` production thumbnail metadata persistence (background requests only)

## Basis

PR #716 measured 10,000 guarded technical metadata writes and exposed SQLite COMMIT as the dominant elapsed phase, particularly when hosted Windows runner latency varies. PR #717 subsequently introduced `LibraryRepository.UpdateTechnicalMetadataBatchAsync` via `LibraryService`, keeping the existing 64-row hard bound, revision, file stat, normalized source identity and transaction safety.

## Product integration

`ViewerImageMetadataBridge.RequestThumbnailWithRepairAsync` now selects the coalescer only for `ThumbnailPriority.Background`. `Interactive`/`Foreground` thumbnails and original-image preparation use the existing single-asset commit path. The main thumbnail result is not returned until its technical metadata write is COMMITTED or a stale source is rejected, retaining the existing refresh/retry behavior.

`ViewerTechnicalMetadataBatcher` is per `LibraryService` (weak-keyed) and has at most 128 queued entries. One worker per service holds a short 2ms background-only coalescing window, then executes up to 64 adjacent writes for the same library ID under one production batch transaction. A singleton group uses the existing single-write API. Completion is per row, mapped to the original source version and identity guard. No caller receives a successful result before COMMIT. Canceled entries waiting in the queue are discarded; an individual caller's cancellation does not abort unrelated writes in a group. Once a shared transaction has started, that caller can still be persisted even if its waiting task is canceled, matching the unavoidable cancellation/commit race in SQLite.

Error handling completes all waiting callers with the same transaction failure, never falsely records their metadata as persisted, and releases every bounded queue slot. Foreground requests cannot be stalled by the deliberate coalescing delay (they may still contend for SQLite's writer lock while a bounded background transaction runs).

## Verification

An App functional acceptance scenario enqueues 16 valid writes concurrently via the real per-library service coalescer, verifies grouped commits, stale revision rejection, persisted dimensions and source identity. The existing Windows App/UI/Viewers, 10k/100k Library, NativeAOT, portable and real-library acceptance must remain green.

## Acceptance remaining

- Show actual grouped commit rate and p95/p99 write latency on representative **real Viewer background workloads**. A unit/fixture coalescing success is not a measured end-user speedup.
- Demonstrate cold/warm performance across repeated strict Windows runs with original <=8,000ms 10k individual-write control gate unchanged. Document cold-vs-warm variance; do not relabel a second-pass warm batch benchmark as cold A/B.
- Validate first-raster, interactive scroll and cancellation under GPU pressure on the final owner Windows acceptance pass, together with Issues #634/#635.
- Keep Issue #714 open until a consistently effective, production-used optimization and full acceptance evidence exist.
