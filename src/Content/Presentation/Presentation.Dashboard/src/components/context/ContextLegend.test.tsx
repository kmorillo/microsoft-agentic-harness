import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ContextLegend } from './ContextLegend';
import type { CategoryBreakdown } from '@/lib/categories';

const sample: CategoryBreakdown = {
  system: 8_200,
  agents: 4_100,
  skills: 6_000,
  tools: 3_500,
  mcp: 1_200,
  messages: 12_000,
};

describe('ContextLegend', () => {
  it('renders six tiles in load-bearing order', () => {
    render(<ContextLegend breakdown={sample} />);
    expect(screen.getByTestId('context-legend-tile-system')).toBeInTheDocument();
    expect(screen.getByTestId('context-legend-tile-agents')).toBeInTheDocument();
    expect(screen.getByTestId('context-legend-tile-skills')).toBeInTheDocument();
    expect(screen.getByTestId('context-legend-tile-tools')).toBeInTheDocument();
    expect(screen.getByTestId('context-legend-tile-mcp')).toBeInTheDocument();
    expect(screen.getByTestId('context-legend-tile-messages')).toBeInTheDocument();
  });

  it('renders token counts with thousands separators', () => {
    render(<ContextLegend breakdown={sample} />);
    expect(screen.getByTestId('context-legend-tile-messages')).toHaveTextContent('12,000');
  });

  it('renders % of total when showPercent is true', () => {
    render(<ContextLegend breakdown={sample} showPercent />);
    // 8200 / 35000 total ≈ 23.4%
    expect(screen.getByTestId('context-legend-tile-system')).toHaveTextContent('23.4%');
  });

  it('hides percent when showPercent is false', () => {
    render(<ContextLegend breakdown={sample} showPercent={false} />);
    expect(screen.getByTestId('context-legend-tile-system')).not.toHaveTextContent('%');
  });

  it('renders tiles as div when no onSelect provided', () => {
    render(<ContextLegend breakdown={sample} />);
    expect(screen.getByTestId('context-legend-tile-system').tagName).toBe('DIV');
  });

  it('renders tiles as buttons when onSelect provided', () => {
    render(<ContextLegend breakdown={sample} onSelect={() => {}} />);
    expect(screen.getByTestId('context-legend-tile-system').tagName).toBe('BUTTON');
  });

  it('marks active tile with data-active=true', () => {
    render(
      <ContextLegend breakdown={sample} activeCategory="tools" onSelect={() => {}} />,
    );
    expect(screen.getByTestId('context-legend-tile-tools')).toHaveAttribute(
      'data-active',
      'true',
    );
    expect(screen.getByTestId('context-legend-tile-system')).toHaveAttribute(
      'data-active',
      'false',
    );
  });

  it('fires onSelect with category on inactive tile click', () => {
    const onSelect = vi.fn();
    render(<ContextLegend breakdown={sample} onSelect={onSelect} />);
    fireEvent.click(screen.getByTestId('context-legend-tile-tools'));
    expect(onSelect).toHaveBeenCalledWith('tools');
  });

  it('fires onSelect with null when clicking the already-active tile (toggle)', () => {
    const onSelect = vi.fn();
    render(
      <ContextLegend breakdown={sample} activeCategory="tools" onSelect={onSelect} />,
    );
    fireEvent.click(screen.getByTestId('context-legend-tile-tools'));
    expect(onSelect).toHaveBeenCalledWith(null);
  });
});
