import { cn } from '@/lib/utils';
import {
  CATEGORY_ORDER,
  CATEGORY_BG_CLASS,
  CATEGORY_LABEL,
  DEFAULT_CONTEXT_BUDGET,
  breakdownTotal,
  type CategoryKey,
  type CategoryBreakdown,
} from '@/lib/categories';

export type ContextBarSize = 'sm' | 'md' | 'lg';

interface ContextBarProps {
  /** Tokens consumed per category at the moment this bar represents. */
  breakdown: CategoryBreakdown;
  /** Total context window budget for the model (e.g. 200_000). */
  budget?: number;
  /** Visual height. sm = table mini-bar, md = timeline node, lg = hero rail. */
  size?: ContextBarSize;
  /** When set, all other segments dim and only this one stays at full opacity. */
  activeCategory?: CategoryKey | null;
  /** Click handler. Receives the category clicked. */
  onSegmentClick?: (cat: CategoryKey) => void;
  /** Optional extra classes appended to the outer rail. */
  className?: string;
  /** Accessible label for the whole rail. Defaults to a token-count summary. */
  ariaLabel?: string;
}

const HEIGHT_BY_SIZE: Record<ContextBarSize, string> = {
  sm: 'h-1.5',
  md: 'h-3',
  lg: 'h-6',
};

/**
 * Foresight's load-bearing primitive: a six-segment proportional rail showing
 * how the model's context window is composed. The same component renders the
 * tiny mini-bar in a table row and the full-width hero rail on the session page.
 *
 * Order, color, and shape are identical at every scale by design — see
 * foresight-dashboard-spec.md §3.1.
 */
export function ContextBar({
  breakdown,
  budget = DEFAULT_CONTEXT_BUDGET,
  size = 'md',
  activeCategory = null,
  onSegmentClick,
  className,
  ariaLabel,
}: ContextBarProps) {
  const used = breakdownTotal(breakdown);
  const clickable = Boolean(onSegmentClick);

  // Guard rails: budget must be a positive finite number to compute proportions.
  // When unknown (0, negative, or non-finite), render an inert muted rail so
  // callers can still mount the component while model metadata is loading.
  const validBudget = Number.isFinite(budget) && budget > 0;
  // Over-budget rendering: when used > budget we scale segments against `used`
  // (so they fill the rail) and tag the bar `data-over-budget="true"` so the
  // rail gains a red ring — the operator sees that the context window blew its
  // cap rather than the bar silently clipping its tail.
  const overBudget = validBudget && used > budget;
  const denom = !validBudget ? 0 : overBudget ? used : budget;
  const headroom = validBudget && !overBudget ? Math.max(0, budget - used) : 0;

  const label =
    ariaLabel ??
    (!validBudget
      ? `Context window: ${used.toLocaleString()} tokens used (budget unknown)`
      : overBudget
        ? `Context window: ${used.toLocaleString()} of ${budget.toLocaleString()} tokens used — OVER BUDGET`
        : `Context window: ${used.toLocaleString()} of ${budget.toLocaleString()} tokens used`);

  return (
    <div
      role="img"
      aria-label={label}
      data-testid="context-bar"
      data-size={size}
      data-over-budget={overBudget || undefined}
      data-budget-unknown={!validBudget || undefined}
      className={cn(
        'flex w-full overflow-hidden rounded',
        HEIGHT_BY_SIZE[size],
        'bg-muted',
        overBudget && 'ring-2 ring-destructive ring-offset-1 ring-offset-background',
        className,
      )}
    >
      {validBudget &&
        CATEGORY_ORDER.map((cat) => {
          const tokens = breakdown[cat];
          if (!Number.isFinite(tokens) || tokens <= 0) return null;
          const pct = (tokens / denom) * 100;
          const dimmed = activeCategory !== null && activeCategory !== cat;
          const segLabel = `${CATEGORY_LABEL[cat]}: ${tokens.toLocaleString()} tokens`;
          const segClass = cn(
            'h-full transition-opacity',
            CATEGORY_BG_CLASS[cat],
            dimmed ? 'opacity-30' : 'opacity-100',
            clickable && 'cursor-pointer',
          );
          const style = { flexBasis: `${pct}%` };

          return clickable ? (
            <button
              key={cat}
              type="button"
              data-testid={`context-bar-segment-${cat}`}
              data-category={cat}
              aria-label={segLabel}
              title={segLabel}
              className={segClass}
              style={style}
              onClick={() => onSegmentClick?.(cat)}
            />
          ) : (
            <div
              key={cat}
              data-testid={`context-bar-segment-${cat}`}
              data-category={cat}
              aria-label={segLabel}
              title={segLabel}
              className={segClass}
              style={style}
            />
          );
        })}
      {headroom > 0 && (
        <div
          data-testid="context-bar-headroom"
          aria-label={`Headroom: ${headroom.toLocaleString()} tokens remaining`}
          title={`Headroom: ${headroom.toLocaleString()} tokens remaining`}
          className="h-full"
          style={{
            flexBasis: `${(headroom / denom) * 100}%`,
            background: 'var(--cat-hatch)',
          }}
        />
      )}
    </div>
  );
}
