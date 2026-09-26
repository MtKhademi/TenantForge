import { ApiUnavailableError } from '@/features/auth/authTypes'
import { ShopClientError, isRateLimitedProblem } from '@/features/shop/contracts/shopContract'
import { shopFetchPublic } from '@/features/shop/clients/shopFetch'
import type { OrderLookupResult } from './orderLookupTypes'

/**
 * S30 guest order lookup — anonymous API data source (F041), now routed
 * through the single shared Shop parser (F053). No Authorization header is
 * ever sent. A 404 (wrong pair, or a wholly nonexistent tracking code)
 * resolves to `null` — the exact same generic outcome for both cases, since
 * B033's real API never distinguishes them either.
 *
 * S42 (B046): order lookup is a rate-limited action. A real 429 arrives as a
 * `ShopClientError` with `retryAfterSeconds` (read from the `Retry-After`
 * header by the shared parser) and is rethrown so the page drives its
 * per-action cooldown. Every other non-2xx stays `ApiUnavailableError`, and a
 * network failure is `ApiUnavailableError` — the retry state.
 */
export async function lookupOrder(
  tenantId: string,
  trackingCode: string,
  customerPhone: string,
): Promise<OrderLookupResult | null> {
  try {
    return (await shopFetchPublic(`/api/shop/${tenantId}/orders/lookup`, {
      method: 'POST',
      json: { trackingCode, customerPhone },
    })) as OrderLookupResult
  } catch (error) {
    if (error instanceof ShopClientError) {
      if (error.problem.status === 404) return null
      if (isRateLimitedProblem(error.problem)) throw error
      throw new ApiUnavailableError()
    }
    // ApiUnavailableError (network) or an AbortError from the shared fetch.
    throw error
  }
}
