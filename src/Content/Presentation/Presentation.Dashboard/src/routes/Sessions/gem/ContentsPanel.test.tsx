import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { ContentsPanel } from './ContentsPanel';
import { CURRENT_TURN } from './useSessionGemState';
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
    capturedAtUtc: new Date().toISOString(),
  };
}

describe('ContentsPanel', () => {
  it('renders an empty-state hint when no items have landed', () => {
    render(
      <ContentsPanel
        snapshots={[]}
        activeTurnIndex={CURRENT_TURN}
        activeCategory={null}
        onItemClick={() => {}}
      />,
    );
    expect(screen.getByTestId('contents-panel-empty')).toBeTruthy();
  });

  it('renders one lane per category when activeCategory is null', () => {
    render(
      <ContentsPanel
        snapshots={[
          snap(0, [
            { what: 'rules/testing', tokens: 100, cat: 'agents' },
            { what: 'Skill X', tokens: 200, cat: 'skills' },
            { what: 'User message', tokens: 50, cat: 'messages' },
          ]),
        ]}
        activeTurnIndex={CURRENT_TURN}
        activeCategory={null}
        onItemClick={() => {}}
      />,
    );

    expect(screen.getByTestId('contents-lane-system')).toBeTruthy();
    expect(screen.getByTestId('contents-lane-agents')).toBeTruthy();
    expect(screen.getByTestId('contents-lane-skills')).toBeTruthy();
    expect(screen.getByTestId('contents-lane-tools')).toBeTruthy();
    expect(screen.getByTestId('contents-lane-mcp')).toBeTruthy();
    expect(screen.getByTestId('contents-lane-messages')).toBeTruthy();
  });

  it('renders only the active category lane when activeCategory is set', () => {
    render(
      <ContentsPanel
        snapshots={[
          snap(0, [
            { what: 'rules/testing', tokens: 100, cat: 'agents' },
            { what: 'Skill X', tokens: 200, cat: 'skills' },
          ]),
        ]}
        activeTurnIndex={CURRENT_TURN}
        activeCategory="agents"
        onItemClick={() => {}}
      />,
    );

    expect(screen.getByTestId('contents-lane-agents')).toBeTruthy();
    expect(screen.queryByTestId('contents-lane-skills')).toBeNull();
    expect(screen.queryByTestId('contents-lane-messages')).toBeNull();
  });

  it('sorts items biggest-first inside a lane', () => {
    render(
      <ContentsPanel
        snapshots={[
          snap(0, [
            { what: 'small', tokens: 10, cat: 'tools' },
            { what: 'big', tokens: 500, cat: 'tools' },
            { what: 'medium', tokens: 100, cat: 'tools' },
          ]),
        ]}
        activeTurnIndex={CURRENT_TURN}
        activeCategory="tools"
        onItemClick={() => {}}
      />,
    );

    const lane = screen.getByTestId('contents-lane-tools');
    const rows = within(lane).getAllByRole('button');
    expect(rows[0]!.textContent).toContain('big');
    expect(rows[1]!.textContent).toContain('medium');
    expect(rows[2]!.textContent).toContain('small');
  });

  it('caps at activeTurnIndex so later-turn items are excluded', () => {
    render(
      <ContentsPanel
        snapshots={[
          snap(0, [{ what: 'turn-0', tokens: 100, cat: 'messages' }]),
          snap(1, [{ what: 'turn-1', tokens: 200, cat: 'messages' }]),
          snap(2, [{ what: 'turn-2', tokens: 400, cat: 'messages' }]),
        ]}
        activeTurnIndex={0}
        activeCategory="messages"
        onItemClick={() => {}}
      />,
    );

    const lane = screen.getByTestId('contents-lane-messages');
    expect(lane.textContent).toContain('turn-0');
    expect(lane.textContent).not.toContain('turn-1');
    expect(lane.textContent).not.toContain('turn-2');
  });

  it('invokes onItemClick with the clicked LoadedItem', () => {
    const onItemClick = vi.fn();
    const target = { what: 'big', tokens: 500, cat: 'tools' as const };
    render(
      <ContentsPanel
        snapshots={[snap(0, [target])]}
        activeTurnIndex={CURRENT_TURN}
        activeCategory="tools"
        onItemClick={onItemClick}
      />,
    );

    fireEvent.click(screen.getByTestId('contents-row-tools-0'));
    expect(onItemClick).toHaveBeenCalledTimes(1);
    expect(onItemClick).toHaveBeenCalledWith(target, 0);
  });
});
