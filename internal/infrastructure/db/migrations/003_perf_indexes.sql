CREATE INDEX IF NOT EXISTS idx_assets_library_folder_modified ON assets(library_id, folder_path, modified_at_fs DESC);
CREATE INDEX IF NOT EXISTS idx_assets_library_status_modified ON assets(library_id, status_label, modified_at_fs DESC);
CREATE INDEX IF NOT EXISTS idx_assets_library_favorite_modified ON assets(library_id, is_favorite, modified_at_fs DESC);
CREATE INDEX IF NOT EXISTS idx_assets_extension ON assets(extension);
CREATE INDEX IF NOT EXISTS idx_asset_tags_tag_id ON asset_tags(tag_id, asset_id);
CREATE INDEX IF NOT EXISTS idx_assets_thumb_status ON assets(thumb_status);
CREATE INDEX IF NOT EXISTS idx_post_destinations_post_id ON post_destinations(post_id);
