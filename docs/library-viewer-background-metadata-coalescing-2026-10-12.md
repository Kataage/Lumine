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


## Latency distributions & comparable evidence (next Issue #714 PR)

The existing last-turn PR #719 passed the **full unchanged Windows CI on attempt 2** and was squash-merged to `develop@a069b2e3dc4a023e94053f6a1e5612637be91322`. Attempt 1 failed the independent synthetic 50k Viewer fast-scroll refresh threshold at 1,683.9344ms versus its unchanged 1,500ms hard limit; attempt 2 passed. That observed variance is **preserved**, not considered solved. Neither a synthetic scroll pass nor a warm database pass proves cold product responsiveness.

To enable useful p95/p99 production observations **without storing per-image history or allocating on each image**, a fixed 10-bin, exclusive histogram now samples every completed background metadata write from queue admission through post-COMMIT:

| Bucket | Meaning |
| --- | --- |
| `le_2_ms` | up to 2 ms |
| `le_5_ms` | above 2, up to 5 ms |
| `le_10_ms` | above 5, up to 10 ms |
| `le_20_ms` | above 10, up to 20 ms |
| `le_50_ms` | above 20, up to 50 ms |
| `le_100_ms` | above 50, up to 100 ms |
| `le_250_ms` | above 100, up to 250 ms |
| `le_500_ms` | above 250, up to 500 ms |
| `le_1000_ms` | above 500, up to 1000 ms |
| `over_1000_ms` | strictly above 1000 ms |

`viewer.metadata_background_latency_p95_upper_bound_ms` and
`viewer.metadata_background_latency_p99_upper_bound_ms` are the **upper
edge of the percentile's bucket, never a fabricated exact percentile**.
`no_samples` means no completed background writes and `over_1000`
means only that the tail exceeded this histogram's maximum numeric bound.
All 10 counters are exported under `viewer.metadata_background_latency_bucket_*_ms`.
The snapshot itself is nonblocking and at most 10 `long` values; each completed
write does one additional atomic increment. The method's bounded queue size
and the transaction's maximum 64 rows are unchanged.

The distribution also counts **committed** batch groups by size (2–8, 9–32,
33–64), and reports `viewer.metadata_background_avoided_individual_commits`
as (batched rows – committed batches), which is only a count of
potential individual transactions saved, not a timing or throughput claim.
A SQLite stale-guard rejection within an otherwise successful batch can
still be included in the batch's attempted-row count. Single-item false
guard results do **not** count as committed successful single writes.
Failed/canceled-before-write entries never become histogram samples.

The existing 16-way repository coalescer functional acceptance and the real
32-PNG adapter smoke now also check histogram sample counts, transaction
accounting and deterministic percentile bucket boundaries. They **do not**
assert a timing cutoff from synthetic fixtures, nor weaken the unchanged
8-second 10k metadata gate or 1.5-second Viewer fast-scroll gate.

### What still blocks #714 completion

Obtain repeatable strict Windows **post-merge `develop`** evidence, and
representative real-library cold/warm Viewer metrics with nonzero write sample
counts, including source size distribution, p95/p99 coarse completion latency,
batch group sizes and commits avoided. First-raster responsiveness and
real hardware GPU-pressure acceptance remain separate checks to coordinate
with #634/#635 on the final physical acceptance pass. Until those results are
verified, `#714` stays OPEN.


## Concurrent foreground requests and warm thumbnail revisits — Issue #714 follow-up

Previous App integration verification launched 32 real PNG background requests
through the **production** `ImageViewerThumbnailProvider`/
`ThumbnailPipeline`/`ViewerTechnicalMetadataBatcher` path but requested
foreground and interactive thumbnails only *after* every background job had
completed. That proved routing on idle, not while competing work was present.

The updated existing App smoke fixture now starts **32 background, one
foreground, and one interactive request together** on the same registered
scratch library and single production thumbnail pipeline (12 image workers;
64 queue entries), with separate fresh paths for foreground/interactive.
It awaits all requests, requires source identity and dimensions for all 34,
and asserts only the 32 background records reached the bounded metadata
queue. Both direct priorities must return thumbnails with source metadata
and persist guarded SQLite metadata correctly. Report individual
foreground/interactive completion time, whole mixed cold-pass completion
time, and the background queue commit histogram **as diagnostics**; do
not impose a new hosted-runner-dependent time threshold.

A second pass requests the same 32 stale `ViewerAsset` descriptors on
the already-warm pipeline. Their cached source identity must be reused
without any further background enqueue, batch/single COMMIT or latency
histogram sample. The measured warm pass is a **cache reuse behavior check**,
not a claimed cold apples-to-apples batch speedup. After the pipeline is
disposed, SQLite pools are cleared and a distinct `LibraryService`
instance reopens the exact same database to check all **34** persisted
source metadata records (correct dimensions, valid source identities),
including foreground and interactive entries.

The experiment is small compared with a real 100k image library and does
not exercise a physical GPU, but it closes three specific functional gaps:
(1) routing under mixed-priority concurrent activity,
(2) avoiding redundant database writes during repeated thumbnail viewing,
and (3) durable metadata after service/database restart. Existing safety
guards, batching bounds (128 queued, 64 rows/transaction) and the strict
8-second/1.5-second Windows gates stay unchanged. User physical approval
remains deferred until consolidated #634/#635/#714 final acceptance.


## Real-library acceptance evidence classification (Issue #714)

