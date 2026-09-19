CREATE TABLE IF NOT EXISTS ai_semantic_index_generation (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    generation INTEGER NOT NULL DEFAULT 0
);

INSERT OR IGNORE INTO ai_semantic_index_generation (id, generation) VALUES (1, 0);

-- Embedding changes only invalidate the exact index when the row currently
-- participates in the ready semantic-search join. The analysis ready-state
-- trigger below covers the common "embedding written, then analysis becomes
-- ready" sequence without invalidating snapshots for queued/running work.
CREATE TRIGGER IF NOT EXISTS trg_semantic_embedding_generation_insert
AFTER INSERT ON ai_semantic_embeddings
WHEN EXISTS (
    SELECT 1 FROM ai_asset_analysis aa
    WHERE aa.asset_id = NEW.asset_id
      AND aa.capability = 'semantic_search'
      AND aa.state = 'ready'
      AND aa.engine = NEW.engine
      AND aa.model_id = NEW.model_id
      AND aa.model_version = NEW.model_version
)
BEGIN
    UPDATE ai_semantic_index_generation
    SET generation = generation + 1
    WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS trg_semantic_embedding_generation_update
AFTER UPDATE ON ai_semantic_embeddings
WHEN EXISTS (
    SELECT 1 FROM ai_asset_analysis aa
    WHERE aa.asset_id = NEW.asset_id
      AND aa.capability = 'semantic_search'
      AND aa.state = 'ready'
      AND (
          (aa.engine = OLD.engine AND aa.model_id = OLD.model_id AND aa.model_version = OLD.model_version)
          OR
          (aa.engine = NEW.engine AND aa.model_id = NEW.model_id AND aa.model_version = NEW.model_version)
      )
)
BEGIN
    UPDATE ai_semantic_index_generation
    SET generation = generation + 1
    WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS trg_semantic_embedding_generation_delete
AFTER DELETE ON ai_semantic_embeddings
WHEN EXISTS (
    SELECT 1 FROM ai_asset_analysis aa
    WHERE aa.asset_id = OLD.asset_id
      AND aa.capability = 'semantic_search'
      AND aa.state = 'ready'
      AND aa.engine = OLD.engine
      AND aa.model_id = OLD.model_id
      AND aa.model_version = OLD.model_version
)
BEGIN
    UPDATE ai_semantic_index_generation
    SET generation = generation + 1
    WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS trg_semantic_analysis_generation_insert
AFTER INSERT ON ai_asset_analysis
WHEN NEW.capability = 'semantic_search' AND NEW.state = 'ready'
BEGIN
    UPDATE ai_semantic_index_generation
    SET generation = generation + 1
    WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS trg_semantic_analysis_generation_update
AFTER UPDATE ON ai_asset_analysis
WHEN (
    OLD.capability = 'semantic_search' AND OLD.state = 'ready'
) OR (
    NEW.capability = 'semantic_search' AND NEW.state = 'ready'
)
BEGIN
    UPDATE ai_semantic_index_generation
    SET generation = generation + 1
    WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS trg_semantic_analysis_generation_delete
AFTER DELETE ON ai_asset_analysis
WHEN OLD.capability = 'semantic_search' AND OLD.state = 'ready'
BEGIN
    UPDATE ai_semantic_index_generation
    SET generation = generation + 1
    WHERE id = 1;
END;
