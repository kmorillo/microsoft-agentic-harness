import { usePromQuery } from '@/hooks/usePromQuery';
import { metricCatalog } from '@/config/metricCatalog';
import { KpiCard } from '@/components/panels/KpiCard';
import { PanelCard } from '@/components/panels/PanelCard';
import { PanelGrid } from '@/components/panels/PanelGrid';
import { LoadingSkeleton } from '@/components/panels/LoadingSkeleton';
import { TimeSeriesChart } from '@/components/charts/TimeSeriesChart';
import { PageHeader } from '@/components/primitives/PageHeader';
import { Section } from '@/components/primitives/Section';
import { MetricPanel } from '@/components/metrics/MetricPanel';
import { latestValue, formatKpi } from '@/routes/Pulse/pulse-helpers';
import { budgetStatusFromUtilization } from '@/lib/metricStatus';

export default function BudgetPage() {
  const spent = usePromQuery(metricCatalog['budget_spent']!.query);
  const limit = usePromQuery(metricCatalog['budget_limit']!.query);
  const remaining = usePromQuery(metricCatalog['budget_remaining']!.query);
  const utilization = usePromQuery(metricCatalog['budget_utilization']!.query);
  const spendRate = usePromQuery(metricCatalog['budget_spend_rate']!.query);
  const statusVal = usePromQuery(metricCatalog['budget_status']!.query);

  if (spent.isLoading) {
    return (
      <div className="space-y-6">
        <PageHeader title="Budget" subtitle="Daily and monthly cap utilization" />
        <PanelGrid columns={3}>
          {Array.from({ length: 3 }).map((_, i) => <LoadingSkeleton key={i} />)}
        </PanelGrid>
      </div>
    );
  }

  const spentVal = latestValue(spent.data);
  const limitVal = latestValue(limit.data);
  const remainingVal = latestValue(remaining.data);
  const utilizationVal = latestValue(utilization.data);
  const statusCode = latestValue(statusVal.data);

  const utilizationStatus = budgetStatusFromUtilization(utilizationVal);

  const statusLabel = statusCode >= 2 ? 'CRITICAL' : statusCode >= 1 ? 'WARNING' : 'OK';
  const statusToneClass =
    statusCode >= 2
      ? 'text-otel-negative'
      : statusCode >= 1
        ? 'text-otel-warning'
        : 'text-otel-positive';

  return (
    <div className="space-y-6">
      <PageHeader title="Budget" subtitle="Daily and monthly cap utilization" />

      <Section title="Budget Metrics" kicker="01">
        <PanelGrid columns={3}>
          <KpiCard
            title="Total Spent"
            description="Cumulative USD spent against the configured budget cap in the current period. Shows $0 when no LLM API calls have incurred cost."
            value={formatKpi(spentVal, 'usd')}
            unit="USD"
            sparklineData={spent.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Budget Limit"
            description="Maximum USD spend allowed for the current budget period, set in application configuration. Shows $0 when no budget cap has been configured."
            value={`$${limitVal.toFixed(2)}`}
            unit="USD"
          />
          <KpiCard
            title="Remaining"
            description="USD left before the budget cap is reached and requests may be throttled. Shows $0 when the budget is fully consumed or no cap is configured."
            value={`$${remainingVal.toFixed(2)}`}
            unit="USD"
            sparklineData={remaining.data?.series[0]?.dataPoints}
          />
        </PanelGrid>
      </Section>

      <Section title="Utilization" kicker="02">
        <PanelGrid columns={2}>
          <MetricPanel
            title="Budget Utilization"
            value={`${(utilizationVal * 100).toFixed(0)}%`}
            description={
              limitVal > 0
                ? `$${spentVal.toFixed(2)} of $${limitVal.toFixed(2)} daily cap`
                : 'no cap set'
            }
            status={utilizationStatus}
            sparklineData={utilization.data?.series[0]?.dataPoints}
          />
          <PanelCard title="Spend Rate" description="USD burn rate per hour">
            <TimeSeriesChart series={spendRate.data?.series ?? []} unit="usd" />
          </PanelCard>
        </PanelGrid>
      </Section>

      <Section title="Alerts" kicker="03">
        <PanelGrid columns={1}>
          <PanelCard title="Budget Status">
            <div className="flex flex-col items-center justify-center h-[200px]">
              <div
                className={`text-4xl font-bold font-mono tabular-nums ${statusToneClass}`}
              >
                {statusLabel}
              </div>
              <div className="text-sm text-muted-foreground mt-2">current budget health</div>
            </div>
          </PanelCard>
        </PanelGrid>
      </Section>
    </div>
  );
}
