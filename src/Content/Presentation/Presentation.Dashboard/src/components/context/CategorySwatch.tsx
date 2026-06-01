import { cn } from '@/lib/utils';
import { CATEGORY_BG_CLASS, type CategoryKey } from '@/lib/categories';

export type CategorySwatchSize = 'xs' | 'sm' | 'md' | 'lg';

interface CategorySwatchProps {
  /** Which category determines the color (uses CATEGORY_BG_CLASS).
   *  Pass `'headroom'` to render the hatched headroom pattern instead. */
  category: CategoryKey | 'headroom';
  size?: CategorySwatchSize;
  className?: string;
}

const SIZE_CLASS: Record<CategorySwatchSize, string> = {
  xs: 'h-2 w-2 rounded-sm',
  sm: 'h-3 w-3 rounded-sm',
  md: 'h-4 w-4 rounded',
  lg: 'h-8 w-8 rounded',
};

/**
 * The colored square that identifies a context category. Used by ContextLegend
 * tiles, ContextDrawer header, and the design-system token grid — all three
 * sites previously inlined slightly different swatch styles, which drifted
 * (h-2 vs h-3 vs h-8, rounded-sm vs rounded). Centralizing here removes that
 * drift class.
 *
 * `category="headroom"` is a special mode that renders the hatched fill from
 * `--cat-hatch` so the headroom card on the design-system page stays inside
 * this primitive instead of using an inline style.
 */
export function CategorySwatch({
  category,
  size = 'sm',
  className,
}: CategorySwatchProps) {
  if (category === 'headroom') {
    return (
      <span
        aria-hidden="true"
        data-testid="category-swatch"
        data-category="headroom"
        data-size={size}
        className={cn(SIZE_CLASS[size], 'shrink-0', className)}
        style={{ background: 'var(--cat-hatch)' }}
      />
    );
  }
  return (
    <span
      aria-hidden="true"
      data-testid="category-swatch"
      data-category={category}
      data-size={size}
      className={cn(SIZE_CLASS[size], 'shrink-0', CATEGORY_BG_CLASS[category], className)}
    />
  );
}
