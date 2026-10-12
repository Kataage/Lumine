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
