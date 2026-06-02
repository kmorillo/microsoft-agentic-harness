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
import { Pill } from '@/components/primitives/Pill';
import { latestValue, formatKpi, seriesToBars } from '@/routes/Pulse/pulse-helpers';

function formatMs(v: number): string {
  return v >= 1000 ? `${(v / 1000).toFixed(2)}s` : `${v.toFixed(0)}ms`;
}

function errorRateVariant(rate: number): 'positive' | 'warning' | 'negative' {
  if (rate < 0.05) return 'positive';
  if (rate < 0.1) return 'warning';
  return 'negative';
}

interface ToolRow {
  name: string;
  calls: number;
  errors: number;
  errorRate: number;
  avgLatency: number;
}

function buildReliabilityRows(
  callsData: ReturnType<typeof usePromQuery>['data'],
  errorsData: ReturnType<typeof usePromQuery>['data'],
  latencyData: ReturnType<typeof usePromQuery>['data'],
): ToolRow[] {
  const callsMap = new Map<string, number>();
  const errorsMap = new Map<string, number>();
  const latencyMap = new Map<string, number>();

  for (const s of callsData?.series ?? []) {
    const name = s.labels['tool_name'] ?? 'unknown';
    const dp = s.dataPoints;
    callsMap.set(name, parseFloat(dp[dp.length - 1]?.value ?? '0'));
  }

  for (const s of errorsData?.series ?? []) {
    const name = s.labels['tool_name'] ?? 'unknown';
    const dp = s.dataPoints;
    errorsMap.set(name, parseFloat(dp[dp.length - 1]?.value ?? '0'));
  }

  for (const s of latencyData?.series ?? []) {
    const name = s.labels['tool_name'] ?? 'unknown';
    const dp = s.dataPoints;
    latencyMap.set(name, parseFloat(dp[dp.length - 1]?.value ?? '0'));
  }

  const allTools = new Set([...callsMap.keys(), ...errorsMap.keys(), ...latencyMap.keys()]);
  const rows: ToolRow[] = [];

  for (const name of allTools) {
    const calls = callsMap.get(name) ?? 0;
    const errors = errorsMap.get(name) ?? 0;
    const avgLatency = (latencyMap.get(name) ?? 0) * 1000;
    rows.push({
      name,
      calls,
      errors,
      errorRate: calls > 0 ? errors / calls : 0,
      avgLatency,
    });
  }

  return rows.sort((a, b) => b.calls - a.calls);
}

