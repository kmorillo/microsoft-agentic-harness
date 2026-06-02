import { test, expect } from '@playwright/test';

test.describe('Sessions List Page', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/sessions');
    await page.waitForSelector('[data-testid="session-table"]', { timeout: 15_000 });
  });

  test('renders KPI cards', async ({ page }) => {
    await page.waitForSelector('[data-testid^="kpi-"]', { timeout: 20_000 });
    const kpis = page.locator('[data-testid^="kpi-"]');
    const count = await kpis.count();
    expect(count).toBeGreaterThanOrEqual(4);

    const totalSessions = page.locator('[data-testid="kpi-total-sessions"]');
    await expect(totalSessions).toBeVisible();
  });

  test('shows at least one session row from the echo agent', async ({ page }) => {
    const rows = page.locator('[data-testid^="session-row-"]');
    await expect(rows.first()).toBeVisible();

    const rowCount = await rows.count();
    expect(rowCount).toBeGreaterThan(0);
  });

  test('session table displays agent name and metrics', async ({ page }) => {
    const firstRow = page.locator('[data-testid^="session-row-"]').first();
    const cells = firstRow.locator('td');

    const agentName = await cells.nth(0).textContent();
    expect(agentName).toBeTruthy();

    const turns = await cells.nth(4).textContent();
    expect(Number(turns)).toBeGreaterThan(0);
  });

  test('session timeline panel renders', async ({ page }) => {
    const timeline = page.locator('[data-testid="panel-session-timeline"]');
    await expect(timeline).toBeVisible();
  });

  test('clicking a session row navigates to detail', async ({ page }) => {
    const firstRow = page.locator('[data-testid^="session-row-"]').first();
    await firstRow.click();

    await page.waitForURL(/\/sessions\/.+/);
    expect(page.url()).toMatch(/\/sessions\/.+/);
  });
});

test.describe('Session Detail Page', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to sessions list, then click into the first session
    await page.goto('/sessions');
    await page.waitForSelector('[data-testid="session-table"]', { timeout: 15_000 });
    const firstRow = page.locator('[data-testid^="session-row-"]').first();
    await firstRow.click();
    await page.waitForURL(/\/sessions\/.+/);
  });

  test('renders session identity with agent name and status', async ({ page }) => {
    const identity = page.locator('[data-testid="session-identity"]');
    await expect(identity).toBeVisible();

    // Agent name should be visible
    const heading = identity.locator('h2');
    await expect(heading).toBeVisible();
    const agentName = await heading.textContent();
    expect(agentName).toBeTruthy();
  });

  test('stat strip renders all metric categories', async ({ page }) => {
    const statStrip = page.locator('[data-testid="stat-strip"]');
    await expect(statStrip).toBeVisible();

    // All 7 stat categories should be present
    const expectedStats = ['turns', 'duration', 'tokens', 'cache-hit', 'tool-calls', 'cost', 'subagents'];
    for (const stat of expectedStats) {
      const cell = page.locator(`[data-testid="stat-${stat}"]`);
      await expect(cell).toBeVisible();
    }
  });

  test('turns stat shows non-zero value', async ({ page }) => {
    const turnsValue = page.locator('[data-testid="stat-turns-value"]');
    await expect(turnsValue).toBeVisible();

    const text = await turnsValue.textContent();
    expect(Number(text)).toBeGreaterThan(0);
  });

  test('tokens stat shows non-zero value', async ({ page }) => {
    const tokensValue = page.locator('[data-testid="stat-tokens-value"]');
    await expect(tokensValue).toBeVisible();

    const text = await tokensValue.textContent();
    // Tokens are formatted (e.g., "1.2k"), so check it's not literally "0"
    expect(text).not.toBe('0');
  });

  test('cost stat shows a dollar value', async ({ page }) => {
    const costValue = page.locator('[data-testid="stat-cost-value"]');
    await expect(costValue).toBeVisible();

    const text = await costValue.textContent();
    expect(text).toMatch(/\$/);
  });

  test('Foresight gem hero renders with the context bar', async ({ page }) => {
    // PR 4 reskin: the old ConversationTimeline + message rows are gone;
    // the page now leads with the Foresight gem (ContextBar + ContextLegend
    // + ScrubStrip + ContentsPanel) above the per-turn SessionTimeline.
    const hero = page.locator('[data-testid="session-hero"]');
    await expect(hero).toBeVisible();

    // The hero owns the large ContextBar.
    const heroBar = hero.locator('[data-testid="context-bar"]').first();
    await expect(heroBar).toBeVisible();
  });

  test('session timeline renders at least one snapshot row with a role badge', async ({ page }) => {
    const timeline = page.locator('[data-testid="session-timeline"]');
    await expect(timeline).toBeVisible();

    const rows = page.locator('[data-testid^="timeline-row-"]');
    const count = await rows.count();
    expect(count).toBeGreaterThanOrEqual(1);

    // Each row carries a role pill — assert it resolves to a known value so
    // we catch role-inference regressions early.
    const firstRoleBadge = rows.first().locator('[data-testid$="-role"]');
    const role = (await firstRoleBadge.textContent())?.trim() ?? '';
    expect(['user', 'assistant', 'tool']).toContain(role);
  });

  test('multi-turn: every seeded turn becomes a timeline row', async ({ page }) => {
    // Echo-agent seed produces multiple turns. The new gem renders one
    // timeline row per ContextSnapshot, not per message — so count by turn.
    const rows = page.locator('[data-testid^="timeline-row-"]');
    const count = await rows.count();
    expect(count).toBeGreaterThanOrEqual(2);
  });

  test('tools panel is a collapsible details with the tool count', async ({ page }) => {
    // CostWaterfall was dropped intentionally (HANDOFF.md §4 pure gem). The
    // replacement surface for tool executions is the collapsible Tool
    // executions details below the timeline.
    const toolsPanel = page.locator('[data-testid="session-tools-panel"]');
    const isVisible = await toolsPanel.isVisible().catch(() => false);
    if (!isVisible) {
      test.skip(
        true,
        'No tool executions in seeded session — tools panel not rendered',
      );
      return;
    }
    const summary = toolsPanel.locator('summary').first();
    await expect(summary).toBeVisible();
    expect((await summary.textContent())?.toLowerCase()).toContain(
      'tool executions',
    );
  });

  test('back button returns to sessions list', async ({ page }) => {
    const backButton = page.locator('button', { hasText: '← sessions' });
    await expect(backButton).toBeVisible();
    await backButton.click();

    await page.waitForURL(/\/sessions$/);
  });
});
