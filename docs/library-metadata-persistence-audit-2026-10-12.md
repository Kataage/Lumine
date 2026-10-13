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


## Repeatability evidence from post-merge develop and paired warm A/B

After the initial three independently failing post-merge `develop` runs,
five later strict **post-merge push** workflows passed on the original unchanged
10,000 individually awaited metadata write gate:

| develop workflow | Same original 10k individual write metric | Strict <=8,000ms |
| --- | ---: | --- |
| [#38230808756](https://github.com/Kataage/Lumine/actions/runs/38230808756) | 3,960.6ms | pass |
| [#38234053120](https://github.com/Kataage/Lumine/actions/runs/38234053120) | 2,398.1ms | pass |
| [#38236350982](https://github.com/Kataage/Lumine/actions/runs/38236350982) | 3,485.9ms | pass |
| [#38239543073](https://github.com/Kataage/Lumine/actions/runs/38239543073) | 3,836.1ms | pass |
| [#38264263328](https://github.com/Kataage/Lumine/actions/runs/38264263328) | 4,175.2ms | pass |

**Interpretation:** strict post-merge success is now repeatable across
five different `develop` SHAs, in stark contrast to the earlier repeated
9,950–16,569ms failures. This does not prove host variance is eliminated.
The product improvement operates in concurrent background Viewer requests;
the unchanged strict control remains the single-item API, so do not
attribute its changing latency solely to the new batched path.

An additional **opt-in, paired warm A-B-B-A** probe is now planned on
the existing **warm 100k fixture** in the same CI runner:
after preserving the original single-write test and separate warm-batch
second pass, the probe snapshots exactly 10,000 existing source-guarded
records once. It rewrites precisely the **same rows** four times with
the same production Library repository APIs and same revision/stat/identity
conditions, in order **single / batch / batch / single**. Batch groups
contain at most 64 records. Every write must be acknowledged as
source-current; all four durations and each batch count must exist in
the benchmark JSON and printed log. The existing <=8,000ms single-write
gate remains unchanged, applied to the same pre-probe metric. This
experiment is **warm-vs-warm same-run comparison**, not equivalent cold
samples and not a replacement for real Viewer/owner hardware evidence.

The next outstanding qualification is consistency on the post-merge
`develop` of this additional probe and the final actual Windows
real-library evidence with nonzero completed background writes, p95/p99
bucket bounds, saved commit counts and no scroll/first-raster regressions.
Keep #714 OPEN; coordinate eventual owner Windows assessment with
#634/#635 as one final pass rather than repeated setup requests.


## The original 8-second gate fails again after ten green develop pushes (2026-10-13)

Strict Windows [post-merge develop CI #38280087286](https://github.com/Kataage/Lumine/actions/runs/38280087286)
on `develop@6e30186c6ddf3d0c70907d09730013f740cfe0b8`
**FAILED** the unchanged 10,000-individual-transaction metadata gate at
**9,870.0ms**. The measured per-write internals break down to:

- pooled connection opens/PRAGMAs: **81.0ms**
- BEGIN: **61.6ms**
- guarded asset UPDATE: **177.1ms**
- metadata UPSERT: **151.4ms**
- **SQLite COMMIT: 9,309.3ms** (~94.3% of the gate)
- full individual-write maximum: **759.2ms**, 38 full writes >=8ms.

On the separately scheduled [PR #726 CI #38280679826](https://github.com/Kataage/Lumine/actions/runs/38280679826)
the same 10,000 individually committed fixture passed at **2,889.9ms**,
with **2,529.0ms COMMIT** and a full-write maximum **120.6ms**.
This PR-head success does not refute post-merge storage-timing variance.
The two runs did not share a single isolated controlled VM or I/O
context, so runner storage contention / WAL checkpointing remain
hypotheses rather than established causes. Existing metadata write
revision/file-stat/identity checks and SQLite WAL/synchronous durability
settings are not changed.

The next narrow benchmark diagnostic, which runs as a **production-repository
timing observer only in the strict 100k fixture**, captures exactly 10,000
exclusive individual SQLite COMMIT duration buckets:

`<1ms`, `[1,8)ms`, `[8,32)ms`, `[32,128)ms`, `>=128ms`, alongside
the maximum COMMIT duration and total time spent in COMMITs >=8ms.
The strict Windows acceptance verifies that all five buckets add up to
exactly 10,000 writes and that the histogram and slow-COMMIT totals
agree with existing whole-write observations **before** it applies the
same original hard <=8,000ms gate.

This distinguishes a small number of extreme COMMIT stalls from
pervasive commit slowdown, *without* hiding or retrying a failing
performance result, disabling WAL checkpointing, weakening the hard
gate, or moving the benchmark to a non-production write path.

Viewer background batching and its separate queue-vs-repository-call
diagnostics address real production use, but they cannot logically
guarantee a 10k **individually COMMITted** synthetic workload fits
within 8s under every hosted-disk condition. Issue #714 remains OPEN
for an evidence-led root-cause/architecture decision and final
representative owner Windows acceptance together with #634/#635.


## Bounded 1,000-write COMMIT windows to investigate clustered stalls (#714)

[PR #727](https://github.com/Kataage/Lumine/pull/727) passed strict
Windows CI [#38283482983](https://github.com/Kataage/Lumine/actions/runs/38283482983)
on its first attempt and was merged into `develop`. The 10,000
individual repository COMMITs showed a surprisingly skewed distribution:

| Single SQLite COMMIT wall duration | Count |
| --- | ---: |
| <1ms | 9,933 |
| 1–<8ms | 34 |
| 8–<32ms | 0 |
| 32–<128ms | **33** |
| >=128ms | 0 |

The **33 COMMITs >=8ms consumed 2,112.6ms** out of the total
**2,448.4ms COMMIT wall time**, and the full 10k guarded metadata
persistence finished within the unchanged 8-second limit at 3,156.8ms.
This supports the hypothesis that occasional long commits can dominate
the result, but the earlier 9,870.0ms red develop run predates the new
histogram and cannot be assumed to have the identical distribution.

To identify whether >=32ms commits arrive periodically (for example,
around WAL checkpoint activity), or cluster around other hosted-runner
I/O scheduling events, the follow-up benchmark observer collects **two
fixed-length arrays of ten elements** over the *same original 10,000
individual production repository calls*. Each window represents 1,000
successfully persisted, separately committed image records and contains
(1) summed SQLite COMMIT duration ticks and (2) number of COMMITs
lasting >=32ms. The strict Windows acceptance verifies both arrays
have exactly 10 nonnegative elements and sum back to the previously
required full-run COMMIT time and >=32ms histogram bucket counts.

This adds **no new SQLite statements, sleeps, file probes, connection
handling, checkpoint PRAGMAs or timing-gate changes**. No inference that
checkpointing is the cause is made without supporting evidence.
The diagnostic is not a fix and may fail on the same unchanged 8,000ms
hard budget. Issue #714 remains OPEN until trustworthy repeated
post-merge/representative physical validation with #634/#635.


## PR #728 — unchanged strict gate RED with precise COMMIT windows (2026-10-13)

The first Windows [CI #38292620467](https://github.com/Kataage/Lumine/actions/runs/38292620467) after integrating completed Issue #729 **FAILED** the original unchanged 8-second 10k-individual-commit gate at **15,933.5261ms**. The same run completed all ten diagnostic windows **before** the hard rejection. This is actual performance evidence, not a Viewer cancellation smoke false positive.

| Production write phase | Measured duration |
| --- | ---: |
| Pooled connection open + PRAGMAs | 62.4ms |
| BEGIN | 50.4ms |
| Guarded UPDATE | 140.3ms |
| Metadata UPSERT | 138.3ms |
| SQLite COMMIT total | **15,453.0ms** |
| 10k original elapsed | **15,933.5ms (FAIL)** |

Per-1k COMMIT totals (ms): `2449.8,8495.9,1531.2,429.8,387.8,455.5,350.1,397.0,532.5,423.6`. COMMITs >=32ms per 1k window: `7,4,5,3,3,4,3,3,4,3` (39). Single largest COMMIT: **5,716.1ms**. The second 1k window consumed **8,495.9ms**, proving that one unusually slow region can push the full run over the strict gate. The same job's earlier 10k-fixture runs completed individual writes in **3,442.7ms, 2,874.9ms and 3,088.3ms**, rather than reproducing the extreme delay on every attempt/fixture.

SQLite documents [automatic WAL checkpoints](https://www.sqlite.org/wal.html) as a default 1000-page per-connection mechanism invoked by a COMMIT and as a cause of occasional longer COMMITs. The approximately 3–4 >=32ms events per ordinary 1k writes are **consistent** with this, but neither the particular SQLite checkpoint responsible nor the 5.7-second hosted runner delay is independently proven. With `synchronous=NORMAL`, moving synchronization into a separate checkpoint worker alters the power-loss window; disabling checkpoints, weakening synchronous mode, altering the original 8s gate or claiming an unmeasured optimization are **not** acceptable shortcuts.

The next strictly additive diagnostic records the **one-based write ordinal** for each >=32ms COMMIT, in a fixed 256-entry memory buffer with an overflow count. It performs no extra SQL, file probes, WAL operations, timers, sleeps or logging per write; the post-benchmark JSON and Windows gate verify exact ordinal/window/histogram consistency. This allows evaluation of inter-stall spacing rather than inferring periodicity from window aggregate counts alone. If evidence supports checkpoint tuning, evaluate **bounded automatic checkpoint frequency** under the normal production `LibraryDatabase.OpenConnectionAsync` path, with WAL growth, crash/restart, cancellation, concurrency, and foreground-latency safety checks **before** deciding to adopt it. A green CI is not proof of repeatable stability; #714 remains OPEN.


## Controlled production-path WAL threshold ABBA probe (2026-10-13; Issue #714)

[PR #728](https://github.com/Kataage/Lumine/pull/728) captured precise 32ms+ COMMIT positions recurring every ~295–297 writes in four 10k source-guarded datasets, then passed strict Windows CI and merged to `develop@07753d5f8de7d7810888f3505acb5240eb9fd7ff`. The observation is highly consistent with SQLite's documented **per-connection** default 1,000-page passive WAL auto-checkpoint, which is triggered by a committing writer. These data do **not** prove that the earlier 5.7-second red COMMIT was entirely checkpoint work.

The next experiment explicitly sets the checkpoint policy to **1,000 (control) or 2,048 pages (candidate)** through `LibraryDatabase.OpenConnectionAsync` itself, with the **same existing guarded per-asset Repository API and individual COMMITs**. Both policies use WAL, `synchronous=NORMAL`, the same source-identity/revision checks and the same bounded batch second pass. The setting is **always applied on pooled connection open** (even for the default), preventing an older pooled SQLite handle from leaking the previous experiment's threshold. Zero/disabled checkpoint and excessively large values are rejected. All ordinary callers continue to use 1,000 pages, the documented default; the comparison does **not** adopt 2,048 pages for the product.

Strict Windows CI first runs the exact existing 10k/100k Library acceptance with the unchanged <=8,000ms and sample count. **Only after passing** does the new diagnostic run `build/Compare-LibraryWalCheckpoint.ps1` as an **A(1000) → B(2048) → B(2048) → A(1000)** paired sequence on separately created temporary database fixtures on the same Windows runner. The script validates ten thousand individually persisted guarded records and nonnegative timings for every phase and emits a JSON artifact with individual-save duration, total COMMIT duration, maximum COMMIT duration, >=32ms COMMIT count, and WAL size sampled **outside** the original save timer. It does not assert that B is faster or relax product gates if A or B runs slowly.

Because the candidate is still a synchronous *COMMIT-thread auto-checkpoint* policy, this is intended to compare checkpoint frequency and stall-tail cost **without changing thread ownership or power-loss behavior**. Larger thresholds can grow WAL disk usage and sometimes make rare checkpoints more expensive; use both repeats and strict resource gates, and keep the production 1,000-page default unless a later distinct PR provides repeatably better real-product measurements and full recovery/concurrency/cancellation/foreground proof. SQLite: https://sqlite.org/wal.html and https://sqlite.org/c3ref/wal_autocheckpoint.html.


## Strict-red evidence retention and smaller WAL threshold experiment (2026-10-14, #714)

After [PR #731](https://github.com/Kataage/Lumine/pull/731) completed strict Windows CI and merged at `develop@c570327b740aae762841679cc9a9db425183ba8e`, its four **separate** original Repository 10,000-individual-COMMIT ABBA trials produced the following data:

| Trial | Auto-checkpoint | 10k individual writes | 32ms+ COMMITs | Max single COMMIT | WAL allocated |
| --- | ---: | ---: | ---: | ---: | ---: |
| A1 | 1000 pages | 19,821.1ms | 41 | 2,669.5ms | 5,858,672B |
| B1 | 2048 pages | 16,035.3ms | 20 | 3,025.5ms | 8,454,272B |
| B2 | 2048 pages | 2,877.4ms | 16 | 159.1ms | 8,454,272B |
| A2 | 1000 pages | 4,155.7ms | 32 | 104.3ms | 5,858,672B |

A doubled checkpoint interval reduces periodic COMMIT stalls but **does not prevent multi-second outliers**. The 2,048-page candidate also grows the WAL allocated size ~44%. Do not adopt it as default, nor infer per-commit I/O causation from wall-clock times.

**New control:** In the strict Windows workflow, upload all available `library-{10000,50000,100000,100000-cold}.json` results to a separate **14-day diagnostic artifact before the unchanged 8-second enforcement**. This fixes the previous red-run observability gap (when later `Upload Core benchmark results` did not execute after `Test-LibraryPerformance.ps1` rejected an outlier). The first upload is after all Library measurements have ended, so it cannot affect any original measured time; upload failure remains a true CI failure if it occurs. No command suppresses the test gate.

**New hypothesis:** Try *smaller*, more frequent synchronous on-COMMIT checkpoints using an opt-in candidate of **512 pages** on the same production `LibraryDatabase.OpenConnectionAsync` path. The already-merged ABBA runner now accepts only a validated candidate of **512 or 2048**, still A=1000, B=candidate, B=candidate, A=1000, with 10,000 independently committed guarded Repository writes per phase. The strict original 8s acceptance continues to run **first**. Inspect the complete saved A/B/B/A JSON for COMMIT total, worst single stall, 32ms+ count, WAL size and cross-phase variance. More frequent checkpoints might yield shorter individual sync stalls but could *increase* total sync/I/O time and foreground latency. Neither outcome authorizes a product default change without repeatability, restart/power-loss, concurrent-reader/writer and cancellation checks. Zero/disabled checkpoints and relaxed durability remain disallowed.

SQLite's documented automatic WAL checkpoints run in the writer's COMMIT, with `NORMAL` synchronization concentrated on checkpoint events: https://www.sqlite.org/wal.html#performance_considerations.

## Failed strict run retains JSON but prevents ABBA comparison without an explicit failure-safe path (2026-10-14; Issue #714)

In the original [PR #732 first strict Windows CI #38450679157](https://github.com/Kataage/Lumine/actions/runs/38450679157), the **unchanged <=8,000ms acceptance rejected 10,937.8726ms** for the 100k-fixture original 10k individual-transaction write path. COMMITs consumed **9,997.8ms**, with a single **5,477.7ms COMMIT**. The first 1,000 writes alone spent **6,463.5ms** inside COMMIT. The exact slow-COMMIT positions remain strongly periodic at ~296 writes, plus unexpected ordinals **1, 334 and 8038** adjacent to ordinary checkpoint-correlated positions. The other three fixture phases from the same job persisted 10k metadata writes in **4,172.5 / 3,351.6 / 3,086.3ms**, so a strict-run failure is not caused by every sequential COMMIT being uniformly slow.

**The separate 14-day strict-red Library JSON artifact `lumine-library-gate-evidence-38450679157` uploaded successfully** and preserved all four `library-*.json` results before the hard rejection. This is the first direct proof of the new failure-evidence capture.

A CI workflow limitation was then detected: by default, Actions skips the separate candidate ABBA diagnostic step when the prior strict-gate step fails, so this red run did **not** execute the intended 512-page comparison. The follow-up changes only CI flow: give the strict gate a step ID, and execute the diagnostic **if that exact gate ran and is either success or failure**, provided the job was not cancelled. Independently upload A/B/B/A JSON (including partial files) with a separate 14-day artifact before subsequent unrelated steps; this second evidence upload still runs if the strict gate or an A/B/B/A candidate itself failed. If build or source tests prevent the strict gate from starting, candidate comparison remains skipped.

**This explicitly does not override the gate failure or mark the job successful.** The strict gate still exits non-zero, no `continue-on-error` is used, and all original 10,000 separate guarded transactions and durability settings are unchanged. The only purpose is to obtain 512-page evidence on red hosted runners that otherwise never reach the comparison. Do not merge a red PR to `develop`. Prove full strict CI success before merging the diagnostic changes, and then verify post-merge `develop` again; #714 remains OPEN.


## Opt-in PASSIVE checkpoint scheduling experiment — following 512/2048 rejection (#714, 2026-10-14)

The current [PR #732](https://github.com/Kataage/Lumine/pull/732) red CI #38451820556 ran the 512-page candidate **despite a strict 37,154.3ms / 8,000ms failure** and recovered both original fixture data and complete A/B/B/A evidence. The 512-page candidate exhibited 65 checkpoint-correlated >=32ms events per 10k individual writes vs 33/34 at 1,000 pages, with **6,071.9ms/4,854.7ms** candidate totals vs **3,768.0ms/8,756.4ms** controls. These nonrepeatable results do **not** support adopting 512 or 2,048. A seven-second single COMMIT outlier in the strict gate remains unexplained.

**New research question:** SQLite's documented `PRAGMA wal_checkpoint(PASSIVE)` can run on a distinct connection without waiting for active readers or writers; automatic checkpoints remain enabled at their existing **1000-page fallback**. Does an explicitly scheduled PASSIVE checkpoint off the writer's execution thread reduce the periodic on-COMMIT stalls without introducing worse foreground contention, growth or multi-second tails? Unlike disabling autocheckpoint, this probe **never removes the existing safety fallback**.

This adds an explicitly invoked production `LibraryDatabase.CheckpointPassiveAsync` API plus `LibraryService.CheckpointPassiveAsync` off-UI wrapper that report SQLite's **busy, WAL frame count and checkpointed frame count**. The regular `CheckpointAsync` TRUNCATE lifecycle path, WAL/NORMAL, connection-pool quarantine, source guards, DB schema and normal product behavior remain unchanged. A Library smoke check exercises each API on an initialized real WAL database.

The opt-in benchmark flag `--proactive-passive-checkpoint-every 128` schedules at most one PASSIVE checkpoint task at a time during 10,000 individually committed source-guarded production metadata writes; it **never awaits an unfinished checkpoint inside the timed write phase**, and joins any outstanding task outside that phase. The bounded observer records requests, completions, busy results, max WAL frames and duration in additive JSON. At most a single background checkpoint is in flight, while the normal 1000-page automatic checkpoint remains enabled. The extra scheduler work is part of the experimental timed path; compare ABBA controls without it rather than making an apples-to-apples assumption.

Strict Windows CI still executes exactly the **same mandatory 100k-fixture <=8000ms individual-write gate first** and preserves the red result. A separate A/B/B/A after it — including when red — uses A=auto1000, B=auto1000 plus passive checkpoint every 128 writes, B again, A again in isolated real Repository DBs, preserving both artifacts. No result is claimed before the CI actually reports it. This is an **experimental measurement**, not an adopted new product scheduling policy. Further adoption requires bounded WAL growth, incomplete-checkpoint retry progress, app disposal/shutdown, cancellation, restart integrity, 16-way read/write concurrency, foreground responsiveness and multi-run variance reviews. A successful single run does not justify closing Issue #714.

Official SQLite docs: https://www.sqlite.org/pragma.html#pragma_wal_checkpoint (PASSIVE) and https://www.sqlite.org/wal.html#performance_considerations.


## PASSIVE checkpoint opt-in measurement and safety follow-up (Issue #714; 2026-10-14)

[PR #732](https://github.com/Kataage/Lumine/pull/732) first-pass [Windows CI #38456124822](https://github.com/Kataage/Lumine/actions/runs/38456124822) **succeeded all 74 core steps**. The original unchanged 100k-fixture 10k individually committed metadata gate passed at **3,050.5123ms**, with three other fixture phases at **3,466.1 / 3,268.6 / 3,776.0ms**. The ABBA independent Repository-source-guarded write experiments produced:

| Phase | Automatic policy | Opt-in PASSIVE worker | Original 10k writes | COMMIT max | COMMIT >=32ms | completed PASSIVE calls |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| A1 | 1000 pages | off | 3,287.7ms | 154.2ms | 32 | 0 |
| B1 | 1000 pages | every 128 writes | 2,945.5ms | 38.0ms | 3 | 59 |
| B2 | 1000 pages | every 128 writes | 2,784.6ms | 54.5ms | 3 | 73 |
| A2 | 1000 pages | off | 3,557.1ms | 113.1ms | 33 | 0 |

Both B trials returned **0 SQLITE_BUSY**, though their individual PASSIVE tasks reached 69.3ms and 32.5ms respectively. The 10k write average fell 3,422.4ms → 2,865.1ms (~16.3%) **for this single controlled run only**. A B1 WAL file snapshot was 6,571,432B vs 5,858,672B for A1/B2/A2 (~12.2% larger); this alone cannot bound disk growth over hours, or prove progress with long-lived readers. Earlier red CI runs still had up to 7.4-second individual COMMIT stalls. **No product scheduler was enabled.** Strict 8s gate remains unchanged. Issue #714 remains OPEN for true stability acceptance.

The next narrow follow-up keeps the production auto-checkpoint fallback at 1,000 pages and adds safety validation on the opt-in API: a checkpoint requested with a pre-cancelled token must not enter SQLite; a bounded (30s) concurrent PASSIVE call alongside the existing 16 source-guarded writes must complete and leave a read-only set of 16 lookups returning the original normalized source identity and image metadata. Existing restart and failed-batch rollback coverage remains mandatory. These are smoke controls, **not a simulated power-cut test**, and do not prove all long-lived reader cases.

The ABBA observer also records how often SQLite's PASSIVE result reports *partial* progress (`checkpointed_frames < wal_frames`), the maximum outstanding WAL frame count after one reported checkpoint, and the **sum of reported checkpointed frames** across calls. The sum may include re-checkpointed frames after WAL resets; it is *not* a unique-frame count. Windows script validates partial/total counters against the number of actual completed checkpoints, includes metrics in the separate ABBA artifact, and rejects nonsensical negative or off-mode nonzero values. These metrics help identify starvation without adding queries to the original strict write path or silently claiming bounded WAL growth. Dedicated sustained-readers, cancellation-in-flight and restart-under-load trials are still needed before product adoption.
