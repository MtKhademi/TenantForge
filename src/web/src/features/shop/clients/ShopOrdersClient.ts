import type { AdminOrderDetail, AdminOrderListResponse, AdminOrderStatus } from '../contracts/adminOrdersContract'

/**
 * S38 (B042): the admin order read client port.
 *
 * F050 binds the deterministic mock; F060 replaces exactly this binding with
 * an HTTP implementation of the same interface — the pages never change again.
 * Every method is abort-aware: an in-flight request is cancelled when the user
 * navigates away or a newer request supersedes it, and an aborted request must
 * never surface as an error.
 *
 * Routes and semantics these methods carry (per the B042 contract):
 * - `list` → `GET /api/tenants/{tenantId}/shop/orders` — optional `q`,
 *   `status`, `fromUtc`, `toUtc` filters (combined with AND), always ordered
 *   `CreatedAtUtc desc, Id desc`; an invalid filter is a `400` naming the field;
 * - `get` → `GET /api/tenants/{tenantId}/shop/orders/{orderId}` — a malformed,
 *   missing or other-tenant order id all return the same non-leaking `404`;
 *   payment attempts are capped at the 20 newest.
 *
 * Both require a valid JWT and `Shop.Orders.View`; a member without the key is
 * a `403` (the permission-denied state), a network failure throws
 * `ApiUnavailableError` (the retryable state).
 */

/** The list filters, one per B042 query parameter. All optional, AND-combined. */
export interface AdminOrderFilters {
  /** 1-based page number. */
  pageNumber: number
  /** Page size. */
  pageSize: number
  /** Case-insensitive contains on order number, tracking code, phone, name (≤ 100 chars, trimmed). */
  q?: string
  /** One of the defined `AdminOrderStatus` values (case-sensitive). */
  status?: AdminOrderStatus
  /** Inclusive start (parseable date-time). */
  fromUtc?: string
  /** Exclusive end (parseable date-time); range ≤ 366 days from `fromUtc`. */
  toUtc?: string
}

export interface ShopOrdersClient {
  /** Reads one filtered, paginated page of the tenant's orders. */
  list(tenantId: string, filters: AdminOrderFilters, signal?: AbortSignal): Promise<AdminOrderListResponse>

  /** Reads one order's frozen detail. Throws a non-leaking `404` for any unknown/foreign id. */
  get(tenantId: string, orderId: string, signal?: AbortSignal): Promise<AdminOrderDetail>
}
