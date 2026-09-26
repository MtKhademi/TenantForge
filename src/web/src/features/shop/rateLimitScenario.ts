import { useSyncExternalStore } from 'react'
import {
  RATE_LIMITED_RETRY_AFTER_SECONDS,
  rateLimitedProblemSchema,
  ShopClientError,
} from './contracts/shopContract'

/**
 * S42 (B046) — the ONE dev-only rate-limit scenario switch (F053).
 *
 * A real rate limit can only be forced on demand when the request goes through
 * a mock client (cart mutations, payment initiation). The other three
 * sensitive actions were already connected to the real API in earlier tasks
 * (order lookup F041, checkout summary F037, order creation F039), so this
 * module also provides a page-level throttle gate the pages call right before
 * firing that action: while the scenario is active the gate THROWS the B046 429
 * instead of letting the request out. The page then handles the 429 exactly the
 * same way a real one from the shared parser would.
 *
 * EVERYTHING here is a no-op in a production build: `isRateLimitScenarioDevEnabled`
 * is `import.meta.env.DEV`, so a production bundle never activates a scenario,
 * never throws a simulated 429, and never renders the switcher. Scenario names
 * live here and in the dev-only switcher; they never reach product markup.
 */

/** The five sensitive actions B046's named policies cover. */
export type RateLimitedAction = 'lookup' | 'cart' | 'checkout' | 'order' | 'payment'

/** The selectable scenarios. `off` (the default) never rate-limits. */
export type RateLimitScenario = 'off' | RateLimitedAction

export interface RateLimitScenarioOption {
  key: RateLimitScenario
  label: string
}

export const RATE_LIMIT_SCENARIOS: readonly RateLimitScenarioOption[] = [
  { key: 'off', label: 'خاموش (پیش‌فرض)' },
  { key: 'lookup', label: '۴۲۹ در پیگیری سفارش' },
  { key: 'cart', label: '۴۲۹ در سبد خرید' },
  { key: 'checkout', label: '۴۲۹ در تسویه حساب' },
  { key: 'order', label: '۴۲۹ در ثبت سفارش' },
  { key: 'payment', label: '۴۲۹ در پرداخت' },
] as const

const SCENARIO_STORAGE_KEY = 'tfRateLimitScenario'

/** True only in a local dev build. The switcher, the gate and the mocks all check this. */
export function isRateLimitScenarioDevEnabled(): boolean {
  return import.meta.env.DEV
}

function readStoredScenario(): RateLimitScenario {
  try {
    const stored = window.sessionStorage.getItem(SCENARIO_STORAGE_KEY)
    if (RATE_LIMIT_SCENARIOS.some((entry) => entry.key === stored)) {
      return stored as RateLimitScenario
    }
  } catch {
    // Storage can be unavailable; the default (off) wins.
  }
  return 'off'
}

let activeScenario: RateLimitScenario = readStoredScenario()
const scenarioListeners = new Set<() => void>()

export function getRateLimitScenario(): RateLimitScenario {
  return activeScenario
}

export function setRateLimitScenario(scenario: RateLimitScenario): void {
  if (!RATE_LIMIT_SCENARIOS.some((entry) => entry.key === scenario)) {
    throw new Error(`Unknown rate-limit scenario: ${scenario}`)
  }
  activeScenario = scenario
  try {
    window.sessionStorage.setItem(SCENARIO_STORAGE_KEY, scenario)
  } catch {
    // Non-fatal: the scenario still applies for this page lifetime.
  }
  scenarioListeners.forEach((listener) => listener())
}

function subscribeToRateLimitScenario(listener: () => void): () => void {
  scenarioListeners.add(listener)
  return () => {
    scenarioListeners.delete(listener)
  }
}

/**
 * Reactively tracks the active scenario so the switcher and the gated pages
 * update the moment a scenario is picked (no reload). Returns `'off'` in a
 * production build, so a production page never enables a cooldown. The hook is
 * called unconditionally (rules of hooks); the production branch only changes
 * the returned value.
 */
export function useRateLimitScenario(): RateLimitScenario {
  const scenario = useSyncExternalStore(subscribeToRateLimitScenario, getRateLimitScenario, getRateLimitScenario)
  return isRateLimitScenarioDevEnabled() ? scenario : 'off'
}

/**
 * The B046 rate-limit error the cooldown UI drives from. It carries the exact
 * generic, Persian-safe detail and the positive-integer `retryAfterSeconds` the
 * contract requires, and is the SAME object shape the shared parser produces
 * from a real `Retry-After` response — so a page cannot tell the simulated 429
 * from a real one.
 */
export function buildRateLimitedError(): ShopClientError {
  return new ShopClientError(
    rateLimitedProblemSchema.parse({
      status: 429,
      type: 'shop_rate_limit',
      title: 'درخواست‌های خیلی زیاد',
      detail: 'درخواست‌های شما زیاد بود. لطفاً کمی صبر کنید و دوباره تلاش کنید.',
      retryAfterSeconds: RATE_LIMITED_RETRY_AFTER_SECONDS,
    }),
  )
}

/**
 * The page-level throttle gate. Pages call this right before firing a sensitive
 * action; when (and only when) a dev scenario targets that action it throws the
 * B046 429 so the page runs its uniform 429 cooldown path. In production this
 * always returns without throwing, so the real request always goes out.
 */
export function assertNotRateLimited(action: RateLimitedAction): void {
  if (!isRateLimitScenarioDevEnabled()) return
  if (activeScenario === action) throw buildRateLimitedError()
}
