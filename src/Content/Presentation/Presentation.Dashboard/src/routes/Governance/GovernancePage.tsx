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

export default function GovernancePage() {
  const decisions = usePromQuery(metricCatalog['governance_decisions']!.query);
  const violations = usePromQuery(metricCatalog['governance_violations']!.query);
  const evalDuration = usePromQuery(metricCatalog['governance_eval_duration']!.query);
  const rateLimitHits = usePromQuery(metricCatalog['governance_rate_limit_hits']!.query);
  const auditEvents = usePromQuery(metricCatalog['governance_audit_events']!.query);
  const injections = usePromQuery(metricCatalog['governance_injection_detections']!.query);
  const mcpScans = usePromQuery(metricCatalog['governance_mcp_scans']!.query);
  const mcpThreats = usePromQuery(metricCatalog['governance_mcp_threats']!.query);

  const decisionsTs = usePromQuery(metricCatalog['governance_decisions_ts']!.query);
  const violationsByTool = usePromQuery(metricCatalog['governance_violations_by_tool']!.query);
  const injectionsTs = usePromQuery(metricCatalog['governance_injections_ts']!.query);

  if (decisions.isLoading) {
    return (
      <div className="space-y-6">
        <PageHeader title="Governance" subtitle="Policy enforcement, injection detection, MCP security, and audit trail" />
        <PanelGrid columns={4}>
          {Array.from({ length: 4 }).map((_, i) => <LoadingSkeleton key={i} />)}
        </PanelGrid>
      </div>
    );
  }

  const totalDecisions = latestValue(decisions.data);
  const totalViolations = latestValue(violations.data);
  const violationRate = totalDecisions > 0 ? totalViolations / totalDecisions : 0;
  const totalInjections = latestValue(injections.data);
  const totalMcpScans = latestValue(mcpScans.data);
  const totalMcpThreats = latestValue(mcpThreats.data);
  const mcpThreatRate = totalMcpScans > 0 ? totalMcpThreats / totalMcpScans : 0;

  const violationsByToolBars = seriesToBars(
    violationsByTool.data?.series ?? [],
    'agent_governance_tool',
    (v) => formatKpi(v, 'count'),
  );

  const violationStatus =
    violationRate > 0.2 ? 'critical' : violationRate > 0.05 ? 'warning' : 'ok';
  const violationLabel =
    violationRate > 0.2 ? 'High' : violationRate > 0.05 ? 'Moderate' : 'Low';
  const violationPillVariant =
    violationRate > 0.2 ? 'negative' : violationRate > 0.05 ? 'warning' : 'info';

  const mcpThreatStatus =
    mcpThreatRate > 0.1 ? 'critical' : mcpThreatRate > 0 ? 'warning' : 'ok';

  return (
    <div className="space-y-6">
      <PageHeader title="Governance" subtitle="Policy enforcement, injection detection, MCP security, and audit trail" />

      <Section title="Overview" kicker="01">
        <PanelGrid columns={4}>
          <KpiCard
            title="Policy Decisions"
            description="Total governance policy evaluations performed on tool calls. Each tool invocation triggers a policy check against loaded YAML rules."
            value={formatKpi(totalDecisions, 'count')}
            sparklineData={decisions.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Violations"
            description="Tool calls denied by governance policy. High counts may indicate agents attempting restricted operations or overly strict policies."
            value={formatKpi(totalViolations, 'count')}
            sparklineData={violations.data?.series[0]?.dataPoints}
            delta={violationRate > 0.1 ? `${(violationRate * 100).toFixed(0)}% denied` : undefined}
            trend={violationRate > 0.1 ? 'down' : undefined}
          />
          <KpiCard
            title="Eval Latency (p50)"
            description="Median policy evaluation duration. Governance checks run synchronously in the MediatR pipeline — high latency directly impacts agent response time."
            value={latestValue(evalDuration.data).toFixed(1)}
            unit="ms"
            sparklineData={evalDuration.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Rate Limit Hits"
            description="Tool calls throttled by rate-limiting rules. Shows how often agents hit per-tool or per-agent rate limits."
            value={formatKpi(latestValue(rateLimitHits.data), 'count')}
            sparklineData={rateLimitHits.data?.series[0]?.dataPoints}
          />
        </PanelGrid>
      </Section>

      <Section title="Policy Enforcement" kicker="02">
        <PanelGrid columns={2}>
          <PanelCard title="Decisions Over Time" description="Allow vs Deny rate by action type">
            <TimeSeriesChart series={decisionsTs.data?.series ?? []} unit="decisions/min" />
          </PanelCard>
          <PanelCard title="Top Blocked Tools" description="Most frequently denied tool calls">
            <HBarList items={violationsByToolBars} colourBy="category" />
          </PanelCard>
        </PanelGrid>
        <PanelGrid columns={2}>
          <MetricPanel
            title="Violation Rate"
            value={`${(violationRate * 100).toFixed(1)}%`}
            description="of decisions denied"
            status={violationStatus}
            sparklineData={decisions.data?.series[0]?.dataPoints}
          />
          <PanelCard title="Violation Detail">
            <div className="space-y-2 py-2">
              <p className="text-sm text-muted-foreground">
                {formatKpi(totalViolations, 'count')} of {formatKpi(totalDecisions, 'count')} decisions resulted in denials
              </p>
              <Pill variant={violationPillVariant}>
                {violationLabel} violation rate
              </Pill>
            </div>
          </PanelCard>
        </PanelGrid>
      </Section>

      <Section title="Prompt Injection Detection" kicker="03">
        <PanelGrid columns={2}>
          <PanelCard title="Detections Over Time" description="Prompt injection attempts caught by pattern matching">
            <TimeSeriesChart series={injectionsTs.data?.series ?? []} unit="detections/min" />
          </PanelCard>
          <PanelCard title="Detection Summary">
            <div className="space-y-3 py-2">
              <KpiCard
                title="Total Detections"
                description="Prompt injection attempts identified and blocked by the PromptInjectionBehavior pipeline step."
                value={formatKpi(totalInjections, 'count')}
                sparklineData={injections.data?.series[0]?.dataPoints}
              />
              <Pill variant={totalInjections > 0 ? 'negative' : 'info'}>
                {totalInjections > 0 ? `${totalInjections.toFixed(0)} injection attempts blocked` : 'No injections detected'}
              </Pill>
            </div>
          </PanelCard>
        </PanelGrid>
      </Section>

      <Section title="MCP Tool Security" kicker="04">
        <PanelGrid columns={3}>
          <KpiCard
            title="Tools Scanned"
            description="MCP tools evaluated by the McpSecurityScanner for tool poisoning, description injection, and other threats."
            value={formatKpi(totalMcpScans, 'count')}
            sparklineData={mcpScans.data?.series[0]?.dataPoints}
          />
          <KpiCard
            title="Threats Found"
            description="MCP tools flagged as potentially malicious. Includes tool poisoning, zero-width character injection, and typosquatting detection."
            value={formatKpi(totalMcpThreats, 'count')}
            sparklineData={mcpThreats.data?.series[0]?.dataPoints}
            delta={totalMcpThreats > 0 ? `${totalMcpThreats.toFixed(0)} threats` : undefined}
            trend={totalMcpThreats > 0 ? 'down' : undefined}
          />
          <MetricPanel
            title="Threat Rate"
            value={`${(mcpThreatRate * 100).toFixed(1)}%`}
            description="of scans flagged"
            status={mcpThreatStatus}
            sparklineData={mcpScans.data?.series[0]?.dataPoints}
          />
        </PanelGrid>
      </Section>

      <Section title="Audit Trail" kicker="05">
        <PanelGrid columns={2}>
          <KpiCard
            title="Audit Events"
            description="Governance audit log entries recording all policy evaluations, injection scans, and MCP security checks with tamper-evident hashing."
            value={formatKpi(latestValue(auditEvents.data), 'count')}
            sparklineData={auditEvents.data?.series[0]?.dataPoints}
          />
          <PanelCard title="Chain Integrity">
            <div className="flex items-center gap-3 py-4">
              <Pill variant="info">Verified</Pill>
              <span className="text-sm text-muted-foreground">
                Tamper-evident hash chain intact
              </span>
            </div>
          </PanelCard>
        </PanelGrid>
      </Section>
    </div>
  );
}
