import { describe, it, expect } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useSessionsFilter } from './useSessionsFilter';
import type { SessionRecord } from '@/api/types';
import type { AgentRollup } from '@/lib/agentRoster';

function session(name: string, id: string): SessionRecord {
  return {
    id,
    conversationId: `conv-${id}`,
    agentName: name,
    model: null,
    startedAt: new Date(2026, 0, 1).toISOString(),
    endedAt: null,
    durationMs: null,
    turnCount: 0,
    toolCallCount: 0,
    subagentCount: 0,
    totalInputTokens: 0,
    totalOutputTokens: 0,
    totalCacheRead: 0,
    totalCacheWrite: 0,
    totalCostUsd: 0,
    cacheHitRate: 0,
    status: 'completed',
    errorMessage: null,
    createdAt: new Date(2026, 0, 1).toISOString(),
    breakdown: null,
  };
}

function rollup(id: string, name: string): AgentRollup {
  return {
    id,
    name,
    role: '',
    sessionCount: 0,
    lastActivity: null,
    color: 'system',
    initials: name.slice(0, 1),
  };
}

const sessions: SessionRecord[] = [
  session('CodeAssistant', 'a1'),
  session('CodeAssistant', 'a2'),
  session('ResearchAgent', 'r1'),
];

const roster: AgentRollup[] = [
  rollup('agent-code', 'CodeAssistant'),
  rollup('agent-research', 'ResearchAgent'),
];

describe('useSessionsFilter', () => {
  it('starts with no selection and returns all sessions', () => {
    const { result } = renderHook(() => useSessionsFilter({ sessions, roster }));
    expect(result.current.selectedAgentId).toBeNull();
    expect(result.current.isFiltered).toBe(false);
    expect(result.current.filteredSessions).toHaveLength(3);
  });

  it('narrows the list when an agent is selected', () => {
    const { result } = renderHook(() => useSessionsFilter({ sessions, roster }));
    act(() => result.current.selectAgent('agent-code'));

    expect(result.current.isFiltered).toBe(true);
    expect(result.current.filteredSessions.map((s) => s.id)).toEqual([
      'a1',
      'a2',
    ]);
  });

  it('clears the filter when null is passed to selectAgent', () => {
    const { result } = renderHook(() => useSessionsFilter({ sessions, roster }));
    act(() => result.current.selectAgent('agent-research'));
    expect(result.current.filteredSessions).toHaveLength(1);

    act(() => result.current.selectAgent(null));
    expect(result.current.filteredSessions).toHaveLength(3);
    expect(result.current.isFiltered).toBe(false);
  });

  it('auto-clears the selection when the id is unknown to the roster', () => {
    // Code-review fix: a stale selectedAgentId (left over from a roster
    // transition where ids shifted) used to render a silent empty list with
    // no active tile. Now the hook resets to null in a useEffect so the
    // "All agents" tile reappears as the active state.
    const { result } = renderHook(() => useSessionsFilter({ sessions, roster }));
    act(() => result.current.selectAgent('agent-ghost'));

    // After the effect runs, selection clears and the full list returns.
    expect(result.current.selectedAgentId).toBeNull();
    expect(result.current.filteredSessions).toHaveLength(3);
    expect(result.current.isFiltered).toBe(false);
  });

  it('matches by name even when the id differs (fallback rosters use name as id)', () => {
    const fallbackRoster: AgentRollup[] = [
      rollup('CodeAssistant', 'CodeAssistant'),
    ];
    const { result } = renderHook(() =>
      useSessionsFilter({ sessions, roster: fallbackRoster }),
    );
    act(() => result.current.selectAgent('CodeAssistant'));
    expect(result.current.filteredSessions).toHaveLength(2);
  });
});
