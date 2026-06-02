import { describe, it, expect } from 'vitest';
import { screen, waitFor, fireEvent } from '@testing-library/react';
import { renderPage } from '@/test/helpers/renderPage';
import PulsePage from './PulsePage';

describe('PulsePage — Foresight reskin (PR 7)', () => {
  it('mounts the Health tab without throwing', async () => {
    renderPage(<PulsePage />);
    // Page heading should be present once render completes.
    await waitFor(
      () => {
        expect(screen.getByText('Pulse')).toBeInTheDocument();
      },
      { timeout: 3000 },
    );
  });

  it('renders the Spend tab with a MetricPanel replacing the budget gauge', async () => {
    renderPage(<PulsePage />);
    await waitFor(() => expect(screen.getByText('Spend')).toBeInTheDocument(), { timeout: 3000 });
    fireEvent.click(screen.getByText('Spend'));

    await waitFor(
      () => {
        // At least one MetricPanel rendered (budget). The decorative
        // GaugeChart it replaced should not be in the DOM.
        expect(screen.getAllByTestId('metric-panel').length).toBeGreaterThanOrEqual(1);
      },
      { timeout: 5000 },
    );

    // Negative assertion — the old ArcGauge / GaugeChart container is gone.
    expect(document.querySelector('[data-testid*="gauge"]')).toBeNull();
  });

  it('renders the Quality tab with a MetricPanel replacing the safety gauge', async () => {
    renderPage(<PulsePage />);
    await waitFor(() => expect(screen.getByText('Quality')).toBeInTheDocument(), { timeout: 3000 });
    fireEvent.click(screen.getByText('Quality'));

    await waitFor(
      () => {
        expect(screen.getAllByTestId('metric-panel').length).toBeGreaterThanOrEqual(1);
      },
      { timeout: 5000 },
    );
    expect(document.querySelector('[data-testid*="gauge"]')).toBeNull();
  });

  it('renders the Activity tab without throwing (HBarLists colour-by-category)', async () => {
    renderPage(<PulsePage />);
    await waitFor(() => expect(screen.getByText('Activity')).toBeInTheDocument(), { timeout: 3000 });
    fireEvent.click(screen.getByText('Activity'));

    // Tab should mount; if HBarList signature regressed, the page would throw.
    await waitFor(
      () => {
        // The page header is still present, confirming no crash unmounted the tree.
        expect(screen.getByText('Pulse')).toBeInTheDocument();
      },
      { timeout: 5000 },
    );
  });
});
