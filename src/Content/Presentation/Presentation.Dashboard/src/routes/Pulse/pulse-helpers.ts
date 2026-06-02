import { usePromQuery } from '@/hooks/usePromQuery';
import { metricCatalog } from '@/config/metricCatalog';
import type { MetricDataPoint, MetricSeries, MetricsQueryResponse } from '@/api/types';

export function useMetric(catalogId: string) {
  const entry = metricCatalog[catalogId]!;
  return { entry, ...usePromQuery(entry.query) };
}

export function primarySeries(data: MetricsQueryResponse | undefined): MetricSeries | undefined {
  if (!data?.series?.length) return undefined;
  return data.series.find(s => Object.keys(s.labels).length > 0) ?? data.series[0];
}

export function latestValue(data: ReturnType<typeof usePromQuery>['data']): number {
  const dp = primarySeries(data)?.dataPoints;
  if (!dp || dp.length === 0) return 0;
  return parseFloat(dp[dp.length - 1]!.value) || 0;
}

export function computeDelta(
  dataPoints?: MetricDataPoint[],
): { text: string; trend: 'up' | 'down' | 'flat' } | null {
  if (!dataPoints || dataPoints.length < 2) return null;
  const first = parseFloat(dataPoints[0]!.value) || 0;
  const last = parseFloat(dataPoints[dataPoints.length - 1]!.value) || 0;
  if (first === 0) return null;
  const pct = ((last - first) / first) * 100;
  if (Math.abs(pct) < 0.1) return { text: '0%', trend: 'flat' };
  return {
    text: `${pct > 0 ? '+' : ''}${pct.toFixed(1)}%`,
    trend: pct > 0 ? 'up' : 'down',
  };
}

export function formatKpi(value: number, unit: string): string {
  if (unit === 'usd') return `$${value.toFixed(4)}`;
  if (unit === 'percent') return `${(value * 100).toFixed(1)}%`;
  if (unit === 'percent0') return `${(value * 100).toFixed(0)}%`;
  if (unit === 'tokens/min')
    return value >= 1000 ? `${(value / 1000).toFixed(1)}K` : value.toFixed(0);
  if (unit === 'ms') return `${value.toFixed(0)} ms`;
  if (unit === 's') return `${value.toFixed(2)}s`;
  if (unit === 'usd/hr') return `$${value.toFixed(2)}/hr`;
  if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(1)}M`;
  if (value >= 1000) return `${(value / 1000).toFixed(1)}K`;
  return value.toFixed(0);
}

export function fmtRelative(ts: number): string {
  const diff = Math.floor((Date.now() / 1000 - ts) / 60);
  if (diff < 1) return 'just now';
  if (diff < 60) return `${diff}m ago`;
  return `${Math.floor(diff / 60)}h ago`;
}

export function seriesToBars(
  series: MetricSeries[],
  labelKey: string,
  formatter: (v: number) => string,
  /**
   * Optional value transform applied before dedup/format. Use for unit
   * conversion (e.g. seconds → ms by passing `v => v * 1000`). Identity
   * by default.
   */
  transform: (v: number) => number = (v) => v,
) {
  // Dedupe by label so multiple unlabeled series (all falling back to
  // 'unknown') collapse into a single bar. Prevents React duplicate-key
  // warnings when HBarList uses item.label as the row key.
  const totals = new Map<string, number>();
  for (const s of series) {
    const label = s.labels[labelKey] ?? 'unknown';
    const raw =
      parseFloat(s.dataPoints[s.dataPoints.length - 1]?.value ?? '0') || 0;
    totals.set(label, (totals.get(label) ?? 0) + transform(raw));
  }
  return Array.from(totals.entries())
    .map(([label, value]) => ({ label, value, formatted: formatter(value) }))
    .sort((a, b) => b.value - a.value);
}
