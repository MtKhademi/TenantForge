import { ApiUnavailableError, SessionExpiredError } from '@/features/auth/authTypes'
import { ShopForbiddenError, ShopValidationError } from './shopCatalogAdapter'

// Re-exported so pages keep one import site for the Shop admin error
// classes (the Spec's page edits import them from this adapter).
export { ShopForbiddenError, ShopValidationError }

import {
  CouponConflictError,
  type Coupon,
  type CreateCouponRequest,
  type SetShippingRateRequest,
  type ShippingAndCouponAdapter,
  type ShippingRate,
} from './shopCheckoutAdminTypes'

/**
 * S28 admin shipping-rate/coupon management — real API data source (F035),
 * replacing F034's mock. Calls B029's tenant-scoped admin endpoints with the
 * current session bearer token, following F029's `shopCatalogAdapter.ts`
 * shape exactly: 8s timeout, per-status error mapping (401 →
 * SessionExpiredError, 403 → ShopForbiddenError, 400 → ShopValidationError
 * with field errors, 409 → CouponConflictError).
 *
 * Deactivate uses PATCH, B029's delivered verb (this Spec's own note makes
 * B029's route the source of truth over its POST guess).
 */
const REQUEST_TIMEOUT_MS = 8_000

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return { signal: controller.signal, clear: () => window.clearTimeout(timeoutId) }
}

function authHeaders(accessToken: string) {
  return { Authorization: `Bearer ${accessToken}` }
}

async function request(path: string, init: RequestInit): Promise<Response> {
  const abort = createRequestAbortSignal()
  try {
    return await fetch(path, { ...init, signal: abort.signal })
  } catch {
    throw new ApiUnavailableError()
  } finally {
    abort.clear()
  }
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    throw new ApiUnavailableError()
  }
}

function mapServerValidation(payload: unknown): Record<string, string> {
  const fallback = { _: 'مقدار واردشده معتبر نیست.' }
  if (typeof payload !== 'object' || payload === null) return fallback
  const errors = (payload as Record<string, unknown>).errors
  if (typeof errors !== 'object' || errors === null) return fallback
  const mapped: Record<string, string> = {}
  for (const [field, value] of Object.entries(errors as Record<string, unknown>)) {
    if (Array.isArray(value) && typeof value[0] === 'string') mapped[field] = value[0]
  }
  return Object.keys(mapped).length > 0 ? mapped : fallback
}

async function handleCommonErrors(response: Response): Promise<void> {
  if (response.status === 401) throw new SessionExpiredError()
  if (response.status === 403) throw new ShopForbiddenError()
}

export function createShippingAndCouponAdapter(accessToken: string): ShippingAndCouponAdapter {
  const headers = { ...authHeaders(accessToken), 'Content-Type': 'application/json' }

  return {
    async listShippingRates(tenantId): Promise<ShippingRate[]> {
      const response = await request(`/api/tenants/${tenantId}/shop/shipping-rates`, {
        method: 'GET',
        headers: authHeaders(accessToken),
      })
      await handleCommonErrors(response)
      if (!response.ok) throw new ApiUnavailableError()
      const body = (await readJson(response)) as { rates: ShippingRate[] }
      return body.rates
    },

    async setShippingRate(tenantId, req: SetShippingRateRequest): Promise<ShippingRate> {
      const response = await request(`/api/tenants/${tenantId}/shop/shipping-rates`, {
        method: 'POST',
        headers,
        body: JSON.stringify(req),
      })
      await handleCommonErrors(response)
      if (response.status === 400) throw new ShopValidationError(mapServerValidation(await readJson(response)))
      if (!response.ok) throw new ApiUnavailableError()
      return (await readJson(response)) as ShippingRate
    },

    async listCoupons(tenantId): Promise<Coupon[]> {
      const response = await request(`/api/tenants/${tenantId}/shop/coupons?pageNumber=1&pageSize=100`, {
        method: 'GET',
        headers: authHeaders(accessToken),
      })
      await handleCommonErrors(response)
      if (!response.ok) throw new ApiUnavailableError()
      const body = (await readJson(response)) as { coupons: Coupon[] }
      return body.coupons
    },

    async createCoupon(tenantId, req: CreateCouponRequest): Promise<Coupon> {
      const response = await request(`/api/tenants/${tenantId}/shop/coupons`, {
        method: 'POST',
        headers,
        body: JSON.stringify(req),
      })
      await handleCommonErrors(response)
      if (response.status === 409) throw new CouponConflictError()
      if (response.status === 400) throw new ShopValidationError(mapServerValidation(await readJson(response)))
      if (!response.ok) throw new ApiUnavailableError()
      return (await readJson(response)) as Coupon
    },

    async deactivateCoupon(tenantId, couponId): Promise<Coupon> {
      const response = await request(`/api/tenants/${tenantId}/shop/coupons/${couponId}/deactivate`, {
        method: 'PATCH',
        headers,
      })
      await handleCommonErrors(response)
      if (!response.ok) throw new ApiUnavailableError()
      return (await readJson(response)) as Coupon
    },
  }
}
