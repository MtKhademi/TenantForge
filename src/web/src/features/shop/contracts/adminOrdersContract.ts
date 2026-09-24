import { z } from 'zod'
import { paginationSchema } from './shopContract'

/**
 * S38 (B042): the tenant operator's admin order read wire shapes — the exact
 * `AdminOrder*` records the backend returns for
 * `GET /api/tenants/{tenantId}/shop/orders` (list) and
 * `GET /api/tenants/{tenantId}/shop/orders/{orderId}` (detail).
 *
 * Field-for-field with `docs/design/shop/http-contracts.md` "S38 / B042" and
 * the backend `AdminOrderContracts.cs`: same member names, casing, nullability
 * and enum strings. These are read-only views of the order's frozen snapshot —
 * the item name/price/variant are the point-in-time values stored at
 * order-creation time, never a live join to the current product. No UI-only
 * members are added to any wire type.
 *
 * The read routes require a valid JWT **and** `Shop.Orders.View`; a member
 * without the key gets a `403`, a detail hit on a malformed / missing /
 * other-tenant order id returns the SAME non-leaking `404`, and payment
 * attempts are capped at the 20 newest.
 */

/** The four stable admin order status values (case-sensitive). */
export const adminOrderStatusSchema = z.enum(['PendingPayment', 'Paid', 'Cancelled', 'Fulfilled'])
export type AdminOrderStatus = z.infer<typeof adminOrderStatusSchema>

/** One row of the paginated order list. */
export const adminOrderSummarySchema = z.object({
  id: z.string(),
  orderNumber: z.string(),
  customerName: z.string(),
  customerPhone: z.string(),
  status: adminOrderStatusSchema,
  grandTotal: z.number(),
  createdAtUtc: z.string(),
})
export type AdminOrderSummary = z.infer<typeof adminOrderSummarySchema>

/** The point-in-time customer + shipping-address snapshot (6 fields, per B042). */
export const adminOrderCustomerSchema = z.object({
  name: z.string(),
  phone: z.string(),
  shippingProvince: z.string(),
  shippingCity: z.string(),
  shippingAddressLine: z.string(),
  shippingPostalCode: z.string(),
})
export type AdminOrderCustomer = z.infer<typeof adminOrderCustomerSchema>

/** The order's frozen totals (the same four values the guest lookup returns). */
export const adminOrderTotalsSchema = z.object({
  subTotal: z.number(),
  shippingCost: z.number(),
  discountAmount: z.number(),
  grandTotal: z.number(),
})
export type AdminOrderTotals = z.infer<typeof adminOrderTotalsSchema>

/**
 * One order line — the S30 guest-lookup item reused verbatim (B042's
 * `OrderLookupItemResponse`). Values are the stored snapshot, not live data.
 */
export const adminOrderItemSchema = z.object({
  productNameSnapshot: z.string(),
  variantLabelSnapshot: z.string(),
  unitPrice: z.number(),
  quantity: z.number().int(),
})
export type AdminOrderItem = z.infer<typeof adminOrderItemSchema>

/** One payment-attempt summary — the minimal id/status/created triple B042 exposes. */
export const adminPaymentAttemptSchema = z.object({
  id: z.string(),
  status: z.string(),
  createdAtUtc: z.string(),
})
export type AdminPaymentAttempt = z.infer<typeof adminPaymentAttemptSchema>

/** The full admin order detail. */
export const adminOrderDetailSchema = z.object({
  id: z.string(),
  orderNumber: z.string(),
  trackingCode: z.string(),
  status: adminOrderStatusSchema,
  customer: adminOrderCustomerSchema,
  totals: adminOrderTotalsSchema,
  items: z.array(adminOrderItemSchema),
  paymentAttempts: z.array(adminPaymentAttemptSchema),
  version: z.number().int(),
  createdAtUtc: z.string(),
})
export type AdminOrderDetail = z.infer<typeof adminOrderDetailSchema>

/** The paginated list envelope. */
export const adminOrderListResponseSchema = z.object({
  orders: z.array(adminOrderSummarySchema),
  pagination: paginationSchema,
})
export type AdminOrderListResponse = z.infer<typeof adminOrderListResponseSchema>
