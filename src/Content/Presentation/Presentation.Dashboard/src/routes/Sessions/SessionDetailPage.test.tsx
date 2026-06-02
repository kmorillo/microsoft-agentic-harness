import { screen, waitFor, within } from '@testing-library/react';
import { describe, it, expect, beforeEach } from 'vitest';
import { renderRoutedPage } from '@/test/helpers/renderPage';
import SessionDetailPage from './SessionDetailPage';
import { useSessionSnapshotsStore } from '@/stores/sessionSnapshotsStore';

const testSessionId = '11111111-1111-1111-1111-111111111111';

describe('SessionDetailPage', () => {
  beforeEach(() => {
    // Each test starts with a clean snapshot buffer so hydrate-from-MSW is
    // the only path that populates it.
    useSessionSnapshotsStore.setState({ byConversation: {} });
  });

  it('renders session header with agent info', async () => {
    renderRoutedPage(SessionDetailPage, {
      route: `/sessions/${testSessionId}`,
      path: '/sessions/:sessionId',
    });

    await waitFor(
      () => {
        expect(screen.getByText('CodeAssistant')).toBeInTheDocument();
      },
      { timeout: 3000 },
    );

    expect(screen.getByText('claude-3-opus')).toBeInTheDocument();
  });

  it('renders the Foresight hero gem when snapshots are available', async () => {
    renderRoutedPage(SessionDetailPage, {
      route: `/sessions/${testSessionId}`,
      path: '/sessions/:sessionId',
    });

    await waitFor(
      () => {
        expect(screen.getByTestId('session-hero')).toBeInTheDocument();
      },
      { timeout: 3000 },
    );

    // The hero owns the large ContextBar at the top of the page; timeline
    // rows render md bars too, so scope the assertion to the hero subtree.
    const hero = screen.getByTestId('session-hero');
    expect(within(hero).getByTestId('context-bar')).toBeInTheDocument();
  });

  it('renders one timeline row per hydrated snapshot with the message excerpt', async () => {
    renderRoutedPage(SessionDetailPage, {
      route: `/sessions/${testSessionId}`,
      path: '/sessions/:sessionId',
    });

    await waitFor(
      () => {
        // mockSnapshotsConv1 has two snapshots (turn 0, turn 1).
        expect(screen.getByTestId('timeline-row-0')).toBeInTheDocument();
        expect(screen.getByTestId('timeline-row-1')).toBeInTheDocument();
      },
      { timeout: 3000 },
    );

    // The turn-0 message excerpt joins by turnIndex from messages.
    expect(
      screen.getByText(/refactor the authentication module/i),
    ).toBeInTheDocument();
  });

  it('renders the Tool executions panel as a collapsible details summary', async () => {
    renderRoutedPage(SessionDetailPage, {
      route: `/sessions/${testSessionId}`,
      path: '/sessions/:sessionId',
    });

    await waitFor(
      () => {
        expect(screen.getByTestId('session-tools-panel')).toBeInTheDocument();
      },
      { timeout: 3000 },
    );

    const panel = screen.getByTestId('session-tools-panel');
    expect(panel.tagName.toLowerCase()).toBe('details');
    // Summary text reflects the count from the MSW fixture (2 tool rows).
    expect(panel.querySelector('summary')?.textContent).toMatch(
      /Tool executions/i,
    );
    // The ToolsTable body lives inside the details — present in the DOM even
    // when visually collapsed, so tool names are queryable.
    expect(within(panel).getByText('file_search')).toBeInTheDocument();
    expect(within(panel).getByText('code_exec')).toBeInTheDocument();
  });

  it('opens the ContextDrawer when a timeline loaded-item row is clicked', async () => {
    const { fireEvent } = await import('@testing-library/react');
    renderRoutedPage(SessionDetailPage, {
      route: `/sessions/${testSessionId}`,
      path: '/sessions/:sessionId',
    });

    await waitFor(
      () => {
        expect(screen.getByTestId('timeline-loaded-0-0')).toBeInTheDocument();
      },
      { timeout: 3000 },
    );

    fireEvent.click(screen.getByTestId('timeline-loaded-0-0'));

    await waitFor(() => {
      expect(screen.getByTestId('context-drawer')).toBeInTheDocument();
    });
  });
});
