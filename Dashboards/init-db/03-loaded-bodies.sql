-- =============================================================================
-- Foresight loaded-item bodies — sidecar to context_snapshots.
-- One row per (conversation_id, turn_index, loaded_index). Stores the full
-- body text of a loaded artifact (composed system prompt, skill instructions,
-- tool schema, MCP descriptor, sub-agent description) so the dashboard's
-- ContextDrawer can lazily fetch on open. The parent context_snapshots row
-- carries metadata + token counts only, keeping SignalR / HTTP wire payloads
-- small.
--
-- Loaded after 02-context-snapshots.sql via the alphabetical Docker entrypoint
-- order. For an existing observability database, apply manually:
--   psql -d observability -f 03-loaded-bodies.sql
-- =============================================================================

CREATE TABLE IF NOT EXISTS context_snapshot_loaded_bodies (
    conversation_id   TEXT        NOT NULL,
    turn_index        INTEGER     NOT NULL,
    loaded_index      INTEGER     NOT NULL,
    body              TEXT        NOT NULL,
    captured_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- Idempotent replays: re-emitting bodies for an existing
    -- (conversation_id, turn_index, loaded_index) overwrites rather than
    -- duplicates — matches the parent context_snapshots' replay semantics.
    CONSTRAINT pk_context_snapshot_loaded_bodies
        PRIMARY KEY (conversation_id, turn_index, loaded_index)
);

CREATE INDEX IF NOT EXISTS idx_loaded_bodies_conv_turn
    ON context_snapshot_loaded_bodies (conversation_id, turn_index);

GRANT SELECT ON context_snapshot_loaded_bodies TO grafana_reader;
