-- Preserve Lumine's existing Semantic backfill ordering exactly while letting
-- SQLite satisfy both the library filter and newest-first keyset order from an
-- index instead of building a temporary sort B-tree.
CREATE INDEX IF NOT EXISTS idx_assets_semantic_backfill_newest
    ON assets(library_id, COALESCE(modified_at_fs, '') DESC, id DESC);
