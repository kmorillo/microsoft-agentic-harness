import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ContextBar } from './ContextBar';
import type { CategoryBreakdown } from '@/lib/categories';

const sample: CategoryBreakdown = {
  system: 10_000,
  agents: 20_000,
  skills: 5_000,
  tools: 15_000,
  mcp: 0,
  messages: 50_000,
};

describe('ContextBar', () => {
  it('renders one segment per non-zero category', () => {
    render(<ContextBar breakdown={sample} budget={200_000} />);
    expect(screen.getByTestId('context-bar-segment-system')).toBeInTheDocument();
    expect(screen.getByTestId('context-bar-segment-agents')).toBeInTheDocument();
    expect(screen.getByTestId('context-bar-segment-skills')).toBeInTheDocument();
    expect(screen.getByTestId('context-bar-segment-tools')).toBeInTheDocument();
    expect(screen.getByTestId('context-bar-segment-messages')).toBeInTheDocument();
  });

  it('omits zero-token categories', () => {
    render(<ContextBar breakdown={sample} budget={200_000} />);
    expect(screen.queryByTestId('context-bar-segment-mcp')).not.toBeInTheDocument();
  });

  it('renders proportional segment widths via flex-basis', () => {
    render(<ContextBar breakdown={sample} budget={200_000} />);
    const messages = screen.getByTestId('context-bar-segment-messages');
    // 50_000 / 200_000 = 25%
    expect(messages.style.flexBasis).toBe('25%');
    const system = screen.getByTestId('context-bar-segment-system');
    // 10_000 / 200_000 = 5%
    expect(system.style.flexBasis).toBe('5%');
  });

  it('renders hatched headroom when used < budget', () => {
    render(<ContextBar breakdown={sample} budget={200_000} />);
    const headroom = screen.getByTestId('context-bar-headroom');
    expect(headroom).toBeInTheDocument();
    // 200_000 - 100_000 used = 100_000 / 200_000 = 50%
    expect(headroom.style.flexBasis).toBe('50%');
    expect(headroom.style.background).toContain('var(--cat-hatch)');
  });

  it('omits headroom when used >= budget', () => {
    const full: CategoryBreakdown = {
      system: 50_000, agents: 50_000, skills: 50_000,
      tools: 25_000, mcp: 12_500, messages: 12_500,
    };
    render(<ContextBar breakdown={full} budget={200_000} />);
    expect(screen.queryByTestId('context-bar-headroom')).not.toBeInTheDocument();
  });

  it('fires onSegmentClick with the clicked category', () => {
    const onClick = vi.fn();
    render(<ContextBar breakdown={sample} budget={200_000} onSegmentClick={onClick} />);
    fireEvent.click(screen.getByTestId('context-bar-segment-tools'));
    expect(onClick).toHaveBeenCalledWith('tools');
  });

  it('renders segments as buttons only when interactive', () => {
    const { rerender } = render(<ContextBar breakdown={sample} budget={200_000} />);
    expect(screen.getByTestId('context-bar-segment-system').tagName).toBe('DIV');

    rerender(<ContextBar breakdown={sample} budget={200_000} onSegmentClick={() => {}} />);
    expect(screen.getByTestId('context-bar-segment-system').tagName).toBe('BUTTON');
  });

  it('dims non-active segments when activeCategory is set', () => {
    render(
      <ContextBar
        breakdown={sample}
        budget={200_000}
        activeCategory="tools"
        onSegmentClick={() => {}}
      />,
    );
    expect(screen.getByTestId('context-bar-segment-tools').className).toContain('opacity-100');
    expect(screen.getByTestId('context-bar-segment-system').className).toContain('opacity-30');
  });

  it('applies size attribute to the outer rail', () => {
    render(<ContextBar breakdown={sample} budget={200_000} size="lg" />);
    expect(screen.getByTestId('context-bar')).toHaveAttribute('data-size', 'lg');
    expect(screen.getByTestId('context-bar').className).toContain('h-6');
  });

  it('applies a default aria-label summarizing usage', () => {
    render(<ContextBar breakdown={sample} budget={200_000} />);
    expect(screen.getByTestId('context-bar')).toHaveAttribute(
      'aria-label',
      'Context window: 100,000 of 200,000 tokens used',
    );
  });

  it('renders an inert rail when budget is 0', () => {
    render(<ContextBar breakdown={sample} budget={0} />);
    expect(screen.getByTestId('context-bar')).toHaveAttribute('data-budget-unknown', 'true');
    expect(screen.queryByTestId('context-bar-segment-system')).not.toBeInTheDocument();
    expect(screen.queryByTestId('context-bar-headroom')).not.toBeInTheDocument();
  });

  it('renders an inert rail when budget is non-finite', () => {
    render(<ContextBar breakdown={sample} budget={NaN} />);
    expect(screen.getByTestId('context-bar')).toHaveAttribute('data-budget-unknown', 'true');
    expect(screen.queryByTestId('context-bar-segment-system')).not.toBeInTheDocument();
  });

  it('skips segments whose token count is NaN', () => {
    const dirty = { ...sample, tools: Number.NaN };
    render(<ContextBar breakdown={dirty} budget={200_000} />);
    expect(screen.queryByTestId('context-bar-segment-tools')).not.toBeInTheDocument();
    expect(screen.getByTestId('context-bar-segment-system')).toBeInTheDocument();
  });

  it('marks the rail over-budget and scales segments against used when used > budget', () => {
    // sum(sample) = 100,000; budget = 50,000 → over-budget
    render(<ContextBar breakdown={sample} budget={50_000} />);
    const bar = screen.getByTestId('context-bar');
    expect(bar).toHaveAttribute('data-over-budget', 'true');
    expect(bar.className).toContain('ring-destructive');
    expect(screen.queryByTestId('context-bar-headroom')).not.toBeInTheDocument();
    // messages = 50_000 / used(100_000) = 50%
    expect(screen.getByTestId('context-bar-segment-messages').style.flexBasis).toBe('50%');
  });
});
