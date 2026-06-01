import { useQuery } from '@tanstack/react-query';
import { fetchPromptVersionComparison } from '@/api/evals';

/**
 * Aggregated score per prompt version for a given prompt name. Used by the
 * prompt A/B compare view. Not polled — server-side data only changes when
 * new runs ingest, which the SignalR subscription will invalidate.
 */
export function usePromptVersionComparison(promptName: string | undefined) {
  return useQuery({
    queryKey: ['evals', 'prompt-compare', promptName],
    queryFn: () => fetchPromptVersionComparison(promptName!),
    enabled: !!promptName,
  });
}
