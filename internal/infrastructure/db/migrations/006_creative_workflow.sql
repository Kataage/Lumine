CREATE TABLE IF NOT EXISTS works (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    cover_asset_id INTEGER REFERENCES assets(id) ON DELETE SET NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS work_assets (
    work_id INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
    role TEXT NOT NULL DEFAULT 'member',
    sort_order INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (work_id, asset_id)
);

CREATE TABLE IF NOT EXISTS generation_groups (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    work_id INTEGER REFERENCES works(id) ON DELETE SET NULL,
    name TEXT NOT NULL,
    prompt TEXT NOT NULL DEFAULT '',
    negative_prompt TEXT NOT NULL DEFAULT '',
    model_name TEXT NOT NULL DEFAULT '',
    sampler TEXT NOT NULL DEFAULT '',
    scheduler TEXT NOT NULL DEFAULT '',
    steps INTEGER NOT NULL DEFAULT 0,
    cfg_scale REAL NOT NULL DEFAULT 0,
    workflow_json TEXT NOT NULL DEFAULT '',
    notes TEXT NOT NULL DEFAULT '',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS generation_group_assets (
    generation_group_id INTEGER NOT NULL REFERENCES generation_groups(id) ON DELETE CASCADE,
    asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
    sort_order INTEGER NOT NULL DEFAULT 0,
    is_primary BOOLEAN NOT NULL DEFAULT 0,
    PRIMARY KEY (generation_group_id, asset_id)
);

CREATE TABLE IF NOT EXISTS asset_relations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    parent_asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
    child_asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
    relation_type TEXT NOT NULL,
    note TEXT NOT NULL DEFAULT '',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK(parent_asset_id <> child_asset_id),
    UNIQUE(parent_asset_id, child_asset_id, relation_type)
);

CREATE TABLE IF NOT EXISTS work_posts (
    work_id INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    post_id INTEGER NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
    PRIMARY KEY (work_id, post_id)
);

ALTER TABLE posts ADD COLUMN platform_metadata_json TEXT NOT NULL DEFAULT '{}';
ALTER TABLE post_destinations ADD COLUMN external_url TEXT NOT NULL DEFAULT '';

CREATE INDEX IF NOT EXISTS idx_work_assets_asset ON work_assets(asset_id);
CREATE INDEX IF NOT EXISTS idx_generation_group_assets_asset ON generation_group_assets(asset_id);
CREATE INDEX IF NOT EXISTS idx_generation_groups_work ON generation_groups(work_id);
CREATE INDEX IF NOT EXISTS idx_asset_relations_parent ON asset_relations(parent_asset_id);
CREATE INDEX IF NOT EXISTS idx_asset_relations_child ON asset_relations(child_asset_id);
CREATE INDEX IF NOT EXISTS idx_work_posts_post ON work_posts(post_id);
