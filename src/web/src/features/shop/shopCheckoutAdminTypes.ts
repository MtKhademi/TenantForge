export type ShippingRate = {
  id: string
  provinceName: string
  cost: number
}

export type SetShippingRateRequest = {
  provinceName: string
  cost: number
}

/** Matches B029's ShopDiscountType enum exactly — do not add a third value. */
export type DiscountType = 'Percentage' | 'FixedAmount'

export type Coupon = {
  id: string
  code: string
  discountType: DiscountType
  discountValue: number
  isActive: boolean
  expiresAtUtc: string | null
}

export type CreateCouponRequest = {
  code: string
  discountType: DiscountType
  discountValue: number
  expiresAtUtc: string | null
}

export class CouponConflictError extends Error {
  constructor(message = 'کد تخفیفی با این کد از قبل وجود دارد.') {
    super(message)
    this.name = 'CouponConflictError'
  }
}

/**
 * S28 (F035): the shipping-rate/coupon admin data-source contract. F034
 * satisfied it with an in-memory mock; F035 satisfies it with
 * `createShippingAndCouponAdapter` (real B029 calls). The mock file is
 * deleted once the real adapter lands — same reasoning as F029's
 * `ShopCatalogAdapter` move.
 */
export type ShippingAndCouponAdapter = {
  listShippingRates(tenantId: string): Promise<ShippingRate[]>
  setShippingRate(tenantId: string, request: SetShippingRateRequest): Promise<ShippingRate>
  listCoupons(tenantId: string): Promise<Coupon[]>
  createCoupon(tenantId: string, request: CreateCouponRequest): Promise<Coupon>
  deactivateCoupon(tenantId: string, couponId: string): Promise<Coupon>
}
