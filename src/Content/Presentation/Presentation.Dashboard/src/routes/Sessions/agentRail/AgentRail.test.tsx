import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { AgentRail } from './AgentRail';
import type { AgentRollup } from '@/lib/agentRoster';

function rollup(id: string, name: string, overrides: Partial<AgentRollup> = {}): AgentRollup {
  return {
    id,
    name,
    role: '',
    sessionCount: 0,
    lastActivity: null,
    color: 'system',
    initials: name.slice(0, 1),
    ...overrides,
  };
}

const roster: AgentRollup[] = [
  rollup('a-code', 'CodeAssistant', { sessionCount: 5, role: 'Pair coder' }),
  rollup('a-research', 'ResearchAgent', { sessionCount: 2 }),
];

describe('AgentRail', () => {
  it('renders one tile per roster entry plus the "All agents" pseudo-tile', () => {
    render(
      <AgentRail
        agents={roster}
        selectedAgentId={null}
        onSelectAgent={() => {}}
      />,
    );

    expect(screen.getByTestId('agent-rail-all')).toBeTruthy();
    expect(screen.getByTestId('agent-rail-tile-a-code')).toBeTruthy();
    expect(screen.getByTestId('agent-rail-tile-a-research')).toBeTruthy();
  });

  it('"All agents" is active when no selection is set', () => {
    render(
      <AgentRail
        agents={roster}
        selectedAgentId={null}
        onSelectAgent={() => {}}
      />,
    );

    expect(screen.getByTestId('agent-rail-all').getAttribute('data-active')).toBe('true');
    expect(
      screen.getByTestId('agent-rail-tile-a-code').getAttribute('data-active'),
    ).toBeNull();
  });

  it('marks the matching tile active and dims the others when filtered', () => {
    render(
      <AgentRail
        agents={roster}
        selectedAgentId="a-research"
        onSelectAgent={() => {}}
      />,
    );

    expect(
      screen.getByTestId('agent-rail-tile-a-research').getAttribute('data-active'),
    ).toBe('true');
    expect(
      screen.getByTestId('agent-rail-tile-a-code').getAttribute('data-dimmed'),
    ).toBe('true');
    expect(screen.getByTestId('agent-rail-all').getAttribute('data-active')).toBeNull();
  });

  it('clicking a tile forwards the agent id to onSelectAgent', () => {
    const onSelect = vi.fn();
    render(
      <AgentRail
        agents={roster}
        selectedAgentId={null}
        onSelectAgent={onSelect}
      />,
    );

    fireEvent.click(screen.getByTestId('agent-rail-tile-a-code'));
    expect(onSelect).toHaveBeenCalledWith('a-code');
  });

  it('clicking "All agents" forwards null', () => {
    const onSelect = vi.fn();
    render(
      <AgentRail
        agents={roster}
        selectedAgentId="a-code"
        onSelectAgent={onSelect}
      />,
    );

    fireEvent.click(screen.getByTestId('agent-rail-all'));
    expect(onSelect).toHaveBeenCalledWith(null);
  });

  it('renders an empty-state hint when the roster is empty', () => {
    render(
      <AgentRail
        agents={[]}
        selectedAgentId={null}
        onSelectAgent={() => {}}
      />,
    );

    expect(screen.getByTestId('agent-rail-empty')).toBeTruthy();
    expect(screen.getByTestId('agent-rail-all')).toBeTruthy();
  });

  it('renders the session count for the All agents tile', () => {
    render(
      <AgentRail
        agents={roster}
        selectedAgentId={null}
        onSelectAgent={() => {}}
      />,
    );

    const allTile = screen.getByTestId('agent-rail-all');
    expect(allTile.textContent).toContain('7'); // 5 + 2
  });
});
