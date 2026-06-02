import { usePromQuery } from '@/hooks/usePromQuery';
import { metricCatalog } from '@/config/metricCatalog';
import { KpiCard } from '@/components/panels/KpiCard';
import { PanelCard } from '@/components/panels/PanelCard';
import { PanelGrid } from '@/components/panels/PanelGrid';
import { LoadingSkeleton } from '@/components/panels/LoadingSkeleton';
import { TimeSeriesChart } from '@/components/charts/TimeSeriesChart';
import { PageHeader } from '@/components/primitives/PageHeader';
import { Section } from '@/components/primitives/Section';
import { HBarList } from '@/components/charts/HBarList';
import { MetricPanel } from '@/components/metrics/MetricPanel';
import { Pill } from '@/components/primitives/Pill';
import { latestValue, formatKpi, seriesToBars } from '@/routes/Pulse/pulse-helpers';

function extractActionCounts(
  categoryData: ReturnType<typeof usePromQuery>['data'],
): { blocked: number; flagged: number; redacted: number } {
  let blocked = 0;
  let flagged = 0;
  let redacted = 0;

  for (const s of categoryData?.series ?? []) {
    const cat = (s.labels['category'] ?? '').toLowerCase();
    const val = parseFloat(s.dataPoints[s.dataPoints.length - 1]?.value ?? '0');

    if (cat.includes('block')) blocked += val;
    else if (cat.includes('flag')) flagged += val;
    else if (cat.includes('redact')) redacted += val;
  }

  return { blocked, flagged, redacted };
}

export default function SafetyPage() {
  const total = usePromQuery(metricCatalog['safety_total']!.query);
  const blocked = usePromQuery(metricCatalog['safety_blocked']!.query);
  const checks = usePromQuery(metricCatalog['safety_checks_total']!.query);
  const violationsTs = usePromQuery(metricCatalog['safety_violations_ts']!.query);
  const byCategory = usePromQuery(metricCatalog['safety_by_category']!.query);
  const blockRate = usePromQuery(metricCatalog['safety_block_rate']!.query);

  if (total.isLoading) {
    return (
      <div className="space-y-6">
        <PageHeader title="Safety" subtitle="Content safety blocks, flags, and redactions" />
        <PanelGrid columns={3}>
          {Array.from({ length: 3 }).map((_, i) => <LoadingSkeleton key={i} />)}
        </PanelGrid>
      </div>
    );
  }

  const blockRateValue = latestValue(blockRate.data);
  const actions = extractActionCounts(byCategory.data);
  const categoryBars = seriesToBars(
    byCategory.data?.series ?? [],
    'category',
    (v) => formatKpi(v, 'count'),
  );

  const blockRateStatus =
    blockRateValue > 0.1 ? 'critical' : blockRateValue > 0 ? 'warning' : 'ok';

  return (
    <div className="space-y-6">
      <PageHeader title="Safety" subtitle="Content safety blocks, flags, and redactions" />

      <Section title="Safety Overview" kicker="01">
        <PanelGrid columns={3}>
          <KpiCard
            title="Total Violations"
            description="Count of inputs or outputs that triggered a content safety rule (blocked, flagged, or redacted). Shows 0 when all content has passed safety checks cleanly."
            value={formatKpi(latestValue(total.data), 'count')}
            sparklineData={total.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Blocked Requests"
            description="Number of requests fully rejected by the content safety middleware before reaching the LLM or the user. Shows 0 when no content has been severe enough to block."
            value={formatKpi(latestValue(blocked.data), 'count')}
            sparklineData={blocked.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Safety Checks"
            description="Total number of content safety evaluations performed on inputs and outputs. Shows 0 when no agent activity has occurred or content safety middleware is not configured."
            value={formatKpi(latestValue(checks.data), 'count')}
            sparklineData={checks.data?.series[0]?.dataPoints}
          />
        </PanelGrid>
      </Section>

      <Section title="Trend" kicker="02">
        <PanelCard title="Violation Trend" description="Violations per minute">
          <TimeSeriesChart series={violationsTs.data?.series ?? []} unit="count/min" />
        </PanelCard>
      </Section>

      <Section title="Analysis" kicker="03">
        <PanelGrid columns={2}>
          <PanelCard title="Violations by Category">
            <HBarList items={categoryBars} colourBy="category" />
          </PanelCard>
          <MetricPanel
            title="Block Rate"
            value={`${(blockRateValue * 100).toFixed(1)}%`}
            description="of requests blocked"
            status={blockRateStatus}
            sparklineData={blockRate.data?.series[0]?.dataPoints}
          />
        </PanelGrid>
      </Section>

      <Section title="Action Summary" kicker="04">
        <div className="flex gap-3">
          <div className="bg-card border border-border rounded-lg p-3 flex-1 text-center">
            <div className="text-lg font-bold tabular-nums text-foreground mb-1 font-mono">
              {actions.blocked.toFixed(0)}
            </div>
            <Pill variant="negative">BLOCKED</Pill>
          </div>
          <div className="bg-card border border-border rounded-lg p-3 flex-1 text-center">
            <div className="text-lg font-bold tabular-nums text-foreground mb-1 font-mono">
              {actions.flagged.toFixed(0)}
            </div>
            <Pill variant="warning">FLAGGED</Pill>
          </div>
          <div className="bg-card border border-border rounded-lg p-3 flex-1 text-center">
            <div className="text-lg font-bold tabular-nums text-foreground mb-1 font-mono">
              {actions.redacted.toFixed(0)}
            </div>
            <Pill variant="info">REDACTED</Pill>
          </div>
        </div>
      </Section>
    </div>
  );
}
