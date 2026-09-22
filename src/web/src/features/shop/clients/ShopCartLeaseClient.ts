import type { CartResponse } from '../contracts/cartLeaseContract'

/**
 * S36 client port: cart reservation lease (B040).
 *
 * F048 binds the deterministic mock; F058 replaces exactly this binding
 * with an HTTP implementation of the same interface — the pages never
 * change again. Every method is abort-aware: an in-flight request is
 * cancelled when the user navigates away or a newer request supersedes it,
 * and an aborted request must never surface as an error.
 *
 * Parameter lists mirror the real `cartAdapter` (F033) verbatim plus a
 * trailing `signal` — the read takes an explicit `cartId` (the page owns
 * the stored id); add/update/remove resolve the tenant's cart id
 * internally, exactly as the real adapter does.
 *
 * B040 lease semantics these methods carry:
 * - a plain `getCart` is a read: it never extends the lease;
 * - a successful `addItem` / `updateItem` / `removeItem` extends it;
 * - an expired lease answers RFC 7807 `410` with `type: 'shop_cart_expired'`
 *   (thrown as `CartLeaseExpired` — the ONE trigger of the UI's recovery
 *   flow on cart, checkout and review);
 * - a cart or item that no longer exists is a plain `404`
 *   (`ShopClientError`), which is NOT an expiry;
 * - a network failure throws `ApiUnavailableError` (the retry state).
 */
export interface ShopCartLeaseClient {
  /**
   * Reads the tenant's named cart. Throws `CartLeaseExpired` on the 410
   * problem, `ShopClientError` on a plain 404, `ApiUnavailableError` when
   * the request could not be made.
   */
  getCart(tenantId: string, cartId: string, signal?: AbortSignal): Promise<CartResponse>

  /** Adds (or re-sets the quantity of) a variant. A successful add extends the lease. */
  addItem(
    tenantId: string,
    productVariantId: string,
    quantity: number,
    signal?: AbortSignal,
  ): Promise<CartResponse>

  /** Changes an item's quantity (1..99). A successful update extends the lease. */
  updateItem(
    tenantId: string,
    itemId: string,
    quantity: number,
    signal?: AbortSignal,
  ): Promise<CartResponse>

  /** Removes an item. A successful remove extends the lease. */
  removeItem(tenantId: string, itemId: string, signal?: AbortSignal): Promise<CartResponse>
}
