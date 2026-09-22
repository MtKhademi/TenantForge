import { Layers } from 'lucide-react'
import { cn } from '@/lib/utils'
import {
  getShopCategoryScenarioKey,
  SHOP_CATEGORY_SCENARIOS,
  setShopCategoryScenario,
  type ShopCategoryScenarioKey,
} from './clients/mockShopCategoryClient'

/**
 * S34 dev-only category scenario switcher (F046).
 *
 * This file is imported conditionally on `import.meta.env.DEV` from
 * `ShopClientsProvider`, so the production build never bundles it: the
 * scenario labels below never reach product-facing markup. Selecting a
 * scenario persists it for the page reload that follows, then reloads so the
 * new scenario renders from zero.
 *
 * The toolbar sits above the two corner toolbars (media: bottom-end,
 * discovery: bottom-start) so all three can be visible at once without
 * overlapping.
 */
export function DevCategoryScenarioSwitcher() {
  const currentKey = getShopCategoryScenarioKey()

  const handleSwitch = (key: ShopCategoryScenarioKey) => {
    setShopCategoryScenario(key)
    window.location.reload()
  }

  return (
    <div
      aria-label="سناریوهای دسته‌بندی (فقط توسعه)"
      className="fixed bottom-[4.75rem] start-4 z-50 flex max-w-[min(24rem,calc(100vw-2rem))] flex-wrap items-center gap-2 rounded-lg border border-border bg-surface p-2 shadow-lg"
    >
      <span className="inline-flex items-center gap-1.5 px-1 text-xs font-bold text-muted-foreground">
        <Layers aria-hidden="true" className="size-3.5" />
        سناریوهای دسته‌بندی
      </span>
      {SHOP_CATEGORY_SCENARIOS.map((scenario) => (
        <button
          key={scenario.key}
          type="button"
          aria-pressed={currentKey === scenario.key}
          onClick={() => handleSwitch(scenario.key)}
          className={cn(
            'rounded-md border px-2 py-1 text-[11px] font-medium transition-colors',
            currentKey === scenario.key
              ? 'border-primary bg-primary/10 text-primary'
              : 'border-border bg-surface text-muted-foreground hover:bg-muted',
          )}
        >
          {scenario.label}
        </button>
      ))}
    </div>
  )
}
