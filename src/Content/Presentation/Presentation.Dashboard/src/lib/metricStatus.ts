import type { MetricStatus } from '@/components/metrics/MetricPanel';

/**
 * Maps a 0-1 utilization ratio (e.g. budget spend / cap, cache fill / size)
 * to the standard Foresight three-tier MetricPanel status. Inclusive
 * thresholds match the canonical Pulse/SpendTab pattern from PR 7.
 *
 * Reused by CostPage (Budget Progress) and BudgetPage (Budget Utilization)
 * so a single edit retunes every tier badge in the Spend hub.
 */
export function budgetStatusFromUtilization(util: number): MetricStatus {
  if (util >= 0.9) return 'critical';
  if (util >= 0.75) return 'warning';
  return 'ok';
}
