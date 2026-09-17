---
id: F032
slice: S27
title: Cart page mock
agent: ui-engineer
source: tasks/slices/027-shop-cart.md
---

# Objective

Build the storefront cart page: an item list (thumbnail, color/size
label, quantity stepper, remove action), a computed subtotal, a "proceed
to checkout" action and an empty-cart state — against mocked data.

This Spec gives you the exact file path, the exact route to add, and a
concrete component skeleton built directly on F030's
`mockStorefrontCartState.ts` module. Follow it literally.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/027-shop-cart.md`. This page lives inside the same
unauthenticated `StorefrontLayout` F030 built — reuse it, do not create a
second public layout. Read F030's delivered
`src/web/src/features/shop/mockStorefrontCartState.ts` fully — this
task's page reads and mutates that exact module (`mockStorefrontCart`),
it does not create a second, disconnected mock cart store.

# Scope — every file, in order

## 1. `src/web/src/pages/shop/storefront/CartPage.tsx`

```tsx
import { Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { mockStorefrontCart, type MockCartLine } from '@/features/shop/mockStorefrontCartState'

/**
 * S27 storefront cart (F032 mock, F033 connects to B028's real API).
 * Reads/mutates F030's mockStorefrontCart module directly — the exact
 * same store the product detail page's add-to-cart action populates.
 */
export function CartPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [lines, setLines] = useState<MockCartLine[]>(mockStorefrontCart.getLines())

  useEffect(() => {
    return mockStorefrontCart.subscribe(() => setLines([...mockStorefrontCart.getLines()]))
  }, [])

  const subTotal = mockStorefrontCart.subTotal()

  if (lines.length === 0) {
    return (
      <section aria-label="سبد خرید" className="space-y-4 text-center">
        <p className="text-lg font-semibold">سبد خرید شما خالی است</p>
        <Link to={`/shop/${tenantId}`} className="inline-block">
          <Button type="button">بازگشت به فروشگاه</Button>
        </Link>
      </section>
    )
  }

  return (
    <section aria-label="سبد خرید" className="space-y-6">
      <h1 className="text-2xl font-semibold">سبد خرید</h1>

      <div className="divide-y divide-border rounded-xl border border-border bg-surface shadow-soft">
        {lines.map((line) => (
          <div key={line.variantId} className="flex items-center gap-4 p-4">
            <div className="size-16 shrink-0 rounded-md bg-muted" aria-hidden="true" />
            <div className="flex-1">
              <p className="font-semibold">{line.productName}</p>
              <p className="text-sm text-muted-foreground">{line.variantLabel}</p>
            </div>
            <input
              type="number"
              min={1}
              value={line.quantity}
              aria-label={`تعداد ${line.productName}`}
              onChange={(event) => mockStorefrontCart.setQuantity(line.variantId, Math.max(1, Number(event.target.value)))}
              className="w-16 rounded-md border border-input bg-surface px-2 py-1 text-sm"
            />
            <p className="w-24 text-end text-sm font-semibold">
              {(line.unitPrice * line.quantity).toLocaleString('fa-IR')} تومان
            </p>
            <SecondaryButton
              type="button"
              aria-label={`حذف ${line.productName}`}
              onClick={() => mockStorefrontCart.removeLine(line.variantId)}
            >
              <Trash2 aria-hidden="true" className="size-4" />
            </SecondaryButton>
          </div>
        ))}
      </div>

      <div className="flex items-center justify-between rounded-xl border border-border bg-surface p-4 shadow-soft">
        <p className="font-semibold">جمع کل</p>
        <p className="font-semibold">{subTotal.toLocaleString('fa-IR')} تومان</p>
      </div>

      <Link to={`/shop/${tenantId}/checkout`} className="block">
        <Button type="button" className="w-full">ادامه به تسویه حساب</Button>
      </Link>
    </section>
  )
}
```

## 2. Add the route to `App.tsx`

Edit `src/web/src/App.tsx`. F030 left the storefront's nested routes as:

```tsx
      <Route path="/shop/:tenantId" element={<StorefrontLayout />}>
        <Route index element={<CategoryPage />} />
        <Route path="categories/:categorySlug" element={<CategoryPage />} />
        <Route path="products/:productSlug" element={<ProductDetailPage />} />
      </Route>
```

Add the cart route inside the same nested block:

```tsx
      <Route path="/shop/:tenantId" element={<StorefrontLayout />}>
        <Route index element={<CategoryPage />} />
        <Route path="categories/:categorySlug" element={<CategoryPage />} />
        <Route path="products/:productSlug" element={<ProductDetailPage />} />
        <Route path="cart" element={<CartPage />} />
      </Route>
```

Add the matching import:

```tsx
import { CartPage } from './pages/shop/storefront/CartPage'
```

This is also the route `StorefrontLayout.tsx`'s cart-icon link
(`` `/shop/${tenantId}/cart` ``, already written in F030) already points
to.

# Non-goals

- No connection to a real API (F033 connects this page once B028 exists).
- No checkout page itself (F036) — the "proceed to checkout" link above
  points at `/shop/:tenantId/checkout`, which does not exist until F036;
  leave the link as-is (a working route it will simply be a 404 to at
  this stage is expected and matches every other cross-task forward
  reference in this repository's task history).

# Acceptance

- The cart page reflects the same mocked cart state F030's add-to-cart
  action populates, with working quantity change and remove actions and a
  correct subtotal.
- The empty-cart state renders correctly when no items are present.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Add an item from the product detail page, open the cart, change its
  quantity, remove it, and confirm the empty state appears; add it back
  and confirm the subtotal recomputes correctly.
- No browser console error.

# Lifecycle

Add row `F032` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F030`, and Spec link
`tasks/front/F032-cart-page-mock.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/027-shop-cart.md` is the permanent record and is
never deleted.
