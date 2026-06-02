import { useCallback, useMemo, useState } from 'react';
import type { CategoryBreakdown, CategoryKey } from '@/lib/categories';
import type {
  ContextSnapshotEvent,
  LoadedItem,
  SessionMessageRecord,
  ToolExecutionRecord,
} from '@/api/types';
import { aggregateLoadedItems } from '@/lib/loadedItems';
import {
  buildDrawerContent,
  type DrawerContent,
} from './buildDrawerContent';

/**
 * "Live tail" sentinel for {@link SessionGemState.activeTurnIndex}. The hero
 * shows the most recent snapshot's `ctxAfter` (or the persisted session-level
 * `breakdown` when no snapshots have arrived yet).
 */
export const CURRENT_TURN = -1 as const;

export interface DrawerTarget {
  /** The LoadedItem the user clicked. */
  item: LoadedItem;
  /** Where the click originated (drives prev/next walking semantics). */
  source: 'hero' | 'timeline';
  /** Resolved content built once on open and refreshed on walk. */
  content: DrawerContent;
  /** Display name + path used in the drawer header. */
  name: string;
  path: string;
  /** Index inside the active category's items array — used by `walkDrawer`. */
  loadedIndex: number;
  /**
   * The snapshot turn this item belongs to. Set by the caller of openDrawer
   * (timeline knows from its render loop; hero passes the active scrub turn).
   * Drawer content resolution joins messages by this index.
   */
  turnIndex: number;
}

interface UseSessionGemStateOptions {
  snapshots: ContextSnapshotEvent[];
  fallbackBreakdown?: CategoryBreakdown | null;
  messages: SessionMessageRecord[];
  tools: ToolExecutionRecord[];
}

export interface SessionGemState {
  // Scrub
  activeTurnIndex: number;
  setActiveTurnIndex: (i: number) => void;
  jumpToCurrent: () => void;
  isScrubbed: boolean;

  // Category filter
  activeCategory: CategoryKey | null;
  setActiveCategory: (c: CategoryKey | null) => void;

  // Drawer
  drawerItem: DrawerTarget | null;
  /**
   * Opens the drawer for {@link item}. Callers MUST pass the turnIndex the
   * item belongs to — the timeline knows it from its render loop; the hero
   * passes `activeTurnIndex` (or the live tail's turnIndex when at CURRENT).
   * Threading the index through avoids a reference-equality scan over
   * `snapshots[].loaded[]` which fails on store-merge reshuffles.
   */
  openDrawer: (
    item: LoadedItem,
    source: 'hero' | 'timeline',
    turnIndex: number,
  ) => void;
  closeDrawer: () => void;
  walkDrawer: (direction: -1 | 1) => void;

  // Derived
  displayedBreakdown: CategoryBreakdown | null;
}

/**
 * Owns the per-page interaction state for the Foresight gem: which turn the
 * hero is rewound to, which category lane is filtered, and which artifact (if
 * any) the drawer is showing. Pure derivations (displayedBreakdown) are
 * memoised against snapshot / sentinel changes.
 */
export function useSessionGemState({
  snapshots,
  fallbackBreakdown,
  messages,
  tools,
}: UseSessionGemStateOptions): SessionGemState {
  const [activeTurnIndex, setActiveTurnIndexInternal] =
    useState<number>(CURRENT_TURN);
  const [activeCategory, setActiveCategory] = useState<CategoryKey | null>(
    null,
  );
  const [drawerItem, setDrawerItem] = useState<DrawerTarget | null>(null);

  const snapshotByTurn = useMemo(() => {
    const map = new Map<number, ContextSnapshotEvent>();
    for (const s of snapshots) map.set(s.turnIndex, s);
    return map;
  }, [snapshots]);

  const lastSnapshot =
    snapshots.length > 0 ? snapshots[snapshots.length - 1] : null;

  const displayedBreakdown = useMemo<CategoryBreakdown | null>(() => {
    if (activeTurnIndex === CURRENT_TURN) {
      return lastSnapshot?.ctxAfter ?? fallbackBreakdown ?? null;
    }
    return (
      snapshotByTurn.get(activeTurnIndex)?.ctxAfter ??
      lastSnapshot?.ctxAfter ??
      fallbackBreakdown ??
      null
    );
  }, [activeTurnIndex, snapshotByTurn, lastSnapshot, fallbackBreakdown]);

  const isScrubbed = useMemo(() => {
    if (activeTurnIndex === CURRENT_TURN) return false;
    return activeTurnIndex !== (lastSnapshot?.turnIndex ?? CURRENT_TURN);
  }, [activeTurnIndex, lastSnapshot]);

  const setActiveTurnIndex = useCallback(
    (i: number) => setActiveTurnIndexInternal(i),
    [],
  );
  const jumpToCurrent = useCallback(
    () => setActiveTurnIndexInternal(CURRENT_TURN),
    [],
  );

  /**
   * Hoisted lane map: aggregateLoadedItems is called once per snapshots
   * change instead of on every drawer interaction. ContentsPanel may also
   * read this via a prop in the future to avoid its own duplicate scan.
   */
  const allLanes = useMemo(() => aggregateLoadedItems(snapshots), [snapshots]);

  const buildTarget = useCallback(
    (
      item: LoadedItem,
      source: 'hero' | 'timeline',
      turnIndex: number,
    ): DrawerTarget => {
      const content = buildDrawerContent(item, { messages, tools, turnIndex });
      const lane = allLanes[item.cat];
      const idx = lane.indexOf(item);
      // -1 only happens when the clicked item came from a stale snapshot
      // that's been replaced by a hydrate; falling back to 0 means walk
      // starts at the head of the lane, which is the safest non-crashing
      // behaviour.
      const loadedIndex = idx >= 0 ? idx : 0;
      return {
        item,
        source,
        content,
        name: item.what,
        path: item.ref ?? '—',
        loadedIndex,
        turnIndex,
      };
    },
    [messages, tools, allLanes],
  );

  const openDrawer = useCallback(
    (item: LoadedItem, source: 'hero' | 'timeline', turnIndex: number) => {
      setDrawerItem(buildTarget(item, source, turnIndex));
    },
    [buildTarget],
  );

  const closeDrawer = useCallback(() => setDrawerItem(null), []);

  const walkDrawer = useCallback(
    (direction: -1 | 1) => {
      setDrawerItem((current) => {
        if (!current) return current;
        const lane = allLanes[current.item.cat];
        if (lane.length <= 1) return current;
        const next =
          (current.loadedIndex + direction + lane.length) % lane.length;
        const nextItem = lane[next]!;
        // The next item may belong to a different turn — its position in the
        // lane is preserved by aggregateLoadedItems' insertion order, which
        // walks snapshots ascending, so we recompute turnIndex by scanning
        // forward through the lane to count items per turn. Simpler: reuse
        // the current target's turnIndex when items don't expose origin —
        // walk semantics are "the next loaded artifact in this category",
        // not "the next turn". Drawer content for messages is anchored to
        // the originating turn, so we keep current.turnIndex when category
        // is messages, otherwise reuse it as a best-effort fallback.
        return buildTarget(nextItem, current.source, current.turnIndex);
      });
    },
    [allLanes, buildTarget],
  );

  return {
    activeTurnIndex,
    setActiveTurnIndex,
    jumpToCurrent,
    isScrubbed,

    activeCategory,
    setActiveCategory,

    drawerItem,
    openDrawer,
    closeDrawer,
    walkDrawer,

    displayedBreakdown,
  };
}
