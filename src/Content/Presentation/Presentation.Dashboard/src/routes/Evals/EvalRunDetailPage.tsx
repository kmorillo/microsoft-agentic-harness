import { useParams, Link } from 'react-router-dom';
import { useEvalRunDetail } from '@/hooks/useEvalRunDetail';
import { PageHeader } from '@/components/primitives/PageHeader';
import { LoadingSkeleton } from '@/components/panels/LoadingSkeleton';
import type { EvalResult, MetricScore } from '@/api/evals';

function verdictColor(verdict: string): string {
  switch (verdict) {
    case 'Pass': return 'text-otel-positive';
    case 'Fail': return 'text-otel-negative';
    case 'Warn': return 'text-otel-accent';
    default: return 'text-otel-muted';
  }
}

function formatScore(s: MetricScore): string {
  return `${s.score.toFixed(3)} (${s.verdict})`;
}

function CaseRow({ result }: { result: EvalResult }) {
  return (
    <tr className="border-t border-otel-border">
      <td className="px-3 py-2 font-mono text-xs">{result.case.id}</td>
      <td className={`px-3 py-2 ${verdictColor(result.verdict)}`}>{result.verdict}</td>
      <td className="px-3 py-2">
        <div className="space-y-0.5 text-xs">
          {Object.entries(result.aggregatedScores).map(([k, v]) => (
            <div key={k}>
              <span className="text-otel-muted">{k}:</span> {formatScore(v)}
            </div>
          ))}
        </div>
      </td>
      <td className="px-3 py-2 text-right">${result.costUsd.toFixed(4)}</td>
      <td className="px-3 py-2 text-xs text-otel-muted">{result.error ?? '—'}</td>
    </tr>
  );
}

export default function EvalRunDetailPage() {
  const { runId } = useParams<{ runId: string }>();
  const { data: report, isLoading, isError, error } = useEvalRunDetail(runId);

  if (isLoading) {
    return (
      <div className="space-y-6">
        <PageHeader title="Run Detail" subtitle={runId ?? ''} />
        <LoadingSkeleton />
      </div>
    );
  }

  if (isError || !report) {
    return (
      <div className="space-y-6">
        <PageHeader title="Run Detail" subtitle={runId ?? ''} />
        <div className="text-otel-negative">
          Failed to load run: {String(error ?? 'not found')}
        </div>
        <Link to="/evals" className="text-otel-accent underline">← Back to runs</Link>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <PageHeader
        title={`Run ${report.runId}`}
        subtitle={`Started ${new Date(report.startedAtUtc).toLocaleString()} · ${report.overallVerdict}`}
      />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <div className="rounded border border-otel-border p-3">
          <div className="text-xs text-otel-muted">Pass / Fail / Warn</div>
          <div className="text-lg">
            <span className="text-otel-positive">{report.passedCount}</span>
            {' / '}
            <span className="text-otel-negative">{report.failedCount}</span>
            {' / '}
            <span className="text-otel-accent">{report.warnedCount}</span>
          </div>
        </div>
        <div className="rounded border border-otel-border p-3">
          <div className="text-xs text-otel-muted">Errored</div>
          <div className="text-lg">{report.erroredCount}</div>
        </div>
        <div className="rounded border border-otel-border p-3">
          <div className="text-xs text-otel-muted">Total Cost</div>
          <div className="text-lg">${report.totalCostUsd.toFixed(4)}</div>
        </div>
        <div className="rounded border border-otel-border p-3">
          <div className="text-xs text-otel-muted">Repeats</div>
          <div className="text-lg">{report.repeats}</div>
        </div>
      </div>

      {report.warnings.length > 0 && (
        <div className="rounded border border-otel-accent/40 bg-otel-accent/10 p-3">
          <div className="mb-2 text-sm font-semibold text-otel-accent">Warnings</div>
          <ul className="list-inside list-disc text-xs">
            {report.warnings.map((w, i) => <li key={i}>{w}</li>)}
          </ul>
        </div>
      )}

      <div className="overflow-x-auto rounded-lg border border-otel-border">
        <table className="w-full text-sm">
          <thead className="bg-otel-surface/50 text-otel-muted">
            <tr>
              <th className="px-3 py-2 text-left">Case ID</th>
              <th className="px-3 py-2 text-left">Verdict</th>
              <th className="px-3 py-2 text-left">Scores</th>
              <th className="px-3 py-2 text-right">Cost</th>
              <th className="px-3 py-2 text-left">Error</th>
            </tr>
          </thead>
          <tbody>
            {report.results.map((r) => <CaseRow key={r.case.id} result={r} />)}
          </tbody>
        </table>
      </div>

      <Link to="/evals" className="text-otel-accent underline">← Back to runs</Link>
    </div>
  );
}
