/**
 * Foresight category taxonomy.
 *
 * Single source of truth for the six context-window segments. Every visualization
 * in the Foresight design language groups tokens by these keys. Order is
 * load-bearing — see foresight-dashboard-spec.md §3.1 ("same color, same order, same shape everywhere").
 *
 * If you change keys, labels, or order here, every Foresight component picks it up
 * automatically. CSS variables live in `src/index.css` under `--cat-*`.
 */

export type CategoryKey =
  | 'system'
  | 'agents'
  | 'skills'
  | 'tools'
  | 'mcp'
  | 'messages';

export const CATEGORY_ORDER: readonly CategoryKey[] = [
  'system',
  'agents',
  'skills',
  'tools',
  'mcp',
  'messages',
] as const;

export const CATEGORY_LABEL: Record<CategoryKey, string> = {
  system: 'System',
  agents: 'Agents',
  skills: 'Skills',
  tools: 'Tools',
  mcp: 'MCP',
  messages: 'Messages',
};

/** Long-form descriptions used in tooltips and the design-system legend. */
export const CATEGORY_DESCRIPTION: Record<CategoryKey, string> = {
  system: 'Harness system prompt',
  agents: 'agents.md, rules/ files',
  skills: 'Loaded skills (SKILL.md bodies)',
  tools: 'Tool JSON Schema definitions',
  mcp: 'MCP server descriptions',
  messages: 'User / assistant / tool messages',
};

/** Tailwind background utility for each category. Drives the segmented context bar. */
export const CATEGORY_BG_CLASS: Record<CategoryKey, string> = {
  system: 'bg-cat-system',
  agents: 'bg-cat-agents',
  skills: 'bg-cat-skills',
  tools: 'bg-cat-tools',
  mcp: 'bg-cat-mcp',
  messages: 'bg-cat-messages',
};

/** Tailwind text utility for each category. Used for category-tinted labels. */
export const CATEGORY_TEXT_CLASS: Record<CategoryKey, string> = {
  system: 'text-cat-system',
  agents: 'text-cat-agents',
  skills: 'text-cat-skills',
  tools: 'text-cat-tools',
  mcp: 'text-cat-mcp',
  messages: 'text-cat-messages',
};

/** Tailwind border utility for each category. Used for ring outlines on active state. */
export const CATEGORY_BORDER_CLASS: Record<CategoryKey, string> = {
  system: 'border-cat-system',
  agents: 'border-cat-agents',
  skills: 'border-cat-skills',
  tools: 'border-cat-tools',
  mcp: 'border-cat-mcp',
  messages: 'border-cat-messages',
};

/**
 * Tokens consumed by one category. Shape mirrors foresight-dashboard-spec.md §6.3 `CategoryBreakdown`.
 * Every Foresight visualization (context bar, legend, table mini-bar, timeline node)
 * accepts this exact shape.
 */
export type CategoryBreakdown = Record<CategoryKey, number>;

/** Empty breakdown — useful as a starting accumulator. */
export const EMPTY_BREAKDOWN: CategoryBreakdown = {
  system: 0,
  agents: 0,
  skills: 0,
  tools: 0,
  mcp: 0,
  messages: 0,
};

/**
 * Sum of all categories in a breakdown (total tokens used). Iterates
 * CATEGORY_ORDER so adding a 7th category lights up automatically; a
 * hand-listed sum would silently undercount.
 */
export function breakdownTotal(b: CategoryBreakdown): number {
  return CATEGORY_ORDER.reduce((sum, k) => sum + b[k], 0);
}

/** Default context budget used when no per-model value is supplied. See foresight-dashboard-spec.md §6.1. */
export const DEFAULT_CONTEXT_BUDGET = 200_000;
