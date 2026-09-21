import { Search } from 'lucide-react'
import { cn } from '@/lib/utils'
import {
  getShopDiscoveryScenarioKey,
  SHOP_DISCOVERY_SCENARIOS,
  setShopDiscoveryScenario,
  type ShopDiscoveryScenarioKey,
} from './clients/mockShopDiscoveryClient'

/**
 * S33 dev-only discovery scenario switcher (F045).
 *
 * This file is imported conditionally on `import.meta.env.DEV` from
 * `ShopClientsProvider`, so the production build never bundles it: the
 * scenario labels below never reach product-facing markup. Selecting a
 * scenario persists it for the page reload that follows, then reloads so the
 * new scenario renders from zero.
 */
export function DevDiscoveryScenarioSwitcher() {
  const currentKey = getShopDiscoveryScenarioKey()

  const handleSwitch = (key: ShopDiscoveryScenarioKey) => {
    setShopDiscoveryScenario(key)
    window.location.reload()
  }

  return (
    <div
      aria-label="سناریوهای کشف فروشگاه (فقط توسعه)"
      className="fixed bottom-4 start-4 z-50 flex max-w-[min(24rem,calc(100vw-2rem))] flex-wrap items-center gap-2 rounded-lg border border-border bg-surface p-2 shadow-lg"
    >
      <span className="inline-flex items-center gap-1.5 px-1 text-xs font-bold text-muted-foreground">
        <Search aria-hidden="true" className="size-3.5" />
        سناریوهای کشف
      </span>
      {SHOP_DISCOVERY_SCENARIOS.map((scenario) => (
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
