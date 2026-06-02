import type {
  LoadedItem,
  SessionMessageRecord,
  ToolExecutionRecord,
} from '@/api/types';
import type {
  DrawerLang,
  DrawerRole,
} from '@/components/context/ContextDrawer';

export interface DrawerContent {
  /** The body rendered in the drawer. May be a placeholder for PR 5. */
  body: string;
  /** Optional role banner — only present for message items. */
  role?: DrawerRole;
  /** Drives lightweight syntax styling inside the drawer. */
  lang: DrawerLang;
}

interface BuildContext {
  /** Full message list for the session. Joined by `turnIndex` on demand. */
  messages: SessionMessageRecord[];
  /** Full tool-execution list for the session. */
  tools: ToolExecutionRecord[];
  /** The snapshot turn this LoadedItem belongs to. */
  turnIndex: number;
}

const PR5_PLACEHOLDER =
  'Full content for this item will be available once the ' +
  '`/api/sessions/{id}/files/{idx}` endpoint ships in PR 5.';

const TRUNCATED_FOOTER =
  '\n\n— Truncated. Full body endpoint pending in PR 5.';

/**
 * Resolves what to show in the ContextDrawer for a given LoadedItem. Encodes
 * the per-category rules confirmed by the PR 4 plan:
 *
 * - `messages` → `SessionMessageRecord.contentPreview` text + truncation note.
 *   Role banner reflects the message role (`user` / `assistant` / `tool`).
 * - `tools`    → synthetic metadata card (name / status / duration / size)
 *   rendered as JSON so the drawer's lightweight syntax tints the keys.
 * - `skills` / `agents` / `mcp` / `system` → PR 5 placeholder.
 *
 * Pure function — easy to test, easy to swap content sources in PR 5 by
 * editing this one file.
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
    default:
      return {
        body: item.ref
          ? `${item.ref}\n\n${PR5_PLACEHOLDER}`
          : PR5_PLACEHOLDER,
        lang: 'text',
      };
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

  const body = preview
    ? `${preview}${TRUNCATED_FOOTER}`
    : `${item.what}\n\n${PR5_PLACEHOLDER}`;

  return { body, role, lang: 'text' };
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
    note:
      'Tool stdout/args drilldown ships in PR 6. This card shows metadata only.',
  };

  return {
    body: JSON.stringify(card, null, 2),
    lang: 'json',
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
