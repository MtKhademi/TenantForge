/**
 * S28/S29 checkout → order-review → bank → result flow (F036–F039), S36
 * tenant isolation (F048). A small, module-scoped carrier for the checkout
 * inputs and computed summary collected on CheckoutPage — sessionStorage-
 * backed (not localStorage: an order draft, like an auth session, has no
 * reason to outlive the tab) so a reload on the review/bank/result pages
 * does not lose it, matching this repository's existing sessionStorage-for-
 * session-scoped-data convention (httpAuthAdapter.ts).
 *
 * S36/F048 keys every entry per tenant: the key itself is shared, the VALUE
 * is a per-tenant record (`{ "<tenantId>": <entry> }`). Two tenants in the
 * same tab keep two independent drafts, and clearing one tenant's entry (the
 * B040 expiry recovery flow) leaves every other tenant's entry untouched.
 * The record is rewritten whole on every write.
 */
const DRAFT_KEY = 'tenantforge:shop:orderDraft'
const PLACED_ORDER_KEY = 'tenantforge:shop:placedOrder'

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

export type PlacedOrder = {
  orderId: string
  orderNumber: string
  trackingCode: string
  gatewayReference: string
}

function readRecord<T>(key: string): Record<string, T> {
  try {
    const raw = window.sessionStorage.getItem(key)
    if (!raw) return {}
    const parsed: unknown = JSON.parse(raw)
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return {}
    return parsed as Record<string, T>
  } catch {
    // Corrupt or unavailable storage: best-effort only.
    return {}
  }
}

function writeRecord(key: string, record: Record<string, unknown>): void {
  try {
    window.sessionStorage.setItem(key, JSON.stringify(record))
  } catch {
    // Best-effort only, matching the design system's storage guidance.
  }
}

export function saveOrderDraft(tenantId: string, draft: OrderDraft): void {
  const record = readRecord<OrderDraft>(DRAFT_KEY)
  record[tenantId] = draft
  writeRecord(DRAFT_KEY, record)
}

/** This tenant's order draft, or null when none is stored yet. */
export function loadOrderDraft(tenantId: string): OrderDraft | null {
  return readRecord<OrderDraft>(DRAFT_KEY)[tenantId] ?? null
}

/** Clear ONLY this tenant's order draft — other tenants' entries are untouched. */
export function clearOrderDraft(tenantId: string): void {
  const record = readRecord<OrderDraft>(DRAFT_KEY)
  if (!(tenantId in record)) return
  delete record[tenantId]
  writeRecord(DRAFT_KEY, record)
}

export function savePlacedOrder(tenantId: string, order: PlacedOrder): void {
  const record = readRecord<PlacedOrder>(PLACED_ORDER_KEY)
  record[tenantId] = order
  writeRecord(PLACED_ORDER_KEY, record)
}

/** This tenant's placed order, or null when none is stored yet. */
export function loadPlacedOrder(tenantId: string): PlacedOrder | null {
  return readRecord<PlacedOrder>(PLACED_ORDER_KEY)[tenantId] ?? null
}
