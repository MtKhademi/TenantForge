/**
 * S28 checkout (F036): a small hardcoded mock of B030's checkout-summary
 * computation. It matches `CheckoutSummaryResponse` field-for-field
 * (`subTotal`, `discountAmount`, `shippingCost`, `grandTotal`) and throws the
 * same two named errors B030 will throw — unshippable province and invalid
 * coupon — so F037 can swap this file for the real adapter without touching
 * `CheckoutPage.tsx`. Mocked data only; nothing here persists or calls an API.
 */

export type CheckoutSummary = {
  subTotal: number
  discountAmount: number
  shippingCost: number
  grandTotal: number
}

export class UnshippableProvinceError extends Error {
  constructor(message = 'ارسال به این استان در حال حاضر ممکن نیست.') {
    super(message)
    this.name = 'UnshippableProvinceError'
  }
}

export class InvalidCouponError extends Error {
  constructor(message = 'کد تخفیف وارد شده معتبر نیست.') {
    super(message)
    this.name = 'InvalidCouponError'
  }
}

const MOCK_SUB_TOTAL = 890_000
const SHIPPABLE_PROVINCES: Record<string, number> = {
  تهران: 50_000,
  اصفهان: 70_000,
}
const VALID_COUPON = 'WELCOME10'

export async function computeMockCheckoutSummary(province: string, couponCode: string): Promise<CheckoutSummary> {
  const shippingCost = SHIPPABLE_PROVINCES[province]
  if (shippingCost === undefined) throw new UnshippableProvinceError()

  let discountAmount = 0
  if (couponCode.trim().length > 0) {
    if (couponCode.trim().toUpperCase() !== VALID_COUPON) throw new InvalidCouponError()
    discountAmount = Math.round(MOCK_SUB_TOTAL * 0.1)
  }

  return {
    subTotal: MOCK_SUB_TOTAL,
    discountAmount,
    shippingCost,
    grandTotal: MOCK_SUB_TOTAL - discountAmount + shippingCost,
  }
}
