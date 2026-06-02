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
import {
  latestValue,
  formatKpi,
  seriesToBars,
} from '@/routes/Pulse/pulse-helpers';
import { budgetStatusFromUtilization } from '@/lib/metricStatus';

export default function CostPage() {
  const costTotal = usePromQuery(metricCatalog['cost_total']!.query);
  const costRate = usePromQuery(metricCatalog['cost_rate']!.query);
  const byModel = usePromQuery(metricCatalog['cost_by_model']!.query);
  const cacheSavings = usePromQuery(metricCatalog['cost_cache_savings']!.query);
  const budgetRemaining = usePromQuery(metricCatalog['cost_budget_remaining']!.query);

  if (costTotal.isLoading) {
    return (
      <div className="space-y-6">
        <PageHeader title="Cost" subtitle="USD spend breakdown by model and environment" />
        <PanelGrid columns={3}>
          {Array.from({ length: 3 }).map((_, i) => <LoadingSkeleton key={i} />)}
        </PanelGrid>
      </div>
    );
  }

  const totalCost = latestValue(costTotal.data);
  const savings = latestValue(cacheSavings.data);
  const remaining = latestValue(budgetRemaining.data);
  const budgetTotal = Math.max(totalCost + remaining, 1);
  const budgetPct = totalCost / budgetTotal;

  const modelBars = seriesToBars(
    byModel.data?.series ?? [],
    'model',
    (v) => formatKpi(v, 'usd'),
  );

  const budgetStatus = budgetStatusFromUtilization(budgetPct);

  return (
    <div className="space-y-6">
      <PageHeader title="Cost" subtitle="USD spend breakdown by model and environment" />

      <Section title="Cost Overview" kicker="01">
        <PanelGrid columns={3}>
          <KpiCard
            title="Total Cost"
            description="Cumulative USD spent on LLM API calls across all models and sessions. Shows $0 when no completions have been recorded in the selected time range."
            value={formatKpi(totalCost, 'usd')}
            unit="USD"
            sparklineData={costTotal.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Cache Savings"
            description="Estimated USD saved by serving tokens from the prompt cache instead of reprocessing them. Shows $0 when caching is disabled or there have been no cache hits."
            value={formatKpi(savings, 'usd')}
            unit="USD"
            sparklineData={cacheSavings.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Budget Remaining"
            description="USD left before hitting the configured daily budget cap. Shows $0 when no budget limit is configured or the budget has been fully consumed."
            value={`$${remaining.toFixed(2)}`}
            unit="USD"
            sparklineData={budgetRemaining.data?.series[0]?.dataPoints}
          />
        </PanelGrid>
      </Section>

      <Section title="Spend Rate" kicker="02">
        <PanelCard title="Cost Rate" description="USD burn rate per hour">
          <TimeSeriesChart series={costRate.data?.series ?? []} unit="usd" />
        </PanelCard>
      </Section>

      <Section title="Distribution" kicker="03">
        <PanelGrid columns={2}>
          <PanelCard title="Cost by Model">
            <HBarList items={modelBars} colourBy="category" />
          </PanelCard>
          <MetricPanel
            title="Budget Progress"
            value={`$${totalCost.toFixed(2)}`}
            description={`${formatKpi(budgetPct, 'percent0')} of $${budgetTotal.toFixed(2)} cap`}
            status={budgetStatus}
            sparklineData={costTotal.data?.series[0]?.dataPoints}
          />
        </PanelGrid>
      </Section>

      <Section title="Efficiency" kicker="04">
        <PanelCard title="Cache ROI">
          <div className="flex flex-col items-center justify-center h-[160px]">
            <div className="text-3xl font-bold text-card-foreground font-mono tabular-nums">
              {totalCost > 0 ? `${((savings / totalCost) * 100).toFixed(1)}%` : '—'}
            </div>
            <div className="text-xs text-muted-foreground mt-1">of total cost saved via caching</div>
            <div className="text-sm text-muted-foreground mt-3 font-mono tabular-nums">
              {formatKpi(savings, 'usd')} saved / {formatKpi(totalCost, 'usd')} total
            </div>
          </div>
        </PanelCard>
      </Section>
    </div>
  );
}
