import { screen, waitFor, fireEvent, within } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { renderPage } from '@/test/helpers/renderPage';
import SessionsPage from './SessionsPage';

describe('SessionsPage', () => {
  it('renders KPI cards for session metrics', async () => {
    renderPage(<SessionsPage />);

    const kpis = await screen.findAllByRole('status', {}, { timeout: 3000 });
    expect(kpis.length).toBeGreaterThanOrEqual(4);

    expect(screen.getByLabelText('Total Sessions')).toBeInTheDocument();
    expect(screen.getByLabelText('Active Sessions')).toBeInTheDocument();
    expect(screen.getByLabelText('Avg Turns/Session')).toBeInTheDocument();
    expect(screen.getByLabelText('Avg Duration')).toBeInTheDocument();
  });

  it('renders session table with data rows including model names', async () => {
    renderPage(<SessionsPage />);

    await waitFor(
      () => {
        expect(screen.getAllByText('CodeAssistant').length).toBeGreaterThanOrEqual(1);
      },
      { timeout: 3000 },
    );

    expect(screen.getAllByText('ResearchAgent').length).toBeGreaterThanOrEqual(1);
    // Pin Model column coverage — the deleted pre-PR-5 test asserted these.
    expect(screen.getAllByText('claude-3-opus').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('claude-3-sonnet').length).toBeGreaterThanOrEqual(1);
  });

  it('renders the agent rail with one tile per agent and an "All agents" pseudo-tile', async () => {
    renderPage(<SessionsPage />);

    // Wait for the rail AND at least one agent tile to mount — the rail
    // structure renders before the /api/agents query resolves so a plain
    // getByTestId('agent-rail') would race the agents fetch.
    await waitFor(
      () => {
        expect(screen.getByTestId('agent-rail-tile-agent-code')).toBeInTheDocument();
      },
      { timeout: 3000 },
    );

    expect(screen.getByTestId('agent-rail-all')).toBeInTheDocument();
    const rail = screen.getByTestId('agent-rail');
    const tiles = within(rail).getAllByTestId(/^agent-rail-tile-/);
    expect(tiles.length).toBeGreaterThanOrEqual(2);
  });

  it('filters the table when an agent tile is clicked, and clears on "All agents"', async () => {
    renderPage(<SessionsPage />);

    await waitFor(
      () => {
        expect(screen.getAllByText('CodeAssistant').length).toBeGreaterThanOrEqual(1);
      },
      { timeout: 3000 },
    );

    // Initial: all sessions rendered (3 from the MSW fixture).
    const initialRows = screen
      .getByTestId('session-table')
      .querySelectorAll('tbody tr').length;
    expect(initialRows).toBeGreaterThanOrEqual(2);

    // Click the CodeAssistant tile by id (id matches /api/agents mock).
    const codeTile = screen.getByTestId('agent-rail-tile-agent-code');
    fireEvent.click(codeTile);

    await waitFor(() => {
      const filteredTable = screen.getByTestId('session-table');
      // After filter, ResearchAgent rows are gone.
      expect(within(filteredTable).queryAllByText('ResearchAgent')).toHaveLength(0);
      // CodeAssistant rows remain.
      expect(within(filteredTable).getAllByText('CodeAssistant').length).toBeGreaterThanOrEqual(1);
    });

    // Clear filter via "All agents" tile.
    fireEvent.click(screen.getByTestId('agent-rail-all'));

    await waitFor(() => {
      expect(
        within(screen.getByTestId('session-table')).getAllByText('ResearchAgent')
          .length,
      ).toBeGreaterThanOrEqual(1);
    });
  });

  it('renders a mini ContextBar on rows whose session carries a breakdown', async () => {
    renderPage(<SessionsPage />);

    await waitFor(
      () => {
        // mockSessions row 0 (CodeAssistant / conv-1) carries `breakdown`.
        expect(
          screen.getByTestId('session-row-bar-11111111-1111-1111-1111-111111111111'),
        ).toBeInTheDocument();
      },
      { timeout: 3000 },
    );

    // The same row should NOT render the fallback muted rail.
    expect(
      screen.queryByTestId(
        'session-row-bar-fallback-11111111-1111-1111-1111-111111111111',
      ),
    ).toBeNull();
  });

  it('renders the muted fallback rail for rows without a breakdown', async () => {
    renderPage(<SessionsPage />);

    await waitFor(
      () => {
        // mockSessions row 2 (conv-3) has `breakdown: null`.
        expect(
          screen.getByTestId(
            'session-row-bar-fallback-33333333-3333-3333-3333-333333333333',
          ),
        ).toBeInTheDocument();
      },
      { timeout: 3000 },
    );
  });

  it('renders the Recent Sessions panel', async () => {
    renderPage(<SessionsPage />);
    await screen.findAllByRole('status', {}, { timeout: 3000 });
    expect(screen.getByText('Recent Sessions')).toBeInTheDocument();
  });
});
