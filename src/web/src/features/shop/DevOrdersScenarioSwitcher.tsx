import { ClipboardList } from 'lucide-react'
import { cn } from '@/lib/utils'
import {
  getShopOrdersScenarioKey,
  SHOP_ORDERS_SCENARIOS,
  setShopOrdersScenario,
  type ShopOrdersScenarioKey,
} from './clients/mockShopOrdersClient'

/**
 * S38 dev-only orders scenario switcher (F050).
 *
 * Imported conditionally on `import.meta.env.DEV` from `ShopClientsProvider`,
 * so the production build never bundles it: the scenario labels never reach
 * product-facing markup. Selecting a scenario persists it for the reload that
 * follows, then reloads so the new scenario renders from zero.
 *
 * The toolbar stacks above the coupons switcher (bottom-[16rem] start-4) so
 * all seven mock toolbars can be visible at once without overlapping: media
 * (bottom-4 end-4), discovery (bottom-4 start-4), categories
 * (bottom-[4.75rem] start-4), profile (bottom-[8.5rem] start-4),
 * cartLease (bottom-[12.25rem] start-4), coupons (bottom-[16rem] start-4).
 */
export function DevOrdersScenarioSwitcher() {
  const currentKey = getShopOrdersScenarioKey()

  const handleSwitch = (key: ShopOrdersScenarioKey) => {
    setShopOrdersScenario(key)
    window.location.reload()
  }

  return (
    <div
      aria-label="سناریوهای سفارش‌ها (فقط توسعه)"
      className="fixed bottom-[19.75rem] start-4 z-50 flex max-w-[min(24rem,calc(100vw-2rem))] flex-wrap items-center gap-2 rounded-lg border border-border bg-surface p-2 shadow-lg"
    >
      <span className="inline-flex items-center gap-1.5 px-1 text-xs font-bold text-muted-foreground">
        <ClipboardList aria-hidden="true" className="size-3.5" />
        سناریوهای سفارش
      </span>
      {SHOP_ORDERS_SCENARIOS.map((scenario) => (
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
