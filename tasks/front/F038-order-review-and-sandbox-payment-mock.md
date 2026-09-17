---
id: F038
slice: S29
title: Order review and sandbox payment mock
agent: ui-engineer
source: tasks/slices/029-shop-order-and-sandbox-payment.md
---

# Objective

Build an order-review/summary screen, the in-app fake "bank page"
(approve/decline buttons) the Sandbox payment provider redirects to, and
a payment-result screen — against mocked data.

This Spec gives you the exact file paths, the exact routes, a small
draft-passing module so the checkout form's data survives across the
review/bank/result pages, and a concrete component skeleton for each
page. Follow it literally.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/029-shop-order-and-sandbox-payment.md`, in
particular its statement that the "bank page" is entirely an in-app
frontend screen (no real bank, no external redirect) standing in for a
real gateway's hosted payment page. Make its visual language deliberately
distinct from the rest of TenantForge's storefront (a plain, obviously
"simulation" bank-page treatment — for example a clearly labeled "Sandbox
Bank" header) so nobody mistakes it for a real payment provider's UI.

F037's `CheckoutPage.tsx` collects the address/coupon fields and the
computed summary but keeps them only in its own component state — they
do not currently survive a navigation to a different route. This task
needs that data on the review, bank and result pages, so it introduces
one small carrier module (`orderDraftState.ts` below) that
`CheckoutPage.tsx`'s "proceed to order review" action writes to just
before navigating.

# Scope — every file, in order

## 1. `src/web/src/features/shop/orderDraftState.ts`

```typescript
/**
 * S28/S29 checkout → order-review → bank → result flow (F036–F039).
 * A small, module-scoped carrier for the checkout inputs and computed
 * summary collected on CheckoutPage — sessionStorage-backed (not
 * localStorage: an order draft, like an auth session, has no reason to
 * outlive the tab) so a reload on the review/bank/result pages does not
 * lose it, matching this repository's existing sessionStorage-for-
 * session-scoped-data convention (httpAuthAdapter.ts).
 */
const DRAFT_KEY = 'tenantforge:shop:orderDraft'

export type OrderDraft = {
  customerName: string
  customerPhone: string
  shippingProvince: string
  shippingCity: string
  shippingAddressLine: string
  shippingPostalCode: string
  couponCode: string | null
  subTotal: number
  discountAmount: number
  shippingCost: number
  grandTotal: number
}

export function saveOrderDraft(draft: OrderDraft): void {
  try {
    window.sessionStorage.setItem(DRAFT_KEY, JSON.stringify(draft))
  } catch {
    // Best-effort only, matching the design system's storage guidance.
  }
}

export function loadOrderDraft(): OrderDraft | null {
  try {
    const raw = window.sessionStorage.getItem(DRAFT_KEY)
    return raw ? (JSON.parse(raw) as OrderDraft) : null
  } catch {
    return null
  }
}

export function clearOrderDraft(): void {
  try {
    window.sessionStorage.removeItem(DRAFT_KEY)
  } catch {
    // Best-effort only.
  }
}
```

## 2. Update `CheckoutPage.tsx`'s "proceed" action

In `src/web/src/pages/shop/storefront/CheckoutPage.tsx`, replace the
plain `<Link to={.../order-review}>` wrapping the primary button with a
button whose `onClick` saves the draft first, then navigates
programmatically (import `useNavigate` from `react-router-dom` and
`saveOrderDraft` from `@/features/shop/orderDraftState`):

```tsx
<Button
  type="button"
  className="w-full"
  disabled={!summary}
  onClick={() => {
    if (!summary) return
    const values = getValues()
    saveOrderDraft({
      customerName: values.customerName,
      customerPhone: values.customerPhone,
      shippingProvince: values.shippingProvince,
      shippingCity: values.shippingCity,
      shippingAddressLine: values.shippingAddressLine,
      shippingPostalCode: values.shippingPostalCode,
      couponCode: values.couponCode || null,
      ...summary,
    })
    navigate(`/shop/${tenantId}/order-review`)
  }}
>
  ادامه به بررسی سفارش
</Button>
```

Add `const navigate = useNavigate()` alongside the component's other
hooks, and add `getValues` to the destructured `useForm()` result if not
already there from F037.

## 3. `src/web/src/pages/shop/storefront/OrderReviewPage.tsx`

```tsx
import { useEffect, useState } from 'react'
import { Link, Navigate, useParams } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { loadOrderDraft, type OrderDraft } from '@/features/shop/orderDraftState'

