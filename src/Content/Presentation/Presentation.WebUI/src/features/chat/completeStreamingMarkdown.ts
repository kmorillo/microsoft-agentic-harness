/**
 * Stabilises partially-streamed markdown so an in-progress fenced code block
 * renders as a (growing) code block instead of flickering as raw ``` / ~~~
 * delimiters until its closing fence finally arrives.
 *
 * While a response streams in, the markdown is re-parsed on every chunk. An
 * opening code fence with no matching close parses as literal backticks, then
 * snaps into a styled block once the close arrives — a visible flicker on any
 * code-heavy answer. Closing the dangling fence ourselves makes the parser
 * treat the in-progress text as a code block immediately.
 *
 * Only the unclosed-fence case is handled — that is the dominant visible
 * offender during streaming. When all fences are balanced (well-formed or
 * completed content) the input is returned unchanged, so this is a no-op for
 * finished messages.
 *
 * Inline code spans (single backticks) are intentionally out of scope: they
 * almost always open and close within a single streamed chunk, so they don't
 * produce a sustained flicker.
 *
 * @param content The accumulated markdown received so far.
 * @returns The content with any dangling code fence closed; unchanged when balanced.
 */
export function completeStreamingMarkdown(content: string): string {
  if (!content) return content;

  // A fence is a line whose first non-whitespace characters are ``` or ~~~.
  // Counting line-anchored fences (rather than every ``` occurrence) avoids
  // miscounting triple backticks that appear mid-line inside prose.
  const fencePattern = /^[ \t]*(`{3,}|~{3,})/;

  // Tracks the marker family (` or ~) of the currently open fence, or null when
  // no fence is open. A fence only closes against the same family.
  let openFence: string | null = null;

  for (const line of content.split('\n')) {
    const match = fencePattern.exec(line);
    if (match === null) continue;

    const marker = match[1][0]; // ` or ~
    if (openFence === null) {
      openFence = marker;
    } else if (openFence === marker) {
      openFence = null;
    }
  }

  if (openFence === null) return content;

  const closer = openFence === '`' ? '```' : '~~~';
  const separator = content.endsWith('\n') ? '' : '\n';
  return `${content}${separator}${closer}`;
}
