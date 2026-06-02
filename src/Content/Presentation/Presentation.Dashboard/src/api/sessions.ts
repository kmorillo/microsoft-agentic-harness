import { apiClient } from './client';
import { useSessionSnapshotsStore } from '@/stores/sessionSnapshotsStore';
import type {
  SessionRecord,
  SessionDetail,
  ToolInvocationDetail,
  MessageBody,
} from './types';

export async function fetchSessions(
  limit = 50,
  offset = 0,
  status?: string,
  since?: string,
  until?: string,
): Promise<SessionRecord[]> {
  const params: Record<string, string | number> = { limit, offset };
  if (status) params['status'] = status;
  if (since) params['since'] = since;
  if (until) params['until'] = until;
  const { data } = await apiClient.get<SessionRecord[]>('/api/sessions', { params });
  return data;
}

export async function fetchSessionDetail(id: string): Promise<SessionDetail> {
  const { data } = await apiClient.get<SessionDetail>(`/api/sessions/${id}`);

  // PR 3: hydrate the per-session snapshot buffer from the persisted timeline
  // so a page refresh during a live conversation replays the Foresight
  // context-window timeline. Subsequent SignalR `ContextSnapshot` events
  // append on top via the same store. Hydrate keyed by conversationId — the
  // SignalR event payloads use the same key.
  if (data.session?.conversationId && data.snapshots) {
    useSessionSnapshotsStore
      .getState()
      .hydrateSession(data.session.conversationId, data.snapshots);
  }

  return data;
}

/**
 * Foresight per-invocation deep-link
 * (`GET /api/sessions/:id/tools/:invocationId`). Returns the full args + stdout
 * for one tool execution. Backend scopes the lookup to the parent session id,
 * so passing a tool row id from a different session returns 404.
 */
export async function fetchToolInvocation(
  sessionId: string,
  invocationId: string,
): Promise<ToolInvocationDetail> {
  const { data } = await apiClient.get<ToolInvocationDetail>(
    `/api/sessions/${sessionId}/tools/${invocationId}`,
  );
  return data;
}

/**
 * File-body deep-link (`GET /api/sessions/:id/messages/:messageId`). Returns
 * the full message body captured before the 500-char preview truncation, or
 * `contentFull: null` when the row predates the content_full column.
 */
export async function fetchMessageBody(
  sessionId: string,
  messageId: string,
): Promise<MessageBody> {
  const { data } = await apiClient.get<MessageBody>(
    `/api/sessions/${sessionId}/messages/${messageId}`,
  );
  return data;
}
