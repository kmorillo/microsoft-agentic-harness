-- =============================================================================
-- Foresight context snapshots — one row per turn per conversation.
-- Loaded after 01-schema.sql via the alphabetical Docker entrypoint order, so
-- the table is available in fresh deployments. For an existing observability
-- database, apply this file manually:  psql -d observability -f 02-context-snapshots.sql
-- =============================================================================

CREATE TABLE IF NOT EXISTS context_snapshots (
    id                BIGSERIAL PRIMARY KEY,
    conversation_id   TEXT        NOT NULL,
    turn_index        INTEGER     NOT NULL,
    turn_id           TEXT        NOT NULL,

    -- CategoryBreakdown — one integer column per Foresight category.
    -- Wide schema (not JSONB) so per-category aggregates / Grafana panels
    -- are cheap and don't require jsonb path extraction.
    cat_system        INTEGER     NOT NULL DEFAULT 0,
    cat_agents        INTEGER     NOT NULL DEFAULT 0,
    cat_skills        INTEGER     NOT NULL DEFAULT 0,
    cat_tools         INTEGER     NOT NULL DEFAULT 0,
    cat_mcp           INTEGER     NOT NULL DEFAULT 0,
    cat_messages      INTEGER     NOT NULL DEFAULT 0,

    -- LoadedItem[] — serialized via System.Text.Json with camelCase property
    -- names so the SignalR wire payload is the same shape the dashboard reads
    -- from the snapshots[] field in /api/sessions/:id.
    loaded_json       JSONB       NOT NULL DEFAULT '[]'::jsonb,

    captured_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- Idempotent replays: re-emitting a snapshot for an existing
    -- (conversation_id, turn_index) overwrites rather than duplicates.
    CONSTRAINT uq_context_snapshots_conv_turn UNIQUE (conversation_id, turn_index)
);

CREATE INDEX IF NOT EXISTS idx_context_snapshots_conv
    ON context_snapshots (conversation_id, turn_index);

CREATE INDEX IF NOT EXISTS idx_context_snapshots_captured
    ON context_snapshots (captured_at DESC);

GRANT SELECT ON context_snapshots TO grafana_reader;
