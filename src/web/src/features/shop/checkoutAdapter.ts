/**
 * S28 checkout (F037): the real, thin adapter for B030's compute-only
 * checkout-summary endpoint — the swap-in replacement for F036's
 * `mockCheckoutSummary`. Field names mirror B030's `CheckoutContracts.cs`
 * camelCased; it mirrors `cartAdapter`'s request idiom (8s abort timeout,
 * `ApiUnavailableError` on any network/parse failure) and reads the stored
 * cart id from `cartStorage` (F033) rather than accepting one, since the
 * shopper's cart is already the browser's.
 */
import { ApiUnavailableError } from '@/features/auth/authTypes'
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

const REQUEST_TIMEOUT_MS = 8_000

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return { signal: controller.signal, clear: () => window.clearTimeout(timeoutId) }
}

function mapServerValidation(payload: unknown): Record<string, string> {
  const fallback = { _: 'مقدار واردشده معتبر نیست.' }
  if (typeof payload !== 'object' || payload === null) return fallback
  const errors = (payload as Record<string, unknown>).errors
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
  const cartId = getCartId()
  if (!cartId) throw new EmptyCartError()

  const abort = createRequestAbortSignal()
  let response: Response
  try {
    response = await fetch(`/api/shop/${tenantId}/checkout/summary`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ cartId, ...fields }),
      signal: abort.signal,
    })
  } catch {
    throw new ApiUnavailableError()
  } finally {
    abort.clear()
  }

  if (response.status === 404) throw new EmptyCartError()
  if (response.status === 400) {
    let payload: unknown
    try {
      payload = await response.json()
    } catch {
      throw new ApiUnavailableError()
    }
    throw new CheckoutValidationError(mapServerValidation(payload))
  }
  if (!response.ok) throw new ApiUnavailableError()

  try {
    return (await response.json()) as CheckoutSummaryResponse
  } catch {
    throw new ApiUnavailableError()
  }
}
