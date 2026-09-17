---
id: F040
slice: S30
title: Order tracking page mock
agent: ui-engineer
source: tasks/slices/030-guest-order-tracking.md
---

# Objective

Build the guest order-tracking page: a tracking-code + phone-number form
and an order-status result view — against mocked data.

This Spec gives you the exact file path, the exact route, and a concrete
component skeleton whose mocked lookup matches B033's real
`OrderLookupResponse` shape field-for-field. Follow it literally.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/030-guest-order-tracking.md`, in particular its
rule that both fields are always required together and that a lookup
failure shows one generic message regardless of which field was wrong.

# Scope — every file, in order

## 1. `src/web/src/features/shop/mockOrderLookup.ts`

Field names matching B033's real `OrderLookupResponse`:

```typescript
export type OrderLookupItem = {
  productNameSnapshot: string
  variantLabelSnapshot: string
  unitPrice: number
  quantity: number
}

export type OrderLookupResult = {
  orderNumber: string
  status: string
  createdAtUtc: string
  shippingProvince: string
  shippingCity: string
  shippingAddressLine: string
  shippingPostalCode: string
  subTotal: number
  shippingCost: number
  discountAmount: number
  grandTotal: number
  items: OrderLookupItem[]
}

const MOCK_TRACKING_CODE = 'MOCKTRACK123'
const MOCK_PHONE = '09121234567'

export async function mockLookupOrder(trackingCode: string, customerPhone: string): Promise<OrderLookupResult | null> {
  if (trackingCode.trim() !== MOCK_TRACKING_CODE || customerPhone.trim() !== MOCK_PHONE) {
    return null
  }
  return {
    orderNumber: 'ORD-000001',
    status: 'Paid',
    createdAtUtc: new Date().toISOString(),
    shippingProvince: 'تهران',
    shippingCity: 'تهران',
    shippingAddressLine: 'خیابان ولیعصر',
    shippingPostalCode: '1234567890',
    subTotal: 890_000,
    shippingCost: 50_000,
    discountAmount: 89_000,
    grandTotal: 851_000,
    items: [{ productNameSnapshot: 'پیراهن کلاسیک', variantLabelSnapshot: 'سفید / M', unitPrice: 890_000, quantity: 1 }],
  }
}
```

## 2. `src/web/src/pages/shop/storefront/OrderTrackingPage.tsx`

```tsx
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { Button } from '@/components/ui/Button'
import { TextInput } from '@/components/ui/TextInput'
import { mockLookupOrder, type OrderLookupResult } from '@/features/shop/mockOrderLookup'

const trackingSchema = z.object({
  trackingCode: z.string().min(1, 'کد پیگیری الزامی است.'),
  customerPhone: z.string().min(1, 'شماره تماس الزامی است.'),
})
type TrackingFormValues = z.infer<typeof trackingSchema>

/**
 * S30 guest order tracking (F040 mock, F041 connects to B033). Both
 * fields are always required together, and a failed lookup always shows
 * one generic message — never branching copy on which field was wrong.
 */
export function OrderTrackingPage() {
  const { register, handleSubmit, formState: { errors } } = useForm<TrackingFormValues>({
    resolver: zodResolver(trackingSchema),
    defaultValues: { trackingCode: '', customerPhone: '' },
  })
  const [result, setResult] = useState<OrderLookupResult | null | undefined>(undefined)

  const onSubmit = async (values: TrackingFormValues) => {
    setResult(await mockLookupOrder(values.trackingCode, values.customerPhone))
  }

  return (
    <section aria-label="پیگیری سفارش" className="mx-auto max-w-md space-y-6">
      <h1 className="text-2xl font-semibold">پیگیری سفارش</h1>

      <form className="space-y-4" onSubmit={handleSubmit(onSubmit)} noValidate>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="tracking-code">کد پیگیری</label>
          <TextInput id="tracking-code" {...register('trackingCode')} />
          {errors.trackingCode && <p className="mt-2 text-sm text-destructive">{errors.trackingCode.message}</p>}
        </div>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="tracking-phone">شماره تماس</label>
          <TextInput id="tracking-phone" {...register('customerPhone')} />
          {errors.customerPhone && <p className="mt-2 text-sm text-destructive">{errors.customerPhone.message}</p>}
        </div>
        <Button type="submit" className="w-full">پیگیری</Button>
      </form>

      {result === null && (
        <p role="alert" className="text-sm font-semibold text-destructive">
          سفارشی با این کد پیگیری و شماره تماس یافت نشد.
        </p>
      )}

      {result && (
        <div className="space-y-4 rounded-xl border border-border bg-surface p-5 shadow-soft">
          <p className="font-semibold">سفارش {result.orderNumber} — {result.status}</p>
          <ul className="space-y-1 text-sm">
            {result.items.map((item, index) => (
              <li key={index}>{item.productNameSnapshot} ({item.variantLabelSnapshot}) × {item.quantity}</li>
            ))}
          </ul>
          <p className="text-sm font-semibold">مجموع نهایی: {result.grandTotal.toLocaleString('fa-IR')} تومان</p>
        </div>
      )}
    </section>
  )
}
```

## 3. Link the page and add the route

In `src/web/src/pages/shop/storefront/StorefrontLayout.tsx`'s header,
add a link next to the cart-icon link:

```tsx
<Link to={`/shop/${tenantId}/track-order`} className="text-sm font-medium hover:underline">
  پیگیری سفارش
</Link>
```

Edit `src/web/src/App.tsx`. Add the route inside the storefront's nested
route block:

```tsx
        <Route path="track-order" element={<OrderTrackingPage />} />
```

Add the matching import:

```tsx
import { OrderTrackingPage } from './pages/shop/storefront/OrderTrackingPage'
```

# Non-goals

- No connection to a real API (F041 connects this page once B033 exists).
- No order-modification action from this page.

# Acceptance

- Submitting a mocked valid pair shows the order's status/items/totals;
  submitting a mocked invalid pair shows the one generic not-found
  message, never a message that reveals which field was wrong.
- Submit is blocked (with a clear validation message) when either field
  is empty.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Submit a mocked valid pair and a mocked invalid pair and confirm the
  two distinct outcomes (result view vs. generic not-found).
- No browser console error.

# Lifecycle

Add row `F040` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F039`, and Spec link
`tasks/front/F040-order-tracking-mock.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/030-guest-order-tracking.md` is the permanent
record and is never deleted.
