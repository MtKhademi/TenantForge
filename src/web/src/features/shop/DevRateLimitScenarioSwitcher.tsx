import { Gauge } from 'lucide-react'
import { cn } from '@/lib/utils'
import {
  RATE_LIMIT_SCENARIOS,
  setRateLimitScenario,
  useRateLimitScenario,
} from './rateLimitScenario'

/**
 * S42 dev-only rate-limit scenario switcher (F053).
 *
 * Imported conditionally on `import.meta.env.DEV` from `ShopClientsProvider`,
 * so the production build never bundles it: the scenario labels never reach
 * product-facing markup. Selecting a scenario is applied LIVE (the shared
 * `useRateLimitScenario` store notifies the gated pages) — no reload — and is
 * persisted to sessionStorage so it survives a same-tab reload. Only ONE action
 * can be rate-limited at a time; `خاموش` turns the simulation off.
 *
 * Stacks above the payments switcher (bottom-[27.25rem] start-4) so the ten
 * mock toolbars remain visible without overlapping: media (bottom-4 end-4),
 * discovery (bottom-4 start-4), categories (bottom-[4.75rem]), profile
 * (bottom-[8.5rem]), cartLease (bottom-[12.25rem]), coupons (bottom-[16rem]),
 * orders (bottom-[19.75rem]), orderOperations (bottom-[23.5rem]), payments
 * (bottom-[27.25rem]) — all `start-4`.
 */
export function DevRateLimitScenarioSwitcher() {
  const currentKey = useRateLimitScenario()

  return (
    <div
      aria-label="سناریوهای محدودیت درخواست (فقط توسعه)"
      className="fixed bottom-[31rem] start-4 z-50 flex max-w-[min(24rem,calc(100vw-2rem))] flex-wrap items-center gap-2 rounded-lg border border-border bg-surface p-2 shadow-lg"
    >
      <span className="inline-flex items-center gap-1.5 px-1 text-xs font-bold text-muted-foreground">
        <Gauge aria-hidden="true" className="size-3.5" />
        محدودیت درخواست
      </span>
      {RATE_LIMIT_SCENARIOS.map((scenario) => (
        <button
          key={scenario.key}
          type="button"
          aria-pressed={currentKey === scenario.key}
          onClick={() => setRateLimitScenario(scenario.key)}
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
