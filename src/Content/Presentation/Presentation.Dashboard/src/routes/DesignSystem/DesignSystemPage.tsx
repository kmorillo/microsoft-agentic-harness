import { useState } from 'react';
import { ContextBar } from '@/components/context/ContextBar';
import { ContextLegend } from '@/components/context/ContextLegend';
import { ScrubStrip, type ScrubTurn } from '@/components/context/ScrubStrip';
import { ContextDrawer } from '@/components/context/ContextDrawer';
import { CategorySwatch } from '@/components/context/CategorySwatch';
import {
  CATEGORY_ORDER,
  CATEGORY_LABEL,
  CATEGORY_DESCRIPTION,
  type CategoryKey,
  type CategoryBreakdown,
} from '@/lib/categories';

const SAMPLE_BREAKDOWN: CategoryBreakdown = {
  system: 8_200,
  agents: 4_100,
  skills: 6_000,
  tools: 3_500,
  mcp: 1_200,
  messages: 17_000,
};

const SAMPLE_TURNS: ScrubTurn[] = [
  { id: 'boot', type: 'assistant', tokens: 8_200, label: 'boot' },
  { id: 't1', type: 'user', tokens: 520, label: 'U1' },
  { id: 't2', type: 'assistant', tokens: 4_540, label: 'A2' },
  { id: 't3', type: 'tool', tokens: 1_200, label: 'T3' },
  { id: 't4', type: 'assistant', tokens: 800, label: 'A4' },
  { id: 't5', type: 'user', tokens: 300, label: 'U5' },
  { id: 't6', type: 'assistant', tokens: 2_100, label: 'A6' },
  { id: 't7', type: 'assistant', tokens: 1_500, label: 'A7' },
  { id: 't8', type: 'tool', tokens: 980, label: 'T8' },
];

const SAMPLE_DRAWER_BODY = [
  '# rules/testing',
  '',
  '## Bug Fix Workflow (MANDATORY)',
  '',
  'When I report a bug, do not start by trying to fix it. Instead:',
  '',
  '1. Write a test that reproduces the bug.',
  '2. Have subagents try to fix the bug and prove it with a passing test.',
  '3. The fix is not done until the test passes.',
  '',
  '## Coverage: 80% minimum',
  '',
  'Unit + Integration + E2E for critical flows.',
].join('\n');

/**
 * Dev-only design-system sandbox for Foresight primitives. Visit at
 * `/design-system` in `npm run dev`. Production builds redirect to `/`.
 */
export default function DesignSystemPage() {
  // No runtime DEV gate needed — router.tsx omits the `/design-system` route
  // entirely from the prod bundle via `import.meta.env.DEV ? lazy(...) : null`,
  // so this module is never imported in production.
  const [activeCategory, setActiveCategory] = useState<CategoryKey | null>(null);
  const [scrubIndex, setScrubIndex] = useState(-1);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [showSparkline, setShowSparkline] = useState(true);

  return (
    <div className="p-6 space-y-10 max-w-5xl mx-auto">
      <header>
        <h1 className="text-2xl font-bold text-foreground">Foresight Design System</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Dev-only sandbox for the load-bearing Foresight primitives. See
          <code className="font-mono text-xs ml-1">HANDOFF.md</code> for the full design spec.
        </p>
      </header>

      <section>
        <h2 className="text-sm font-semibold uppercase tracking-wider text-muted-foreground mb-3">
          Category tokens
        </h2>
        <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
          {CATEGORY_ORDER.map((cat) => (
            <div
              key={cat}
              className="rounded-lg border border-border bg-card p-3 flex items-center gap-3"
            >
              <CategorySwatch category={cat} size="lg" />
              <div className="min-w-0">
                <div className="text-sm font-medium text-foreground">{CATEGORY_LABEL[cat]}</div>
                <div className="text-[11px] text-muted-foreground truncate">
                  {CATEGORY_DESCRIPTION[cat]}
                </div>
                <code className="font-mono text-[10px] text-muted-foreground">
                  --cat-{cat}
                </code>
              </div>
            </div>
          ))}
          <div className="rounded-lg border border-border bg-card p-3 flex items-center gap-3">
            <CategorySwatch category="headroom" size="lg" />
            <div>
              <div className="text-sm font-medium text-foreground">Headroom</div>
              <div className="text-[11px] text-muted-foreground">Remaining budget</div>
              <code className="font-mono text-[10px] text-muted-foreground">--cat-hatch</code>
            </div>
          </div>
        </div>
      </section>

      <section>
        <h2 className="text-sm font-semibold uppercase tracking-wider text-muted-foreground mb-3">
          ContextBar — three sizes
        </h2>
        <div className="space-y-4 rounded-lg border border-border bg-card p-4">
          <div>
            <div className="text-[11px] uppercase text-muted-foreground mb-1">sm (table row)</div>
            <ContextBar breakdown={SAMPLE_BREAKDOWN} size="sm" />
          </div>
          <div>
            <div className="text-[11px] uppercase text-muted-foreground mb-1">md (timeline node)</div>
            <ContextBar breakdown={SAMPLE_BREAKDOWN} size="md" />
          </div>
          <div>
            <div className="text-[11px] uppercase text-muted-foreground mb-1">lg (hero rail) — interactive</div>
            <ContextBar
              breakdown={SAMPLE_BREAKDOWN}
              size="lg"
              activeCategory={activeCategory}
              onSegmentClick={(cat) =>
                setActiveCategory((curr) => (curr === cat ? null : cat))
              }
            />
          </div>
        </div>
      </section>

      <section>
        <h2 className="text-sm font-semibold uppercase tracking-wider text-muted-foreground mb-3">
          ContextLegend — linked to bar above
        </h2>
        <ContextLegend
          breakdown={SAMPLE_BREAKDOWN}
          activeCategory={activeCategory}
          onSelect={setActiveCategory}
        />
      </section>

      <section>
        <h2 className="text-sm font-semibold uppercase tracking-wider text-muted-foreground mb-3">
          ScrubStrip
        </h2>
        <div className="rounded-lg border border-border bg-card p-4 space-y-3">
          <label className="flex items-center gap-2 text-xs text-muted-foreground">
            <input
              type="checkbox"
              checked={showSparkline}
              onChange={(e) => setShowSparkline(e.target.checked)}
              className="accent-cat-accent"
            />
            Show sparkline
          </label>
          <ScrubStrip
            turns={SAMPLE_TURNS}
            activeIndex={scrubIndex}
            onScrub={setScrubIndex}
            showSparkline={showSparkline}
          />
          <div className="font-mono tabular-nums text-xs text-muted-foreground">
            Active index: {scrubIndex === -1 ? 'current end state' : scrubIndex}
          </div>
        </div>
      </section>

      <section>
        <h2 className="text-sm font-semibold uppercase tracking-wider text-muted-foreground mb-3">
          ContextDrawer
        </h2>
        <button
          type="button"
          onClick={() => setDrawerOpen(true)}
          className="rounded-md border border-cat-accent bg-cat-accent px-3 py-1.5 text-sm font-medium text-white hover:opacity-90"
        >
          Open sample drawer
        </button>
        <ContextDrawer
          open={drawerOpen}
          onOpenChange={setDrawerOpen}
          category="skills"
          name="rules/testing"
          path="/.claude/rules/testing.md"
          body={SAMPLE_DRAWER_BODY}
          lang="markdown"
          onPrev={() => {}}
          onNext={() => {}}
          prevLabel="rules/style"
          nextLabel="rules/security"
        />
      </section>
    </div>
  );
}
