import { cn } from '@/lib/utils';
import {
  CATEGORY_ORDER,
  CATEGORY_LABEL,
  CATEGORY_DESCRIPTION,
  breakdownTotal,
  type CategoryKey,
  type CategoryBreakdown,
} from '@/lib/categories';
import { CategorySwatch } from './CategorySwatch';

interface ContextLegendProps {
  breakdown: CategoryBreakdown;
  /** Currently filtered category (highlighted). `null` = no filter. */
  activeCategory?: CategoryKey | null;
  /** Click handler. Passing `null` clears the active filter. */
  onSelect?: (cat: CategoryKey | null) => void;
  /** Show "% of total" line under each token count. */
  showPercent?: boolean;
  className?: string;
}

/**
 * Six clickable category tiles. The interactive companion to `ContextBar` —
 * clicking a tile drives the same filter state the bar reflects.
 *
 * Designed to be horizontally scrollable / wrappable so it stays readable in
 * narrow side panels.
 */
export function ContextLegend({
  breakdown,
  activeCategory = null,
  onSelect,
  showPercent = true,
  className,
}: ContextLegendProps) {
  const total = breakdownTotal(breakdown);
  const interactive = Boolean(onSelect);

  return (
    <div
      data-testid="context-legend"
      className={cn('flex flex-wrap gap-2', className)}
    >
      {CATEGORY_ORDER.map((cat) => {
        const tokens = breakdown[cat];
        const pct = total > 0 ? (tokens / total) * 100 : 0;
        const active = activeCategory === cat;
        const handleClick = () => {
          if (!onSelect) return;
          onSelect(active ? null : cat);
        };

        const inner = (
          <>
            <CategorySwatch category={cat} size="xs" />
            <span className="flex flex-col items-start leading-tight">
              <span className="text-[11px] font-medium uppercase tracking-wider text-muted-foreground">
                {CATEGORY_LABEL[cat]}
              </span>
              <span className="font-mono tabular-nums text-xs text-foreground">
                {tokens.toLocaleString()}
                {showPercent && total > 0 && (
                  <span className="text-muted-foreground ml-1">
                    · {pct.toFixed(1)}%
                  </span>
                )}
              </span>
            </span>
          </>
        );

        const tileClass = cn(
          'flex items-center gap-2 rounded-md border px-2.5 py-1.5 text-left transition-colors',
          active
            ? 'border-cat-accent bg-accent'
            : 'border-border bg-card hover:bg-accent/40',
          interactive && 'cursor-pointer',
        );

        return interactive ? (
          <button
            key={cat}
            type="button"
            data-testid={`context-legend-tile-${cat}`}
            data-category={cat}
            data-active={active}
            aria-label={`${CATEGORY_LABEL[cat]} — ${CATEGORY_DESCRIPTION[cat]}`}
            aria-pressed={active}
            title={CATEGORY_DESCRIPTION[cat]}
            className={tileClass}
            onClick={handleClick}
          >
            {inner}
          </button>
        ) : (
          <div
            key={cat}
            data-testid={`context-legend-tile-${cat}`}
            data-category={cat}
            data-active={active}
            title={CATEGORY_DESCRIPTION[cat]}
            className={tileClass}
          >
            {inner}
          </div>
        );
      })}
    </div>
  );
}
