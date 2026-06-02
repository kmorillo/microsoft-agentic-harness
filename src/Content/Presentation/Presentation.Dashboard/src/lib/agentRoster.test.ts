import { describe, it, expect } from 'vitest';
import {
  buildAgentRoster,
  deriveInitials,
  hashToCategory,
} from './agentRoster';
import type { AgentSummary, SessionRecord } from '@/api/types';

function session(overrides: Partial<SessionRecord>): SessionRecord {
  return {
    id: overrides.id ?? 'sess-1',
    conversationId: overrides.conversationId ?? 'conv-1',
    agentName: overrides.agentName ?? 'CodeAssistant',
    model: null,
    startedAt: overrides.startedAt ?? new Date(2026, 0, 1).toISOString(),
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
    ...overrides,
  };
}

function agent(overrides: Partial<AgentSummary>): AgentSummary {
  return {
    id: overrides.id ?? 'a-1',
    name: overrides.name ?? 'CodeAssistant',
    description: overrides.description ?? 'General purpose pair-coder',
    ...overrides,
  };
}

describe('buildAgentRoster — registry path', () => {
  it('returns one rollup per registry agent', () => {
    const roster = buildAgentRoster(
      [
        agent({ id: 'a-1', name: 'CodeAssistant' }),
        agent({ id: 'a-2', name: 'ResearchAgent', description: 'Reads the docs' }),
      ],
      [],
    );

    expect(roster).toHaveLength(2);
    expect(roster.map((r) => r.name)).toEqual(['CodeAssistant', 'ResearchAgent']);
  });

  it('includes registry agents that have zero sessions', () => {
    const roster = buildAgentRoster(
      [
        agent({ id: 'a-1', name: 'CodeAssistant' }),
        agent({ id: 'a-2', name: 'NeverRun' }),
      ],
      [session({ agentName: 'CodeAssistant' })],
    );

    const neverRun = roster.find((r) => r.name === 'NeverRun')!;
    expect(neverRun.sessionCount).toBe(0);
    expect(neverRun.lastActivity).toBeNull();
  });

  it('joins session count and most-recent startedAt onto each rollup', () => {
    const roster = buildAgentRoster(
      [agent({ id: 'a-1', name: 'CodeAssistant' })],
      [
        session({
          id: 's-old',
          agentName: 'CodeAssistant',
          startedAt: new Date(2026, 0, 1).toISOString(),
        }),
        session({
          id: 's-new',
          agentName: 'CodeAssistant',
          startedAt: new Date(2026, 5, 1).toISOString(),
        }),
        session({
          id: 's-mid',
          agentName: 'CodeAssistant',
          startedAt: new Date(2026, 2, 1).toISOString(),
        }),
      ],
    );

    expect(roster[0]!.sessionCount).toBe(3);
    expect(roster[0]!.lastActivity).toBe(new Date(2026, 5, 1).toISOString());
  });

  it('uses description as role; empty string when description is missing', () => {
    const roster = buildAgentRoster(
      [
        agent({ id: 'a-1', name: 'WithDesc', description: 'Does the thing' }),
        // record fields are non-nullable in TS — simulate the empty case
        // explicitly to pin behaviour.
        agent({ id: 'a-2', name: 'NoDesc', description: '' }),
      ],
      [],
    );

    expect(roster[0]!.role).toBe('Does the thing');
    expect(roster[1]!.role).toBe('');
  });
});

describe('buildAgentRoster — fallback path', () => {
  it('synthesises a roster from sessions when the registry is empty', () => {
    const roster = buildAgentRoster(
      [],
      [
        session({ id: 's-1', agentName: 'AlphaAgent' }),
        session({ id: 's-2', agentName: 'AlphaAgent' }),
        session({ id: 's-3', agentName: 'BetaAgent' }),
      ],
    );

    expect(roster).toHaveLength(2);
    expect(roster.map((r) => r.name)).toEqual(['AlphaAgent', 'BetaAgent']);
    expect(roster.find((r) => r.name === 'AlphaAgent')!.sessionCount).toBe(2);
    expect(roster.find((r) => r.name === 'BetaAgent')!.sessionCount).toBe(1);
  });

  it('fallback rollups have an empty role', () => {
    const roster = buildAgentRoster(
      [],
      [session({ agentName: 'AlphaAgent' })],
    );
    expect(roster[0]!.role).toBe('');
  });

  it('fallback ordering is stable (alphabetic by name)', () => {
    const roster = buildAgentRoster(
      [],
      [
        session({ id: 's-1', agentName: 'Charlie' }),
        session({ id: 's-2', agentName: 'Alpha' }),
        session({ id: 's-3', agentName: 'Bravo' }),
      ],
    );
    expect(roster.map((r) => r.name)).toEqual(['Alpha', 'Bravo', 'Charlie']);
  });

  it('returns an empty array when neither agents nor sessions are present', () => {
    expect(buildAgentRoster([], [])).toEqual([]);
  });
});

describe('hashToCategory', () => {
  it('is deterministic across calls', () => {
    const a = hashToCategory('CodeAssistant');
    const b = hashToCategory('CodeAssistant');
    expect(a).toBe(b);
  });

  it('returns a known CategoryKey value', () => {
    const cat = hashToCategory('AnyName');
    expect(['system', 'agents', 'skills', 'tools', 'mcp', 'messages']).toContain(cat);
  });

  it('produces different colours for different names (mostly)', () => {
    // Hash collisions are possible with 6 buckets, but a small sample should
    // hit at least two distinct categories.
    const cats = new Set([
      hashToCategory('one'),
      hashToCategory('two'),
      hashToCategory('three'),
      hashToCategory('four'),
      hashToCategory('five'),
      hashToCategory('six'),
    ]);
    expect(cats.size).toBeGreaterThan(1);
  });
});

describe('deriveInitials', () => {
  it('uppercases the first letter of each of the first two tokens', () => {
    expect(deriveInitials('Code Assistant')).toBe('CA');
    expect(deriveInitials('research agent')).toBe('RA');
  });

  it('returns a single letter for single-token names', () => {
    expect(deriveInitials('ResearchAgent')).toBe('R');
    expect(deriveInitials('x')).toBe('X');
  });

  it('returns "?" for empty / whitespace-only names', () => {
    expect(deriveInitials('')).toBe('?');
    expect(deriveInitials('   ')).toBe('?');
  });
});
