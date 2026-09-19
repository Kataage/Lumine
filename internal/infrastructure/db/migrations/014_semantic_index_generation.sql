CREATE TABLE IF NOT EXISTS ai_semantic_index_generation (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    generation INTEGER NOT NULL DEFAULT 0
);

INSERT OR IGNORE INTO ai_semantic_index_generation (id, generation) VALUES (1, 0);

CREATE TRIGGER IF NOT EXISTS trg_semantic_embedding_generation_insert
AFTER INSERT ON ai_semantic_embeddings
BEGIN
    UPDATE ai_semantic_index_generation
    SET generation = generation + 1
    WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS trg_semantic_embedding_generation_update
AFTER UPDATE ON ai_semantic_embeddings
BEGIN
    UPDATE ai_semantic_index_generation
    SET generation = generation + 1
    WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS trg_semantic_embedding_generation_delete
AFTER DELETE ON ai_semantic_embeddings
BEGIN
    UPDATE ai_semantic_index_generation
    SET generation = generation + 1
    WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS trg_semantic_analysis_generation_insert
AFTER INSERT ON ai_asset_analysis
WHEN NEW.capability = 'semantic_search'
BEGIN
    UPDATE ai_semantic_index_generation
    SET generation = generation + 1
    WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS trg_semantic_analysis_generation_update
AFTER UPDATE ON ai_asset_analysis
WHEN OLD.capability = 'semantic_search' OR NEW.capability = 'semantic_search'
BEGIN
    UPDATE ai_semantic_index_generation
    SET generation = generation + 1
    WHERE id = 1;
END;

CREATE TRIGGER IF NOT EXISTS trg_semantic_analysis_generation_delete
AFTER DELETE ON ai_asset_analysis
WHEN OLD.capability = 'semantic_search'
BEGIN
    UPDATE ai_semantic_index_generation
    SET generation = generation + 1
    WHERE id = 1;
END;
