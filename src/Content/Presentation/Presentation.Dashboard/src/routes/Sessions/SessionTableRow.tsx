import type { SessionRecord } from '@/api/types';
import { ContextBar } from '@/components/context/ContextBar';
import { DEFAULT_CONTEXT_BUDGET } from '@/lib/categories';
import { StatusBadge } from './StatusBadge';
import {
  formatCost,
  formatDuration,
  formatTimestamp,
  formatTokens,
} from './format';

interface SessionTableRowProps {
  session: SessionRecord;
  onRowClick: (id: string) => void;
}

/**
 * One row in the SessionsPage table. Adds the per-session mini ContextBar
 * (size `sm`) rendered from `SessionRecord.breakdown` &mdash; the field shipped
 * in PR 3 part 5 but never rendered until PR 5. Rows for sessions without a
 * breakdown yet show a muted fallback rail of the same height so the column
 * doesn't jiggle across rows.
 *
 * Click anywhere on the row → forwards the session id to the parent for
 * navigation. Extracted from the inline `<tr>` block in SessionsPage so the
 * table loop stays readable as columns expand.
 */
export function SessionTableRow({ session, onRowClick }: SessionTableRowProps) {
  return (
    <tr
      data-testid={`session-row-${session.id}`}
      onClick={() => onRowClick(session.id)}
      className="border-b border-border/50 cursor-pointer hover:bg-muted/50 transition-colors"
    >
      <td className="py-2.5 pr-4 font-medium text-card-foreground">
        {session.agentName}
      </td>
      <td className="py-2.5 pr-4 text-muted-foreground">
        {session.model ?? '--'}
      </td>
      <td className="py-2.5 pr-4 text-muted-foreground">
        {formatTimestamp(session.startedAt)}
      </td>
      <td className="py-2.5 pr-4 text-right text-muted-foreground">
        {formatDuration(session.durationMs)}
      </td>
      <td className="py-2.5 pr-4 text-right">{session.turnCount}</td>
      <td className="py-2.5 pr-4 text-right">{session.toolCallCount}</td>
      <td className="py-2.5 pr-4 text-right text-muted-foreground">
        {formatTokens(session.totalInputTokens)} /{' '}
        {formatTokens(session.totalOutputTokens)}
      </td>
      <td className="py-2.5 pr-4 text-right">
        {formatCost(session.totalCostUsd)}
      </td>
      <td className="py-2.5 pr-4">
        <StatusBadge status={session.status} />
      </td>
      <td
        className="py-2.5 w-32"
        data-testid={`session-row-bar-cell-${session.id}`}
      >
        {session.breakdown ? (
          <div data-testid={`session-row-bar-${session.id}`}>
            <ContextBar
              breakdown={session.breakdown}
              budget={DEFAULT_CONTEXT_BUDGET}
              size="sm"
              ariaLabel={`Context window for ${session.agentName}`}
            />
          </div>
        ) : (
          <div
            data-testid={`session-row-bar-fallback-${session.id}`}
            aria-label="No context snapshot yet"
            className="h-1.5 w-full rounded bg-muted"
          />
        )}
      </td>
    </tr>
  );
}
