import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MetricPanel } from './MetricPanel';
import type { MetricDataPoint } from '@/api/types';

function dp(values: number[]): MetricDataPoint[] {
  return values.map((v, i) => ({ timestamp: i, value: v.toString() }));
}

describe('MetricPanel', () => {
  it('renders the title and value', () => {
    render(<MetricPanel title="Budget" value="42% used" />);
    expect(screen.getByText('Budget')).toBeInTheDocument();
    expect(screen.getByTestId('metric-panel-value').textContent).toBe('42% used');
  });

  it('drops the sparkline when sparklineData is empty or missing', () => {
    const { rerender } = render(<MetricPanel title="t" value="0" />);
    expect(screen.queryByTestId('metric-panel-sparkline')).toBeNull();

    rerender(<MetricPanel title="t" value="0" sparklineData={[]} />);
    expect(screen.queryByTestId('metric-panel-sparkline')).toBeNull();
  });

  it('renders the sparkline when sparklineData has at least one point', () => {
    render(
      <MetricPanel title="t" value="0" sparklineData={dp([1, 2, 3, 4])} />,
    );
    expect(screen.getByTestId('metric-panel-sparkline')).toBeInTheDocument();
  });

  it('drops the description when not provided', () => {
    render(<MetricPanel title="t" value="0" />);
    expect(screen.queryByTestId('metric-panel-description')).toBeNull();
  });

  it('renders the description when provided', () => {
    render(<MetricPanel title="t" value="0" description="of $10K budget" />);
    expect(screen.getByTestId('metric-panel-description').textContent).toBe(
      'of $10K budget',
    );
  });

  describe('delta pill', () => {
    it('omits the pill when no delta is provided', () => {
      render(<MetricPanel title="t" value="0" />);
      expect(screen.queryByTestId('metric-panel-delta')).toBeNull();
    });

    it('renders a positive delta on a "up is good" metric as good (green tone)', () => {
      render(
        <MetricPanel
          title="t"
          value="0"
          delta={{ pct: 3.2, positiveDirection: 'up' }}
        />,
      );
      const pill = screen.getByTestId('metric-panel-delta');
      expect(pill.getAttribute('data-tone')).toBe('good');
      expect(pill.textContent).toContain('+3.2%');
      expect(pill.textContent).toContain('▲');
    });

    it('renders a positive delta on a "down is good" metric as bad (red tone)', () => {
      // Cost increased — bad.
      render(
        <MetricPanel
          title="t"
          value="0"
          delta={{ pct: 3.2, positiveDirection: 'down' }}
        />,
      );
      const pill = screen.getByTestId('metric-panel-delta');
      expect(pill.getAttribute('data-tone')).toBe('bad');
    });

    it('renders a negative delta on a "down is good" metric as good', () => {
      // Cost decreased — good.
      render(
        <MetricPanel
          title="t"
          value="0"
          delta={{ pct: -2.5, positiveDirection: 'down' }}
        />,
      );
      const pill = screen.getByTestId('metric-panel-delta');
      expect(pill.getAttribute('data-tone')).toBe('good');
      expect(pill.textContent).toContain('-2.5%');
      expect(pill.textContent).toContain('▼');
    });

    it('renders a zero delta as neutral', () => {
      render(
        <MetricPanel
          title="t"
          value="0"
          delta={{ pct: 0, positiveDirection: 'up' }}
        />,
      );
      expect(
        screen.getByTestId('metric-panel-delta').getAttribute('data-tone'),
      ).toBe('neutral');
    });
  });

  describe('status accent', () => {
    it('reflects status via data-status', () => {
      const { rerender } = render(
        <MetricPanel title="t" value="0" status="ok" />,
      );
      expect(
        screen.getByTestId('metric-panel').getAttribute('data-status'),
      ).toBe('ok');

      rerender(<MetricPanel title="t" value="0" status="critical" />);
      expect(
        screen.getByTestId('metric-panel').getAttribute('data-status'),
      ).toBe('critical');
    });
  });

  describe('category accent', () => {
    it('exposes the category on the panel for theming', () => {
      render(<MetricPanel title="t" value="0" category="tools" />);
      expect(
        screen.getByTestId('metric-panel').getAttribute('data-category'),
      ).toBe('tools');
    });
  });
});
