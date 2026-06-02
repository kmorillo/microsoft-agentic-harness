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
import { latestValue, formatKpi, seriesToBars } from '@/routes/Pulse/pulse-helpers';

export default function RagPage() {
  const ingestionTotal = usePromQuery(metricCatalog['rag_ingestion_total']!.query);
  const retrievalTotal = usePromQuery(metricCatalog['rag_retrieval_total']!.query);
  const avgLatency = usePromQuery(metricCatalog['rag_avg_latency']!.query);
  const chunksAvg = usePromQuery(metricCatalog['rag_chunks_avg']!.query);
  const ingestionRate = usePromQuery(metricCatalog['rag_ingestion_rate']!.query);
  const latencyTs = usePromQuery(metricCatalog['rag_retrieval_latency_ts']!.query);
  const bySource = usePromQuery(metricCatalog['rag_by_source']!.query);

  if (ingestionTotal.isLoading) {
    return (
      <div className="space-y-6">
        <PageHeader title="RAG" subtitle="Retrieval performance and source quality" />
        <PanelGrid columns={4}>
          {Array.from({ length: 4 }).map((_, i) => <LoadingSkeleton key={i} />)}
        </PanelGrid>
      </div>
    );
  }

  const latencyMs = latestValue(avgLatency.data) * 1000;
  const ingestionVal = latestValue(ingestionTotal.data);
  const retrievalVal = latestValue(retrievalTotal.data);
  const chunksVal = latestValue(chunksAvg.data);
  const total = ingestionVal + retrievalVal;
  const ingestionPct = total > 0 ? (ingestionVal / total) * 100 : 50;
  const retrievalPct = total > 0 ? (retrievalVal / total) * 100 : 50;
  const ratio = ingestionVal > 0 ? (retrievalVal / ingestionVal) * 100 : 0;

  const sourceBars = seriesToBars(
    bySource.data?.series ?? [],
    'source',
    (v) => formatKpi(v, 'count'),
  );

  const latencyStatus =
    latencyMs > 1000 ? 'critical' : latencyMs > 500 ? 'warning' : 'ok';

  return (
    <div className="space-y-6">
      <PageHeader title="RAG" subtitle="Retrieval performance and source quality" />

      <Section title="Retrieval Metrics" kicker="01">
        <PanelGrid columns={4}>
          <KpiCard
            title="Documents Ingested"
            description="Total documents processed and chunked into the vector store. Shows 0 when the RAG ingestion pipeline has not been triggered or no documents have been indexed."
            value={formatKpi(ingestionVal, 'count')}
            sparklineData={ingestionTotal.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Retrievals"
            description="Number of retrieval queries executed against the vector and sparse stores. Shows 0 when no agent turns have triggered RAG lookups."
            value={formatKpi(retrievalVal, 'count')}
            sparklineData={retrievalTotal.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Avg Latency"
            description="Mean time from retrieval query submission to ranked chunk assembly. Shows 0ms when no retrieval operations have been recorded."
            value={`${latencyMs.toFixed(0)}ms`}
            sparklineData={avgLatency.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Avg Chunks"
            description="Mean number of document chunks returned per retrieval query after reranking and budget enforcement. Shows 0 when no retrievals have occurred."
            value={chunksVal.toFixed(1)}
            sparklineData={chunksAvg.data?.series[0]?.dataPoints}
          />
        </PanelGrid>
      </Section>

      <Section title="Performance" kicker="02">
        <PanelGrid columns={2}>
          <PanelCard title="Ingestion Throughput" description="Documents per minute">
            <TimeSeriesChart series={ingestionRate.data?.series ?? []} unit="docs/min" />
          </PanelCard>
          <PanelCard title="Retrieval Latency" description="Average retrieval time">
            <TimeSeriesChart series={latencyTs.data?.series ?? []} unit="ms" />
          </PanelCard>
        </PanelGrid>
      </Section>

      <Section title="Quality" kicker="03">
        <PanelGrid columns={2}>
          <PanelCard title="Top Sources">
            <HBarList items={sourceBars} colourBy="category" />
          </PanelCard>
          <MetricPanel
            title="Retrieval Latency"
            value={`${latencyMs.toFixed(0)}ms`}
            description="avg retrieval"
            status={latencyStatus}
            sparklineData={latencyTs.data?.series[0]?.dataPoints}
          />
        </PanelGrid>
      </Section>

      <Section title="Pipeline" kicker="04">
        <div className="bg-card border border-border rounded-lg p-4">
          <div className="flex gap-0 h-6 rounded overflow-hidden mb-3">
            <div
              className="h-full bg-[var(--chart-1)] transition-all duration-300"
              style={{ width: `${ingestionPct}%` }}
            />
            <div
              className="h-full bg-[var(--chart-2)] transition-all duration-300"
              style={{ width: `${retrievalPct}%` }}
            />
          </div>
          <div className="flex gap-6 text-xs font-mono tabular-nums">
            <span className="flex items-center gap-1.5">
              <span className="w-2 h-2 rounded-full bg-[var(--chart-1)]" />
              Ingested: {formatKpi(ingestionVal, 'count')}
            </span>
            <span className="flex items-center gap-1.5">
              <span className="w-2 h-2 rounded-full bg-[var(--chart-2)]" />
              Retrieved: {formatKpi(retrievalVal, 'count')}
            </span>
          </div>
          <div className="text-[11px] text-muted-foreground mt-2 font-mono tabular-nums">
            Retrieval-to-ingestion ratio: {ratio.toFixed(1)}%
          </div>
        </div>
      </Section>
    </div>
  );
}
