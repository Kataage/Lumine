-- Add missing indexes for performance

-- Critical: library_id is used in every asset query
CREATE INDEX IF NOT EXISTS idx_assets_library_id ON assets(library_id);

-- Composite index for the most common query pattern
CREATE INDEX IF NOT EXISTS idx_assets_library_modified ON assets(library_id, modified_at_fs DESC);
