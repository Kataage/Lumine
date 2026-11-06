# Lumine v2 Metadata and Local Search Core

Issue #292 adds non-AI organization and search to the greenfield Library Core. The feature is deliberately independent from future machine/AI metadata.

## Persistent user metadata

User-owned state is stored separately from source technical metadata:

- rating: nullable 0..5
- favorite
- notes
- status label
- color label
- user tags

User metadata is keyed by stable asset identity and does not follow `source_revision`. Replacing image bytes at the same tracked asset therefore invalidates technical image metadata but preserves the user's organization state.

Tags are normalized with Unicode NFKC plus an invariant comparison key. Asset/tag assignment is relational; exact tag filters never depend on full-text search.

## Search-index boundary

Full-text search data is derived and rebuildable. It is not the source of truth.

The asset ingest hot path does not tokenize filenames or write FTS postings. Asset insert/rename only records a small row in `asset_search_dirty`. A completed full scan or reconciliation incrementally refreshes dirty rows in one batch. A text search also refreshes any remaining dirty rows before querying, so correctness is preserved after incremental filesystem events or an interrupted maintenance pass.

`RebuildSearchIndexAsync` remains an explicit repair/migration operation. Normal scans use incremental refresh rather than deleting and recreating the entire index.

## SQLite index strategy

The pinned runtime uses:

- FTS5 with the `trigram` tokenizer for partial text terms of 3+ characters
- a contentless-delete FTS table so source text is not duplicated in a separate search-document table
- an auxiliary CJK bigram table for two-character Japanese/CJK terms
- relational tag indexes for exact-tag filters

The Foundation smoke test verifies the required FTS5 trigram/contentless-delete feature set at runtime. A SQLite operation name or version string alone is not treated as sufficient capability evidence.

For 3+ character partial search, FTS supplies the indexed candidate set and the candidate is then checked against the current filename/path/notes/tags text. This avoids making a library-wide `%LIKE%` scan the primary search strategy while preserving true substring semantics.

Two-character Japanese/CJK queries use the bigram index directly. Non-CJK partial terms shorter than three characters are rejected rather than silently falling back to a full scan.

## Query composition

`AssetQuery` composes:

- local text search
- required exact tags
- minimum/maximum rating
- favorite
- status label
- color label
- modified-newest / modified-oldest sorting
- filename ascending / descending sorting

Pagination remains keyset/cursor based. Search and filters do not reintroduce deep `OFFSET` paging.

The current Viewer adapter uses the same Library query boundary. The high-volume Viewer continues to receive bounded pages rather than materializing the complete result set.

## Bounds

Current defensive limits include:

- search text: 256 characters
- notes: 16,384 characters
- tags per asset: 128
- tag name: 128 characters
- query page size: 1..1000

SQLite work stays behind `LibraryBackgroundExecution` so UI code does not execute database work inline.

## Acceptance

CI covers:

- rating/favorite/notes/status/color persistence
- exact tag persistence and filtering
- two-character Japanese search
- ASCII filename partial search
- relative-path search
- mixed Japanese/English search
- composed text/tag/rating/favorite/status/color filters
- keyset sort pagination
- index rebuild
- cancellation
- user metadata surviving a source revision change
- migration from prior Library schemas
- Viewer page-source integration
- required SQLite FTS5 runtime capability
- 100k bulk-ingest regression independently from search-index construction
- 100k search-index rebuild latency
- 100k ASCII, Japanese two-character, and exact-tag/filter query latency

Search-index construction is measured separately from Library bulk ingest so a derived feature cannot silently make the base Library ingestion path slower.

### Multi-library rebuild and rollback safety (Issue #774)

The FTS5 contentless-delete index is **shared across libraries**. Library Core smoke uses two different registered libraries with Japanese notes, ASCII filename text and exact tags to prove that rebuilding one index scope does not remove the other library's FTS postings or CJK bigrams. A smoke-only SQLite trigger also deliberately aborts a CJK insert after the current library's FTS DELETE and INSERT, verifying that transaction rollback preserves the previously committed Japanese/tag search result. The temporary trigger is always removed after the test.

These are correctness safeguards for future search performance work, not a claim that intermittent Windows 100k search-index rebuilds have met the **unchanged 12,000ms** budget. The #774 diagnostic-only draft and the separate #714 WAL durability/8-second metadata COMMIT review remain distinct.

