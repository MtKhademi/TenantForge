import { FlaskConical } from 'lucide-react'
import { cn } from '@/lib/utils'
import { SecondaryButton } from '@/components/ui/Button'
import {
  getShopMediaScenarioKey,
  SHOP_MEDIA_SCENARIOS,
  setShopMediaScenario,
  type ShopMediaScenarioKey,
} from './clients/mockShopMediaClient'

/**
 * S32 dev-only mock scenario switcher (F044).
 *
 * This file is imported *conditionally* on `import.meta.env.DEV` from
 * `ShopClientsProvider`, so the production build never bundles it: the
 * scenario labels below (which mention the mock's named states) can never
 * reach production markup. Selecting a scenario re-seeds the mock
 * deterministically and reloads the page so the fresh state renders from
 * zero — the same path a first visit to the page would take.
 */
export function DevMediaScenarioSwitcher() {
  const currentKey = getShopMediaScenarioKey()

  const handleSwitch = (key: ShopMediaScenarioKey) => {
    setShopMediaScenario(key)
    window.location.reload()
  }

  return (
    <div
      aria-label="ابزار سناریوهای نمایشی (فقط توسعه)"
      className="fixed bottom-4 end-4 z-50 flex max-w-[min(24rem,calc(100vw-2rem))] flex-wrap items-center gap-2 rounded-lg border border-border bg-surface p-2 shadow-lg"
    >
      <span className="inline-flex items-center gap-1.5 px-1 text-xs font-bold text-muted-foreground">
        <FlaskConical aria-hidden="true" className="size-3.5" />
        سناریوهای نمایشی
      </span>
      {SHOP_MEDIA_SCENARIOS.map((scenario) => (
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
