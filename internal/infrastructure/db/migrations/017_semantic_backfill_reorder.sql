-- Older Lumine builds enqueued the entire Semantic Search backfill in asset-id
-- order. Because queue claims are intentionally cheap (priority DESC, id ASC),
-- carrying that durable backlog forward would keep partial search coverage
-- biased toward old assets even after the producer became newest-first.
--
-- This migration runs once, before workers start. Retire only background
-- semantic work (negative priority), mark its analysis state stale, and let the
-- current newest-first producer recreate the missing work in the right order.
-- Foreground/manual non-negative-priority work is preserved.
UPDATE ai_asset_analysis
SET state = 'stale',
    error_message = '',
    updated_at = CURRENT_TIMESTAMP
WHERE capability = 'semantic_search'
  AND asset_id IN (
      SELECT asset_id
      FROM ai_jobs
      WHERE capability = 'semantic_search'
        AND status IN ('queued', 'running')
        AND priority < 0
  );

UPDATE ai_jobs
SET status = 'cancelled',
    cancel_requested = 0,
    last_error = 'superseded by newest-first semantic backfill',
    finished_at = CURRENT_TIMESTAMP
WHERE capability = 'semantic_search'
  AND status IN ('queued', 'running')
  AND priority < 0;
