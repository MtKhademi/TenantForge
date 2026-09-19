/**
 * S28/S29 checkout → order-review → bank → result flow (F036–F039).
 * A small, module-scoped carrier for the checkout inputs and computed
 * summary collected on CheckoutPage — sessionStorage-backed (not
 * localStorage: an order draft, like an auth session, has no reason to
 * outlive the tab) so a reload on the review/bank/result pages does not
 * lose it, matching this repository's existing sessionStorage-for-
 * session-scoped-data convention (httpAuthAdapter.ts).
 */
const DRAFT_KEY = 'tenantforge:shop:orderDraft'

export type OrderDraft = {
  customerName: string
  customerPhone: string
  shippingProvince: string
  shippingCity: string
  shippingAddressLine: string
  shippingPostalCode: string
  couponCode: string | null
  subTotal: number
  discountAmount: number
  shippingCost: number
  grandTotal: number
}

export function saveOrderDraft(draft: OrderDraft): void {
  try {
    window.sessionStorage.setItem(DRAFT_KEY, JSON.stringify(draft))
  } catch {
    // Best-effort only, matching the design system's storage guidance.
  }
}

export function loadOrderDraft(): OrderDraft | null {
  try {
    const raw = window.sessionStorage.getItem(DRAFT_KEY)
    return raw ? (JSON.parse(raw) as OrderDraft) : null
  } catch {
    return null
  }
}

export function clearOrderDraft(): void {
  try {
    window.sessionStorage.removeItem(DRAFT_KEY)
  } catch {
    // Best-effort only.
  }
}

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
