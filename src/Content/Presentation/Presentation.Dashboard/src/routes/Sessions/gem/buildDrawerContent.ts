import type {
  LoadedItem,
  SessionMessageRecord,
  ToolExecutionRecord,
} from '@/api/types';
import type {
  DrawerLang,
  DrawerRole,
} from '@/components/context/ContextDrawer';

/**
 * Pointer to the backend record this drawer item maps to. When present, the
 * drawer render site lazily fetches the full body via
 * `GET /api/sessions/:id/messages/:messageId` or
 * `GET /api/sessions/:id/tools/:invocationId` and replaces `body` with the
 * resolved content. When absent (skills / agents / mcp / system), `body`
 * is the only thing the drawer has to show.
 */
export interface DrawerIdRef {
  kind: 'message' | 'tool';
  id: string;
}

export interface DrawerContent {
  /** The body rendered in the drawer. Used as the fallback while a lazy fetch is in flight. */
  body: string;
  /** Optional role banner — only present for message items. */
  role?: DrawerRole;
  /** Drives lightweight syntax styling inside the drawer. */
  lang: DrawerLang;
  /**
   * Set when the item maps to a backend record. The drawer renderer fetches
   * the full body for this ref on open and overrides `body` once loaded.
   */
  idRef?: DrawerIdRef;
}

interface BuildContext {
  /** Full message list for the session. Joined by `turnIndex` on demand. */
  messages: SessionMessageRecord[];
  /** Full tool-execution list for the session. */
  tools: ToolExecutionRecord[];
  /** The snapshot turn this LoadedItem belongs to. */
  turnIndex: number;
}

const NO_CAPTURED_CONTENT =
  'No content captured for this item.';

const CATEGORY_NO_BODY: Record<string, string> =
  {
    skills: 'Skill body is not captured by the observability store.',
    agents: 'Agent manifest body is not captured by the observability store.',
    mcp: 'MCP descriptor body is not captured by the observability store.',
    system: 'System prompt body is not captured by the observability store.',
  };

/**
 * Resolves what to show in the ContextDrawer for a given LoadedItem.
 *
 * - `messages` → `SessionMessageRecord.contentPreview` (as the loading
 *   fallback) plus an `idRef` so the drawer renderer can fetch
 *   `contentFull` from `/api/sessions/:id/messages/:messageId` on open.
 *   Role banner reflects the message role.
 * - `tools` → synthetic metadata card (name / status / duration / size)
 *   as JSON, plus an `idRef` so the drawer renderer can swap in the full
 *   args + stdout from `/api/sessions/:id/tools/:invocationId`.
 * - `skills` / `agents` / `mcp` / `system` → static "no body captured"
 *   note. The observability store does not currently record full bodies
 *   for these categories; the drawer shows the ref and label only.
 *
 * Pure function — easy to test, easy to change content sources.
 */
export function buildDrawerContent(
  item: LoadedItem,
  ctx: BuildContext,
): DrawerContent {
  switch (item.cat) {
    case 'messages':
      return buildMessageBody(item, ctx);
    case 'tools':
      return buildToolBody(item, ctx);
    case 'skills':
    case 'agents':
    case 'mcp':
    case 'system':
    default: {
      const note = CATEGORY_NO_BODY[item.cat] ?? NO_CAPTURED_CONTENT;
      return {
        body: item.ref ? `${item.ref}\n\n${note}` : note,
        lang: 'text',
      };
    }
  }
}

function buildMessageBody(
  item: LoadedItem,
  ctx: BuildContext,
): DrawerContent {
  // Prefer an exact match on (turnIndex, role) when the LoadedItem's `what`
  // discloses the role (e.g. "User message" / "Assistant message"). Falling
  // back to first-match-by-turn keeps the drawer useful when labels diverge.
  const roleHint = inferRoleFromLabel(item.what);
  const candidates = ctx.messages.filter(
    (m) => m.turnIndex === ctx.turnIndex,
  );
  const match =
    (roleHint && candidates.find((m) => m.role === roleHint)) ??
    candidates[0];

  const role = (match?.role as DrawerRole | undefined) ?? roleHint ?? 'user';
  const preview = match?.contentPreview?.trim();

  if (match) {
    return {
      body: preview ?? `${item.what}\n\n${NO_CAPTURED_CONTENT}`,
      role,
      lang: 'text',
      idRef: { kind: 'message', id: match.id },
    };
  }

  return {
    body: `${item.what}\n\n${NO_CAPTURED_CONTENT}`,
    role,
    lang: 'text',
  };
}

function buildToolBody(
  item: LoadedItem,
  ctx: BuildContext,
): DrawerContent {
  // Find the most recent tool execution at this turn whose name matches the
  // label. Tool labels look like "Tool: Read · BillingPipeline.cs".
  const toolName = extractToolName(item.what);
  const candidates = ctx.tools.filter(
    (t) =>
      t.toolName === toolName ||
      (!toolName && t.toolName) /* fall back if label unparsed */,
  );
  const match = candidates[0];

  const card = {
    name: toolName ?? match?.toolName ?? item.what,
    target: item.ref ?? null,
    tokensAddedToContext: item.tokens,
    status: match?.status ?? 'unknown',
    durationMs: match?.durationMs ?? null,
    resultSize: match?.resultSize ?? null,
    errorType: match?.errorType ?? null,
    source: match?.toolSource ?? null,
  };

  return {
    body: JSON.stringify(card, null, 2),
    lang: 'json',
    ...(match ? { idRef: { kind: 'tool' as const, id: match.id } } : {}),
  };
}

function inferRoleFromLabel(label: string): DrawerRole | undefined {
  const lower = label.toLowerCase();
  if (lower.includes('user')) return 'user';
  if (lower.includes('assistant')) return 'assistant';
  if (lower.includes('tool result') || lower.startsWith('tool ')) return 'tool';
  return undefined;
}

/**
 * Extracts a tool name from a label of the form `Tool: Read · X` or `Tool Read`.
 * Returns undefined when the label doesn't follow either convention.
 */
function extractToolName(label: string): string | undefined {
  const colon = label.match(/^Tool:\s*([A-Za-z0-9_-]+)/);
  if (colon) return colon[1];
  const bare = label.match(/^Tool\s+([A-Za-z0-9_-]+)/);
  if (bare) return bare[1];
  return undefined;
}
