CREATE TABLE IF NOT EXISTS ai_tag_suggestions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
    kind TEXT NOT NULL CHECK(kind IN ('general','character','rating')),
    name TEXT NOT NULL,
    confidence REAL NOT NULL CHECK(confidence >= 0 AND confidence <= 1),
    state TEXT NOT NULL DEFAULT 'pending'
        CHECK(state IN ('pending','accepted','rejected')),
    threshold REAL NOT NULL DEFAULT 0 CHECK(threshold >= 0 AND threshold <= 1),
    engine TEXT NOT NULL DEFAULT '',
    model_id TEXT NOT NULL DEFAULT '',
    model_version TEXT NOT NULL DEFAULT '',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_ai_tag_suggestions_asset_state
    ON ai_tag_suggestions(asset_id, state, kind, confidence DESC);

CREATE INDEX IF NOT EXISTS idx_ai_tag_suggestions_model
    ON ai_tag_suggestions(model_id, model_version, kind);
