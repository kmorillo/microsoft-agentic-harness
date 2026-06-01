import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { CategorySwatch } from './CategorySwatch';

describe('CategorySwatch', () => {
  it('renders a category swatch with the matching bg class', () => {
    render(<CategorySwatch category="tools" />);
    const el = screen.getByTestId('category-swatch');
    expect(el).toHaveAttribute('data-category', 'tools');
    expect(el.className).toContain('bg-cat-tools');
  });

  it('renders the headroom hatched fill when category="headroom"', () => {
    render(<CategorySwatch category="headroom" />);
    const el = screen.getByTestId('category-swatch');
    expect(el).toHaveAttribute('data-category', 'headroom');
    expect(el.style.background).toContain('var(--cat-hatch)');
  });

  it('applies the expected size class for each variant', () => {
    const { rerender } = render(<CategorySwatch category="system" size="xs" />);
    expect(screen.getByTestId('category-swatch').className).toContain('h-2');
    rerender(<CategorySwatch category="system" size="lg" />);
    expect(screen.getByTestId('category-swatch').className).toContain('h-8');
  });

  it('appends custom className', () => {
    render(<CategorySwatch category="agents" className="mt-2" />);
    expect(screen.getByTestId('category-swatch').className).toContain('mt-2');
  });
});
