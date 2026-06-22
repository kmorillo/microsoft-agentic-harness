import { useMemo } from 'react';
import { cn } from '@/lib/utils';
import {
  CATEGORY_ORDER,
  CATEGORY_LABEL,
  type CategoryKey,
} from '@/lib/categories';
import { CategorySwatch } from '@/components/context/CategorySwatch';
import { totalTokensInCategory } from '@/lib/loadedItems';
import type { ContextSnapshotEvent, LoadedItem } from '@/api/types';
import { CURRENT_TURN } from './useSessionGemState';

/** One row in the contents panel — the LoadedItem plus the turn it landed in. */
interface ContentsRow {
  item: LoadedItem;
  turnIndex: number;
}

interface ContentsPanelProps {
  /** Full timeline; consumer should pass the merged store-backed array. */
  snapshots: ContextSnapshotEvent[];
  /**
   * Snapshot we're currently focused on. {@link CURRENT_TURN} (-1) means
   * "live tail — show items from every snapshot".
   */
  activeTurnIndex: number;
  /** When set, only this category lane renders. Null = show all lanes. */
  activeCategory: CategoryKey | null;
  /** Click handler — surface item to the drawer state machine. */
  onItemClick: (item: LoadedItem, turnIndex: number) => void;
  className?: string;
}

type ContentsLanes = Record<CategoryKey, ContentsRow[]>;

function emptyLanes(): ContentsLanes {
  return CATEGORY_ORDER.reduce((acc, key) => {
    acc[key] = [];
    return acc;
  }, {} as ContentsLanes);
}

/**
 * Hero contents panel: the "what's currently in context?" list that lives
 * under the ContextBar + ScrubStrip. Aggregates `LoadedItem[]` across every
 * snapshot up to (and including) the active turn, groups by category, and
 * renders one lane per category — or only the filtered lane when
 * `activeCategory` is set.
 *
 * Implements foresight-dashboard-spec.md §4 ("CONTENTS panel — populated for active category").
 */
export function ContentsPanel({
  snapshots,
  activeTurnIndex,
  activeCategory,
  onItemClick,
  className,
}: ContentsPanelProps) {
  // Aggregate snapshots into per-category lanes, REMEMBERING which turn each
  // item came from so the click handler can thread that turn through to
  // openDrawer without a reference-equality scan. Walking snapshots once
  // here avoids the double-scan we used to do (also in useSessionGemState).
  const lanes = useMemo<ContentsLanes>(() => {
    const cap =
      activeTurnIndex === CURRENT_TURN
        ? Number.POSITIVE_INFINITY
        : activeTurnIndex;
    const result = emptyLanes();
    for (const snap of snapshots) {
      if (snap.turnIndex > cap) break;
      for (const item of snap.loaded) {
        result[item.cat].push({ item, turnIndex: snap.turnIndex });
      }
    }
    return result;
  }, [snapshots, activeTurnIndex]);

  const visibleCategories = activeCategory
    ? ([activeCategory] as const)
    : CATEGORY_ORDER;

  const totalAcrossLanes = useMemo(
    () =>
      CATEGORY_ORDER.reduce(
        (sum, cat) =>
          sum + totalTokensInCategory(lanes[cat].map((r) => r.item)),
        0,
      ),
    [lanes],
  );

  return (
    <div
      data-testid="contents-panel"
      data-active-category={activeCategory ?? undefined}
      className={cn('space-y-4', className)}
    >
      {visibleCategories.map((cat) => {
        const rows = lanes[cat];
        const laneTotal = totalTokensInCategory(rows.map((r) => r.item));
        return (
          <ContentsLane
            key={cat}
            category={cat}
            rows={rows}
            laneTotal={laneTotal}
            grandTotal={totalAcrossLanes}
            onItemClick={onItemClick}
          />
        );
      })}
      {visibleCategories.every((cat) => lanes[cat].length === 0) && (
        <p
          data-testid="contents-panel-empty"
          className="text-sm text-muted-foreground italic"
        >
          Nothing has landed in context yet — the panel populates as turns arrive.
        </p>
      )}
    </div>
  );
}

interface ContentsLaneProps {
  category: CategoryKey;
  rows: ContentsRow[];
  laneTotal: number;
  grandTotal: number;
  onItemClick: (item: LoadedItem, turnIndex: number) => void;
}

function ContentsLane({
  category,
  rows,
  laneTotal,
  grandTotal,
  onItemClick,
}: ContentsLaneProps) {
  // Sort biggest-first so the dominant contributors land at the top of each
  // lane — easier to scan for "what's eating my context window?".
  const sorted = useMemo(
    () => [...rows].sort((a, b) => b.item.tokens - a.item.tokens),
    [rows],
  );

  return (
    <section
      data-testid={`contents-lane-${category}`}
      data-category={category}
      className="rounded-md border border-border bg-card"
    >
      <header className="flex items-center justify-between gap-2 border-b border-border px-3 py-2">
        <div className="flex items-center gap-2">
          <CategorySwatch category={category} size="xs" />
          <h4 className="text-[11px] font-medium uppercase tracking-wider text-muted-foreground">
            {CATEGORY_LABEL[category]}
          </h4>
        </div>
        <div className="font-mono text-xs tabular-nums text-muted-foreground">
          {laneTotal.toLocaleString()}
          {grandTotal > 0 && (
            <span className="ml-2 text-[11px] opacity-70">
              · {((laneTotal / grandTotal) * 100).toFixed(1)}%
            </span>
          )}
        </div>
      </header>
      {sorted.length === 0 ? (
        <p
          data-testid={`contents-lane-${category}-empty`}
          className="px-3 py-2 text-xs italic text-muted-foreground"
        >
          No {CATEGORY_LABEL[category].toLowerCase()} loaded yet.
        </p>
      ) : (
        <ul className="divide-y divide-border">
          {sorted.map((row, idx) => {
            const { item, turnIndex } = row;
            const pct = laneTotal > 0 ? (item.tokens / laneTotal) * 100 : 0;
            return (
              <li key={`${category}-${idx}-${item.what}`}>
                <button
                  type="button"
                  data-testid={`contents-row-${category}-${idx}`}
                  onClick={() => onItemClick(item, turnIndex)}
                  className="flex w-full items-center gap-3 px-3 py-2 text-left hover:bg-accent/40 focus-visible:bg-accent/60 focus-visible:outline-none"
                >
                  <span className="min-w-0 flex-1 truncate text-sm text-foreground">
                    {item.what}
                    {item.ref && (
                      <span className="ml-2 font-mono text-[11px] text-muted-foreground">
                        · {item.ref}
                      </span>
                    )}
                  </span>
                  <span className="font-mono text-xs tabular-nums text-muted-foreground">
                    {item.tokens.toLocaleString()}
                  </span>
                  <span className="w-12 text-right font-mono text-[11px] tabular-nums text-muted-foreground/80">
                    {pct.toFixed(1)}%
                  </span>
                  <span className="text-muted-foreground/60" aria-hidden="true">
                    →
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
