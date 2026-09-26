/**
 * S29 order + sandbox payment (F039), now routed through the single shared Shop
 * parser (F053). Field names mirror B031's `OrderContracts.cs` and B032's
 * `PaymentContracts.cs` camelCased; it reads the stored cart id from
 * `cartStorage` (F033), clearing it after a successful order — B031 consumes the
 * cart server-side as part of the order transaction, so the client must not keep
 * pointing at it.
 *
 * S42 (B046): order creation is a rate-limited action (`shop-checkout-order`).
 * A real 429 is rethrown as a `ShopClientError` carrying `retryAfterSeconds`
 * (read from the `Retry-After` header by the shared parser) so the page drives
 * its per-action cooldown. Every other non-2xx stays an
 * `OrderCreationFailedError`, and a network failure is an `ApiUnavailableError`.
 */
import { ApiUnavailableError } from '@/features/auth/authTypes'
import { ShopClientError, isRateLimitedProblem } from '@/features/shop/contracts/shopContract'
import { shopFetchPublic } from '@/features/shop/clients/shopFetch'
import { clearCartId, getCartId } from './cartStorage'
import type { OrderDraft } from './orderDraftState'

export type CreateOrderRequest = {
  cartId: string
  customerName: string
  customerPhone: string
  shippingProvince: string
  shippingCity: string
  shippingAddressLine: string
  shippingPostalCode: string
  couponCode: string | null
}

export type OrderCreatedResponse = {
  orderId: string
  orderNumber: string
  trackingCode: string
  status: string
  subTotal: number
  discountAmount: number
  shippingCost: number
  grandTotal: number
}

export type InitiatePaymentResponse = {
  gatewayReference: string
  redirectUrl: string
}

export type PaymentCallbackResponse = {
  orderId: string
  status: string
}

export class OrderCreationFailedError extends Error {
  constructor(message = 'ثبت سفارش ممکن نشد؛ ممکن است سبد خرید شما دیگر معتبر نباشد.') {
    super(message)
    this.name = 'OrderCreationFailedError'
  }
}

/**
 * Creates the real order from the stored cart id and the draft's address
 * fields, then immediately initiates payment. On success, clears the stored
 * cart id. A 429 (S42/B046) is rethrown for the caller's cooldown; any other
 * order-creation failure is an `OrderCreationFailedError`.
 */
export async function placeOrderAndInitiatePayment(
  tenantId: string,
  draft: OrderDraft,
): Promise<{ order: OrderCreatedResponse; payment: InitiatePaymentResponse }> {
  const cartId = getCartId(tenantId)
  if (!cartId) throw new OrderCreationFailedError()

  const orderRequest: CreateOrderRequest = {
    cartId,
    customerName: draft.customerName,
    customerPhone: draft.customerPhone,
    shippingProvince: draft.shippingProvince,
    shippingCity: draft.shippingCity,
    shippingAddressLine: draft.shippingAddressLine,
    shippingPostalCode: draft.shippingPostalCode,
    couponCode: draft.couponCode,
  }

  let order: OrderCreatedResponse
  try {
    order = (await shopFetchPublic(`/api/shop/${tenantId}/orders`, {
      method: 'POST',
      json: orderRequest,
    })) as OrderCreatedResponse
  } catch (error) {
    if (error instanceof ShopClientError && isRateLimitedProblem(error.problem)) throw error
    if (error instanceof ApiUnavailableError) throw error
    throw new OrderCreationFailedError()
  }

  clearCartId(tenantId)

  const payment = await initiatePayment(tenantId, order.orderId)
  return { order, payment }
}

/**
 * Mints one fresh sandbox payment attempt for an existing order.
 *
 * B032 resolves each attempt exactly once (`TryResolve`), so a declined
 * reference can never be re-callback'd — "arriving at the bank page again" must
 * mint a new attempt instead. Every bank-page visit is therefore a genuine, live
 * initiation, exactly like arriving at a real gateway's hosted page.
 */
export async function initiatePayment(
  tenantId: string,
  orderId: string,
): Promise<InitiatePaymentResponse> {
  try {
    return (await shopFetchPublic(`/api/shop/${tenantId}/orders/${orderId}/payments/initiate`, {
      method: 'POST',
    })) as InitiatePaymentResponse
  } catch (error) {
    if (error instanceof ApiUnavailableError) throw error
    throw new ApiUnavailableError()
  }
}

export async function submitPaymentCallback(
  tenantId: string,
  orderId: string,
  gatewayReference: string,
  approved: boolean,
): Promise<PaymentCallbackResponse> {
  try {
    return (await shopFetchPublic(`/api/shop/${tenantId}/orders/${orderId}/payments/callback`, {
      method: 'POST',
      json: { gatewayReference, approved },
    })) as PaymentCallbackResponse
  } catch (error) {
    if (error instanceof ApiUnavailableError) throw error
    throw new ApiUnavailableError()
  }
}
