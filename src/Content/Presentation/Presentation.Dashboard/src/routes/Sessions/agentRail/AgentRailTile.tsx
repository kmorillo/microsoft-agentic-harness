import { useMemo } from 'react';
import { cn } from '@/lib/utils';
import { CATEGORY_BG_CLASS } from '@/lib/categories';
import type { AgentRollup } from '@/lib/agentRoster';

interface AgentRailTileProps {
  agent: AgentRollup;
  active: boolean;
  /** Dimmed when another tile is selected; clears on "All agents". */
  dimmed: boolean;
  onSelect: (id: string) => void;
}

/**
 * One row in the AgentRail: a coloured circular avatar with initials, the
 * agent name + (optional) role, and a small footer line with session count
 * and last-activity relative time. Click forwards the agent id to the parent
 * filter hook.
 */
export function AgentRailTile({
  agent,
  active,
  dimmed,
  onSelect,
}: AgentRailTileProps) {
  const lastActivityLabel = useMemo(
    () => formatRelative(agent.lastActivity),
    [agent.lastActivity],
  );

  return (
    <button
      type="button"
      data-testid={`agent-rail-tile-${agent.id}`}
      data-active={active || undefined}
      data-dimmed={dimmed || undefined}
      aria-pressed={active}
      onClick={() => onSelect(agent.id)}
      className={cn(
        'flex w-full items-center gap-3 rounded-md border px-3 py-2 text-left transition-colors',
        active
          ? 'border-cat-accent bg-accent'
          : 'border-border bg-card hover:bg-accent/40',
        dimmed && !active && 'opacity-50',
      )}
    >
      <span
        aria-hidden="true"
        data-testid={`agent-rail-avatar-${agent.id}`}
        className={cn(
          'flex h-9 w-9 shrink-0 items-center justify-center rounded-full font-semibold text-[12px] text-foreground',
          CATEGORY_BG_CLASS[agent.color],
        )}
      >
        {agent.initials}
      </span>
      <span className="min-w-0 flex-1">
        <span
          data-testid={`agent-rail-tile-name-${agent.id}`}
          className="block truncate text-sm font-medium text-foreground"
        >
          {agent.name}
        </span>
        {agent.role && (
          <span className="block truncate text-[11px] text-muted-foreground">
            {agent.role}
          </span>
        )}
        <span className="mt-0.5 block text-[10px] font-mono tabular-nums text-muted-foreground/80">
          {agent.sessionCount} session{agent.sessionCount === 1 ? '' : 's'}
          {lastActivityLabel && (
            <>
              <span aria-hidden="true"> · </span>
              {lastActivityLabel}
            </>
          )}
        </span>
      </span>
    </button>
  );
}

/**
 * Compact relative-time label ("8m ago", "2h ago", "3d ago"). Returns an
 * empty string for null inputs so the tile collapses the footer cleanly.
 * Intentionally tiny — for the rail we want one-glance recency, not a
 * full-fidelity formatter.
 */
function formatRelative(iso: string | null): string {
  if (!iso) return '';
  const ms = Date.now() - new Date(iso).getTime();
  if (!Number.isFinite(ms) || ms < 0) return '';
  const seconds = Math.floor(ms / 1000);
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days}d ago`;
  const months = Math.floor(days / 30);
  if (months < 12) return `${months}mo ago`;
  return `${Math.floor(months / 12)}y ago`;
}
