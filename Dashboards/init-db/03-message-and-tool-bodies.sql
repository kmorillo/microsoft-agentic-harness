-- =============================================================================
-- Foresight message + tool-execution body capture — adds the full-content
-- columns the file-body and per-invocation deep-link endpoints read.
-- Loaded after 02-context-snapshots.sql via the alphabetical Docker entrypoint
-- order. For an existing observability database, apply manually:
--   psql -d observability -f 03-message-and-tool-bodies.sql
-- All ADDs are guarded with IF NOT EXISTS so re-applying is a no-op.
-- =============================================================================

ALTER TABLE session_messages
    ADD COLUMN IF NOT EXISTS content_full TEXT;

ALTER TABLE tool_executions
    ADD COLUMN IF NOT EXISTS call_id TEXT,
    ADD COLUMN IF NOT EXISTS args    TEXT,
    ADD COLUMN IF NOT EXISTS stdout  TEXT;

-- call_id is part of the per-invocation deep-link lookup keyspace alongside
-- (session_id, id); the dedicated index keeps debug-time CallId lookups cheap
-- without bloating the main session_id index.
CREATE INDEX IF NOT EXISTS idx_tools_call_id
    ON tool_executions (call_id)
    WHERE call_id IS NOT NULL;
