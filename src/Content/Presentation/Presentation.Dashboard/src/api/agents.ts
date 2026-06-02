import { apiClient } from './client';
import type { AgentSummary } from './types';

/**
 * Fetches the canonical agent roster from the AgentHub `/api/agents` endpoint.
 * Used by the SessionsPage agent rail; the rail enriches each row with
 * session counts derived client-side from the existing sessions query.
 */
export async function fetchAgents(): Promise<AgentSummary[]> {
  const { data } = await apiClient.get<AgentSummary[]>('/api/agents');
  return data;
}
