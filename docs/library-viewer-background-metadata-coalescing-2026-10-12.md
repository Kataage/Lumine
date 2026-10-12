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


## Production evidence upgrade (PR after #718)

PR #718's background-only coalescer completed strict Windows CI #38236503553 on its first attempt and was squash-merged into `develop` at `e8d52327122f45b42476810a5003163f511a2df6`. That CI reported the **unchanged individually committed 10,000-write control** as 3,057.8ms (8,000ms hard gate passed) and the **warm second-pass** production bounded API test as 650.6ms / 160 transactions. These runs are not identical cold A/B workloads; avoid stating the quotient as a proven end-user speedup.

The real background Viewer coalescer now exposes a snapshot (no per-image log spam) with:

- `QueuedWrites`: actual background writes admitted into the coalescing queue, not simply thumbnail requests or a benchmark fixture count
- `SingleWriteCommits`, `CommittedBatches`, and `BatchedRows`: completed single-asset commits, successful batch transactions, and their included attempts (some rows can be guard-rejected)
- `RejectedWrites`: returns of false from original revision/stat/identity guard
- `CanceledBeforeWrite`: entries discarded before a write was initiated; cancellation racing with an in-flight transaction is not counted here
- `FailedGroups`: groups whose database operation raised an exception, never counted as a successful batch commit
- `PeakPendingQueueDepth`: observed length of the in-memory waiting queue (128 bounded admissions in total, including selected/in-flight writes)
- `CompletedWriteLatencyTicks` and `MaxCompletedWriteLatencyTicks`: wall-clock time from successful queue admission until a returned single-asset write or transaction COMMIT. This includes queue delay, scheduling, SQLite work and CPU/OS scheduling. **It is not an isolated SQLite COMMIT duration and is not a first-raster latency.** Canceled or exception-failed requests are excluded.

The actual representative-library acceptance JSON will emit these under
`viewer.metadata_background_*` alongside existing viewer, image and resource
diagnostics; a zero count means there were no new background writes during
that workload (possibly because metadata was already persisted), *not*
proof the batching optimization passed a throughput objective.

An additional App smoke suite uses **actual PNG files and the production
`ImageViewerThumbnailProvider` / `ThumbnailPipeline` / `LibraryService`**:
32 background image requests in parallel, persisted source-identity round
trip, queue admission counts, no group failures, and explicit separate
foreground / interactive requests proving they do not enter the coalescer.
A separate existing functional test still guarantees 16 concurrent direct
coalescer calls actually form a grouped transaction, independent of
filesystem decode scheduling variance.

**Not yet resolved:** real representative-library cold/warm benefit,
background request p95/p99 under CPU/GPU contention, and no regressions to
first raster or the strict 8,000ms single-transaction baseline. Continue
Issue #714; defer final physical-user validation to the single combined
#634/#635/#714 Windows acceptance pass.
