import { describe, it, expect } from 'vitest';
import { buildDrawerContent } from './buildDrawerContent';
import type {
  LoadedItem,
  SessionMessageRecord,
  ToolExecutionRecord,
} from '@/api/types';

const baseCtx = { messages: [] as SessionMessageRecord[], tools: [] as ToolExecutionRecord[], turnIndex: 0 };

function msg(overrides: Partial<SessionMessageRecord>): SessionMessageRecord {
  return {
    id: 'm-' + Math.random(),
    sessionId: 's-1',
    turnIndex: 0,
    role: 'user',
    source: null,
    contentPreview: null,
    model: null,
    inputTokens: 0,
    outputTokens: 0,
    cacheRead: 0,
    cacheWrite: 0,
    costUsd: 0,
    cacheHitPct: 0,
    toolNames: null,
    createdAt: new Date(2026, 0, 1).toISOString(),
    ...overrides,
  };
}

function tool(overrides: Partial<ToolExecutionRecord>): ToolExecutionRecord {
  return {
    id: 't-' + Math.random(),
    sessionId: 's-1',
    messageId: null,
    toolName: 'Read',
    toolSource: 'builtin',
    durationMs: 12,
    status: 'success',
    errorType: null,
    resultSize: 1024,
    createdAt: new Date(2026, 0, 1).toISOString(),
    ...overrides,
  };
}

describe('buildDrawerContent — messages', () => {
  it('returns contentPreview + truncated footer with the user role banner', () => {
    const item: LoadedItem = { what: 'User message', tokens: 100, cat: 'messages' };
    const ctx = {
      ...baseCtx,
      turnIndex: 3,
      messages: [msg({ turnIndex: 3, role: 'user', contentPreview: 'Hello world' })],
    };

    const result = buildDrawerContent(item, ctx);

    expect(result.role).toBe('user');
    expect(result.lang).toBe('text');
    expect(result.body.startsWith('Hello world')).toBe(true);
    expect(result.body).toContain('Truncated');
    expect(result.body).toContain('PR 5');
  });

  it('disambiguates by role hint when both user and assistant share a turn', () => {
    const item: LoadedItem = { what: 'Assistant response', tokens: 100, cat: 'messages' };
    const ctx = {
      ...baseCtx,
      turnIndex: 2,
      messages: [
        msg({ turnIndex: 2, role: 'user', contentPreview: 'should NOT be used' }),
        msg({ turnIndex: 2, role: 'assistant', contentPreview: 'PICK ME' }),
      ],
    };

    const result = buildDrawerContent(item, ctx);
    expect(result.role).toBe('assistant');
    expect(result.body).toContain('PICK ME');
  });

  it('falls back to the first message of the turn when the role hint cannot be inferred', () => {
    const item: LoadedItem = { what: 'Some opaque label', tokens: 100, cat: 'messages' };
    const ctx = {
      ...baseCtx,
      turnIndex: 5,
      messages: [msg({ turnIndex: 5, role: 'assistant', contentPreview: 'first hit' })],
    };

    const result = buildDrawerContent(item, ctx);
    expect(result.body).toContain('first hit');
    expect(result.role).toBe('assistant');
  });

  it('shows a PR 5 placeholder when the turn has no message with a contentPreview', () => {
    const item: LoadedItem = { what: 'User message', tokens: 100, cat: 'messages' };
    const result = buildDrawerContent(item, { ...baseCtx, turnIndex: 99 });
    expect(result.body).toContain('PR 5');
    expect(result.role).toBe('user'); // role hint applied even without a match
  });
});

describe('buildDrawerContent — tools', () => {
  it('renders a JSON metadata card from the matching ToolExecutionRecord', () => {
    const item: LoadedItem = {
      what: 'Tool: Read · BillingPipeline.cs',
      tokens: 200,
      cat: 'tools',
      ref: 'BillingPipeline.cs',
    };
    const ctx = {
      ...baseCtx,
      tools: [tool({ toolName: 'Read', durationMs: 42, resultSize: 8192, status: 'success' })],
    };

    const result = buildDrawerContent(item, ctx);
    expect(result.lang).toBe('json');
    const parsed = JSON.parse(result.body);
    expect(parsed.name).toBe('Read');
    expect(parsed.target).toBe('BillingPipeline.cs');
    expect(parsed.durationMs).toBe(42);
    expect(parsed.resultSize).toBe(8192);
    expect(parsed.status).toBe('success');
    expect(parsed.tokensAddedToContext).toBe(200);
  });

  it('still emits a useful card when no matching tool execution is found', () => {
    const item: LoadedItem = {
      what: 'Tool: UnknownTool',
      tokens: 50,
      cat: 'tools',
    };
    const result = buildDrawerContent(item, baseCtx);
    expect(result.lang).toBe('json');
    const parsed = JSON.parse(result.body);
    expect(parsed.name).toBe('UnknownTool');
    expect(parsed.status).toBe('unknown');
    expect(parsed.note).toContain('PR 6');
  });
});

describe('buildDrawerContent — placeholders', () => {
  it.each(['skills', 'agents', 'mcp', 'system'] as const)(
    'returns the PR 5 placeholder for category %s',
    (cat) => {
      const item: LoadedItem = { what: 'rules/testing', tokens: 1234, cat };
      const result = buildDrawerContent(item, baseCtx);
      expect(result.lang).toBe('text');
      expect(result.role).toBeUndefined();
      expect(result.body).toContain('PR 5');
    },
  );

  it('prepends the ref to placeholder bodies when available', () => {
    const item: LoadedItem = {
      what: 'Skill X',
      tokens: 500,
      cat: 'skills',
      ref: 'skills/foresight/SKILL.md',
    };
    const result = buildDrawerContent(item, baseCtx);
    expect(result.body.startsWith('skills/foresight/SKILL.md')).toBe(true);
    expect(result.body).toContain('PR 5');
  });
});
