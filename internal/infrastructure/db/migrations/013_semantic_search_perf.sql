-- Semantic Search first narrows the ready embeddings by capability/model
-- before exact vector scoring. Cover the full ready-model predicate plus the
-- asset join so large libraries avoid scanning unrelated AI analysis rows.
CREATE INDEX IF NOT EXISTS idx_ai_analysis_semantic_ready_model_asset
    ON ai_asset_analysis(capability, state, engine, model_id, model_version, asset_id);
