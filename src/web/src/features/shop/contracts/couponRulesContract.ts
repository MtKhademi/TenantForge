import { z } from 'zod'
import { paginationSchema } from './shopContract'

/**
 * S37 / B041 wire contract: enforceable coupon limits and atomic redemption.
 *
 * Mirrors the persistent Shop HTTP contract (docs/design/shop/http-contracts.md,
 * section "S37 / B041 — coupon rules") and the delivered C# in
 * `TenantForge.Modules.Shop/features/coupons/CouponContracts.cs`, field for
 * field. The existing POST/list/deactivate routes keep their shape; the
 * response gains `minimumSubtotal`, `maximumDiscountAmount`, `redemptionLimit`,
 * `redeemedCount` and `version`. A NEW `PUT …/coupons/{couponId}` accepts an
 * `UpdateCouponRequest` and is protected by `Shop.Shipping.Manage`; it returns
 * `200 CouponResponse`, `409` with `type: 'stale_version'` when the
 * `expectedVersion` guard fails, and `400` naming `redemptionLimit` when the
 * new limit is set below the current `redeemedCount`.
 *
 * `null` means "unlimited" for both `maximumDiscountAmount` and
 * `redemptionLimit` — the UI renders that as «نامحدود», never an empty cell.
 *
 * `Coupon` here is the SINGLE B041 coupon wire type for this capability. It is
 * deliberately distinct from the older F033 admin `Coupon` in
 * `shopCheckoutAdminTypes.ts` (a pre-B041 shape with fewer members) — the two
 * describe different endpoints, so they are not competing copies of one shape.
 */
export const couponSchema = z.object({
  id: z.string(),
  code: z.string(),
  discountType: z.enum(['Percentage', 'FixedAmount']),
  discountValue: z.number(),
  minimumSubtotal: z.number(),
  maximumDiscountAmount: z.number().nullable(),
  redemptionLimit: z.number().int().nullable(),
  redeemedCount: z.number().int(),
  isActive: z.boolean(),
  expiresAtUtc: z.string().nullable(),
  version: z.number().int(),
})
export type Coupon = z.infer<typeof couponSchema>

/** Body for `POST …/coupons`. A created coupon is always active, version 1. */
export type CreateCouponRequest = Pick<
  Coupon,
  | 'code'
  | 'discountType'
  | 'discountValue'
  | 'minimumSubtotal'
  | 'maximumDiscountAmount'
  | 'redemptionLimit'
  | 'expiresAtUtc'
>

/**
 * Body for `PUT …/coupons/{couponId}`. Carries neither `code` nor
 * `discountType`, so those two cannot change through this endpoint. The
 * `expectedVersion` guard is the optimistic-concurrency check: the server
 * rejects the update with a `409 stale_version` when it does not match the
 * stored `version`.
 */
export type UpdateCouponRequest = Pick<
  Coupon,
  | 'discountValue'
  | 'minimumSubtotal'
  | 'maximumDiscountAmount'
  | 'redemptionLimit'
  | 'expiresAtUtc'
  | 'isActive'
> & { expectedVersion: number }

export const couponListResponseSchema = z.object({
  coupons: z.array(couponSchema),
  pagination: paginationSchema,
})
export type CouponListResponse = z.infer<typeof couponListResponseSchema>

/**
 * The five stable coupon reason codes B041 defines, returned verbatim (in
 * parentheses) inside the `couponCode` field error of a rejected
 * `checkout/summary` / `orders` request. Each needs its own user-facing
 * message — never collapsed into one generic message.
 */
export const COUPON_REASON_CODES = [
  'coupon_not_found',
  'coupon_inactive',
  'coupon_expired',
  'coupon_minimum_not_met',
  'coupon_limit_reached',
] as const
export type CouponReasonCode = (typeof COUPON_REASON_CODES)[number]

/**
 * One distinct Persian message per B041 reason code. Each embeds the exact
 * stable code in parentheses so it is assertable. This is the single source of
 * the reason→message mapping; the checkout UI maps a rejected preview (produced
 * by the mock's B041 evaluation) to the exact message here rather than echoing
 * raw text.
 */
export const COUPON_REASON_MESSAGES: Record<CouponReasonCode, string> = {
  coupon_not_found: 'این کد تخفیف معتبر نیست. (coupon_not_found)',
  coupon_inactive: 'این کد تخفیف فعلاً غیرفعال است. (coupon_inactive)',
  coupon_expired: 'این کد تخفیف منقضی شده است. (coupon_expired)',
  coupon_minimum_not_met: 'برای استفاده از این کد تخفیف، جمع سبد باید بیشتر باشد. (coupon_minimum_not_met)',
  coupon_limit_reached: 'این کد تخفیف به سقف استفاده‌ی خود رسیده است. (coupon_limit_reached)',
}

/**
 * The outcome of a checkout coupon preview. `applied` carries the discount to
 * show; `rejected` carries the exact B041 reason code (mapped to a distinct
 * message via `COUPON_REASON_MESSAGES`).
 */
export type CouponPreviewResult =
  | { status: 'applied'; discountAmount: number }
  | { status: 'rejected'; reasonCode: CouponReasonCode }

/**
 * A coupon lookup that mirrors the backend's tenant-first, case-insensitive
 * code match (`NormalizedCode` = trim + upper). Returns `null` when no coupon
 * matches — the caller maps that to `coupon_not_found` (which is also what a
 * foreign-tenant code must look like, so the two stay indistinguishable).
 */
export function findCouponByCode(coupons: readonly Coupon[], code: string): Coupon | null {
  const normalized = code.trim().toUpperCase()
  if (normalized === '') return null
  return coupons.find((c) => c.code.trim().toUpperCase() === normalized) ?? null
}

/**
 * Pure, read-only mirror of B041's `ShopCouponPolicy.Evaluate` (the C# policy
 * is pure — it reads the loaded coupon and computes, it never writes). Checks
 * in the fixed order — inactive, expired, minimum-not-met, limit-reached — and
 * on success computes the discount, capped at `maximumDiscountAmount` (when set)
 * and never past the subtotal. It NEVER touches `redeemedCount`, so a checkout
 * preview can never decrease the displayed usage (only a real redemption, which
 * this mock does not simulate, changes it).
 */
export function evaluateCouponPreview(coupon: Coupon, subTotal: number, nowUtc: number): CouponPreviewResult {
  if (!coupon.isActive) return { status: 'rejected', reasonCode: 'coupon_inactive' }
  if (coupon.expiresAtUtc !== null && nowUtc > Date.parse(coupon.expiresAtUtc)) {
    return { status: 'rejected', reasonCode: 'coupon_expired' }
  }
  if (subTotal < coupon.minimumSubtotal) return { status: 'rejected', reasonCode: 'coupon_minimum_not_met' }
  if (coupon.redemptionLimit !== null && coupon.redeemedCount >= coupon.redemptionLimit) {
    return { status: 'rejected', reasonCode: 'coupon_limit_reached' }
  }
  const raw =
    coupon.discountType === 'Percentage'
      ? Math.round((subTotal * coupon.discountValue) / 100 * 100) / 100
      : coupon.discountValue
  let discount = coupon.maximumDiscountAmount !== null ? Math.min(raw, coupon.maximumDiscountAmount) : raw
  discount = Math.min(discount, subTotal)
  return { status: 'applied', discountAmount: discount }
}
