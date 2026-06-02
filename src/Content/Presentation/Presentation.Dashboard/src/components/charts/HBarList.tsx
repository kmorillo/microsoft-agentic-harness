import { cn } from '@/lib/utils';
import { getChartColor } from './chartTheme';
import { hashToCategory } from '@/lib/agentRoster';
import {
  CATEGORY_BG_CLASS,
  CATEGORY_ORDER,
  type CategoryKey,
} from '@/lib/categories';

export interface HBarItem {
  label: string;
  value: number;
  formatted: string;
  /**
   * Optional explicit category tint — overrides whatever `colourBy` would
   * pick. Used by SpendTab's token-shape list where each row IS a category
   * and the bar colour should match the corresponding ContextBar segment.
   */
  category?: CategoryKey;
}

interface HBarListProps {
  items: HBarItem[];
  /**
   * Bar fill source:
   * - `'fixed'` (default) — original behaviour: per-index palette from
   *   `chartTheme.getChartColor`. Preserves visuals for any consumer that
   *   hasn't been reskinned yet.
   * - `'category'` — Foresight token, derived by hashing each item's label
   *   into a CategoryKey via `hashToCategory`. Agents named CodeAssistant
   *   read the same colour here as on the SessionsPage rail.
   */
  colourBy?: 'fixed' | 'category';
  className?: string;
}

export function HBarList({
  items,
  colourBy = 'fixed',
  className,
}: HBarListProps) {
  if (items.length === 0)
    return <div className="text-muted-foreground text-sm">No data</div>;
  const max = Math.max(...items.map((i) => i.value), 1);

  return (
    <div className={cn('space-y-2', className)} data-colour-by={colourBy}>
      {items.map((item, i) => {
        const cat = resolveCategory(item, colourBy);
        const fillClass = cat ? CATEGORY_BG_CLASS[cat] : undefined;
        const fixedColour = !cat ? getChartColor(i) : undefined;
        return (
          <div key={item.label} className="flex items-center gap-3">
            <span
              className="text-xs text-muted-foreground w-28 shrink-0 truncate"
              title={item.label}
            >
              {item.label}
            </span>
            <div className="flex-1 h-5 bg-muted/30 rounded overflow-hidden">
              <div
                data-testid={`hbar-row-${i}`}
                data-category={cat}
                className={cn('h-full rounded', fillClass)}
                style={{
                  width: `${(item.value / max) * 100}%`,
                  ...(fixedColour ? { backgroundColor: fixedColour } : {}),
                }}
              />
            </div>
            <span className="text-xs font-mono tabular-nums text-card-foreground w-16 text-right shrink-0">
              {item.formatted}
            </span>
          </div>
        );
      })}
    </div>
  );
}

function resolveCategory(
  item: HBarItem,
  colourBy: 'fixed' | 'category',
): CategoryKey | undefined {
  if (item.category && CATEGORY_ORDER.includes(item.category)) {
    return item.category;
  }
  if (colourBy === 'category') return hashToCategory(item.label);
  return undefined;
}
