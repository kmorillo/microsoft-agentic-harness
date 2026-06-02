import { useEffect, useRef } from 'react';
import type { HubConnection } from '@microsoft/signalr';
import { buildHubConnection } from './signalrClient';
import { HUB_EVENTS, type HubEventName } from './eventTypes';
import { useTelemetryStore } from '@/stores/telemetryStore';
import { useSessionSnapshotsStore } from '@/stores/sessionSnapshotsStore';
import { IS_AUTH_DISABLED } from '@/auth/devAuth';
import { msalInstance, loginRequest } from '@/auth/authConfig';
import type { TelemetryEvent, ContextSnapshotEvent } from '@/api/types';

async function getToken(): Promise<string> {
  if (IS_AUTH_DISABLED) return '';
  const account = msalInstance.getAllAccounts()[0];
  if (!account) return '';
  const result = await msalInstance.acquireTokenSilent({ account, scopes: loginRequest.scopes });
  return result.accessToken;
}

// Module-scoped handle so non-React callers (session-detail page hooks in PR 4)
// can join the per-conversation Foresight observer group without re-creating
// a connection. Set by useTelemetryStream on mount, cleared on unmount.
let activeConnection: HubConnection | null = null;

/**
 * Conversations the app wants to be subscribed to. The set is maintained even
 * when {@link activeConnection} is null or not yet `Connected` — once the hub
 * connects, every entry is replayed as a `SubscribeToConversationSnapshots`
 * invocation. This closes the cold-load race where a page mounts and calls
 * `subscribeToConversationSnapshots` before `connection.start()` resolves.
 *
 * Eagerly-tracked even while disconnected so the cleanup path can pop an
 * intent off the set even if the actual server-side join never happened —
 * preventing orphan group joins after rapid mount/unmount cycles.
 */
const desiredSubscriptions = new Set<string>();

async function flushSubscription(conversationId: string): Promise<void> {
  if (!activeConnection || activeConnection.state !== 'Connected') return;
  try {
    await activeConnection.invoke(
      'SubscribeToConversationSnapshots',
      conversationId,
    );
  } catch (err) {
    // Swallow individual subscribe failures — typically auth / role denials.
    // The desired-subscriptions set still tracks intent so a reconnect can
    // retry; the caller has already logged the failure surface.
    console.warn(
      '[Foresight] Subscribe to conversation snapshots failed.',
      conversationId,
      err,
    );
    return;
  }
  // Cleanup race: a caller may have unsubscribed (cleared the intent) while
  // our subscribe invoke was in-flight. The server-side group join has now
  // landed but no React tree wants it — immediately leave so we don't leak
  // a membership that keeps pushing snapshots into the store for nobody.
  if (!desiredSubscriptions.has(conversationId)) {
    void flushUnsubscription(conversationId);
  }
}

async function flushUnsubscription(conversationId: string): Promise<void> {
  if (!activeConnection || activeConnection.state !== 'Connected') return;
  try {
    await activeConnection.invoke(
      'UnsubscribeFromConversationSnapshots',
      conversationId,
    );
  } catch {
    // Best-effort cleanup; nothing useful to do on disconnect.
  }
}

/**
 * Joins the per-conversation Foresight observer group so this connection
 * starts receiving `ContextSnapshot` broadcasts for that conversation. Records
 * the intent immediately; if the hub isn't yet connected, the subscription is
 * replayed once `connection.start()` resolves. Requires the
 * `AgentHub.Foresight.Observe` app role on the user.
 */
export async function subscribeToConversationSnapshots(
  conversationId: string,
): Promise<void> {
  desiredSubscriptions.add(conversationId);
  await flushSubscription(conversationId);
}

/**
 * Pairs with {@link subscribeToConversationSnapshots}. Removes the intent so
 * an unmounted page doesn't accidentally re-join on a future reconnect, and
 * invokes the server-side leave when the connection is live.
 */
export async function unsubscribeFromConversationSnapshots(
  conversationId: string,
): Promise<void> {
  desiredSubscriptions.delete(conversationId);
  await flushUnsubscription(conversationId);
}

export function useTelemetryStream(): void {
  const connectionRef = useRef<HubConnection | null>(null);
  const push = useTelemetryStore((s) => s.push);
  const setConnected = useTelemetryStore((s) => s.setConnected);
  const appendSnapshot = useSessionSnapshotsStore((s) => s.appendSnapshot);

  useEffect(() => {
    const connection = buildHubConnection('/hubs/agent', getToken);
    connectionRef.current = connection;
    activeConnection = connection;

    const eventNames = Object.values(HUB_EVENTS) as HubEventName[];
    for (const eventName of eventNames) {
      connection.on(eventName, (data: Record<string, unknown>) => {
        // ContextSnapshot is routed exclusively to the per-session store —
        // the generic ring buffer would double-hold the largest event type
        // for no consumer benefit.
        if (eventName === HUB_EVENTS.ContextSnapshot) {
          appendSnapshot(data as unknown as ContextSnapshotEvent);
          return;
        }

        const event: TelemetryEvent = {
          type: eventName,
          timestamp: Date.now(),
          data,
        };
        push(event);
      });
    }

    connection.onclose(() => setConnected(false));
    connection.onreconnected(() => {
      setConnected(true);
      // Reconnects drop server-side group memberships; replay our desired set
      // so pages that subscribed before the reconnect don't silently lose
      // live updates.
      for (const id of desiredSubscriptions) void flushSubscription(id);
    });
    connection.onreconnecting(() => setConnected(false));

    connection
      .start()
      .then(() => {
        setConnected(true);
        // Drain any subscribe intents that arrived before connect resolved
        // (the cold-load race: page mounts and invokes subscribe while we're
        // still negotiating).
        for (const id of desiredSubscriptions) void flushSubscription(id);
      })
      .catch(() => setConnected(false));

    return () => {
      if (activeConnection === connection) activeConnection = null;
      connection.stop();
    };
  }, [push, setConnected, appendSnapshot]);
}
