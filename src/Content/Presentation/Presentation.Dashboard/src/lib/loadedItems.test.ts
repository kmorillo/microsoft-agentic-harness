import { describe, it, expect } from 'vitest';
import { aggregateLoadedItems, totalTokensInCategory } from './loadedItems';
import type { ContextSnapshotEvent } from '@/api/types';

function snap(
  turnIndex: number,
  loaded: ContextSnapshotEvent['loaded'],
): ContextSnapshotEvent {
  return {
    conversationId: 'c',
    turnIndex,
    turnId: `t-${String(turnIndex).padStart(2, '0')}`,
    ctxAfter: { system: 0, agents: 0, skills: 0, tools: 0, mcp: 0, messages: 0 },
    loaded,
    capturedAtUtc: new Date(2026, 0, 1, 0, turnIndex).toISOString(),
  };
}

describe('aggregateLoadedItems', () => {
  it('returns an empty record with every category present when given no snapshots', () => {
    const result = aggregateLoadedItems([]);
    expect(Object.keys(result).sort()).toEqual(
      ['agents', 'mcp', 'messages', 'skills', 'system', 'tools'].sort(),
    );
    expect(Object.values(result).every((v) => v.length === 0)).toBe(true);
  });

  it('groups items across multiple snapshots into the right category lanes', () => {
    const result = aggregateLoadedItems([
      snap(0, [
        { what: 'User message', tokens: 100, cat: 'messages' },
        { what: 'Skill X', tokens: 200, cat: 'skills' },
      ]),
      snap(1, [
        { what: 'Assistant', tokens: 300, cat: 'messages' },
        { what: 'Tool Read', tokens: 50, cat: 'tools' },
      ]),
    ]);

    expect(result.messages).toHaveLength(2);
    expect(result.messages.map((i) => i.what)).toEqual(['User message', 'Assistant']);
    expect(result.skills).toHaveLength(1);
    expect(result.tools).toHaveLength(1);
    expect(result.agents).toEqual([]);
    expect(result.mcp).toEqual([]);
    expect(result.system).toEqual([]);
  });

  it('caps inclusively at upToTurnInclusive — items from later turns are excluded', () => {
    const all = [
      snap(0, [{ what: 'A', tokens: 1, cat: 'messages' }]),
      snap(1, [{ what: 'B', tokens: 2, cat: 'messages' }]),
      snap(2, [{ what: 'C', tokens: 4, cat: 'messages' }]),
    ];

    expect(aggregateLoadedItems(all, 0).messages.map((i) => i.what)).toEqual(['A']);
    expect(aggregateLoadedItems(all, 1).messages.map((i) => i.what)).toEqual(['A', 'B']);
    expect(aggregateLoadedItems(all, 2).messages.map((i) => i.what)).toEqual(['A', 'B', 'C']);
  });

  it('breaks early when a snapshot beyond the cap is encountered', () => {
    // Indirect verification: passing an enormous cap is equivalent to no cap.
    const snaps = [
      snap(0, [{ what: 'A', tokens: 1, cat: 'messages' }]),
      snap(1, [{ what: 'B', tokens: 2, cat: 'messages' }]),
    ];
    expect(aggregateLoadedItems(snaps).messages).toHaveLength(2);
    expect(aggregateLoadedItems(snaps, 999).messages).toHaveLength(2);
  });

  it('treats nullish ref as a normal value', () => {
    const result = aggregateLoadedItems([
      snap(0, [
        { what: 'A', tokens: 1, cat: 'tools', ref: 'BillingPipeline.cs' },
        { what: 'B', tokens: 2, cat: 'tools', ref: null },
        { what: 'C', tokens: 3, cat: 'tools' },
      ]),
    ]);
    expect(result.tools).toHaveLength(3);
    expect(result.tools.map((i) => i.ref ?? null)).toEqual([
      'BillingPipeline.cs',
      null,
      null,
    ]);
  });
});

describe('totalTokensInCategory', () => {
  it('sums tokens', () => {
    expect(
      totalTokensInCategory([
        { what: 'A', tokens: 100, cat: 'messages' },
        { what: 'B', tokens: 250, cat: 'messages' },
      ]),
    ).toBe(350);
  });

  it('returns 0 for empty arrays', () => {
    expect(totalTokensInCategory([])).toBe(0);
  });
});
