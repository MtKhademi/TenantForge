import { Search, SlidersHorizontal, Tag } from 'lucide-react'
import type { DiscoverySort } from '@/features/shop/contracts/discoveryContract'
import { TextInput } from '@/components/ui/TextInput'

/** Sort options, in the order they should render in the select. */
const SORT_OPTIONS: ReadonlyArray<{ value: DiscoverySort; label: string }> = [
  { value: 'newest', label: 'جدیدترین' },
  { value: 'price-asc', label: 'ارزان‌ترین' },
  { value: 'price-desc', label: 'گران‌ترین' },
  { value: 'name', label: 'نام (الفبا)' },
] as const

/**
 * S33 storefront discovery filters (F045).
 *
 * Presentational only: every control is fully controlled by the page, which
 * owns the URL as the single source of truth. The mock's resolvable categories
 * are passed in (`categories`) so the options always match what the mock can
 * filter by — no hardcoded slugs in this component.
 */
export type StorefrontFiltersProps = {
  searchValue: string
  onSearchChange: (value: string) => void
  categorySlug: string
  onCategoryChange: (slug: string) => void
  sort: DiscoverySort
  onSortChange: (sort: DiscoverySort) => void
  saleOnly: boolean
  onSaleOnlyChange: (value: boolean) => void
  categories: ReadonlyArray<{ slug: string; label: string }>
  /** Disables every control (used while a request is in flight). */
  disabled?: boolean
}

export function StorefrontFilters({
  searchValue,
  onSearchChange,
  categorySlug,
  onCategoryChange,
  sort,
  onSortChange,
  saleOnly,
  onSaleOnlyChange,
  categories,
  disabled = false,
}: StorefrontFiltersProps) {
  return (
    <div className="space-y-3 rounded-xl border border-border bg-surface p-4 shadow-soft">
      <div className="flex items-center gap-2">
        <SlidersHorizontal aria-hidden="true" className="size-4 text-muted-foreground" />
        <h2 className="text-sm font-semibold">جست‌وجو و فیلتر</h2>
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        <div className="sm:col-span-2">
          <label htmlFor="storefront-search" className="mb-1 block text-xs text-muted-foreground">
            جست‌وجو در نام محصول
          </label>
          <div className="relative">
            <Search aria-hidden="true" className="pointer-events-none absolute inset-y-0 start-3 my-auto size-4 text-muted-foreground" />
            <TextInput
              id="storefront-search"
              type="search"
              value={searchValue}
              disabled={disabled}
              onChange={(event) => onSearchChange(event.target.value)}
              placeholder="نام محصول را تایپ کنید"
              className="ps-9"
              autoComplete="off"
            />
          </div>
        </div>

        <div>
          <label htmlFor="storefront-category" className="mb-1 block text-xs text-muted-foreground">
            دسته‌بندی
          </label>
          <select
            id="storefront-category"
            value={categorySlug}
            disabled={disabled}
            onChange={(event) => onCategoryChange(event.target.value)}
            className="min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-base text-foreground transition-colors focus-visible:border-ring disabled:cursor-not-allowed disabled:opacity-60 md:text-sm"
          >
            <option value="">همه دسته‌بندی‌ها</option>
            {categories.map((category) => (
              <option key={category.slug} value={category.slug}>
                {category.label}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="storefront-sort" className="mb-1 block text-xs text-muted-foreground">
            مرتب‌سازی
          </label>
          <select
            id="storefront-sort"
            value={sort}
            disabled={disabled}
            onChange={(event) => onSortChange(event.target.value as DiscoverySort)}
            className="min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-base text-foreground transition-colors focus-visible:border-ring disabled:cursor-not-allowed disabled:opacity-60 md:text-sm"
          >
            {SORT_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
      </div>

      <label className="flex w-fit cursor-pointer items-center gap-2 rounded-md border border-border px-3 py-2 text-sm font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60">
        <input
          type="checkbox"
          checked={saleOnly}
          disabled={disabled}
          onChange={(event) => onSaleOnlyChange(event.target.checked)}
          className="size-4 rounded border-input bg-surface accent-[var(--primary)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        />
        <Tag aria-hidden="true" className="size-4 text-muted-foreground" />
        فقط تخفیف‌دارها
      </label>
    </div>
  )
}
