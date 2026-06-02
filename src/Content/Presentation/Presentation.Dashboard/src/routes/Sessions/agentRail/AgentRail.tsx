import { cn } from '@/lib/utils';
import type { AgentRollup } from '@/lib/agentRoster';
import { AgentRailTile } from './AgentRailTile';

interface AgentRailProps {
  agents: AgentRollup[];
  /** Currently selected agent id, or null when "All agents" is active. */
  selectedAgentId: string | null;
  onSelectAgent: (id: string | null) => void;
  /**
   * When true, the /api/agents query failed; the rail is showing a
   * sessions-derived fallback roster and should signal degraded mode so
   * operators don't mistake it for a healthy registry view.
   */
  degraded?: boolean;
  className?: string;
}

/**
 * The SessionsPage's left rail. Renders an "All agents" pseudo-tile at the
 * top that clears the filter, followed by one {@link AgentRailTile} per
 * roster entry. Tiles outside the selection dim so the active tile reads as
 * the answer to "whose sessions am I looking at right now?". When the roster
 * is empty (no registry + no sessions), the rail collapses to an inert hint
 * so the page still lays out cleanly.
 */
export function AgentRail({
  agents,
  selectedAgentId,
  onSelectAgent,
  degraded = false,
  className,
}: AgentRailProps) {
  const totalSessions = agents.reduce((sum, a) => sum + a.sessionCount, 0);
  const isFiltered = selectedAgentId !== null;

  return (
    <aside
      data-testid="agent-rail"
      data-filtered={isFiltered || undefined}
      className={cn(
        'flex w-64 shrink-0 flex-col gap-2',
        className,
      )}
      aria-label="Agents"
    >
      {degraded && (
        <p
          data-testid="agent-rail-degraded"
          role="alert"
          className="rounded-md border border-otel-negative/40 bg-otel-negative/10 px-3 py-2 text-[11px] text-otel-negative"
        >
          Registry unavailable — showing agents inferred from recent sessions.
        </p>
      )}
      <button
        type="button"
        data-testid="agent-rail-all"
        data-active={!isFiltered || undefined}
        aria-pressed={!isFiltered}
        onClick={() => onSelectAgent(null)}
        className={cn(
          'flex w-full items-center justify-between rounded-md border px-3 py-2 text-left transition-colors',
          !isFiltered
            ? 'border-cat-accent bg-accent'
            : 'border-border bg-card hover:bg-accent/40',
        )}
      >
        <span className="text-sm font-medium text-foreground">All agents</span>
        <span className="font-mono text-[11px] tabular-nums text-muted-foreground">
          {totalSessions}
        </span>
      </button>

      {agents.length === 0 ? (
        <p
          data-testid="agent-rail-empty"
          className="rounded-md border border-dashed border-border bg-muted/30 px-3 py-4 text-center text-[11px] italic text-muted-foreground"
        >
          No agents registered.
        </p>
      ) : (
        agents.map((agent) => (
          <AgentRailTile
            key={agent.id}
            agent={agent}
            active={selectedAgentId === agent.id}
            dimmed={isFiltered && selectedAgentId !== agent.id}
            onSelect={(id) => onSelectAgent(id)}
          />
        ))
      )}
    </aside>
  );
}
