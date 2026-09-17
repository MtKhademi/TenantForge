---
id: F039
slice: S29
title: Connect order review and payment to the real API
agent: ui-engineer
source: tasks/slices/029-shop-order-and-sandbox-payment.md
---

# Objective

Replace F038's mocked order-review/sandbox-payment flow with real calls
to B031's order-creation API and B032's sandbox-payment API.

This Spec gives you the exact adapter functions (matching B031's
`OrderContracts.cs` and B032's `PaymentContracts.cs` field-for-field)
and the exact call-site changes across `OrderReviewPage.tsx`,
`SandboxBankPage.tsx` and `PaymentResultPage.tsx`. Follow it literally.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/029-shop-order-and-sandbox-payment.md`. Read
B031's and B032's delivered endpoint shapes (from their integration
tests or, if the Spec files still exist at task start,
`tasks/backend/B031-order-creation-api.md` and
`tasks/backend/B032-sandbox-payment-api.md`) before writing the real
adapters. Read F033's delivered `cartStorage.ts` (`getCartId`,
`clearCartId`) — order creation both reads and, on success, clears the
stored cart id.

# Scope — every file, in order

## 1. `src/web/src/features/shop/orderAdapter.ts`

```typescript
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
  const cartId = getCartId()
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

  clearCartId()

  const paymentResponse = await postJson(`/api/shop/${tenantId}/orders/${order.orderId}/payments/initiate`, {})
  if (!paymentResponse.ok) throw new ApiUnavailableError()
  const payment = (await readJson(paymentResponse)) as InitiatePaymentResponse

  return { order, payment }
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
```

## 2. Add a `PlacedOrder` carrier to `orderDraftState.ts`

Add this type and pair of functions to
`src/web/src/features/shop/orderDraftState.ts`, right after the existing
`clearOrderDraft` function, following the exact same `sessionStorage`
try/catch shape already in that file:

```typescript
const PLACED_ORDER_KEY = 'tenantforge:shop:placedOrder'

export type PlacedOrder = {
  orderId: string
  orderNumber: string
  trackingCode: string
  gatewayReference: string
}

export function savePlacedOrder(order: PlacedOrder): void {
  try {
    window.sessionStorage.setItem(PLACED_ORDER_KEY, JSON.stringify(order))
  } catch {
    // Best-effort only, matching the design system's storage guidance.
  }
}

export function loadPlacedOrder(): PlacedOrder | null {
  try {
    const raw = window.sessionStorage.getItem(PLACED_ORDER_KEY)
    return raw ? (JSON.parse(raw) as PlacedOrder) : null
  } catch {
    return null
  }
}
```

## 3. Update `OrderReviewPage.tsx`

Replace the plain `<Link to={.../bank}>` "place order" button with a
button that calls `placeOrderAndInitiatePayment`, stores the resulting
order id/gateway reference via `savePlacedOrder` from step 2, and
navigates to `/bank` only after the order and payment-initiate calls
both succeed:

```tsx
const [isPlacing, setIsPlacing] = useState(false)
const [placeError, setPlaceError] = useState<string | null>(null)

async function handlePlaceOrder() {
  if (!draft) return
  setIsPlacing(true)
  setPlaceError(null)
  try {
    const { order, payment } = await placeOrderAndInitiatePayment(tenantId, draft)
    savePlacedOrder({ orderId: order.orderId, orderNumber: order.orderNumber, trackingCode: order.trackingCode, gatewayReference: payment.gatewayReference })
    navigate(`/shop/${tenantId}/bank`)
  } catch (error) {
    setPlaceError(error instanceof Error ? error.message : 'خطایی رخ داد.')
  } finally {
    setIsPlacing(false)
  }
}
```

Replace the button with
`<Button type="button" className="w-full" disabled={isPlacing} onClick={() => void handlePlaceOrder()}>ثبت سفارش و پرداخت</Button>`
and render `placeError` in a `role="alert"` paragraph when set — this is
the stock-race failure case named in this task's Acceptance (a variant
sold out between checkout summary and order creation shows this clear
error, not a broken page).

## 4. Update `SandboxBankPage.tsx`

Replace `loadOrderDraft()` (still used for the displayed `grandTotal`)
with also reading the placed order via `loadPlacedOrder()`. Replace the
two static `<Link>` "Approve"/"Decline" elements with buttons that call
`submitPaymentCallback(tenantId, placedOrder.orderId,
placedOrder.gatewayReference, true | false)` and navigate to
`/payment-result?outcome=approved` or `...=declined` based on the
resolved `PaymentCallbackResponse.status` (`"Paid"` → approved outcome,
anything else → declined outcome) rather than solely on which button was
clicked — B032's callback can independently reject an approval attempt,
so the UI must reflect the server's resolved status, not just the
button pressed.

## 5. Update `PaymentResultPage.tsx`

Replace the hardcoded `MOCK_ORDER_NUMBER`/`MOCK_TRACKING_CODE` constants
with the real values from `loadPlacedOrder()` (`orderNumber`,
`trackingCode`), read the same way `OrderReviewPage.tsx` reads the
order draft.

## 6. Delete the mock order-number constants

Remove `MOCK_ORDER_NUMBER`/`MOCK_TRACKING_CODE` from
`PaymentResultPage.tsx` entirely once step 4 is done.

# Non-goals

- No admin order-management screen.
- No email/SMS confirmation UI.

# If you get stuck

```bash
curl -X POST http://localhost:5080/api/shop/<tenantId>/orders \
  -H "Content-Type: application/json" \
  -d '{"cartId":"<cartId>","customerName":"علی رضایی","customerPhone":"09121234567","shippingProvince":"تهران","shippingCity":"تهران","shippingAddressLine":"خیابان ولیعصر","shippingPostalCode":"1234567890","couponCode":null}'
```

Compare the response against `OrderCreatedResponse` above before wiring
the page; then:

```bash
curl -X POST http://localhost:5080/api/shop/<tenantId>/orders/<orderId>/payments/initiate \
  -H "Content-Type: application/json" -d '{}'
```

Compare against `InitiatePaymentResponse`.

# Acceptance

- Placing a real order creates it via B031, initiates payment via B032,
  and the approve/decline actions correctly reach `Paid`/still-pending
  outcomes.
- The stored cart id is cleared after a successful order, and a
  subsequent add-to-cart starts a fresh cart.
- A stock-race failure at order-creation time (a variant sold out between
  checkout summary and order creation) shows a clear error, not a broken
  page.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Complete a real checkout into a real order, approve on the sandbox
  bank page, and confirm the order shows `Paid` with a real order
  number/tracking code; repeat and decline, and confirm the order stays
  pending with a working retry.
- No new browser console error.

# Lifecycle

Add row `F039` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependencies `F038, B031, B032`, and Spec link
`tasks/front/F039-connect-order-review-and-payment.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/029-shop-order-and-sandbox-payment.md` is the
permanent record and is never deleted.
