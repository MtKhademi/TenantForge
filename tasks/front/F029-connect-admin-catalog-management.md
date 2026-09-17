---
id: F029
slice: S26
title: Connect admin catalog management to the real API
agent: ui-engineer
source: tasks/slices/026-shop-catalog.md
---

# Objective

Replace F028's mocked category/product admin screens with real calls to
B026's category and product admin API.

This Spec gives you the exact adapter file to add, its exact
function signatures (matching F028's `ShopCatalogAdapter` type
one-for-one, so the page components' call sites need zero changes),
the exact error-mapping code, and the exact two-line change to delete
the mock. Follow it literally.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/026-shop-catalog.md`. Read B026's delivered
endpoint shapes (from its integration tests or, if the Spec file still
exists at task start, `tasks/backend/B026-category-and-product-admin-api.md`)
before writing the real adapter — do not guess field names/casing from
memory. This Spec's DTOs below already match B026's `CategoryContracts.cs`/
`ProductContracts.cs` field-for-field (camelCased) — if B026's delivered
code differs from what this Spec assumes, follow the delivered code, not
this Spec.

Read `src/web/src/features/users/userAdapter.ts` fully for the exact
real-adapter shape this task's Shop adapter must match: a
`createRequestAbortSignal()` helper (8-second timeout), an
`authHeaders(accessToken)` helper, a `request()` wrapper that throws
`ApiUnavailableError` on a network failure, strict `parseX()` functions
that throw `ApiUnavailableError` on a malformed body, and dedicated
`Error` subclasses per distinct HTTP rejection (401 → `SessionExpiredError`
from `@/features/auth/authTypes`, 403 → a new `ShopForbiddenError`, 409 →
`CategoryConflictError`/`ProductConflictError` already defined in F028's
`shopCatalogTypes.ts`, 400 → a new `ShopValidationError` carrying field
errors). Read `src/web/src/features/auth/AuthContext.tsx`'s exported
`useAuth()` shape to see how `session.accessToken` is obtained in a page
component (`UsersPage.tsx` line `const { session, signOut } = useAuth()`)
— this task's pages need the same call added where F028 currently passes
no token to the mock adapter.

# Scope — every file, in order

## 1. `src/web/src/features/shop/shopCatalogAdapter.ts`

Create this file (a new adapter module, separate from F028's now-deleted
`mockCatalogAdapter.ts`, but implementing the exact same
`ShopCatalogAdapter` type from `shopCatalogTypes.ts` — do not change that
type):

```typescript
import { ApiUnavailableError, SessionExpiredError } from '@/features/auth/authTypes'
import type { ShopCatalogAdapter } from './mockCatalogAdapter'
import {
  CategoryConflictError,
  ProductConflictError,
  type CreateCategoryRequest,
  type ProductFormValues,
  type ShopCategory,
  type ShopProduct,
  type ShopProductSummary,
  type UpdateCategoryRequest,
} from './shopCatalogTypes'

/**
 * S26 admin catalog management — real API data source (F029), replacing
 * F028's mock. Calls B026's tenant-scoped admin endpoints with the
 * current session bearer token.
 */
const REQUEST_TIMEOUT_MS = 8_000

export class ShopForbiddenError extends Error {
  constructor(message = 'شما اجازه مدیریت فروشگاه این مستأجر را ندارید.') {
    super(message)
    this.name = 'ShopForbiddenError'
  }
}

export class ShopValidationError extends Error {
  fieldErrors: Record<string, string>

  constructor(fieldErrors: Record<string, string>) {
    super('درخواست معتبر نیست.')
    this.fieldErrors = fieldErrors
    this.name = 'ShopValidationError'
  }
}

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return { signal: controller.signal, clear: () => window.clearTimeout(timeoutId) }
}

function authHeaders(accessToken: string) {
  return { Authorization: `Bearer ${accessToken}` }
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    throw new ApiUnavailableError()
  }
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

