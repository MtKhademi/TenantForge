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
