import { cn } from '@/lib/utils';
import { CATEGORY_BG_CLASS, type CategoryKey } from '@/lib/categories';
import { Sparkline } from '@/components/charts/Sparkline';
import type { MetricDataPoint } from '@/api/types';

export type MetricStatus = 'ok' | 'warning' | 'critical' | 'neutral';

export interface MetricDelta {
  /** Signed percentage change (e.g. +3.2 or -1.5). Sign drives the arrow glyph. */
  pct: number;
  /**
   * Which direction reads as "good" for this metric. For cost, lower is
   * better (`'down'`); for cache hit rate, higher is better (`'up'`). Drives
   * the delta-pill colour: a positive delta on a 'down' metric renders red.
   */
  positiveDirection: 'up' | 'down';
}

interface MetricPanelProps {
  title: string;
  /** Pre-formatted value (e.g. "42%", "$4.2K", "1.2k turns/min"). */
  value: string;
  /** Optional supporting text under the value (e.g. "of $10K budget"). */
  description?: string;
  delta?: MetricDelta;
  /** Time-series for the inline sparkline. Omit to drop the chart cleanly. */
  sparklineData?: MetricDataPoint[];
  /**
   * Category accent. Drives the sparkline stroke and (in 'neutral' status)
   * the value tint. Defaults to the cat-accent token.
   */
  category?: CategoryKey;
  /**
   * Coarse status. Drives the optional left-edge accent and the delta-pill
   * fallback colour when no `delta.positiveDirection` is set.
   */
  status?: MetricStatus;
  className?: string;
}

const STATUS_ACCENT_BORDER: Record<MetricStatus, string> = {
  ok: 'border-l-otel-positive/60',
  warning: 'border-l-otel-warning/60',
  critical: 'border-l-otel-negative/70',
  neutral: 'border-l-border',
};

/**
 * Foresight replacement for `GaugeChart`. Renders a large mono number,
 * optional delta pill, optional inline sparkline, and a one-line description.
 *
 * Pattern intentionally collapses the gauge axis — a 42% gauge says the same
 * thing as `42% used · ▁▂▃▅▇ +3.2%` but takes a fraction of the visual
 * budget. foresight-dashboard-spec.md §3.4 restraint rule: no decorative arcs.
 */
export function MetricPanel({
  title,
  value,
  description,
  delta,
  sparklineData,
  category,
  status = 'neutral',
  className,
}: MetricPanelProps) {
  const sparklineCategoryClass = category
    ? CATEGORY_BG_CLASS[category].replace(/^bg-/, 'text-')
    : 'text-cat-accent';

  return (
    <section
      data-testid="metric-panel"
      data-status={status}
      data-category={category}
      className={cn(
        'flex flex-col gap-2 rounded-md border border-border bg-card p-4 border-l-4',
        STATUS_ACCENT_BORDER[status],
        className,
      )}
    >
      <header className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
        {title}
      </header>

      <div className="flex items-baseline gap-3">
        <span
          data-testid="metric-panel-value"
          className="font-mono tabular-nums text-2xl font-semibold text-foreground"
        >
          {value}
        </span>
        {delta && <DeltaPill delta={delta} />}
      </div>

      {sparklineData && sparklineData.length > 0 && (
        <div
          data-testid="metric-panel-sparkline"
          className={cn('mt-1 h-8', sparklineCategoryClass)}
        >
          <Sparkline dataPoints={sparklineData} color="currentColor" />
        </div>
      )}

      {description && (
        <p
          data-testid="metric-panel-description"
          className="text-xs text-muted-foreground"
        >
          {description}
        </p>
      )}
    </section>
  );
}

interface DeltaPillProps {
  delta: MetricDelta;
}

/**
 * Renders the signed delta as a small pill. Colour comes from whether the
 * direction is "good" for this metric: a +3.2% delta on a `positiveDirection:
 * 'down'` metric (e.g. error rate) reads as bad and renders red.
 */
function DeltaPill({ delta }: DeltaPillProps) {
  const isPositive = delta.pct > 0;
  const isNegative = delta.pct < 0;
  const isZero = delta.pct === 0;

  // A positive delta on a "down is good" metric is bad; negate the read.
  const isGood = isPositive
    ? delta.positiveDirection === 'up'
    : isNegative
      ? delta.positiveDirection === 'down'
      : true;

  const tone = isZero
    ? 'bg-muted text-muted-foreground'
    : isGood
      ? 'bg-otel-positive/15 text-otel-positive'
      : 'bg-otel-negative/15 text-otel-negative';

  const arrow = isPositive ? '▲' : isNegative ? '▼' : '·';
  const sign = isPositive ? '+' : '';

  return (
    <span
      data-testid="metric-panel-delta"
      data-tone={isZero ? 'neutral' : isGood ? 'good' : 'bad'}
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-medium font-mono tabular-nums',
        tone,
      )}
    >
      <span aria-hidden="true">{arrow}</span>
      <span>
        {sign}
        {delta.pct.toFixed(1)}%
      </span>
    </span>
  );
}
