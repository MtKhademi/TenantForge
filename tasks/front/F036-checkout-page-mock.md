---
id: F036
slice: S28
title: Checkout page mock
agent: ui-engineer
source: tasks/slices/028-shop-checkout.md
---

# Objective

Build the checkout page: an address form (province, city, address line,
postal code), a coupon-code field, and a live order summary (subtotal,
discount, shipping, grand total) that recomputes as the shopper edits the
form — against mocked data.

This Spec gives you the exact file path, the exact route, and a concrete
component skeleton whose mocked computation matches B030's real
`CheckoutSummaryResponse` shape field-for-field. Follow it literally.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/028-shop-checkout.md`. This page lives inside the
storefront's public `StorefrontLayout` (F030), reached from the cart
page's (F032/F033) "proceed to checkout" action, which already links to
`` `/shop/${tenantId}/checkout` ``.

# Scope — every file, in order

## 1. `src/web/src/features/shop/mockCheckoutSummary.ts`

Small hardcoded mock computation, matching B030's real
`CheckoutSummaryResponse` fields (`subTotal`, `discountAmount`,
`shippingCost`, `grandTotal`):

```typescript
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
```

## 2. `src/web/src/pages/shop/storefront/CheckoutPage.tsx`

```tsx
import { useCallback, useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useParams } from 'react-router-dom'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { Button } from '@/components/ui/Button'
import { TextInput } from '@/components/ui/TextInput'
import { computeMockCheckoutSummary, type CheckoutSummary } from '@/features/shop/mockCheckoutSummary'

const checkoutSchema = z.object({
  customerName: z.string().min(1, 'نام الزامی است.'),
  customerPhone: z.string().min(1, 'شماره تماس الزامی است.'),
  shippingProvince: z.string().min(1, 'استان الزامی است.'),
  shippingCity: z.string().min(1, 'شهر الزامی است.'),
  shippingAddressLine: z.string().min(1, 'آدرس الزامی است.'),
  shippingPostalCode: z.string().min(1, 'کد پستی الزامی است.'),
  couponCode: z.string(),
})
type CheckoutFormValues = z.infer<typeof checkoutSchema>

export function CheckoutPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const { register, watch, formState: { errors } } = useForm<CheckoutFormValues>({
    resolver: zodResolver(checkoutSchema),
    defaultValues: {
      customerName: '', customerPhone: '', shippingProvince: '', shippingCity: '',
      shippingAddressLine: '', shippingPostalCode: '', couponCode: '',
    },
  })
  const [summary, setSummary] = useState<CheckoutSummary | null>(null)
  const [summaryError, setSummaryError] = useState<string | null>(null)
  const province = watch('shippingProvince')
  const couponCode = watch('couponCode')

  const recompute = useCallback(async () => {
    if (!province) {
      setSummary(null)
      setSummaryError(null)
      return
    }
    try {
      setSummary(await computeMockCheckoutSummary(province, couponCode))
      setSummaryError(null)
    } catch (error) {
      setSummary(null)
      setSummaryError(error instanceof Error ? error.message : 'خطایی رخ داد.')
    }
  }, [province, couponCode])

  useEffect(() => {
    void recompute()
  }, [recompute])

  return (
    <section aria-label="تسویه حساب" className="grid gap-8 md:grid-cols-2">
      <form className="space-y-4" noValidate>
        <h1 className="text-2xl font-semibold">اطلاعات ارسال</h1>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="checkout-name">نام و نام خانوادگی</label>
          <TextInput id="checkout-name" {...register('customerName')} />
          {errors.customerName && <p className="mt-2 text-sm text-destructive">{errors.customerName.message}</p>}
        </div>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="checkout-phone">شماره تماس</label>
          <TextInput id="checkout-phone" {...register('customerPhone')} />
        </div>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="checkout-province">استان</label>
          <TextInput id="checkout-province" {...register('shippingProvince')} />
        </div>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="checkout-city">شهر</label>
          <TextInput id="checkout-city" {...register('shippingCity')} />
        </div>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="checkout-address">آدرس</label>
          <TextInput id="checkout-address" {...register('shippingAddressLine')} />
        </div>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="checkout-postal">کد پستی</label>
          <TextInput id="checkout-postal" {...register('shippingPostalCode')} />
        </div>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="checkout-coupon">کد تخفیف</label>
          <TextInput id="checkout-coupon" {...register('couponCode')} />
        </div>
      </form>

      <div className="space-y-4 rounded-xl border border-border bg-surface p-6 shadow-soft">
        <h2 className="text-lg font-semibold">خلاصه سفارش</h2>
        {summaryError && <p role="alert" className="text-sm font-semibold text-destructive">{summaryError}</p>}
        {summary && (
          <dl className="space-y-2 text-sm">
            <div className="flex justify-between"><dt>جمع کل محصولات</dt><dd>{summary.subTotal.toLocaleString('fa-IR')}</dd></div>
            <div className="flex justify-between"><dt>تخفیف</dt><dd>{summary.discountAmount.toLocaleString('fa-IR')}</dd></div>
            <div className="flex justify-between"><dt>هزینه ارسال</dt><dd>{summary.shippingCost.toLocaleString('fa-IR')}</dd></div>
            <div className="flex justify-between border-t border-border pt-2 font-semibold"><dt>مجموع نهایی</dt><dd>{summary.grandTotal.toLocaleString('fa-IR')}</dd></div>
          </dl>
        )}
        <Link to={`/shop/${tenantId}/order-review`} className="block">
          <Button type="button" className="w-full" disabled={!summary}>ادامه به بررسی سفارش</Button>
        </Link>
      </div>
    </section>
  )
}
```

## 3. Add the route to `App.tsx`

Edit `src/web/src/App.tsx`. Add `checkout` inside the storefront's nested
route block (alongside F032's `cart` route):

```tsx
        <Route path="checkout" element={<CheckoutPage />} />
```

Add the matching import:

```tsx
import { CheckoutPage } from './pages/shop/storefront/CheckoutPage'
```

# Non-goals

- No connection to a real API (F037 connects this page once B030 exists).
- No order creation or payment (F038/F039) — the "ادامه به بررسی سفارش"
  link above points at `/shop/:tenantId/order-review`, which does not
  exist until F038; leave it as a forward reference, matching F032's
  same pattern for its own checkout link.

# Acceptance

- The summary recomputes correctly as the address/coupon fields change,
  against mocked shipping-rate and coupon data.
- The unshippable-province and invalid-coupon mocked cases both show
  clear, distinct messages, never a silent zero/ignored value.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Fill the address form with a shippable and an unshippable mocked
  province and confirm the summary/error behaves correctly for each;
  enter a valid and an invalid mocked coupon code and confirm the same.
- No browser console error.

# Lifecycle

Add row `F036` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F033`, and Spec link
`tasks/front/F036-checkout-page-mock.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/028-shop-checkout.md` is the permanent record and
is never deleted.
