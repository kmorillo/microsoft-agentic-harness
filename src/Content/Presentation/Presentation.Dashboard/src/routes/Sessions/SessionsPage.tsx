import { useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { usePromQuery } from '@/hooks/usePromQuery';
import { metricCatalog } from '@/config/metricCatalog';
import { fetchSessions } from '@/api/sessions';
import { fetchAgents } from '@/api/agents';
import { useTimeRangeStore } from '@/stores/timeRangeStore';
import { KpiCard } from '@/components/panels/KpiCard';
import { PanelCard } from '@/components/panels/PanelCard';
import { PanelGrid } from '@/components/panels/PanelGrid';
import { LoadingSkeleton } from '@/components/panels/LoadingSkeleton';
import { EmptyState } from '@/components/panels/EmptyState';
import { PageHeader } from '@/components/primitives/PageHeader';
import { buildAgentRoster } from '@/lib/agentRoster';
import { AgentRail } from './agentRail/AgentRail';
import { useSessionsFilter } from './agentRail/useSessionsFilter';
import { SessionTableRow } from './SessionTableRow';
import { formatDurationSeconds } from './format';
import type { SessionRecord } from '@/api/types';

function latestValue(data: ReturnType<typeof usePromQuery>['data']): number {
  const dp = data?.series[0]?.dataPoints;
  if (!dp || dp.length === 0) return 0;
  return parseFloat(dp[dp.length - 1]!.value) || 0;
}

export default function SessionsPage() {
  const navigate = useNavigate();
  const preset = useTimeRangeStore((s) => s.preset);
  const customStart = useTimeRangeStore((s) => s.customStart);
  const customEnd = useTimeRangeStore((s) => s.customEnd);
  const getRange = useTimeRangeStore((s) => s.getRange);
  const { start, end } = useMemo(
    () => getRange(),
    [preset, customStart, customEnd, getRange],
  );

  const sessionsTotal = usePromQuery(metricCatalog['sessions_total']!.query);
  const sessionsActive = usePromQuery(metricCatalog['sessions_active']!.query);
  const turnsAvg = usePromQuery(metricCatalog['sessions_turns_avg']!.query);
  const durationAvg = usePromQuery(metricCatalog['sessions_duration_avg']!.query);

  const sessionsQuery = useQuery({
    queryKey: ['sessions-list', start, end],
    queryFn: () => fetchSessions(50, 0, undefined, start, end),
    staleTime: 30_000,
  });

  // Separate query so the rail can render once agents come back; refetches
  // on its own 60s cadence and survives time-range changes (agents are not
  // time-bound). Single retry — registry failures are typically auth/role
  // problems where hammering doesn't help; surface via `degraded` instead.
  const agentsQuery = useQuery({
    queryKey: ['agents'],
    queryFn: fetchAgents,
    staleTime: 60_000,
    retry: 1,
  });

  const sessions = sessionsQuery.data ?? [];
  const roster = useMemo(
    () => buildAgentRoster(agentsQuery.data ?? [], sessions),
    [agentsQuery.data, sessions],
  );

  const filter = useSessionsFilter({ sessions, roster });

  const isKpiLoading = sessionsTotal.isLoading;

  return (
    <div className="space-y-6">
      <PageHeader title="Sessions" subtitle="Agents in flight + per-session context-window composition" />

      {isKpiLoading ? (
        <PanelGrid columns={4}>
          {Array.from({ length: 4 }).map((_, i) => <LoadingSkeleton key={i} />)}
        </PanelGrid>
      ) : (
        <PanelGrid columns={4}>
          <KpiCard
            title="Total Sessions"
            description="Number of agent conversation sessions started in the selected time range. Shows 0 when no conversations have been initiated."
            value={latestValue(sessionsTotal.data).toFixed(0)}
            sparklineData={sessionsTotal.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Active Sessions"
            description="Sessions currently in progress with an open connection. Shows 0 when all sessions have completed or no agents are running."
            value={latestValue(sessionsActive.data).toFixed(0)}
            sparklineData={sessionsActive.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Avg Turns/Session"
            description="Mean number of user-agent exchange turns per session. Shows 0 when no completed sessions exist in the selected time range."
            value={latestValue(turnsAvg.data).toFixed(1)}
            sparklineData={turnsAvg.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Avg Duration"
            description="Mean wall-clock time from session start to session end. Shows 0 when no completed sessions exist in the selected time range."
            value={formatDurationSeconds(latestValue(durationAvg.data))}
            sparklineData={durationAvg.data?.series[0]?.dataPoints}
          />
        </PanelGrid>
      )}

      <div className="flex items-start gap-6">
        <AgentRail
          agents={roster}
          selectedAgentId={filter.selectedAgentId}
          onSelectAgent={filter.selectAgent}
          degraded={agentsQuery.isError}
        />

        <PanelCard
          title="Recent Sessions"
          description={
            filter.isFiltered
              ? `Filtered to one agent — ${filter.filteredSessions.length} session${filter.filteredSessions.length === 1 ? '' : 's'}`
              : 'Click a row to view session details'
          }
          className="flex-1 min-w-0"
        >
          <SessionTable
            sessions={filter.filteredSessions}
            isLoading={sessionsQuery.isLoading}
            isError={sessionsQuery.isError}
            isFiltered={filter.isFiltered}
            onRowClick={(id) => navigate(`/sessions/${id}`)}
          />
        </PanelCard>
      </div>
    </div>
  );
}

interface SessionTableProps {
  sessions: SessionRecord[];
  isLoading: boolean;
  isError: boolean;
  isFiltered: boolean;
  onRowClick: (id: string) => void;
}

function SessionTable({
  sessions,
  isLoading,
  isError,
  isFiltered,
  onRowClick,
}: SessionTableProps) {
  if (isLoading) {
    return (
      <div className="space-y-3">
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="h-10 bg-muted rounded animate-pulse" />
        ))}
      </div>
    );
  }

  if (isError) {
    return (
      <EmptyState
        title="Unable to load sessions"
        description="The session store may not be configured. Sessions are recorded when a PostgreSQL connection is available."
      />
    );
  }

  if (sessions.length === 0) {
    return (
      <EmptyState
        title={isFiltered ? 'No sessions for this agent' : 'No sessions yet'}
        description={
          isFiltered
            ? 'This agent has no sessions in the current time range. Click another tile or "All agents" to reset.'
            : 'Sessions appear here once an agent runs at least one conversation in the selected time range.'
        }
      />
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm" data-testid="session-table">
        <thead>
          <tr className="border-b border-border text-left text-xs font-medium text-muted-foreground uppercase tracking-wider">
            <th className="pb-2 pr-4">Agent</th>
            <th className="pb-2 pr-4">Model</th>
            <th className="pb-2 pr-4">Started</th>
            <th className="pb-2 pr-4 text-right">Duration</th>
            <th className="pb-2 pr-4 text-right">Turns</th>
            <th className="pb-2 pr-4 text-right">Tools</th>
            <th className="pb-2 pr-4 text-right">Tokens</th>
            <th className="pb-2 pr-4 text-right">Cost</th>
            <th className="pb-2 pr-4">Status</th>
            <th className="pb-2 w-32">Context</th>
          </tr>
        </thead>
        <tbody>
          {sessions.map((s) => (
            <SessionTableRow key={s.id} session={s} onRowClick={onRowClick} />
          ))}
        </tbody>
      </table>
    </div>
  );
}
