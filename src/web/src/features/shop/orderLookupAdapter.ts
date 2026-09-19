import { ApiUnavailableError } from '@/features/auth/authTypes'
import type { OrderLookupResult } from './orderLookupTypes'

const REQUEST_TIMEOUT_MS = 8_000

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return { signal: controller.signal, clear: () => window.clearTimeout(timeoutId) }
}

/**
 * S30 guest order lookup — real, anonymous API data source (F041),
 * replacing F040's mock. No Authorization header is ever sent. A 404
 * (wrong pair, or a wholly nonexistent tracking code) resolves to
 * `null` — the exact same generic outcome for both cases, since B033's
 * real API never distinguishes them either (see this Spec's Context).
 */
export async function lookupOrder(
  tenantId: string,
  trackingCode: string,
  customerPhone: string,
): Promise<OrderLookupResult | null> {
  const abort = createRequestAbortSignal()
  let response: Response
  try {
    response = await fetch(`/api/shop/${tenantId}/orders/lookup`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ trackingCode, customerPhone }),
      signal: abort.signal,
    })
  } catch {
    throw new ApiUnavailableError()
  } finally {
    abort.clear()
  }

  if (response.status === 404) return null
  if (!response.ok) throw new ApiUnavailableError()

  try {
    return (await response.json()) as OrderLookupResult
  } catch {
    throw new ApiUnavailableError()
  }
}
