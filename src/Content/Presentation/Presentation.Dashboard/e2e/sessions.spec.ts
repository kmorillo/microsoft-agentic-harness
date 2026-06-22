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

  test('agent rail renders with at least one tile', async ({ page }) => {
    // PR 5: Foresight reskin replaced the Gantt timeline with the agent
    // rail. The rail lives to the left of the table and is the entry point
    // for the in-place filter affordance.
    const rail = page.locator('[data-testid="agent-rail"]');
    await expect(rail).toBeVisible();
    const tiles = rail.locator('[data-testid^="agent-rail-tile-"]');
    expect(await tiles.count()).toBeGreaterThanOrEqual(1);
    await expect(rail.locator('[data-testid="agent-rail-all"]')).toBeVisible();
  });

  test('clicking an agent tile filters the sessions table', async ({ page }) => {
    // Use the dedicated name testid so we read the agent name (not the
    // avatar initials, which are the first span inside the button).
    const firstTile = page.locator('[data-testid^="agent-rail-tile-"]').first();
    const tileName = await firstTile
      .locator('[data-testid^="agent-rail-tile-name-"]')
      .first()
      .textContent();
    await firstTile.click();

    // Every visible row's Agent column should match the selected agent.
    const rows = page.locator('[data-testid="session-table"] tbody tr');
    const count = await rows.count();
    expect(count).toBeGreaterThanOrEqual(1);
    for (let i = 0; i < count; i++) {
      const cellText = await rows.nth(i).locator('td').first().textContent();
      expect(cellText?.trim()).toBe(tileName?.trim());
    }
  });

  test('clicking "All agents" clears the filter', async ({ page }) => {
    const firstTile = page.locator('[data-testid^="agent-rail-tile-"]').first();
    await firstTile.click();
    await page.locator('[data-testid="agent-rail-all"]').click();

    // After clearing, the All-agents pseudo-tile is the active one.
    await expect(
      page.locator('[data-testid="agent-rail-all"]'),
    ).toHaveAttribute('data-active', 'true');
  });

  test('rows expose a mini context bar column (or fallback rail)', async ({ page }) => {
    // The new mini-bar column lives on every row; sessions with a
    // breakdown show the ContextBar, sessions without it show the
    // fallback. Either testid prefix must be present per row.
    const rows = page.locator('[data-testid^="session-row-"]');
    const count = await rows.count();
    expect(count).toBeGreaterThanOrEqual(1);
    const barOrFallback = page.locator(
      '[data-testid^="session-row-bar-"]',
    );
    expect(await barOrFallback.count()).toBeGreaterThanOrEqual(count);
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
    // CostWaterfall was dropped intentionally (foresight-dashboard-spec.md §4 pure gem). The
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
