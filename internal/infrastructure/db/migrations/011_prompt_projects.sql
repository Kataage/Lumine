CREATE TABLE IF NOT EXISTS prompt_projects (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    schema_version INTEGER NOT NULL DEFAULT 1,
    title TEXT NOT NULL,
    idea TEXT NOT NULL DEFAULT '',
    notes TEXT NOT NULL DEFAULT '',
    target_profile_id TEXT NOT NULL DEFAULT '',
    characters_json TEXT NOT NULL DEFAULT '[]',
    loras_json TEXT NOT NULL DEFAULT '[]',
    deleted_at DATETIME,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS prompt_project_assets (
    project_id INTEGER NOT NULL REFERENCES prompt_projects(id) ON DELETE CASCADE,
    asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
    role TEXT NOT NULL CHECK(role IN ('reference', 'generated')),
    sort_order INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (project_id, asset_id, role)
);

CREATE TABLE IF NOT EXISTS prompt_variants (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL REFERENCES prompt_projects(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS prompt_versions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    variant_id INTEGER NOT NULL REFERENCES prompt_variants(id) ON DELETE CASCADE,
    schema_version INTEGER NOT NULL DEFAULT 1,
    parent_version_id INTEGER REFERENCES prompt_versions(id) ON DELETE SET NULL,
    positive TEXT NOT NULL DEFAULT '',
    negative TEXT NOT NULL DEFAULT '',
    source TEXT NOT NULL DEFAULT 'manual',
    change_instruction TEXT NOT NULL DEFAULT '',
    profile_id TEXT NOT NULL DEFAULT '',
    profile_snapshot_json TEXT NOT NULL DEFAULT '{}',
    ai_engine TEXT NOT NULL DEFAULT '',
    ai_model_id TEXT NOT NULL DEFAULT '',
    ai_model_version TEXT NOT NULL DEFAULT '',
    metadata_json TEXT NOT NULL DEFAULT '{}',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_prompt_projects_updated
    ON prompt_projects(deleted_at, updated_at DESC, id DESC);
CREATE INDEX IF NOT EXISTS idx_prompt_project_assets_asset
    ON prompt_project_assets(asset_id, role);
CREATE INDEX IF NOT EXISTS idx_prompt_variants_project
    ON prompt_variants(project_id, id);
CREATE INDEX IF NOT EXISTS idx_prompt_versions_variant
    ON prompt_versions(variant_id, id DESC);
CREATE INDEX IF NOT EXISTS idx_prompt_versions_parent
    ON prompt_versions(parent_version_id);
