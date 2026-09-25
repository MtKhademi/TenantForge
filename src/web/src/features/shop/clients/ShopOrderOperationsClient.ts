import type { AdminOrderDetail } from '../contracts/adminOrdersContract'
import type { ChangeOrderStatusRequest } from '../contracts/orderOperationsContract'

/**
 * S39 (B043): the operator order-status mutation client port.
 *
 * F051 binds the deterministic mock; F061 replaces exactly this binding with
 * an HTTP implementation of the same interface — the pages never change
 * again. The method is abort-aware: an in-flight mutation is cancelled when
 * the user navigates away, and an aborted request must never surface as an
 * error or land stale state.
 *
 * Wire semantics this method carries (per the B043 contract):
 * - `PATCH /api/tenants/{tenantId}/shop/orders/{orderId}/status` with body
 *   `ChangeOrderStatusRequest` and a required UUID `Idempotency-Key` header;
 * - `200` returns the order's new, already-committed `AdminOrderDetail`
 *   (B042's shape) — the UI must render the new status only AFTER this
 *   resolves, never optimistically;
 * - `400` naming `action` / `expectedVersion` / `Idempotency-Key` for a
 *   malformed request;
 * - a non-leaking `404` for a malformed, missing or other-tenant order id;
 * - `403` for an authenticated member without `Shop.Orders.Manage` (a tenant
 *   Owner bypasses the check);
 * - `409` for three DISTINCT reasons — `stale_version`,
 *   `invalid_order_transition`, `idempotency_key_conflict` (each a separate
 *   problem `type`, never collapsed); a same-key/same-action replay is a `200`.
 *
 * `idempotencyKey` is a per-attempt UUID tag: a retry of the SAME unchanged
 * action reuses it (the server answers from the stored response), a different
 * action always gets a fresh one — a key is never reused across two actions.
 *
 * The caller is expected to branch on `Shop.Orders.Manage` before calling;
 * hiding the control is presentation only, and a `403` here is the server's
 * authority surfacing through the same state.
 */
export interface ShopOrderOperationsClient {
  /**
   * Changes one order's status by its legal transition (`Paid -> Fulfilled`,
   * `PendingPayment -> Cancelled`). Resolves with the committed
   * `AdminOrderDetail`; throws `ShopClientError` (400/403/404/409) or
   * `ApiUnavailableError` (network) otherwise.
   */
  changeStatus(
    tenantId: string,
    orderId: string,
    body: ChangeOrderStatusRequest,
    idempotencyKey: string,
    signal?: AbortSignal,
  ): Promise<AdminOrderDetail>
}
