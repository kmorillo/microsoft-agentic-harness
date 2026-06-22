import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  fetchMessageBody,
  fetchToolInvocation,
  fetchLoadedBody,
} from '@/api/sessions';
import type { DrawerTarget } from './useSessionGemState';

/**
 * Resolves the full body for the currently open {@link DrawerTarget}.
 *
 * The drawer renders the static preview/card from `buildDrawerContent`
 * immediately (a short "Loading…" placeholder for the registration categories);
 * this hook then lazily fetches the real body keyed off `drawerItem.content.idRef`
 * and returns it so the caller can hand the resolved string to `ContextDrawer`.
 *
 *  - `message` → full `contentFull` from the message deep-link.
 *  - `tool`    → the JSON metadata card augmented with args + stdout.
 *  - `loaded-body` → the captured body (composed system prompt, skill
 *    instructions, tool/MCP schema, sub-agent description) from the loaded-body
 *    deep-link. This is the path the registration categories (system / skills /
 *    tools / mcp / agents) take.
 *
 * Queries are gated on the idRef kind, so only the relevant fetch fires. When no
 * idRef is present (e.g. a message with no backend record), the static fallback
 * body is returned unchanged.
 *
 * Shared by SessionDetailPage and ContextInspectorPage so both surfaces resolve
 * bodies identically — the two diverged once when this logic lived inline in
 * SessionDetailPage only, leaving the Inspector stuck on "Loading…".
 */
export function useResolvedDrawerBody(
  sessionId: string | undefined,
  drawerItem: DrawerTarget | null,
): string {
  const idRef = drawerItem?.content.idRef;

  // Narrow the discriminated union once, here, so the queries below read typed
  // fields instead of casting past the union (a cast would let a future field
  // rename compile and 404 at runtime).
  const messageId = idRef?.kind === 'message' ? idRef.id : null;
  const toolId = idRef?.kind === 'tool' ? idRef.id : null;
  const loadedRef = idRef?.kind === 'loaded-body' ? idRef : null;

  const messageBodyQuery = useQuery({
    queryKey: ['session-message-body', sessionId, messageId],
    queryFn: () => fetchMessageBody(sessionId!, messageId!),
    enabled: !!sessionId && !!messageId,
    staleTime: 60_000,
  });

  const toolDetailQuery = useQuery({
    queryKey: ['session-tool-invocation', sessionId, toolId],
    queryFn: () => fetchToolInvocation(sessionId!, toolId!),
    enabled: !!sessionId && !!toolId,
    staleTime: 60_000,
  });

  const loadedBodyQuery = useQuery({
    queryKey: [
      'session-loaded-body',
      sessionId,
      loadedRef?.turnIndex ?? null,
      loadedRef?.loadedIndex ?? null,
    ],
    queryFn: () => fetchLoadedBody(sessionId!, loadedRef!.turnIndex, loadedRef!.loadedIndex),
    enabled: !!sessionId && !!loadedRef,
    staleTime: 60_000,
    // 404 is the expected response for rows that predate body capture — don't
    // hammer the server retrying that.
    retry: false,
  });

  return useMemo(() => {
    const fallback = drawerItem?.content.body ?? '';
    if (!drawerItem || !idRef) return fallback;

    if (idRef.kind === 'message') {
      const full = messageBodyQuery.data?.contentFull;
      return typeof full === 'string' && full.length > 0 ? full : fallback;
    }

    if (idRef.kind === 'tool' && toolDetailQuery.data) {
      // Re-serialise the JSON card with args + stdout so the drawer's `json`
      // syntax styling continues to apply. The base card is already a JSON
      // string in `fallback`; parse, augment (immutably), re-stringify.
      try {
        const card = JSON.parse(fallback) as Record<string, unknown>;
        const augmented = {
          ...card,
          args: toolDetailQuery.data.args ?? null,
          stdout: toolDetailQuery.data.stdout ?? null,
          callId: toolDetailQuery.data.callId ?? null,
        };
        return JSON.stringify(augmented, null, 2);
      } catch {
        return fallback;
      }
    }

    if (idRef.kind === 'loaded-body') {
      const body = loadedBodyQuery.data?.body;
      return typeof body === 'string' && body.length > 0 ? body : fallback;
    }

    return fallback;
  }, [
    drawerItem,
    idRef,
    messageBodyQuery.data,
    toolDetailQuery.data,
    loadedBodyQuery.data,
  ]);
}
