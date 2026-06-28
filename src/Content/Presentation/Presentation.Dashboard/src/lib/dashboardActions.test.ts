import { describe, it, expect, beforeEach, vi } from 'vitest';

// vi.hoisted runs before the hoisted vi.mock factories, so these spies exist when the
// factory objects are constructed (a plain top-level const would not yet be initialized).
const { navigateMock, invalidateMock } = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  invalidateMock: vi.fn(),
}));

vi.mock('@/app/router', () => ({
  router: { navigate: navigateMock, state: { location: { pathname: '/spend/tokens' } } },
}));
vi.mock('@/app/queryClient', () => ({
  queryClient: { invalidateQueries: invalidateMock },
}));

import { dispatchDashboardAction, describeAction } from './dashboardActions';
import { useTimeRangeStore } from '@/stores/timeRangeStore';
import { useThemeStore } from '@/stores/themeStore';

describe('dispatchDashboardAction', () => {
  beforeEach(() => {
    navigateMock.mockClear();
    invalidateMock.mockClear();
    useTimeRangeStore.setState({ preset: '1h', customStart: null, customEnd: null });
    useThemeStore.setState({ theme: 'dark' });
  });

  it('get_state returns the current page, preset and theme as JSON', async () => {
    const result = await dispatchDashboardAction('get_state', {});
    const parsed = JSON.parse(result);
    expect(parsed.page).toBe('/spend/tokens');
    expect(parsed.preset).toBe('1h');
    expect(parsed.theme).toBe('dark');
  });

  it('set_time_range applies a valid preset to the store', async () => {
    const result = await dispatchDashboardAction('set_time_range', { preset: '24h' });
    expect(useTimeRangeStore.getState().preset).toBe('24h');
    expect(result).toContain('24h');
  });

  it('set_time_range rejects an unsupported preset without changing state', async () => {
    const result = await dispatchDashboardAction('set_time_range', { preset: '90d' });
    expect(useTimeRangeStore.getState().preset).toBe('1h');
    expect(result.toLowerCase()).toContain('unsupported');
  });

  it('set_time_range applies a custom from/to window', async () => {
    await dispatchDashboardAction('set_time_range', { from: '2026-06-01T00:00:00Z', to: '2026-06-02T00:00:00Z' });
    const tr = useTimeRangeStore.getState();
    expect(tr.preset).toBe('custom');
    expect(tr.customStart).toBe('2026-06-01T00:00:00Z');
    expect(tr.customEnd).toBe('2026-06-02T00:00:00Z');
  });

  it('navigate routes to the supplied path', async () => {
    const result = await dispatchDashboardAction('navigate', { path: '/spend/cost' });
    expect(navigateMock).toHaveBeenCalledWith('/spend/cost');
    expect(result).toContain('/spend/cost');
  });

  it('navigate rejects a non-path argument', async () => {
    const result = await dispatchDashboardAction('navigate', { path: 'spend' });
    expect(navigateMock).not.toHaveBeenCalled();
    expect(result.toLowerCase()).toContain('no valid path');
  });

  it('refresh_data invalidates queries', async () => {
    await dispatchDashboardAction('refresh_data', {});
    expect(invalidateMock).toHaveBeenCalledTimes(1);
  });

  it('unknown operation returns an explanatory message and does not throw', async () => {
    const result = await dispatchDashboardAction('self_destruct', {});
    expect(result.toLowerCase()).toContain('unknown');
  });

  it('describeAction summarizes navigation with the path', () => {
    expect(describeAction('navigate', { path: '/spend' })).toContain('/spend');
  });
});
