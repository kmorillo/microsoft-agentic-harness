import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import type { HubConnection } from '@microsoft/signalr';
import { buildHubConnection } from '@/realtime/signalrClient';
import { IS_AUTH_DISABLED } from '@/auth/devAuth';
import { msalInstance, loginRequest } from '@/auth/authConfig';

const HUB_EVENT_EVAL_RUN_COMPLETED = 'EvalRunCompleted';
const JOIN_METHOD = 'JoinEvalDashboard';
const LEAVE_METHOD = 'LeaveEvalDashboard';

async function getToken(): Promise<string> {
  if (IS_AUTH_DISABLED) return '';
  const account = msalInstance.getAllAccounts()[0];
  if (!account) return '';
  const result = await msalInstance.acquireTokenSilent({ account, scopes: loginRequest.scopes });
  return result.accessToken;
}

/**
 * Subscribes to {@link AgentTelemetryHub.EventEvalRunCompleted} broadcasts and
 * invalidates the run-history query when one arrives. Polling stays in place
 * via {@link useEvalRuns}; this hook makes the refresh faster when the SignalR
 * transport is connected.
 *
 * Payload contract: 13 camelCase properties pinned by SignalREvalRunNotifierTests
 * on the server. We don't deserialise the payload here — the invalidate triggers
 * a fresh fetchEvalRuns() call which uses the canonical types.
 */
export function useEvalRunLive(): void {
  const connectionRef = useRef<HubConnection | null>(null);
  const queryClient = useQueryClient();

  useEffect(() => {
    const connection = buildHubConnection('/hubs/agent', getToken);
    connectionRef.current = connection;

    connection.on(HUB_EVENT_EVAL_RUN_COMPLETED, () => {
      // Coarse invalidation: the new run shifts list ordering and pass-rate
      // sparklines, so a refetch is the cheapest correct option vs surgical
      // cache patching of unknown derived shapes.
      queryClient.invalidateQueries({ queryKey: ['evals', 'runs'] });
    });

    connection
      .start()
      .then(() => connection.invoke(JOIN_METHOD))
      .catch(() => {
        // Transport failure leaves polling as the only signal — by design.
      });

    return () => {
      connection
        .invoke(LEAVE_METHOD)
        .catch(() => {
          // Silent on teardown — connection.stop() below will sever anyway.
        })
        .finally(() => {
          connection.stop();
        });
    };
  }, [queryClient]);
}
