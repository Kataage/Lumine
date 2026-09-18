CREATE TABLE IF NOT EXISTS ai_asset_analysis (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
    capability TEXT NOT NULL,
    state TEXT NOT NULL DEFAULT 'stale'
        CHECK(state IN ('queued','running','ready','failed','stale')),
    engine TEXT NOT NULL DEFAULT '',
    model_id TEXT NOT NULL DEFAULT '',
    model_version TEXT NOT NULL DEFAULT '',
    result_json TEXT NOT NULL DEFAULT '{}',
    error_message TEXT NOT NULL DEFAULT '',
    attempt_count INTEGER NOT NULL DEFAULT 0,
    analyzed_at DATETIME,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(asset_id, capability)
);

CREATE TABLE IF NOT EXISTS ai_jobs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
    capability TEXT NOT NULL,
    source TEXT NOT NULL DEFAULT 'manual'
        CHECK(source IN ('manual','automatic')),
    priority INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'queued'
        CHECK(status IN ('queued','running','completed','failed','cancelled')),
    attempt_count INTEGER NOT NULL DEFAULT 0,
    max_attempts INTEGER NOT NULL DEFAULT 3 CHECK(max_attempts > 0),
    last_error TEXT NOT NULL DEFAULT '',
    cancel_requested BOOLEAN NOT NULL DEFAULT 0,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    started_at DATETIME,
    finished_at DATETIME
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_ai_jobs_one_active
    ON ai_jobs(asset_id, capability)
    WHERE status IN ('queued','running');

CREATE INDEX IF NOT EXISTS idx_ai_jobs_claim
    ON ai_jobs(status, priority DESC, id ASC);

CREATE INDEX IF NOT EXISTS idx_ai_jobs_asset
    ON ai_jobs(asset_id, capability, id DESC);

CREATE INDEX IF NOT EXISTS idx_ai_analysis_state
    ON ai_asset_analysis(capability, state);

CREATE INDEX IF NOT EXISTS idx_ai_analysis_model
    ON ai_asset_analysis(capability, engine, model_id, model_version);
