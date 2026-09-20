-- Queue claims partition by capability and, for Semantic Search, order equal
-- priority jobs by the asset's filesystem modification time. This composite
-- index keeps the per-capability candidate scan bounded for large durable
-- backlogs.
CREATE INDEX IF NOT EXISTS idx_ai_jobs_claim_capability
    ON ai_jobs(status, capability, priority DESC, id ASC);
