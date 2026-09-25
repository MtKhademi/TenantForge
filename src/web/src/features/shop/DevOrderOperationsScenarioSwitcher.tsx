import { Workflow } from 'lucide-react'
import { cn } from '@/lib/utils'
import {
  getOrderOperationsScenarioKey,
  ORDER_OPERATIONS_SCENARIOS,
  setOrderOperationsScenario,
  type OrderOperationsScenarioKey,
} from './clients/mockShopOrderOperationsClient'

/**
 * S39 dev-only order-operations scenario switcher (F051).
 *
 * Imported conditionally on `import.meta.env.DEV` from `ShopClientsProvider`,
 * so the production build never bundles it: the scenario labels never reach
 * product-facing markup. Selecting a scenario persists it for the reload that
 * follows, then reloads so the new scenario renders from zero.
 *
 * This switcher only controls the two mutation-specific 409 outcomes (stale
 * version, invalid transition) that are unique to `changeStatus`. The shared
 * transport/permission states (`unavailable`, `forbidden`) come from the
 * ORDERS switcher (F050) since both slots read the same seeded fixtures.
 *
 * Stacks above the orders switcher (bottom-[19.75rem] start-4) so all eight
 * mock toolbars remain visible without overlapping: media (bottom-4 end-4),
 * discovery (bottom-4 start-4), categories (bottom-[4.75rem] start-4),
 * profile (bottom-[8.5rem] start-4), cartLease (bottom-[12.25rem] start-4),
 * coupons (bottom-[16rem] start-4), orders (bottom-[19.75rem] start-4).
 */
export function DevOrderOperationsScenarioSwitcher() {
  const currentKey = getOrderOperationsScenarioKey()

  const handleSwitch = (key: OrderOperationsScenarioKey) => {
    setOrderOperationsScenario(key)
    window.location.reload()
  }

  return (
    <div
      aria-label="سناریوهای عملیات سفارش (فقط توسعه)"
      className="fixed bottom-[23.5rem] start-4 z-50 flex max-w-[min(24rem,calc(100vw-2rem))] flex-wrap items-center gap-2 rounded-lg border border-border bg-surface p-2 shadow-lg"
    >
      <span className="inline-flex items-center gap-1.5 px-1 text-xs font-bold text-muted-foreground">
        <Workflow aria-hidden="true" className="size-3.5" />
        سناریوهای عملیات سفارش
      </span>
      {ORDER_OPERATIONS_SCENARIOS.map((scenario) => (
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
