import type { ContextSnapshotEvent, LoadedItem } from '@/api/types';
import { CATEGORY_ORDER, type CategoryKey } from '@/lib/categories';

/**
 * Map of category → list of LoadedItems contributing to that segment.
 * Every category key is present (possibly with an empty array) so consumers
 * can iterate `CATEGORY_ORDER` without nullish checks.
 */
export type LoadedItemsByCategory = Record<CategoryKey, LoadedItem[]>;

function emptyByCategory(): LoadedItemsByCategory {
  return CATEGORY_ORDER.reduce((acc, key) => {
    acc[key] = [];
    return acc;
  }, {} as LoadedItemsByCategory);
}

/**
 * Walks the per-turn snapshot deltas and accumulates every `LoadedItem` into
 * its category lane. When `upToTurnInclusive` is provided, items from later
 * turns are excluded — this powers the ContentsPanel's "show me what was in
 * context at the active scrub turn" mode.
 *
 * Snapshots are assumed to arrive sorted by `turnIndex` ascending (the
 * sessionSnapshotsStore enforces this on every write). The loop breaks early
 * when a later turn is reached for performance on long sessions.
 */
export function aggregateLoadedItems(
  snapshots: ContextSnapshotEvent[],
  upToTurnInclusive?: number,
): LoadedItemsByCategory {
  const cap = upToTurnInclusive ?? Number.POSITIVE_INFINITY;
  const result = emptyByCategory();
  for (const snap of snapshots) {
    if (snap.turnIndex > cap) break;
    for (const item of snap.loaded) {
      const lane = result[item.cat];
      if (lane) lane.push(item);
    }
  }
  return result;
}

/**
 * Sums the tokens of every item in a single category lane. Used for "X% of
 * total" labels in the ContentsPanel and as a quick test helper.
 */
export function totalTokensInCategory(items: LoadedItem[]): number {
  return items.reduce((sum, i) => sum + i.tokens, 0);
}
