import { cn } from '@/lib/utils';

type TurnRole = 'user' | 'assistant' | 'tool';

export interface ScrubTurn {
  id: string;
  type: TurnRole;
  /** Tokens added by this turn (delta, not cumulative). Powers the sparkline. */
  tokens: number;
  /** Optional short label shown above each dot ("U1", "A2", "T3"…). */
  label?: string;
}

interface ScrubStripProps {
  turns: ScrubTurn[];
  /** Active turn index. -1 means "current end state" — no halo, last dot bolded. */
  activeIndex: number;
  onScrub: (index: number) => void;
  /** Render a cumulative-token sparkline above the dots. */
  showSparkline?: boolean;
  className?: string;
}

const DOT_FILL_BY_ROLE: Record<TurnRole, string> = {
  user: 'bg-cat-accent',
  assistant: 'bg-cat-messages',
  tool: 'bg-cat-tools',
};

/**
 * Horizontal turn-dot rail. Click any dot to rewind the session to that turn.
 * The active dot is haloed in cobalt; an optional sparkline above plots
 * cumulative tokens-in-context turn by turn.
 *
 * Used inside the hero of `session.html` per HANDOFF.md §4.3.
 */
export function ScrubStrip({
  turns,
  activeIndex,
  onScrub,
  showSparkline = false,
  className,
}: ScrubStripProps) {
  const cumulative = turns.reduce<number[]>((acc, t) => {
    acc.push((acc[acc.length - 1] ?? 0) + t.tokens);
    return acc;
  }, []);
  // Use `|| 1` (not `?? 1`) so all-zero turns don't divide by zero in the
  // sparkline y-coord. The bar gates render on `turns.length > 1`, not on
  // total tokens, so this path is reachable for replay / placeholder turns.
  const maxCumulative = cumulative[cumulative.length - 1] || 1;

  return (
    <div
      data-testid="scrub-strip"
      className={cn('flex flex-col gap-1', className)}
    >
      {showSparkline && turns.length > 1 && (
        <svg
          data-testid="scrub-strip-sparkline"
          viewBox="0 0 100 20"
          preserveAspectRatio="none"
          className="w-full h-5 text-cat-accent"
          aria-hidden="true"
        >
          <path
            d={cumulative
              .map((v, i) => {
                const x = (i / Math.max(1, cumulative.length - 1)) * 100;
                const y = 20 - (v / maxCumulative) * 18 - 1;
                return `${i === 0 ? 'M' : 'L'}${x.toFixed(2)},${y.toFixed(2)}`;
              })
              .join(' ')}
            fill="none"
            stroke="currentColor"
            strokeWidth="1.5"
            strokeLinecap="round"
            strokeLinejoin="round"
            vectorEffect="non-scaling-stroke"
          />
        </svg>
      )}
      <div
        role="group"
        aria-label="Turn scrubber"
        className="flex w-full items-center justify-between gap-1"
      >
        {turns.map((turn, i) => {
          const active = i === activeIndex;
          return (
            <button
              key={turn.id}
              type="button"
              data-testid={`scrub-strip-dot-${i}`}
              data-active={active}
              data-role={turn.type}
              aria-label={`Turn ${i + 1} (${turn.type}, +${turn.tokens.toLocaleString()} tokens)`}
              title={
                turn.label
                  ? `${turn.label} · ${turn.type} · +${turn.tokens.toLocaleString()}`
                  : `${turn.type} · +${turn.tokens.toLocaleString()}`
              }
              onClick={() => onScrub(i)}
              className={cn(
                'relative h-3 w-3 rounded-full transition-transform shrink-0',
                DOT_FILL_BY_ROLE[turn.type],
                active
                  ? 'scale-125 ring-2 ring-cat-accent ring-offset-2 ring-offset-background'
                  : 'opacity-70 hover:opacity-100',
              )}
            />
          );
        })}
      </div>
      {turns.some((t) => t.label) && (
        <div className="flex w-full items-center justify-between gap-1">
          {turns.map((turn) => (
            <span
              key={`${turn.id}-label`}
              className="font-mono text-[10px] text-muted-foreground tabular-nums w-3 text-center"
            >
              {turn.label ?? ''}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
