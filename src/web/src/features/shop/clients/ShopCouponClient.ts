import {
  type Coupon,
  type CreateCouponRequest,
  type CouponListResponse,
  type UpdateCouponRequest,
} from '../contracts/couponRulesContract'
import type { PageQuery } from '../contracts/shopContract'

/**
 * S37 client port: enforceable coupon limits and atomic redemption (B041).
 *
 * F049 binds the deterministic mock; F059 replaces exactly this binding with
 * an HTTP implementation of the same interface — the pages never change
 * again. Every method is abort-aware: an in-flight request is cancelled when
 * the user navigates away or a newer request supersedes it, and an aborted
 * request must never surface as an error.
 *
 * Routes and semantics these methods carry (per the B041 contract):
 * - `list`   → `GET  …/coupons` (paginated, ordered by code);
 * - `create` → `POST …/coupons` — the created coupon is always active,
 *   version 1; a duplicate code is a `409`;
 * - `update` → `PUT  …/coupons/{couponId}` — guarded by `expectedVersion`;
 *   a stale guard is a `409` with `type: 'stale_version'`; a limit set below
 *   the current `redeemedCount` is a `400` naming `redemptionLimit`;
 * - `deactivate` → `PATCH …/coupons/{couponId}/deactivate` — flips
 *   `isActive` to false and bumps `version`.
 *
 * A missing coupon is a plain `404` (`ShopClientError`); a missing
 * `Shop.Shipping.Manage` permission is a `403`; a network failure throws
 * `ApiUnavailableError` (the retry state).
 */
export interface ShopCouponClient {
  /** Reads one page of the tenant's coupons, ordered by code. */
  list(tenantId: string, query: PageQuery, signal?: AbortSignal): Promise<CouponListResponse>

  /** Creates a coupon (always active, version 1). Throws `409` on a duplicate code. */
  create(tenantId: string, body: CreateCouponRequest, signal?: AbortSignal): Promise<Coupon>

  /**
   * Updates a coupon's editable fields. The `expectedVersion` guard is
   * checked after validation: a mismatch is a `409 stale_version`.
   */
  update(tenantId: string, couponId: string, body: UpdateCouponRequest, signal?: AbortSignal): Promise<Coupon>

  /** Deactivates a coupon (isActive=false, version bumped). */
  deactivate(tenantId: string, couponId: string, signal?: AbortSignal): Promise<Coupon>
}
