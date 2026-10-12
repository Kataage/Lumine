# Lumine v2 — 10k technical metadata persistence CI regression audit (2026-10-12)

Tracking: [Issue #714](https://github.com/Kataage/Lumine/issues/714). The immediate parent context is the [#634](https://github.com/Kataage/Lumine/issues/634) scroll-readiness and [#635](https://github.com/Kataage/Lumine/issues/635) integrated UX release validation; neither is closed or re-scoped by this Library investigation.

## Observed failing develop runs

The exact original `build/Test-LibraryPerformance.ps1` acceptance requires **10,000 successful persisted technical records and <=8,000ms** of `library.technical_metadata_persist` in the 100k-asset fixture.

| develop commit | Windows workflow | 10k metadata writes | Verdict |
|---|---|---:|---|
| `f687cee6` | [#38161815215](https://github.com/Kataage/Lumine/actions/runs/38161815215) | 14,317.0621ms | FAIL |
| `150abb66` | [#38164411622](https://github.com/Kataage/Lumine/actions/runs/38164411622) | 16,568.6761ms | FAIL |
| `b7c6c007` | [#38167493802](https://github.com/Kataage/Lumine/actions/runs/38167493802) | 9,950.0779ms | FAIL |

All three logs report `Enforce Library performance acceptance` failing at line 80. The same general benchmark has sometimes passed an unchanged PR head on retry, so **both real slowdown and host/runner variance remain hypotheses**. Do not declare a cause based only on these numbers; do not dismiss the red post-merge `develop` builds.

## Source-reviewed path

`tools/Lumine.Library.Benchmarks/Program.cs` requests up to 500 assets per keyset page and calls `LibraryRepository.UpdateTechnicalMetadataAsync` once for each of 10,000 assets (no bulk-write shortcut). That production repository method normalizes and validates the source identity, opens a pooled SQLite connection through `LibraryDatabase.OpenConnectionAsync`, sets connection PRAGMAs, begins a transaction, updates the guarded `assets` row, UPSERTs `asset_technical_metadata`, and commits. Its revision/stat/identity consistency and nested-transaction pool quarantine are **must-keep correctness constraints**, not optional benchmark overhead.

## First measurement PR — diagnostics, not a claimed speedup

To identify where time is consumed without weakening the acceptance check, the benchmark adds *passive* counters/timers for:
- Total time and call count spent fetching metadata asset pages.
- Total time for actual `UpdateTechnicalMetadataAsync` calls on the **same original one-record-at-a-time path**, maximum individual call, and number of calls taking 8ms or longer.
- The 10k persisted-asset count, original total `library.technical_metadata_persist`, and **hard 8,000ms rejection remain exactly unchanged**.
- JSON keys are additive (`technical_metadata_page_fetch_count`, `technical_metadata_page_fetch_ticks`, `technical_metadata_update_ticks`, `technical_metadata_update_max_ticks`, `technical_metadata_update_over_8ms_count`), with Stopwatch elapsed `TimeSpan.Ticks` (100ns), locale-invariant strings.
- CI prints the phase breakdown **before** applying the existing gate, so a failed GitHub-run log contains useful diagnosis rather than only the final time and exception.

This phase split does **not yet separate** connection+PRAGMAs, UPDATE, UPSERT, and COMMIT inside one `UpdateTechnicalMetadataAsync` call. It establishes whether page lookups or the full per-record write path dominate. If the per-record path dominates, add finer-grained scoped measurement and evaluate **production-used** pooled/batched transactional improvements in a follow-up within #714, with validity, concurrency, cancellation and recovery tests. Do not introduce a benchmark-only fast path or silently change SQLite durability. 

## Acceptance and limits

Do not merge this diagnostic PR without green strict Windows App/Grid/List/10k/100k/Library/Image/BMP/fs/NativeAOT/Portable/PowerShell gates; if the old 8-second gate fails again, preserve that red evidence and continue diagnosis or performance repair, not rerun indefinitely until lucky. This PR is not intended to fix the observed write latency yet. Issue #714 remains open until production-relevant improvements make repeated strict results acceptable. #634 and #635 remain open pending one consolidated final Windows physical owner test after all code is actually ready.

## Follow-up stage: production SQL write-phase attribution (PR following #715)

The first [#715](https://github.com/Kataage/Lumine/pull/715) benchmark artifact for [strict Windows CI #38229687012](https://github.com/Kataage/Lumine/actions/runs/38229687012) recorded **2,547.4316ms total**, 41.0234ms in 20 page reads, and 2,472.9292ms across exactly 10,000 individually awaited production write calls. 33 writes took >=8ms; the slowest was 58.0387ms. This is a **green-run baseline only**, and does not invalidate the previously red post-merge `develop` runs.

To distinguish cost without prematurely changing durability or source-snapshot correctness, this follow-up adds a strictly opt-in callback to `LibraryRepository.UpdateTechnicalMetadataAsync` that reports each **successful** metadata transaction's elapsed time in: (1) pooled connection open + connection PRAGMAs, (2) transaction begin, (3) guarded `assets` UPDATE and parameter setup, (4) `asset_technical_metadata` UPSERT and parameter setup, (5) transaction commit. The callback receives `TimeSpan.Ticks` values in a public immutable record struct, and the normal application path does **not** register a callback. It runs after COMMIT; it is **not** a profiler of disk I/O inside SQLite or proof of which particular SQL suboperation is expensive on a different runner.

The unchanged sequential benchmark registers one observer per run, totals the five phases over all 10,000 successful commits, persists the sums and callback count in the additive JSON metadata keys and prints phase totals **before** the original unchanged <=8,000ms hard rejection. The Library Smoke verifies that a real successful persistence emits one populated timing report and roundtrips the same identity and image metadata. No new batching, no change to SQLite foreign-key/WAL/synchronous settings, no transaction skipping, no connection pooling change, no source-identity weakening, and no suppression of the nested-transaction pool quarantine.

**Decision gate:** inspect both green **and red** production-path measurements before choosing between connection/PRAGMA reinitialization, transaction journal/commit pressure and SQL execution as the optimization target. Keep #714 open until a **real production-used correction** is implemented and validated against the original gate. #634 and #635 physical owner checks remain distinct and pending.


## Follow-up: bounded production metadata batches (Issue #714, PR after #716)

[PR #716](https://github.com/Kataage/Lumine/pull/716) passed Windows CI and was squash-merged to `develop` (`1d631c36`). Its *real* runner logs on four benchmark fixtures show **COMMIT dominates**, especially on slow runs:

| Fixture | 10k individual write calls | Inside COMMIT |
| --- | ---: | ---: |
| 100k cold | 12,837.5 ms | 12,300.5 ms |
| 10k | 5,125.9 ms | 4,561.8 ms |
| 50k | 6,976.6 ms | 6,453.7 ms |
| 100k warm | 2,376.6 ms | 1,823.3 ms |

COMMIT timing includes SQLite WAL work and any hosted-runner scheduling or I/O stalls; these measurements **do not establish** that fsync or any one SQLite internal routine is the cause.

The next bounded implementation introduces `TechnicalMetadataBatchItem` and
`LibraryRepository.UpdateTechnicalMetadataBatchAsync` (also surfaced through
`LibraryService`). It permits at most **64 rows** under **one SQLite
transaction**, calls the exact pre-existing guarded production UPDATE/UPSERT
helper for each entry, and commits only when the whole batch finishes.
Entries that fail source revision, stat or normalized-identity guards return
`false` without an UPSERT; SQL exceptions and cancellation roll back the
entire batch. The existing single-asset API, including one-write atomicity
and pooled poisoned-transaction quarantine, remains available and unchanged.
WAL, foreign keys, synchronous mode and published DB schema are unchanged.

Smoke coverage exercises duplicate/current entries, stale revisions, same-stat
identity conflicts, cancellation, the 64-item bound, a deliberately aborted
second SQL UPDATE with rollback of the first, and new-connection durability.

**Benchmark interpretation and remaining work:** The previous unchanged
10,000 individually awaited transactions still run and remain subject to
the **same <=8,000ms hard gate**, with every stage observer retained.
A separately named `library.technical_metadata_batch_persist` reprocesses
exactly 10,000 source-guarded rows in bounded groups on a **warm second pass**,
recording group count and real elapsed time in the same JSON. This is only a
production-API comparison probe; it cannot be represented as an apples-to-apples
cold performance win, and **does not replace the strict legacy gate**.

The repository/LibraryService API now supports efficient batching but the
Viewer's per-thumbnail `PersistAsync` still awaits the single-write method.
A separate integration change must arrange safe batching for actual concurrent
Viewer work without degrading first-raster responsiveness or cancellation.
Until that and repeatably green strict post-merge Windows runs, Issue #714
**remains OPEN**, and #634/#635 remain open pending the single final real
Windows user acceptance.
