import { useQuery } from '@tanstack/react-query';
import { fetchEvalRuns } from '@/api/evals';

const POLL_INTERVAL_MS = 10_000;

/**
 * Polls the eval run history. SignalR's EvalRunCompleted event provides faster
 * notifications when the AgentHub transport is connected (see useEvalRunLive),
 * but polling is the always-on baseline so a dropped connection doesn't strand
 * the UI on stale data.
 */
export function useEvalRuns(take = 50) {
  return useQuery({
    queryKey: ['evals', 'runs', take],
    queryFn: () => fetchEvalRuns(take),
    refetchInterval: POLL_INTERVAL_MS,
    staleTime: POLL_INTERVAL_MS / 2,
  });
}
