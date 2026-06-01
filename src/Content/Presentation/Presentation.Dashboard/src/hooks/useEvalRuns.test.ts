import { describe, it, expect } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement, type ReactNode } from 'react';
import { useEvalRuns } from './useEvalRuns';
import { useEvalRunDetail } from './useEvalRunDetail';
import { usePromptVersionComparison } from './usePromptVersionComparison';

function wrapper(client: QueryClient) {
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client }, children);
}

function freshClient() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
  });
}

describe('useEvalRuns', () => {
  it('returns the mocked runs list with expected shape', async () => {
    const { result } = renderHook(() => useEvalRuns(50), { wrapper: wrapper(freshClient()) });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(1);

    const row = result.current.data![0]!;
    expect(row.runId).toBe('run-001');
    expect(row.overallVerdict).toBe('Fail');
    expect(row.passRate).toBeCloseTo(0.8);
  });

  it('exposes camelCase property names matching the API contract', async () => {
    const { result } = renderHook(() => useEvalRuns(50), { wrapper: wrapper(freshClient()) });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Spec lock: the SignalREvalRunNotifierTests on the server pin these names —
    // breaking the contract there should fail this test too.
    const row = result.current.data![0]!;
    const keys = Object.keys(row).sort();
    expect(keys).toContain('runId');
    expect(keys).toContain('startedAtUtc');
    expect(keys).toContain('completedAtUtc');
    expect(keys).toContain('passedCount');
    expect(keys).toContain('failedCount');
    expect(keys).toContain('warnedCount');
    expect(keys).toContain('erroredCount');
    expect(keys).toContain('passRate');
    expect(keys).toContain('overallVerdict');
    expect(keys).toContain('repeats');
    expect(keys).toContain('totalCostUsd');
    expect(keys).toContain('receivedAtUtc');
  });
});

describe('useEvalRunDetail', () => {
  it('does not fire when runId is undefined', () => {
    const client = freshClient();
    const { result } = renderHook(() => useEvalRunDetail(undefined), { wrapper: wrapper(client) });
    expect(result.current.fetchStatus).toBe('idle');
  });

  it('returns the report when the run exists', async () => {
    const { result } = renderHook(() => useEvalRunDetail('r-known'), { wrapper: wrapper(freshClient()) });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.runId).toBe('r-known');
  });

  it('surfaces 404 as an error state', async () => {
    const { result } = renderHook(() => useEvalRunDetail('unknown'), { wrapper: wrapper(freshClient()) });
    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

describe('usePromptVersionComparison', () => {
  it('returns versions sorted with the newest first', async () => {
    const { result } = renderHook(
      () => usePromptVersionComparison('faithfulness-judge'),
      { wrapper: wrapper(freshClient()) },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(2);
    expect(result.current.data![0]!.version.major).toBe(2);
    expect(result.current.data![1]!.version.major).toBe(1);
  });

  it('is disabled when promptName is undefined', () => {
    const { result } = renderHook(
      () => usePromptVersionComparison(undefined),
      { wrapper: wrapper(freshClient()) },
    );
    expect(result.current.fetchStatus).toBe('idle');
  });
});
