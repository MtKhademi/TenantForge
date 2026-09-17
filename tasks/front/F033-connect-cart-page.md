---
id: F033
slice: S27
title: Connect cart page to the real API
agent: ui-engineer
source: tasks/slices/027-shop-cart.md
---

# Objective

Replace the mocked cart state F030/F032 built with real calls to B028's
cart API, including persisting the cart id in the browser across page
loads.

This Spec gives you the exact `cartStorage.ts` adapter, the exact real
cart types/adapter (matching B028's `CartContracts.cs` field-for-field)
and the exact call-site changes in `ProductDetailPage.tsx` and
`CartPage.tsx`. Follow it literally.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/027-shop-cart.md`, in particular its storage-
convention reasoning. Read `src/web/src/features/auth/httpAuthAdapter.ts`
fully for the existing client-side-credential adapter shape (a small
typed module, storage-backed, JSON-serialized, guarded by try/catch —
see its `SESSION_KEY`/`getSession()`/`signOut()` trio) before writing
the cart-id adapter below. Follow that same adapter shape but back it
with `localStorage`, not `sessionStorage` — the slice document states
this deliberately: a session should end when the tab closes, a shopping
cart should not. Read B028's delivered endpoint shapes (from its
integration tests or, if the Spec file still exists at task start,
`tasks/backend/B028-cart-persistence-and-api.md`) before writing the
real adapter — do not guess field names/casing from memory.

# Scope — every file, in order

## 1. `src/web/src/features/shop/cartStorage.ts`

```typescript
/**
 * S27 cart-id persistence (F033). Mirrors httpAuthAdapter.ts's shape
 * (a small typed module, JSON-serialized, guarded by try/catch) but
 * deliberately uses localStorage, not sessionStorage: a shopping cart
 * should survive a closed tab/browser restart, unlike an auth session.
 */
const CART_ID_KEY = 'tenantforge:shop:cartId'

export function getCartId(): string | null {
  try {
    return window.localStorage.getItem(CART_ID_KEY)
  } catch {
    return null
  }
}

export function setCartId(cartId: string): void {
  try {
    window.localStorage.setItem(CART_ID_KEY, cartId)
  } catch {
    // Storage unavailable (private mode, quota, etc.) — the cart still
    // works for the current page's lifetime via in-memory adapter state;
    // it simply will not survive a reload. Silently ignored, matching
    // the design system's existing browser-storage guidance.
  }
}

export function clearCartId(): void {
  try {
    window.localStorage.removeItem(CART_ID_KEY)
  } catch {
    // See setCartId — best-effort only.
  }
}
```

## 2. `src/web/src/features/shop/cartTypes.ts`

The exact shape B028's real API returns (field names copied from
B028's `CartContracts.cs`, camelCased):

```typescript
export type CartItemResponse = {
  id: string
  productVariantId: string
  productName: string
  variantLabel: string
  quantity: number
  unitPrice: number
}

export type CartResponse = {
  cartId: string
  items: CartItemResponse[]
  subTotal: number
}

export class InsufficientStockError extends Error {
  constructor(message = 'موجودی کافی برای این تعداد وجود ندارد.') {
    super(message)
    this.name = 'InsufficientStockError'
  }
}
```

## 3. `src/web/src/features/shop/cartAdapter.ts`

```typescript
import { ApiUnavailableError } from '@/features/auth/authTypes'
import { clearCartId, getCartId, setCartId } from './cartStorage'
import { InsufficientStockError, type CartResponse } from './cartTypes'

const REQUEST_TIMEOUT_MS = 8_000

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return { signal: controller.signal, clear: () => window.clearTimeout(timeoutId) }
}

