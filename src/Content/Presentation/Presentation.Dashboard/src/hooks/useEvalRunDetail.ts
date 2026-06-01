import { useQuery } from '@tanstack/react-query';
import { fetchEvalRunDetail } from '@/api/evals';

/**
 * Loads the full report for a single run. Triggered on drill-in from the
 * history list; not polled — detail content is immutable once ingested.
 */
export function useEvalRunDetail(runId: string | undefined) {
  return useQuery({
    queryKey: ['evals', 'run-detail', runId],
    queryFn: () => fetchEvalRunDetail(runId!),
    enabled: !!runId,
  });
}
