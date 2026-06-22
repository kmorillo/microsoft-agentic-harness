import { describe, it, expect, beforeEach } from 'vitest';
import { http, HttpResponse } from 'msw';
import { screen, waitFor, fireEvent } from '@testing-library/react';
import { renderRoutedPage } from '@/test/helpers/renderPage';
import ContextInspectorPage from './ContextInspectorPage';
import { useSessionSnapshotsStore } from '@/stores/sessionSnapshotsStore';
import { server } from '@/test/mocks/server';

const testSessionId = '11111111-1111-1111-1111-111111111111';

/**
 * A session whose snapshot carries a single `system` loaded item. The registration
 * categories (system / skills / tools / mcp / agents) resolve their body lazily via
 * the loaded-body endpoint — exactly the path that was stuck on "Loading…" on this
 * page before the shared resolver hook was wired in.
 */
const systemSnapshot = {
  conversationId: 'conv-1',
  turnIndex: 0,
  turnId: 't-00',
  ctxAfter: { system: 857, agents: 0, skills: 0, tools: 0, mcp: 0, messages: 0 },
  loaded: [{ what: 'System prompt', tokens: 857, cat: 'system' }],
  capturedAtUtc: new Date().toISOString(),
};

// Sentinel the test owns end-to-end: the snapshot exposes a system item, and the
// loaded-body endpoint returns exactly this text. Asserting on a string this test
// controls (rather than the global handler's wording) keeps the regression guard
// from breaking on unrelated mock edits.
const SYSTEM_PROMPT_BODY = 'You are the harness default agent. (inspector-test sentinel)';

function respondWithSystemSnapshot() {
  server.use(
    http.get('/api/sessions/:id', ({ params }) =>
      HttpResponse.json({
        session: {
          id: params['id'] as string,
          conversationId: 'conv-1',
          agentName: 'TestAgent',
        },
        messages: [],
        tools: [],
        safetyEvents: [],
        snapshots: [systemSnapshot],
        breakdown: systemSnapshot.ctxAfter,
      }),
    ),
    http.get(
      '/api/sessions/:id/turns/:turnIndex/loaded/:loadedIndex/body',
      ({ params }) =>
        HttpResponse.json({
          conversationId: 'conv-1',
          turnIndex: Number(params['turnIndex']),
          loadedIndex: Number(params['loadedIndex']),
          body: SYSTEM_PROMPT_BODY,
        }),
    ),
  );
}

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

  it('resolves the real system-prompt body in the drawer instead of a stuck "Loading…"', async () => {
    respondWithSystemSnapshot();

    renderRoutedPage(ContextInspectorPage, {
      route: `/sessions/${testSessionId}/context`,
      path: '/sessions/:sessionId/context',
    });

    await waitFor(
      () => {
        expect(screen.getByTestId('inspector-row-system-0')).toBeInTheDocument();
      },
      { timeout: 3000 },
    );

    fireEvent.click(screen.getByTestId('inspector-row-system-0'));

    await waitFor(() => {
      expect(screen.getByTestId('context-drawer')).toBeInTheDocument();
    });

    // The drawer must swap its "Loading…" placeholder for the body served by the
    // loaded-body endpoint. Before the resolver hook was wired into this page the
    // placeholder was the final state — this is the regression guard.
    await waitFor(() => {
      expect(screen.getByTestId('context-drawer-body')).toHaveTextContent(
        SYSTEM_PROMPT_BODY,
      );
    });
    expect(screen.getByTestId('context-drawer-body')).not.toHaveTextContent(
      'Loading…',
    );
  });
});
