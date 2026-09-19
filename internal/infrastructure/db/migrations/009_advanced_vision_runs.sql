CREATE TABLE IF NOT EXISTS advanced_vision_runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    operation TEXT NOT NULL,
    instruction TEXT NOT NULL DEFAULT '',
    state TEXT NOT NULL DEFAULT 'running'
        CHECK (state IN ('running', 'ready', 'failed')),
    engine TEXT NOT NULL,
    model_id TEXT NOT NULL,
    model_version TEXT NOT NULL,
    result_json TEXT NOT NULL DEFAULT '{}',
    error_message TEXT NOT NULL DEFAULT '',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    completed_at DATETIME
);

CREATE TABLE IF NOT EXISTS advanced_vision_run_assets (
    run_id INTEGER NOT NULL,
    asset_id INTEGER NOT NULL,
    position INTEGER NOT NULL,
    PRIMARY KEY (run_id, asset_id),
    UNIQUE (run_id, position),
    FOREIGN KEY (run_id) REFERENCES advanced_vision_runs(id) ON DELETE CASCADE,
    FOREIGN KEY (asset_id) REFERENCES assets(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_advanced_vision_runs_created
    ON advanced_vision_runs(created_at DESC);

CREATE INDEX IF NOT EXISTS idx_advanced_vision_run_assets_asset
    ON advanced_vision_run_assets(asset_id, run_id DESC);
