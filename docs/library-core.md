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

Image dimensions are nullable because Library Core does not decode images. A later Image Core stage may enrich width/height/format through the same technical metadata upsert path without changing asset identity.

## Acceptance

CI runs:

- Library smoke: scan, stable identity update, transaction rollback, keyset traversal and reopen-without-rescan
- 10k / 50k / 100k synthetic metadata benchmarks
- existing architecture, diagnostics and NativeAOT checks
