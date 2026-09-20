-- Viewer default ordering is library-scoped modified time with a stable id
-- tie-breaker. Without this composite index SQLite filters by library_id and
-- builds a temporary B-tree before it can return the first page.
CREATE INDEX IF NOT EXISTS idx_assets_library_modified_id
    ON assets(library_id, modified_at_fs DESC, id DESC);
