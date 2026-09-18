CREATE TABLE IF NOT EXISTS ai_semantic_embeddings (
    asset_id INTEGER PRIMARY KEY REFERENCES assets(id) ON DELETE CASCADE,
    engine TEXT NOT NULL,
    model_id TEXT NOT NULL,
    model_version TEXT NOT NULL,
    dimensions INTEGER NOT NULL CHECK(dimensions > 0),
    vector BLOB NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_ai_semantic_embeddings_model
    ON ai_semantic_embeddings(engine, model_id, model_version);
