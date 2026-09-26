# Lumine Library Core

Issue #287 establishes the first persistent product subsystem in the greenfield codebase.

## Responsibilities

- register local library roots
- persist library, folder and asset metadata in SQLite
- preserve a stable asset identity while technical metadata changes
- perform bounded batch ingestion
- expose stable keyset/cursor paging to callers
- persist scan completion so restart does not require a full rescan before the existing index is usable

The Library project does not render images and does not expose original absolute file paths as a Viewer resource API.

## SQLite policy

Each connection enables foreign keys and a finite busy timeout. Initialization establishes:

- WAL journal mode
- `synchronous=NORMAL`
- `temp_store=MEMORY`
- schema migrations recorded in `schema_migrations`

Migrations are versioned and transactional.

## Asset identity and paths

`assets.id` is the stable local asset identity. The unique storage locator is `(library_id, relative_path_key)`. Re-ingesting the same path updates technical metadata in place without replacing the row id. A second random identifier/index is intentionally avoided because it adds write amplification without adding a current product requirement.

The original library root is stored only in Library Core. Asset DTOs expose relative paths, not an absolute source path.

## Paging

The default stable ordering is `modified_at_utc_ticks DESC, id DESC`.

The next page uses the previous page's final `(modified_at_utc_ticks, id)` pair. No deep-page `OFFSET` is used.

## Initial scan boundary

`LibraryScanner` performs the one-time/reconciliation enumeration path. It:

- streams filesystem enumeration rather than materializing the library
- ignores reparse points
- writes bounded batches through one connection-scoped ingest session
- uses prepared multi-row asset upserts to avoid one managed/native SQLite command crossing per asset
- uses a bounded 32 MiB ingest cache, defers WAL auto-checkpoint during ingest, then checkpoints and restores normal settings on session disposal
- supports cancellation and progress
- never deletes rows merely because enumeration was incomplete
- records `last_scan_completed_at` only when the scan completes without per-file failures

Incremental change tracking (`ReadDirectoryChangesW` / USN) remains #290.

Image dimensions are nullable because Library Core does not decode images. Issue #308 persists Image Core enrichment as oriented width/height, raw width/height, alpha, normalized format and source content SHA-256. The update is conditional on the exact asset id, source_revision, file size and persisted mtime, so stale background work cannot overwrite a newer source revision.

## Acceptance

CI runs:

- Library smoke: scan, stable identity update, transaction rollback, keyset traversal and reopen-without-rescan
- 10k / 50k / 100k synthetic metadata benchmarks
- existing architecture, diagnostics and NativeAOT checks

## Performance acceptance budgets

The hosted Windows CI baseline is treated as a regression guard, not as a promise for every physical machine. The gate intentionally leaves runner headroom while rejecting the failure class seen during development:

- cold 100k metadata ingest (measured before smaller warm-up runs): <= 12 s
- warm 100k metadata ingest: <= 10 s
- 100k existing-database reopen: <= 1.5 s
- 100k first-page keyset query: <= 50 ms
- complete 100k keyset traversal: <= 1.5 s
- working set after 100k ingest: <= 160 MiB
- 100k metadata database: <= 40 MiB
- 100k ingest may not exceed 3x the 50k result plus 1 s

The current optimized implementation is expected to sit well below these ceilings; the margins exist to absorb hosted-runner variance without allowing multi-tens-of-seconds regressions to become green CI.

## Post-#287 hardening contract

Audit issue #300 tightens the Library Core boundary before Image Core is allowed to depend on it.

- `LibraryService` is the UI-facing boundary. It moves SQLite-backed registration and query work off the UI thread. `LibraryScanner` also enters through `LibraryBackgroundExecution` because Microsoft.Data.Sqlite and filesystem enumeration may synchronously block despite Task-shaped APIs.
- Scan completeness is explicit: `InProgress`, `Complete`, or `Partial`. Filesystem enumeration failures are sampled and a partial scan never advances `last_scan_completed_at`.
- The initial scanner is not a destructive reconciliation engine. It never deletes unseen rows. #290 owns incremental delete/rename handling and safe reconciliation.
- Schema history is strict and sequential. Unknown future versions, gaps, name mismatches, and product tables without migration history fail closed.
- WAL is a verified requirement rather than an assumed pragma.
- `assets.id` uses `AUTOINCREMENT` so a deleted local identity is not reused. Re-adding the same path creates a new identity.
- `source_revision` increments when source size or mtime changes. Explicit filesystem add/modify events also advance the revision when size/mtime are unchanged, preventing same-stat writes from preserving stale metadata.
- Source technical metadata is revision-bound. Width/height/format are invalidated on source revision changes; the separate SHA/raw-dimensions/alpha row automatically stops joining when its stored revision is stale. The content SHA provides a stronger identity check for later Image Core work than size+mtime alone.
- Transaction rollback coverage now fails in SQLite after at least one earlier multi-row statement has executed.
- Performance acceptance uses sampled peak working set rather than only a post-operation memory snapshot.


## #308 metadata persistence acceptance

Schema v4 adds a separate `asset_technical_metadata` table keyed by stable `asset_id` and bound to `source_revision`. The hot `assets` row keeps the pre-#308 ingest shape; oriented `width`/`height`/`format` remain there for existing paging compatibility, while SHA-256, raw dimensions and alpha live off the hot ingest table. Queries expose the metadata only when the metadata row's revision matches the current asset revision. Metadata is not populated by an unconditional full-library image decode. It is derived lazily when Image Core first needs a source for a thumbnail/detail path and then committed transactionally through `LibraryService`.

The synthetic Library benchmark separately measures technical metadata persistence for up to 10,000 assets. This measurement is outside the base ingest timing so the established 10k/50k/100k ingest regression gate remains comparable.
