import { Store } from 'lucide-react'
import { cn } from '@/lib/utils'
import {
  getShopProfileScenarioKey,
  SHOP_PROFILE_SCENARIOS,
  setShopProfileScenario,
  type ShopProfileScenarioKey,
} from './clients/mockShopProfileClient'

/**
 * S35 dev-only profile scenario switcher (F047).
 *
 * This file is imported conditionally on `import.meta.env.DEV` from
 * `ShopClientsProvider`, so the production build never bundles it: the
 * scenario labels below never reach product-facing markup. Selecting a
 * scenario persists it for the page reload that follows, then reloads so the
 * new scenario renders from zero.
 *
 * The toolbar stacks above the categories switcher (bottom-[4.75rem] start-4)
 * so all four profile-related toolbars can be visible at once without
 * overlapping: media (bottom-4 end-4), discovery (bottom-4 start-4),
 * categories (bottom-[4.75rem] start-4). Profile takes the next tier up.
 */
export function DevProfileScenarioSwitcher() {
  const currentKey = getShopProfileScenarioKey()

  const handleSwitch = (key: ShopProfileScenarioKey) => {
    setShopProfileScenario(key)
    window.location.reload()
  }

  return (
    <div
      aria-label="سناریوهای هویت فروشگاه (فقط توسعه)"
      className="fixed bottom-[8.5rem] start-4 z-50 flex max-w-[min(24rem,calc(100vw-2rem))] flex-wrap items-center gap-2 rounded-lg border border-border bg-surface p-2 shadow-lg"
    >
      <span className="inline-flex items-center gap-1.5 px-1 text-xs font-bold text-muted-foreground">
        <Store aria-hidden="true" className="size-3.5" />
        سناریوهای هویت فروشگاه
      </span>
      {SHOP_PROFILE_SCENARIOS.map((scenario) => (
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
