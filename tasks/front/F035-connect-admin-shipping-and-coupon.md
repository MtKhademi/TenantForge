---
id: F035
slice: S28
title: Connect admin shipping-rate and coupon management to the real API
agent: ui-engineer
source: tasks/slices/028-shop-checkout.md
---

# Objective

Replace F034's mocked shipping-rate and coupon admin screens with real
calls to B029's API.

This Spec gives you the exact adapter file (matching F034's
`ShippingAndCouponAdapter` type one-for-one) and the exact call-site
changes. Follow it literally.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/028-shop-checkout.md`. Read B029's delivered
endpoint shapes (from its integration tests or, if the Spec file still
exists at task start, `tasks/backend/B029-shipping-rate-and-coupon-admin-api.md`)
before writing the real adapter. Read F029's delivered
`shopCatalogAdapter.ts` for this repository's exact authenticated-Shop-
adapter shape (`createRequestAbortSignal`, `authHeaders`, `request`,
`readJson`, per-status error mapping) — this task's adapter follows the
identical shape.

# Scope — every file, in order

## 1. `src/web/src/features/shop/shippingAndCouponAdapter.ts`

```typescript
import { ApiUnavailableError, SessionExpiredError } from '@/features/auth/authTypes'
import { ShopForbiddenError, ShopValidationError } from './shopCatalogAdapter'
import {
  CouponConflictError,
  type Coupon,
  type CreateCouponRequest,
  type SetShippingRateRequest,
  type ShippingRate,
} from './shopCheckoutAdminTypes'
import type { ShippingAndCouponAdapter } from './mockShippingAndCouponAdapter'

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
        method: 'POST',
        headers,
      })
      await handleCommonErrors(response)
      if (!response.ok) throw new ApiUnavailableError()
      return (await readJson(response)) as Coupon
    },
  }
}
```

If B029's delivered deactivate endpoint uses a different path/verb than
`POST .../deactivate` (check its Spec's endpoint list or its integration
tests), use the delivered path/verb instead — this Spec's guess follows
B029's own scope description ("a deactivate action per row") but B029's
own Spec is the source of truth for the literal route.

## 2. Update `ShippingRatesPage.tsx` and `CouponsPage.tsx`

In both files:

1. Replace the import
   `import { mockShippingAndCouponAdapter } from '@/features/shop/mockShippingAndCouponAdapter'`
   with:
   ```tsx
   import { createShippingAndCouponAdapter } from '@/features/shop/shippingAndCouponAdapter'
   import { useAuth } from '@/features/auth/AuthContext'
   ```
2. Add `const { session, signOut } = useAuth()` and
   `const adapter = createShippingAndCouponAdapter(session?.accessToken ?? '')`
   at the top of the component, matching F029's exact pattern for the
   catalog admin pages.
3. Replace every `mockShippingAndCouponAdapter.xxx(...)` call with
   `adapter.xxx(...)`.
4. In the coupon create form's `catch` block, map `CouponConflictError`
   to the code field's inline error and `ShopValidationError` to each
   named field, exactly like F029's product-form error mapping.

## 3. Delete the mock

Delete `src/web/src/features/shop/mockShippingAndCouponAdapter.ts`.
Move the `ShippingAndCouponAdapter` type declaration into
`shopCheckoutAdminTypes.ts` first (same reasoning as F029's equivalent
step for `ShopCatalogAdapter`), then update
`shippingAndCouponAdapter.ts`'s import accordingly before deleting the
mock file.

# Non-goals

- No new screen or field beyond what F034 already built.

# Acceptance

- Shipping-rate set/update and coupon create/list/deactivate all work
  end-to-end against the real API.
- A duplicate coupon code shows a clear inline error.
- No mock data path remains reachable.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Set a real shipping rate and coupon, reload the page, and confirm both
  persist. Attempt a duplicate coupon code and confirm the clear error.
- No new browser console error.

# Lifecycle

Add row `F035` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependencies `F034, B029`, and Spec link
`tasks/front/F035-connect-admin-shipping-and-coupon.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/028-shop-checkout.md` is the permanent record and
is never deleted.
