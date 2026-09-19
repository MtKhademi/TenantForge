import {
  CouponConflictError,
  type Coupon,
  type CreateCouponRequest,
  type SetShippingRateRequest,
  type ShippingRate,
} from './shopCheckoutAdminTypes'

/**
 * S28 (F034): mocked admin data source for shipping rates and coupons,
 * scoped by an in-memory `tenantId` filter so the pages behave correctly
 * even though there is only one tenant in this demo session. F035 replaces
 * this with the real B029 API without changing the pages' call shape.
 */
export type ShippingAndCouponAdapter = {
  listShippingRates(tenantId: string): Promise<ShippingRate[]>
  setShippingRate(tenantId: string, request: SetShippingRateRequest): Promise<ShippingRate>
  listCoupons(tenantId: string): Promise<Coupon[]>
  createCoupon(tenantId: string, request: CreateCouponRequest): Promise<Coupon>
  deactivateCoupon(tenantId: string, couponId: string): Promise<Coupon>
}

let nextId = 1
function mockId(prefix: string) {
  return `mock-${prefix}-${nextId++}`
}

const rates: (ShippingRate & { tenantId: string })[] = []
const coupons: (Coupon & { tenantId: string })[] = []

export const mockShippingAndCouponAdapter: ShippingAndCouponAdapter = {
  async listShippingRates(tenantId) {
    return rates.filter((r) => r.tenantId === tenantId)
  },

  async setShippingRate(tenantId, request) {
    const existing = rates.find((r) => r.tenantId === tenantId && r.provinceName === request.provinceName)
    if (existing) {
      existing.cost = request.cost
      return existing
    }
    const rate = { id: mockId('rate'), tenantId, provinceName: request.provinceName, cost: request.cost }
    rates.push(rate)
    return rate
  },

  async listCoupons(tenantId) {
    return coupons.filter((c) => c.tenantId === tenantId)
  },

  async createCoupon(tenantId, request) {
    const code = request.code.trim().toUpperCase()
    if (coupons.some((c) => c.tenantId === tenantId && c.code === code)) {
      throw new CouponConflictError()
    }
    const coupon = {
      id: mockId('coupon'),
      tenantId,
      code,
      discountType: request.discountType,
      discountValue: request.discountValue,
      isActive: true,
      expiresAtUtc: request.expiresAtUtc,
    }
    coupons.push(coupon)
    return coupon
  },

  async deactivateCoupon(tenantId, couponId) {
    const coupon = coupons.find((c) => c.tenantId === tenantId && c.id === couponId)
    if (!coupon) throw new Error('Coupon not found')
    coupon.isActive = false
    return coupon
  },
}
