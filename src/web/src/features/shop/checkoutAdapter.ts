/**
 * S28 checkout (F037): the thin adapter for B030's compute-only
 * checkout-summary endpoint, now routed through the single shared Shop parser
 * (F053). It mirrors `cartAdapter`'s request idiom and reads the stored cart id
 * from `cartStorage` (F033) rather than accepting one, since the shopper's cart
 * is already the browser's.
 *
 * Outcomes are unchanged, EXCEPT the S42/B046 addition: a real 429 (from the
 * `shop-checkout-order` policy on this route) is rethrown as a `ShopClientError`
 * carrying `retryAfterSeconds` (read from the `Retry-After` header by the shared
 * parser) so the page drives its per-action cooldown. `400` is still a
 * `CheckoutValidationError`, `404`/no-cart is still an `EmptyCartError`, and any
 * other failure is still an `ApiUnavailableError`.
 */
import { ApiUnavailableError } from '@/features/auth/authTypes'
import { ShopClientError, isRateLimitedProblem } from '@/features/shop/contracts/shopContract'
import { shopFetchPublic } from '@/features/shop/clients/shopFetch'
import { getCartId } from './cartStorage'

export type CheckoutSummaryRequest = {
  cartId: string
  shippingProvince: string
  shippingCity: string
  shippingAddressLine: string
  shippingPostalCode: string
  couponCode: string | null
}

export type CheckoutSummaryResponse = {
  subTotal: number
  discountAmount: number
  shippingCost: number
  grandTotal: number
}

export class CheckoutValidationError extends Error {
  fieldErrors: Record<string, string>

  constructor(fieldErrors: Record<string, string>) {
    super('اطلاعات وارد شده معتبر نیست.')
    this.fieldErrors = fieldErrors
    this.name = 'CheckoutValidationError'
  }
}

export class EmptyCartError extends Error {
  constructor(message = 'سبد خرید یافت نشد یا خالی است.') {
    super(message)
    this.name = 'EmptyCartError'
  }
}

function mapServerValidation(problem: unknown): Record<string, string> {
  const fallback = { _: 'مقدار واردشده معتبر نیست.' }
  if (typeof problem !== 'object' || problem === null) return fallback
  const errors = (problem as { errors?: unknown }).errors
  if (typeof errors !== 'object' || errors === null) return fallback
  const mapped: Record<string, string> = {}
  for (const [field, value] of Object.entries(errors as Record<string, unknown>)) {
    if (Array.isArray(value) && typeof value[0] === 'string') mapped[field] = value[0]
  }
  return Object.keys(mapped).length > 0 ? mapped : fallback
}

export async function fetchCheckoutSummary(
  tenantId: string,
  fields: Omit<CheckoutSummaryRequest, 'cartId'>,
): Promise<CheckoutSummaryResponse> {
  const cartId = getCartId(tenantId)
  if (!cartId) throw new EmptyCartError()

  try {
    return (await shopFetchPublic(`/api/shop/${tenantId}/checkout/summary`, {
      method: 'POST',
      json: { cartId, ...fields },
    })) as CheckoutSummaryResponse
  } catch (error) {
    if (error instanceof ShopClientError) {
      if (error.problem.status === 404) throw new EmptyCartError()
      if (error.problem.status === 400) {
        throw new CheckoutValidationError(mapServerValidation(error.problem))
      }
      // S42/B046: a real 429 is rethrown for the per-action cooldown.
      if (isRateLimitedProblem(error.problem)) throw error
      throw new ApiUnavailableError()
    }
    // ApiUnavailableError (network) or an AbortError from the shared fetch.
    throw error
  }
}