export default function ToolsPage() {
  const callsTotal = usePromQuery(metricCatalog['tools_calls_total']!.query);
  const errorsTotal = usePromQuery(metricCatalog['tools_errors_total']!.query);
  const avgLatency = usePromQuery(metricCatalog['tools_avg_latency']!.query);
  const resultSize = usePromQuery(metricCatalog['tools_result_size']!.query);
  const callsByTool = usePromQuery(metricCatalog['tools_calls_by_tool']!.query);
  const latencyByTool = usePromQuery(metricCatalog['tools_latency_by_tool']!.query);
  const errorRate = usePromQuery(metricCatalog['tools_error_rate']!.query);
  const errorsByTool = usePromQuery(
    'sum by (tool_name) (agentic_harness_agent_tool_errors_total) or vector(0)',
  );

  if (callsTotal.isLoading) {
    return (
      <div className="space-y-6">
        <PageHeader title="Tools" subtitle="Tool reliability, latency, and error rates" />
        <PanelGrid columns={4}>
          {Array.from({ length: 4 }).map((_, i) => <LoadingSkeleton key={i} />)}
        </PanelGrid>
      </div>
    );
  }

  const latencyMs = latestValue(avgLatency.data) * 1000;
  const callsVal = latestValue(callsTotal.data);
  const errorsVal = latestValue(errorsTotal.data);
  const resultSizeVal = latestValue(resultSize.data);
  const reliabilityRows = buildReliabilityRows(
    callsByTool.data,
    errorsByTool.data,
    latencyByTool.data,
  );

  const callsBars = seriesToBars(
    callsByTool.data?.series ?? [],
    'tool_name',
    (v) => formatKpi(v, 'count'),
  );

  const latencyBars = seriesToBars(
    latencyByTool.data?.series ?? [],
    'tool_name',
    formatMs,
    (v) => v * 1000,
  );

  return (
    <div className="space-y-6">
      <PageHeader title="Tools" subtitle="Tool reliability, latency, and error rates" />

      <Section title="Overview" kicker="01">
        <PanelGrid columns={4}>
          <KpiCard
            title="Total Calls"
            description="Number of tool invocations made by agents across all sessions. Shows 0 when no agent turns have required tool use in the selected time range."
            value={formatKpi(callsVal, 'count')}
            sparklineData={callsTotal.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Errors"
            description="Number of tool invocations that returned an error or threw an exception. Shows 0 when all tool calls have completed successfully."
            value={formatKpi(errorsVal, 'count')}
            sparklineData={errorsTotal.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Avg Latency"
            description="Mean execution time across all tool calls, measured from invocation to result. Shows 0ms when no tool calls have been recorded."
            value={formatMs(latencyMs)}
            sparklineData={avgLatency.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Avg Result Size"
            description="Mean character count of tool return values sent back to the LLM. Shows 0 when no tool results have been recorded."
            value={formatKpi(resultSizeVal, 'count')}
            unit="chars"
            sparklineData={resultSize.data?.series[0]?.dataPoints}
          />
        </PanelGrid>
      </Section>

      <Section title="Tool Breakdown" kicker="02">
        <PanelGrid columns={2}>
          <PanelCard title="Calls by Tool" description="Invocation count per tool">
            <HBarList items={callsBars} colourBy="category" />
          </PanelCard>
          <PanelCard title="Latency by Tool" description="Average execution time">
            <HBarList items={latencyBars} colourBy="category" />
          </PanelCard>
        </PanelGrid>
      </Section>

      <Section title="Reliability Board" kicker="03">
        <div className="bg-card border border-border rounded-lg overflow-hidden">
          <div className="flex px-4 py-2 text-[10px] uppercase tracking-wider text-muted-foreground border-b border-border">
            <div className="flex-[2]">Tool</div>
            <div className="flex-1 text-right">Calls</div>
            <div className="flex-1 text-right">Errors</div>
            <div className="flex-1 text-right">Error Rate</div>
            <div className="flex-1 text-right">Avg Latency</div>
          </div>
          {reliabilityRows.length === 0 && (
            <div className="px-4 py-6 text-center text-[11px] text-muted-foreground">
              No tool data available
            </div>
          )}
          {reliabilityRows.map((row) => (
            <div
              key={row.name}
              className="flex items-center px-4 py-2 text-[11px] border-b border-border last:border-b-0 hover:bg-muted/30 transition-colors"
            >
              <div className="flex-[2] font-mono text-foreground truncate">{row.name}</div>
              <div className="flex-1 text-right font-mono tabular-nums text-foreground">
                {formatKpi(row.calls, 'count')}
              </div>
              <div className="flex-1 text-right font-mono tabular-nums text-foreground">
                {row.errors.toFixed(0)}
              </div>
              <div className="flex-1 text-right">
                <Pill variant={errorRateVariant(row.errorRate)}>
                  {(row.errorRate * 100).toFixed(1)}%
                </Pill>
              </div>
              <div className="flex-1 text-right font-mono tabular-nums text-muted-foreground">
                {formatMs(row.avgLatency)}
              </div>
            </div>
          ))}
        </div>
      </Section>

      <Section title="Error Trend" kicker="04">
        <PanelCard title="Error Rate Over Time" description="Tool error ratio trend">
          <TimeSeriesChart series={errorRate.data?.series ?? []} unit="percent" />
        </PanelCard>
      </Section>
    </div>
  );
}
