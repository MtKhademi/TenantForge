/**
 * S29 order + sandbox payment (F039): the real adapter for B031's
 * order-creation endpoint and B032's sandbox-payment endpoints — the
 * swap-in replacement for F038's fully mocked review → bank → result flow.
 * Field names mirror B031's `OrderContracts.cs` and B032's
 * `PaymentContracts.cs` camelCased; it mirrors `checkoutAdapter`'s request
 * idiom (8s abort timeout, `ApiUnavailableError` on any network/parse
 * failure) and reads the stored cart id from `cartStorage` (F033), clearing
 * it after a successful order — B031 consumes the cart server-side as part
 * of the order transaction, so the client must not keep pointing at it.
 */
import { ApiUnavailableError } from '@/features/auth/authTypes'
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

const REQUEST_TIMEOUT_MS = 8_000

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return { signal: controller.signal, clear: () => window.clearTimeout(timeoutId) }
}

async function postJson(path: string, body: unknown): Promise<Response> {
  const abort = createRequestAbortSignal()
  try {
    return await fetch(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      signal: abort.signal,
    })
  } catch {
    throw new ApiUnavailableError()
  } finally {
    abort.clear()
  }
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    throw new ApiUnavailableError()
  }
}

/**
 * Creates the real order from the stored cart id and the draft's address
 * fields, then immediately initiates payment. On success, clears the
 * stored cart id (B031 already consumed the cart server-side — the
 * client must not keep pointing at it, per this task's Scope).
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

  const orderResponse = await postJson(`/api/shop/${tenantId}/orders`, orderRequest)
  if (!orderResponse.ok) throw new OrderCreationFailedError()
  const order = (await readJson(orderResponse)) as OrderCreatedResponse

  clearCartId(tenantId)

  const paymentResponse = await postJson(`/api/shop/${tenantId}/orders/${order.orderId}/payments/initiate`, {})
  if (!paymentResponse.ok) throw new ApiUnavailableError()
  const payment = (await readJson(paymentResponse)) as InitiatePaymentResponse

  return { order, payment }
}

/**
 * Mints one fresh sandbox payment attempt for an existing order.
 *
 * The Spec's literal flow inlines this call at the end of
 * `placeOrderAndInitiatePayment`, but the sandbox bank page also needs it
 * on its own: B032 resolves each attempt exactly once (`TryResolve`), so a
 * declined reference can never be re-callback'd — "arriving at the bank
 * page again" (the Spec's working-retry acceptance) must mint a new
 * attempt instead. Every bank-page visit is therefore a genuine, live
 * initiation, exactly like arriving at a real gateway's hosted page.
 */
export async function initiatePayment(
  tenantId: string,
  orderId: string,
): Promise<InitiatePaymentResponse> {
  const response = await postJson(`/api/shop/${tenantId}/orders/${orderId}/payments/initiate`, {})
  if (!response.ok) throw new ApiUnavailableError()
  return (await readJson(response)) as InitiatePaymentResponse
}

export async function submitPaymentCallback(
  tenantId: string,
  orderId: string,
  gatewayReference: string,
  approved: boolean,
): Promise<PaymentCallbackResponse> {
  const response = await postJson(`/api/shop/${tenantId}/orders/${orderId}/payments/callback`, {
    gatewayReference,
    approved,
  })
  if (!response.ok) throw new ApiUnavailableError()
  return (await readJson(response)) as PaymentCallbackResponse
}