export function OrderReviewPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [draft, setDraft] = useState<OrderDraft | null | undefined>(undefined)

  useEffect(() => {
    setDraft(loadOrderDraft())
  }, [])

  if (draft === undefined) return null
  if (draft === null) return <Navigate to={`/shop/${tenantId}/checkout`} replace />

  return (
    <section aria-label="بررسی نهایی سفارش" className="max-w-xl space-y-6">
      <h1 className="text-2xl font-semibold">بررسی نهایی سفارش</h1>

      <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
        <p className="font-semibold">{draft.customerName}</p>
        <p className="text-sm text-muted-foreground">{draft.customerPhone}</p>
        <p className="mt-2 text-sm">
          {draft.shippingProvince}، {draft.shippingCity}، {draft.shippingAddressLine} — {draft.shippingPostalCode}
        </p>
      </div>

      <dl className="space-y-2 rounded-xl border border-border bg-surface p-5 text-sm shadow-soft">
        <div className="flex justify-between"><dt>جمع کل محصولات</dt><dd>{draft.subTotal.toLocaleString('fa-IR')}</dd></div>
        <div className="flex justify-between"><dt>تخفیف</dt><dd>{draft.discountAmount.toLocaleString('fa-IR')}</dd></div>
        <div className="flex justify-between"><dt>هزینه ارسال</dt><dd>{draft.shippingCost.toLocaleString('fa-IR')}</dd></div>
        <div className="flex justify-between border-t border-border pt-2 font-semibold"><dt>مجموع نهایی</dt><dd>{draft.grandTotal.toLocaleString('fa-IR')}</dd></div>
      </dl>

      <Link to={`/shop/${tenantId}/bank`} className="block">
        <Button type="button" className="w-full">ثبت سفارش و پرداخت</Button>
      </Link>
    </section>
  )
}
```

## 4. `src/web/src/pages/shop/storefront/SandboxBankPage.tsx`

Deliberately distinct visual language — a flat, obviously-fake
treatment (a dashed border, a plain gray background, a large
"SANDBOX" watermark-style label), never TenantForge's own storefront
styling:

```tsx
import { Link, useParams } from 'react-router-dom'
import { loadOrderDraft } from '@/features/shop/orderDraftState'

export function SandboxBankPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const draft = loadOrderDraft()

  return (
    <div className="mx-auto max-w-md rounded-lg border-2 border-dashed border-neutral-400 bg-neutral-100 p-8 text-center text-neutral-900">
      <p className="text-xs font-bold uppercase tracking-widest text-neutral-500">Sandbox Bank — شبیه‌سازی پرداخت</p>
      <p className="mt-4 text-sm">این یک درگاه پرداخت واقعی نیست.</p>
      <p className="mt-2 text-2xl font-bold">{draft?.grandTotal.toLocaleString('fa-IR') ?? '—'} تومان</p>
      <div className="mt-6 flex gap-3">
        <Link
          to={`/shop/${tenantId}/payment-result?outcome=approved`}
          className="flex-1 rounded-md bg-neutral-800 py-2 text-sm font-semibold text-white"
        >
          Approve
        </Link>
        <Link
          to={`/shop/${tenantId}/payment-result?outcome=declined`}
          className="flex-1 rounded-md border border-neutral-400 py-2 text-sm font-semibold"
        >
          Decline
        </Link>
      </div>
    </div>
  )
}
```

## 5. `src/web/src/pages/shop/storefront/PaymentResultPage.tsx`

```tsx
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { Button } from '@/components/ui/Button'

/** Mock order number/tracking code — F039 replaces these with B031's real values. */
const MOCK_ORDER_NUMBER = 'ORD-000001'
const MOCK_TRACKING_CODE = 'MOCKTRACK123'

export function PaymentResultPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [searchParams] = useSearchParams()
  const approved = searchParams.get('outcome') === 'approved'

  if (approved) {
    return (
      <section aria-label="نتیجه پرداخت" className="max-w-md space-y-4 text-center">
        <p className="text-lg font-semibold text-success">پرداخت با موفقیت انجام شد</p>
        <p>شماره سفارش: {MOCK_ORDER_NUMBER}</p>
        <p>کد پیگیری: {MOCK_TRACKING_CODE}</p>
        <Link to={`/shop/${tenantId}`}>
          <Button type="button">بازگشت به فروشگاه</Button>
        </Link>
      </section>
    )
  }

  return (
    <section aria-label="نتیجه پرداخت" className="max-w-md space-y-4 text-center">
      <p className="text-lg font-semibold text-destructive">پرداخت ناموفق بود</p>
      <p className="text-sm text-muted-foreground">سفارش شما همچنان در انتظار پرداخت است.</p>
      <Link to={`/shop/${tenantId}/bank`}>
        <Button type="button">تلاش دوباره</Button>
      </Link>
    </section>
  )
}
```

## 6. Add the three routes to `App.tsx`

Edit `src/web/src/App.tsx`. Add these inside the storefront's nested
route block, after F036's `checkout` route:

```tsx
        <Route path="order-review" element={<OrderReviewPage />} />
        <Route path="bank" element={<SandboxBankPage />} />
        <Route path="payment-result" element={<PaymentResultPage />} />
```

Add the matching imports:

```tsx
import { OrderReviewPage } from './pages/shop/storefront/OrderReviewPage'
import { SandboxBankPage } from './pages/shop/storefront/SandboxBankPage'
import { PaymentResultPage } from './pages/shop/storefront/PaymentResultPage'
```

# Non-goals

- No connection to a real API (F039 connects this flow once B031/B032
  exist).
- No real payment gateway UI/branding anywhere — the Sandbox page must
  never resemble a real bank's page.

# Acceptance

- The review → bank page → result flow works fully against mocked data
  for both the approve and decline paths.
- The bank page is unmistakably a simulation, not a real payment
  provider's UI.
- The result page shows the order number and tracking code on success.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Walk through review → approve → success result, then review → decline
  → failure result with a retry action back to the bank page.
- No browser console error.

# Lifecycle

Add row `F038` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F037`, and Spec link
`tasks/front/F038-order-review-and-sandbox-payment-mock.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/029-shop-order-and-sandbox-payment.md` is the
permanent record and is never deleted.
