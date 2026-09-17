---
id: F034
slice: S28
title: Admin shipping-rate and coupon management mock
agent: ui-engineer
source: tasks/slices/028-shop-checkout.md
---

# Objective

Build the admin screens for setting per-province shipping rates and
managing coupons, against mocked data.

This Spec gives you the exact file paths, the exact mocked-data types
(matching B029's real API field-for-field), the exact nav/route
additions, and a concrete component skeleton for each page. Follow it
literally.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/028-shop-checkout.md`. This page lives inside
`DashboardShell`, alongside the catalog admin pages F028/F029 already
built — reuse the same admin list-page layout and, once delivered,
`StatePanel`. Read F028's delivered `CategoriesPage.tsx` fully — this
task's two pages follow its exact structural pattern (header, toggle
form, skeleton, empty state, table).

# Scope — every file, in order

## 1. `src/web/src/features/shop/shopCheckoutAdminTypes.ts`

Field names copied from B029's `ShippingRateContracts.cs`/
`CouponContracts.cs`, camelCased:

```typescript
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
```

## 2. `src/web/src/features/shop/mockShippingAndCouponAdapter.ts`

```typescript
import {
  CouponConflictError,
  type Coupon,
  type CreateCouponRequest,
  type SetShippingRateRequest,
  type ShippingRate,
} from './shopCheckoutAdminTypes'

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
```

## 3. `src/web/src/pages/shop/admin/ShippingRatesPage.tsx`

A list of configured province/cost rows plus a form to set/update one
(a free-text province field, since B029's real API upserts by
`provinceName` string — there is no separate province lookup table
anywhere in this module):

```tsx
import { useCallback, useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useParams } from 'react-router-dom'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { TextInput } from '@/components/ui/TextInput'
import { mockShippingAndCouponAdapter } from '@/features/shop/mockShippingAndCouponAdapter'
import type { ShippingRate } from '@/features/shop/shopCheckoutAdminTypes'

const rateSchema = z.object({
  provinceName: z.string().min(1, 'نام استان الزامی است.'),
  cost: z.coerce.number().min(0, 'هزینه ارسال باید صفر یا بیشتر باشد.'),
})
type RateFormValues = z.infer<typeof rateSchema>

export function ShippingRatesPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [rates, setRates] = useState<ShippingRate[] | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const { register, handleSubmit, reset, formState: { errors } } = useForm<RateFormValues>({
    resolver: zodResolver(rateSchema),
    defaultValues: { provinceName: '', cost: 0 },
  })

  const load = useCallback(() => {
    void mockShippingAndCouponAdapter.listShippingRates(tenantId).then(setRates)
  }, [tenantId])

  useEffect(() => { load() }, [load])

  const onSubmit = useCallback(
    async (values: RateFormValues) => {
      await mockShippingAndCouponAdapter.setShippingRate(tenantId, values)
      reset()
      setFormOpen(false)
      load()
    },
    [tenantId, reset, load],
  )

  return (
    <DashboardShell>
      <section aria-label="نرخ‌های ارسال" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <p className="text-sm font-semibold text-primary">مدیریت فروشگاه</p>
            <h2 className="mt-2 text-2xl font-semibold">نرخ‌های ارسال</h2>
          </div>
          <Button type="button" onClick={() => { reset(); setFormOpen((open) => !open) }}>
            تعیین نرخ ارسال
          </Button>
        </div>

        {formOpen && (
          <form className="rounded-xl border border-border bg-surface p-5 shadow-soft" onSubmit={handleSubmit(onSubmit)} noValidate>
            <div className="grid gap-4 md:grid-cols-2">
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="rate-province">استان</label>
                <TextInput id="rate-province" {...register('provinceName')} />
                {errors.provinceName && <p className="mt-2 text-sm text-destructive">{errors.provinceName.message}</p>}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="rate-cost">هزینه (تومان)</label>
                <TextInput id="rate-cost" type="number" {...register('cost')} />
                {errors.cost && <p className="mt-2 text-sm text-destructive">{errors.cost.message}</p>}
              </div>
            </div>
            <div className="mt-4 flex gap-2">
              <Button type="submit">ذخیره</Button>
              <SecondaryButton type="button" onClick={() => setFormOpen(false)}>لغو</SecondaryButton>
            </div>
          </form>
        )}

        {rates === null && <p>در حال بارگذاری...</p>}
        {rates !== null && rates.length === 0 && (
          <div className="rounded-xl border border-border bg-surface p-8 text-center shadow-soft">
            <p className="text-sm font-semibold">هیچ نرخ ارسالی تعیین نشده است</p>
          </div>
        )}
        {rates !== null && rates.length > 0 && (
          <div className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft">
            <table className="w-full text-sm">
              <caption className="sr-only">فهرست نرخ‌های ارسال</caption>
              <thead>
                <tr className="border-b border-border">
                  <th scope="col" className="px-4 py-3 text-start font-semibold">استان</th>
                  <th scope="col" className="px-4 py-3 text-start font-semibold">هزینه</th>
                </tr>
              </thead>
              <tbody>
                {rates.map((rate) => (
                  <tr key={rate.id} className="border-b border-border last:border-b-0">
                    <td className="px-4 py-3">{rate.provinceName}</td>
                    <td className="px-4 py-3">{rate.cost.toLocaleString('fa-IR')} تومان</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </DashboardShell>
  )
}
```

## 4. `src/web/src/pages/shop/admin/CouponsPage.tsx`

A list of coupons (code, discount type/value, active state, expiry) plus
a create form and a deactivate action per row (no edit/reactivate
action, matching B029's scope). Follow `ShippingRatesPage.tsx`'s exact
structural pattern; the create form's discount-type field is a plain
`<select>` with the two literal options `Percentage`/`FixedAmount`
(matching `DiscountType` in `shopCheckoutAdminTypes.ts` exactly — do not
localize the option `value`, only its visible label):

```tsx
<select id="coupon-discount-type" {...register('discountType')} className="w-full rounded-md border border-input bg-surface px-3 py-2 text-sm">
  <option value="Percentage">درصدی</option>
  <option value="FixedAmount">مقدار ثابت</option>
</select>
```

The per-row deactivate action calls
`mockShippingAndCouponAdapter.deactivateCoupon(tenantId, coupon.id)`,
disabled (`disabled={!coupon.isActive}`) once already inactive — there
is no reactivate action anywhere on this page, matching B029's scope
exactly.

## 5. Add both pages to the Shop admin navigation

Edit `src/web/src/components/shell/ShellNav.tsx`. F028 left `navItems`
with two Shop entries after `members`:

```typescript
  { id: 'shop-categories', label: 'دسته‌بندی‌های فروشگاه', icon: ShoppingBag, href: '/t/', tenantScopedSuffix: '/shop/categories' },
  { id: 'shop-products', label: 'محصولات فروشگاه', icon: ShoppingBag, href: '/t/', tenantScopedSuffix: '/shop/products' },
```

Add two more entries right after them:

```typescript
  { id: 'shop-shipping', label: 'نرخ‌های ارسال', icon: ShoppingBag, href: '/t/', tenantScopedSuffix: '/shop/shipping-rates' },
  { id: 'shop-coupons', label: 'کدهای تخفیف', icon: ShoppingBag, href: '/t/', tenantScopedSuffix: '/shop/coupons' },
```

Edit `src/web/src/App.tsx` and add the two matching routes right after
F028's `shop/products` route, inside `ProtectedLayout`:

```tsx
        <Route path="/t/:tenantId/shop/shipping-rates" element={<ShippingRatesPage />} />
        <Route path="/t/:tenantId/shop/coupons" element={<CouponsPage />} />
```

Add the matching imports:

```tsx
import { ShippingRatesPage } from './pages/shop/admin/ShippingRatesPage'
import { CouponsPage } from './pages/shop/admin/CouponsPage'
```

# Non-goals

- No connection to a real API (F035 connects this page once B029 exists).
- No shipping-rate delete UI (B029 has no delete endpoint).

# Acceptance

- Both pages work fully against mocked data, including the coupon
  deactivate action and the shipping-rate upsert-by-province behavior.
- Every required state (idle, loading, empty, validation failure,
  success feedback) is implemented.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Set a shipping rate, update its cost, create a coupon, deactivate it,
  and confirm the list reflects each change.
- No browser console error.

# Lifecycle

Add row `F034` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F029`, and Spec link
`tasks/front/F034-admin-shipping-and-coupon-mock.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/028-shop-checkout.md` is the permanent record and
is never deleted.