/**
 * Every call needs the tenant's own accessToken, obtained by the calling
 * page from `useAuth()` — see this Spec's Context for where that comes
 * from. Pages pass it as the adapter's first argument, exactly mirroring
 * `httpUserAdapter.listUsers(accessToken, page)`'s own parameter order —
 * except the Shop catalog adapter's own `ShopCatalogAdapter` type (from
 * F028's `shopCatalogTypes.ts`) does not carry an accessToken parameter,
 * so this file's exported object wraps it: build one bound instance per
 * render with `createShopCatalogAdapter(accessToken)` below.
 */
export function createShopCatalogAdapter(accessToken: string): ShopCatalogAdapter {
  const headers = { ...authHeaders(accessToken), 'Content-Type': 'application/json' }

  return {
    async listCategories(tenantId) {
      const response = await request(`/api/tenants/${tenantId}/shop/categories?pageNumber=1&pageSize=100`, {
        method: 'GET',
        headers: authHeaders(accessToken),
      })
      await handleCommonErrors(response)
      if (!response.ok) throw new ApiUnavailableError()
      const body = (await readJson(response)) as { categories: ShopCategory[] }
      return body.categories
    },

    async createCategory(tenantId, req: CreateCategoryRequest) {
      const response = await request(`/api/tenants/${tenantId}/shop/categories`, {
        method: 'POST',
        headers,
        body: JSON.stringify(req),
      })
      await handleCommonErrors(response)
      if (response.status === 409) throw new CategoryConflictError()
      if (response.status === 400) throw new ShopValidationError(mapServerValidation(await readJson(response)))
      if (!response.ok) throw new ApiUnavailableError()
      return (await readJson(response)) as ShopCategory
    },

    async updateCategory(tenantId, categoryId, req: UpdateCategoryRequest) {
      const response = await request(`/api/tenants/${tenantId}/shop/categories/${categoryId}`, {
        method: 'PUT',
        headers,
        body: JSON.stringify(req),
      })
      await handleCommonErrors(response)
      if (response.status === 409) throw new CategoryConflictError()
      if (response.status === 400) throw new ShopValidationError(mapServerValidation(await readJson(response)))
      if (!response.ok) throw new ApiUnavailableError()
      return (await readJson(response)) as ShopCategory
    },

    async listProducts(tenantId) {
      const response = await request(`/api/tenants/${tenantId}/shop/products?pageNumber=1&pageSize=100`, {
        method: 'GET',
        headers: authHeaders(accessToken),
      })
      await handleCommonErrors(response)
      if (!response.ok) throw new ApiUnavailableError()
      const body = (await readJson(response)) as { products: ShopProductSummary[] }
      return body.products
    },

    async getProduct(tenantId, productId) {
      const response = await request(`/api/tenants/${tenantId}/shop/products/${productId}`, {
        method: 'GET',
        headers: authHeaders(accessToken),
      })
      await handleCommonErrors(response)
      if (response.status === 404) throw new ApiUnavailableError()
      if (!response.ok) throw new ApiUnavailableError()
      return (await readJson(response)) as ShopProduct
    },

    async createProduct(tenantId, values: ProductFormValues) {
      const response = await request(`/api/tenants/${tenantId}/shop/products`, {
        method: 'POST',
        headers,
        body: JSON.stringify(values),
      })
      await handleCommonErrors(response)
      if (response.status === 409) throw new ProductConflictError()
      if (response.status === 400) throw new ShopValidationError(mapServerValidation(await readJson(response)))
      if (!response.ok) throw new ApiUnavailableError()
      return (await readJson(response)) as ShopProduct
    },

    async updateProduct(tenantId, productId, values: ProductFormValues) {
      const response = await request(`/api/tenants/${tenantId}/shop/products/${productId}`, {
        method: 'PUT',
        headers,
        body: JSON.stringify(values),
      })
      await handleCommonErrors(response)
      if (response.status === 409) throw new ProductConflictError()
      if (response.status === 400) throw new ShopValidationError(mapServerValidation(await readJson(response)))
      if (!response.ok) throw new ApiUnavailableError()
      return (await readJson(response)) as ShopProduct
    },
  }
}
```

Note the `import type { ShopCatalogAdapter } from './mockCatalogAdapter'`
line above only imports the *type*; step 3 below deletes the mock's
implementation file entirely, so before finishing this task move the
`ShopCatalogAdapter` type declaration itself out of
`mockCatalogAdapter.ts` and into `shopCatalogTypes.ts` (alongside the
other types F028 put there), then change this file's import to
`import type { ShopCatalogAdapter } from './shopCatalogTypes'`. This
keeps the type definition alive after the mock file is gone.

## 2. Update `CategoriesPage.tsx` and `ProductsPage.tsx` to use the real adapter

In both `src/web/src/pages/shop/admin/CategoriesPage.tsx` and
`src/web/src/pages/shop/admin/ProductsPage.tsx`:

1. Replace the F028 import
   `import { mockCatalogAdapter } from '@/features/shop/mockCatalogAdapter'`
   with:
   ```tsx
   import { createShopCatalogAdapter, ShopForbiddenError } from '@/features/shop/shopCatalogAdapter'
   import { ShopValidationError } from '@/features/shop/shopCatalogAdapter'
   import { useAuth } from '@/features/auth/AuthContext'
   ```
2. Add `const { session, signOut } = useAuth()` at the top of the
   component (matching `UsersPage.tsx`'s own line) and build the adapter
   once per render: `const adapter = createShopCatalogAdapter(session?.accessToken ?? '')`.
3. Replace every `mockCatalogAdapter.xxx(...)` call site with
   `adapter.xxx(...)` — the argument order and count are unchanged,
   since `ShopCatalogAdapter`'s shape did not change.
4. In each `catch` block, add handling for `SessionExpiredError` (call
   `void signOut()`, exactly like `UsersPage.tsx`'s `handleListFailure`),
   `ShopForbiddenError` (surface its message the same place `UsersPage.tsx`
   shows its "forbidden" list-error panel) and `ShopValidationError`
   (call `setError(field, { message })` for each entry in
   `error.fieldErrors`, the same loop `UsersPage.tsx` uses for
   `UserValidationError`).

## 3. Delete the mock

Delete `src/web/src/features/shop/mockCatalogAdapter.ts`. Confirm (with a
project-wide search) that nothing still imports from it — if the
`ShopCatalogAdapter` type move in step 1 was done correctly, nothing
should.

# Non-goals

- No new screen or field beyond what F028 already built.
- No change to the storefront-facing pages (F030/F031).

# If you get stuck

```bash
curl -X POST http://localhost:5080/api/tenants/<tenantId>/shop/categories \
  -H "Authorization: Bearer <accessToken>" \
  -H "Content-Type: application/json" \
  -d '{"name":"پیراهن","slug":"shirts","displayOrder":1}'
```

Run this once manually to confirm the exact response shape your adapter
must parse (`{"id":"...","tenantId":"...","name":"پیراهن","slug":"shirts","displayOrder":1,"isActive":true}`)
before wiring the page — if the browser network tab's response doesn't
match `ShopCategory` in `shopCatalogTypes.ts` field-for-field, B026's
delivered code and this Spec have drifted; trust B026's delivered code.

# Acceptance

- Category and product create/list/update/edit work end-to-end against
  the real API, including the combined product+variants+size-guide
  authoring payload.
- Every error case B026 defines (duplicate slug, invalid category,
  size-guide mismatch, 401/403) is presented clearly on the page, reusing
  F028's existing error-presentation pattern.
- No mock data path remains reachable.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Sign in as a seeded tenant member, create a category, create a product
  with variants and a size guide, edit it, and confirm the changes
  persist across a page reload (proving the real API, not local mock
  state).
- Trigger a duplicate-slug error and a size-guide-mismatch error and
  confirm both present clearly.
- No new browser console error.

# Lifecycle

Add row `F029` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependencies `F028, B026`, and Spec link
`tasks/front/F029-connect-admin-catalog-management.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/026-shop-catalog.md` is the permanent record and is
never deleted.