async function request(path: string, init?: RequestInit): Promise<Response> {
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

async function createCart(tenantId: string): Promise<string> {
  const response = await request(`/api/shop/${tenantId}/carts`, { method: 'POST' })
  if (!response.ok) throw new ApiUnavailableError()
  const body = (await readJson(response)) as { cartId: string }
  setCartId(body.cartId)
  return body.cartId
}

/**
 * Resolves a usable cart id: the stored one if present, otherwise a
 * freshly created one (also persisting it). Every mutating call below
 * goes through this first.
 */
async function ensureCartId(tenantId: string): Promise<string> {
  const stored = getCartId()
  if (stored) return stored
  return createCart(tenantId)
}

async function parseCartResponse(response: Response): Promise<CartResponse> {
  if (response.status === 409) throw new InsufficientStockError()
  if (!response.ok) throw new ApiUnavailableError()
  return (await readJson(response)) as CartResponse
}

export const cartAdapter = {
  /**
   * Adds a variant to the cart. If no cart id is stored yet, creates one
   * first. If the stored cart id is rejected (404 — e.g. it no longer
   * exists after a later task clears it post-order), clears it and
   * retries once against a freshly created cart, transparently.
   */
  async addItem(tenantId: string, productVariantId: string, quantity: number): Promise<CartResponse> {
    const cartId = await ensureCartId(tenantId)
    const response = await request(`/api/shop/${tenantId}/carts/${cartId}/items`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ productVariantId, quantity }),
    })
    if (response.status === 404) {
      clearCartId()
      const freshCartId = await createCart(tenantId)
      const retry = await request(`/api/shop/${tenantId}/carts/${freshCartId}/items`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ productVariantId, quantity }),
      })
      return parseCartResponse(retry)
    }
    return parseCartResponse(response)
  },

  async updateItemQuantity(tenantId: string, itemId: string, quantity: number): Promise<CartResponse> {
    const cartId = await ensureCartId(tenantId)
    const response = await request(`/api/shop/${tenantId}/carts/${cartId}/items/${itemId}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ quantity }),
    })
    return parseCartResponse(response)
  },

  async removeItem(tenantId: string, itemId: string): Promise<CartResponse> {
    const cartId = await ensureCartId(tenantId)
    const response = await request(`/api/shop/${tenantId}/carts/${cartId}/items/${itemId}`, { method: 'DELETE' })
    return parseCartResponse(response)
  },

  async getCart(tenantId: string): Promise<CartResponse | null> {
    const cartId = getCartId()
    if (!cartId) return null
    const response = await request(`/api/shop/${tenantId}/carts/${cartId}`)
    if (response.status === 404) {
      clearCartId()
      return null
    }
    return parseCartResponse(response)
  },
}
```

## 4. Update `ProductDetailPage.tsx`'s add-to-cart action

In `src/web/src/pages/shop/storefront/ProductDetailPage.tsx`, replace the
import `import { mockStorefrontCart } from
'@/features/shop/mockStorefrontCartState'` with:

```tsx
import { cartAdapter } from '@/features/shop/cartAdapter'
```

Replace the button's `onClick` body (the one calling
`mockStorefrontCart.addLine(...)`) with:

```tsx
onClick={async () => {
  if (!selectedVariant) return
  try {
    await cartAdapter.addItem(tenantId, selectedVariant.id, quantity)
    setAddedMessage('به سبد خرید افزوده شد.')
  } catch (error) {
    setAddedMessage(error instanceof Error ? error.message : 'افزودن به سبد خرید ممکن نشد.')
  }
}}
```

## 5. Rewrite `CartPage.tsx` against the real cart

Replace F032's `mockStorefrontCart`-based state with a real fetch on
mount and after every mutation:

```tsx
import { Trash2 } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { cartAdapter } from '@/features/shop/cartAdapter'
import { InsufficientStockError, type CartResponse } from '@/features/shop/cartTypes'

export function CartPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [cart, setCart] = useState<CartResponse | null | undefined>(undefined)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    void cartAdapter.getCart(tenantId).then(setCart)
  }, [tenantId])

  useEffect(() => {
    load()
  }, [load])

  if (cart === undefined) return <p>در حال بارگذاری...</p>

  if (cart === null || cart.items.length === 0) {
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
      {error && <p role="alert" className="text-sm font-semibold text-destructive">{error}</p>}

      <div className="divide-y divide-border rounded-xl border border-border bg-surface shadow-soft">
        {cart.items.map((item) => (
          <div key={item.id} className="flex items-center gap-4 p-4">
            <div className="size-16 shrink-0 rounded-md bg-muted" aria-hidden="true" />
            <div className="flex-1">
              <p className="font-semibold">{item.productName}</p>
              <p className="text-sm text-muted-foreground">{item.variantLabel}</p>
            </div>
            <input
              type="number"
              min={1}
              defaultValue={item.quantity}
              aria-label={`تعداد ${item.productName}`}
              onBlur={async (event) => {
                setError(null)
                try {
                  const updated = await cartAdapter.updateItemQuantity(tenantId, item.id, Math.max(1, Number(event.target.value)))
                  setCart(updated)
                } catch (err) {
                  setError(err instanceof InsufficientStockError ? err.message : 'به‌روزرسانی تعداد ممکن نشد.')
                }
              }}
              className="w-16 rounded-md border border-input bg-surface px-2 py-1 text-sm"
            />
            <p className="w-24 text-end text-sm font-semibold">
              {(item.unitPrice * item.quantity).toLocaleString('fa-IR')} تومان
            </p>
            <SecondaryButton
              type="button"
              aria-label={`حذف ${item.productName}`}
              onClick={async () => {
                const updated = await cartAdapter.removeItem(tenantId, item.id)
                setCart(updated)
              }}
            >
              <Trash2 aria-hidden="true" className="size-4" />
            </SecondaryButton>
          </div>
        ))}
      </div>

      <div className="flex items-center justify-between rounded-xl border border-border bg-surface p-4 shadow-soft">
        <p className="font-semibold">جمع کل</p>
        <p className="font-semibold">{cart.subTotal.toLocaleString('fa-IR')} تومان</p>
      </div>

      <Link to={`/shop/${tenantId}/checkout`} className="block">
        <Button type="button" className="w-full">ادامه به تسویه حساب</Button>
      </Link>
    </section>
  )
}
```

## 6. Delete the mock

Delete `src/web/src/features/shop/mockStorefrontCartState.ts` and update
`StorefrontLayout.tsx`'s cart-icon count: replace its
`mockStorefrontCart.itemCount()`/`subscribe` usage with a plain
`cartAdapter.getCart(tenantId)` call in a `useEffect` that also re-runs
whenever the route changes (e.g. depend on `useLocation().pathname`),
since there is no longer a synchronous in-memory store to subscribe to.

# Non-goals

- No checkout page (F036).
- No change to the storefront browsing pages beyond wiring the
  add-to-cart action to the real cart adapter.

# If you get stuck

```bash
curl -X POST http://localhost:5080/api/shop/<tenantId>/carts
```

Expected: `201 Created`, `{"cartId":"..."}`. Copy that id and confirm
`localStorage.getItem('tenantforge:shop:cartId')` in the browser devtools
console matches it after clicking "add to cart" once in the running app.

# Acceptance

- Adding an item from the product detail page creates a real cart (first
  time) or adds to the existing one, persisted via `localStorage` and
  surviving a full page reload/browser restart within the same browser
  profile.
- Cart quantity changes and removals call the real API and the displayed
  subtotal matches the API's computed value.
- A stock-insufficient add/update attempt shows the API's clear error,
  not a silent no-op.
- No mock cart state remains reachable.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Add an item, reload the page, and confirm the cart still shows the
  item (proving `localStorage` persistence and the real API, not
  component state). Change quantity beyond available stock and confirm
  the clear rejection.
- No new browser console error.

# Lifecycle

Add row `F033` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependencies `F032, B028`, and Spec link
`tasks/front/F033-connect-cart-page.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/027-shop-cart.md` is the permanent record and is
never deleted.