The representative Windows `Run-RealLibraryAcceptance.ps1` wrapper now
cross-validates the real Viewer background metadata snapshot for each
cold/warm/optional steady-warm run. The check requires every emitted
batch/queue/latency metric, valid nonnegative counters, bounded peak
queue depth (128), plausible row totals for batch sizes 2–8 / 9–32 /
33–64, correctly accounted avoided individual commits, and p95/p99
coarse labels recomputed independently from all ten exclusive histogram
bucket counts. Missing or inconsistent telemetry fails the acceptance
wrapper instead of silently coercing missing metadata to zero.

A new additive `viewerBackgroundMetadata` summary member reports
`cold`, `warm` and `steadyWarm` entries with explicit `state`:

- `observed`: at least one completed real background metadata write
  sampled from enqueue through post-COMMIT
- `not-observed`: no completed writes; valid when prior metadata and
  cache hits already satisfy Viewer requests, **not proof of batching
  latency or savings**

The summary includes queued/completed counts, individual and grouped
commits, attempted batched rows, saved-commit count, rejected/canceled
requests, failed groups, queue peak and p95/p99 bucket upper bounds.
Cold zero-sample evidence generates an explicit reviewer warning, but
does not override the existing valid warm-cache acceptance outcome or
invent a strict minimum background write count on representative
libraries. The original fast-scroll, resource, WAL, thumbnail-cache,
NativeAOT, portability and Library 8s performance gates stay intact.

The NativeAOT real-library wrapper smoke verifies these fields exist
and that a zero-sample run cannot claim `observed`. This is a stronger
*truthfulness and consistency* gate, **not** a performance speedup.
A physical owner run using a representative fresh-source image library
must still produce nonzero write evidence and be reviewed alongside
#634/#635 before #714 can close.


## Accepted-row evidence vs guarded transaction attempts

The original `BatchedRows` diagnostic deliberately counts **attempted**
source-guarded updates in successfully committed grouped transactions. In
SQLite, a transaction can COMMIT successfully even when individual
revision/stat/identity checks return `false`, so `BatchedRows` alone
does **not** prove those image metadata rows were actually persisted.
Likewise, `BatchedRows - CommittedBatches` describes the potential number
of separate transactions avoided for attempts, not a successful persisted
row count or wall-time improvement.

The per-LibraryService production batcher now maintains a separate
`AcceptedBatchedRows` counter: for each successful bounded transaction,
count `true` results and atomically accumulate them. There are no
additional SQL statements, source probes, or memory allocations per image.
The old batch-attempt and histogram counters retain their previous meaning.
The real library telemetry publishes
`viewer.metadata_background_batch_accepted_rows` in addition to
`viewer.metadata_background_batched_rows`.

The Windows acceptance wrapper rejects inconsistent accepted counts
(negative, or greater than batched attempts) and publishes a separate
`viewerBackgroundMetadata.<mode>.batchEvidence`:

- `accepted-batch`: at least one image metadata row passed all source
  guards and was written inside a committed multi-row transaction;
- `no-accepted-batch`: no confirmed source-accepted batch rows, even if
  transactions COMMIT or the histogram records rejected writes.

The existing `state=observed|not-observed` remains an independent
measurement-sample classification. `observed` alone must **never** be
construed as evidence of effective batching. The 32-PNG real-image
integration case confirms all batch attempts passed guards; the existing
16-way batch functional test confirms successful accepted rows and that
a stale subsequent write cannot inflate its count. The NativeAOT wrapper
smoke cross-validates both classifications.

These metrics improve evidence integrity rather than asserting a fixed
performance gain. The original strict 8,000ms Library and 1,500ms
Viewer performance gates remain untouched. Final user-visible hardware
approval is coordinated with #634 and #635 only after #714 is ready.


## Multi-format 192-image burst (>128 accepted queue capacity) — Issue #714

The existing production-adapter 32-PNG mixed foreground/interactive smoke
is retained without changing its fixture count or assertions. A **second
independent Windows App integration scenario** now builds a scratch library
from **168 PNGs (320×200)** and **24 native BMPs (1024×768)**, enumerating
all **192** records via real library keyset pages. The BMP/PNG mix tests
two actual decoders and introduces larger file/decode workloads without
installing a model or requiring owner-provided files.

Using the actual thumbnail provider, pipeline (12 image workers, 64
image queue places, memory-only product default), and per-service guarded
SQLite batcher (128 admitted pending slots; <=64 rows per transaction),
the scenario starts **192 concurrent background thumbnail requests** and
awaits every completed image. It verifies:

- Every source returns a real encoded thumbnail and source metadata.
- Precisely 192 background metadata writes are queued and acknowledged.
- Committed multi-row groups actually occur, and every batched attempt
  passes its source revision/stat/identity guard.
- All completed requests are recorded in the coarse latency histogram,
  no guard rejects or failed groups are reported, and the observed
  pending queue peak remains within its hard 128-slot bound.
- Repeating the identical 192 stale ViewerAsset descriptors through the
  warm production adapter adds **zero** metadata queue admissions,
  individual/batch commits or latency samples.
- After the thumbnail pipeline is disposed, a fresh LibraryService
  reopens SQLite and verifies **all 192** metadata records and their
  correct dimensions/source identities using paginated reads.

The CI log records cold burst wall time (including request admission),
warm revisit wall time, background p95/p99 *bucket upper bounds*, committed
batch count, accepted batched row count, pending depth, and the observed
working-set delta. These are **diagnostics rather than unstable hard timing
thresholds**: existing strict 8-second individual Library persistence,
1.5-second Viewer fast-scroll, NativeAOT, memory resource and product
acceptance gates remain unchanged.

This is deliberately a **bounded mid-scale decoding/concurrency regression
test**, not a representative 100k user image collection: the fixture uses
copies of just two source images and does not measure graphics compositor
pressure or real hardware GPU activity. Final performance acceptance must
still use the actual representative image library with user hardware,
reviewed once alongside #634 and #635.
