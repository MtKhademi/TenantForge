---
id: F031
slice: S26
title: Connect storefront browsing and detail to the real API
agent: ui-engineer
source: tasks/slices/026-shop-catalog.md
---

# Objective

Replace F030's mocked storefront category/product-detail data with real,
anonymous calls to B027's public storefront catalog API.

This Spec gives you the exact adapter file, its exact function
signatures (matching F030's mock catalog module one-for-one) and the
exact call-site changes in `CategoryPage.tsx`/`ProductDetailPage.tsx`.
Follow it literally.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/026-shop-catalog.md`. Read B027's delivered
endpoint shapes (from its integration tests or, if the Spec file still
exists at task start, `tasks/backend/B027-public-storefront-catalog-api.md`)
before writing the real adapter — do not guess field names/casing from
memory. This Spec's DTOs below already match B027's
`StorefrontContracts.cs` field-for-field (camelCased) — if B027's
delivered code differs, follow the delivered code, not this Spec.

Unlike every authenticated adapter in this repository
(`userAdapter.ts`, F029's `shopCatalogAdapter.ts`), this adapter sends
**no** `Authorization` header at all, on any call — there is no session
here (`useAuth()` is never called on this page family). Read
`src/web/src/features/users/userAdapter.ts` only for its
`createRequestAbortSignal()`/`request()`/strict-`parseX()` shape, not
its auth header handling.

# Scope — every file, in order

## 1. `src/web/src/features/shop/storefrontAdapter.ts`

```typescript
import { ApiUnavailableError } from '@/features/auth/authTypes'
import type { StorefrontCategory, StorefrontProductDetail, StorefrontProductSummary } from './storefrontTypes'

/**
 * S26 storefront browsing — real, anonymous API data source (F031),
 * replacing F030's mock. No Authorization header is ever sent from any
 * function in this file — the storefront works identically fully
 * signed out.
 */
const REQUEST_TIMEOUT_MS = 8_000

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return { signal: controller.signal, clear: () => window.clearTimeout(timeoutId) }
}

async function request(path: string): Promise<Response> {
  const abort = createRequestAbortSignal()
  try {
    // No headers object at all — confirms no Authorization is ever attached.
    return await fetch(path, { method: 'GET', signal: abort.signal })
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

export const storefrontAdapter = {
  async listCategories(tenantId: string): Promise<StorefrontCategory[]> {
    const response = await request(`/api/shop/${tenantId}/categories`)
    if (!response.ok) throw new ApiUnavailableError()
    const body = (await readJson(response)) as { categories: StorefrontCategory[] }
    return body.categories
  },

  async listProducts(tenantId: string, categorySlug: string): Promise<StorefrontProductSummary[]> {
    const response = await request(
      `/api/shop/${tenantId}/categories/${categorySlug}/products?pageNumber=1&pageSize=50`,
    )
    if (response.status === 404) return []
    if (!response.ok) throw new ApiUnavailableError()
    const body = (await readJson(response)) as { products: StorefrontProductSummary[] }
    return body.products
  },

  async getProduct(tenantId: string, productSlug: string): Promise<StorefrontProductDetail | null> {
    const response = await request(`/api/shop/${tenantId}/products/${productSlug}`)
    if (response.status === 404) return null
    if (!response.ok) throw new ApiUnavailableError()
    return (await readJson(response)) as StorefrontProductDetail
  },
}
```

Note the real `StorefrontProductSummary`/`StorefrontProductDetail`
responses from B027 carry no `imageUrl`/`imageUrls` field (B027's Scope
explicitly has no image field) — F030's mock types included
mock-only `imageUrl`/`imageUrls` fields for the thumbnail/gallery boxes.
Since this task's pages never had real images to begin with (F030's
Non-goals: "No image upload UI"), leave the gallery/thumbnail blocks as
the plain `bg-muted` placeholder `<div>` already in `CategoryPage.tsx`/
`ProductDetailPage.tsx` — do not attempt to read `imageUrl` from the
real response; if the field is present in your local
`storefrontTypes.ts` from F030, remove it from `StorefrontProductSummary`/
`StorefrontProductDetail` now that nothing populates it.

## 2. Update `CategoryPage.tsx` and `ProductDetailPage.tsx`

In both `src/web/src/pages/shop/storefront/CategoryPage.tsx` and
`ProductDetailPage.tsx`:

1. Replace the import
   `import { mockStorefrontCatalog } from '@/features/shop/mockStorefrontCatalog'`
   with:
   ```tsx
   import { storefrontAdapter } from '@/features/shop/storefrontAdapter'
   ```
2. Replace every `mockStorefrontCatalog.xxx(...)` call with
   `storefrontAdapter.xxx(...)` — same argument order and count, no
   other change needed since the function signatures match exactly.

## 3. Delete the mock

Delete `src/web/src/features/shop/mockStorefrontCatalog.ts`. Confirm no
remaining import references it. Do not delete
`mockStorefrontCartState.ts` — that is the cart mock F032/F033 still
own; this task only removes the *catalog* mock.

# Non-goals

- No change to the cart page (F032/F033 handle the real cart).
- No new screen or interaction beyond what F030 already built.

# If you get stuck

```bash
curl http://localhost:5080/api/shop/<tenantId>/categories
```

Run this with your browser's network tab open on the real page and
confirm zero `Authorization` header is attached to the equivalent
in-app request — a passing build with a header silently added by a
shared `fetch` wrapper would be a real bug this call surfaces.

# Acceptance

- The category grid, product cards and product detail page all load
  real data with no `Authorization` header present in the request.
- Variant selectors reflect real, live stock; a variant that goes out of
  stock (verified by depleting it via a prior manual cart/checkout step,
  once available, or directly in the database for this task) shows as
  sold out.
- A nonexistent or inactive product slug shows the page's not-found
  state, not a broken/blank page.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844, fully signed out:

- Browse the real storefront end-to-end from category grid to product
  detail, confirm live stock/size-guide data matches what was created
  via the connected admin screens (F029), and confirm a deactivated
  product's slug shows a clean not-found state.
- No new browser console error.

# Lifecycle

Add row `F031` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependencies `F030, B027`, and Spec link
`tasks/front/F031-connect-storefront-catalog-browsing.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/026-shop-catalog.md` is the permanent record and is
never deleted.
