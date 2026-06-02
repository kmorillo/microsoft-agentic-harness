import { describe, it, expect, beforeEach } from 'vitest';
import { screen, waitFor, fireEvent } from '@testing-library/react';
import { renderRoutedPage } from '@/test/helpers/renderPage';
import ContextInspectorPage from './ContextInspectorPage';
import { useSessionSnapshotsStore } from '@/stores/sessionSnapshotsStore';

const testSessionId = '11111111-1111-1111-1111-111111111111';

describe('ContextInspectorPage', () => {
  beforeEach(() => {
    useSessionSnapshotsStore.setState({ byConversation: {} });
  });

  it('renders the six category lanes once the session loads', async () => {
    renderRoutedPage(ContextInspectorPage, {
      route: `/sessions/${testSessionId}/context`,
      path: '/sessions/:sessionId/context',
    });

    await waitFor(
      () => {
        expect(screen.getByTestId('context-inspector-grid')).toBeInTheDocument();
      },
      { timeout: 3000 },
    );

    expect(screen.getByTestId('inspector-lane-system')).toBeInTheDocument();
    expect(screen.getByTestId('inspector-lane-agents')).toBeInTheDocument();
    expect(screen.getByTestId('inspector-lane-skills')).toBeInTheDocument();
    expect(screen.getByTestId('inspector-lane-tools')).toBeInTheDocument();
    expect(screen.getByTestId('inspector-lane-mcp')).toBeInTheDocument();
    expect(screen.getByTestId('inspector-lane-messages')).toBeInTheDocument();
  });

  it('renders loaded-item rows for categories with content', async () => {
    renderRoutedPage(ContextInspectorPage, {
      route: `/sessions/${testSessionId}/context`,
      path: '/sessions/:sessionId/context',
    });

    // mockSessionDetail has snapshots with loaded items in 'messages'.
    await waitFor(
      () => {
        expect(screen.getByTestId('inspector-row-messages-0')).toBeInTheDocument();
      },
      { timeout: 3000 },
    );
  });

  it('shows an empty-state hint in lanes with no items', async () => {
    renderRoutedPage(ContextInspectorPage, {
      route: `/sessions/${testSessionId}/context`,
      path: '/sessions/:sessionId/context',
    });

    await waitFor(
      () => {
        expect(screen.getByTestId('inspector-lane-system-empty')).toBeInTheDocument();
      },
      { timeout: 3000 },
    );
  });

  it('opens the ContextDrawer when a row is clicked', async () => {
    renderRoutedPage(ContextInspectorPage, {
      route: `/sessions/${testSessionId}/context`,
      path: '/sessions/:sessionId/context',
    });

    await waitFor(
      () => {
        expect(screen.getByTestId('inspector-row-messages-0')).toBeInTheDocument();
      },
      { timeout: 3000 },
    );

    fireEvent.click(screen.getByTestId('inspector-row-messages-0'));

    await waitFor(() => {
      expect(screen.getByTestId('context-drawer')).toBeInTheDocument();
    });
  });
});
