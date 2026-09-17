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

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/026-shop-catalog.md`. Read B026's delivered
endpoint shapes (from its integration tests or, if the Spec file still
exists at task start, `tasks/backend/B026-category-and-product-admin-api.md`)
before writing the real adapter — do not guess field names/casing from
memory.

Read `src/web/src/features/users/httpUsersAdapter.ts` (or the equivalent
already-connected adapter for a similar admin feature) for the existing
adapter shape (typed request/response, error mapping to the page's
validation/error states) this task's Shop adapter should match.

# Scope

1. Replace the mock adapter under `src/web/src/features/shop/` created in
   F028 with a real HTTP adapter calling
   `/api/tenants/{tenantId}/shop/categories` and
   `/api/tenants/{tenantId}/shop/products` (list/create/update, and the
   single-product detail fetch for the edit form).
2. Map B026's validation error shapes (duplicate slug, category not
   found, size-guide row/column-count mismatch) to the same inline
   field-error presentation F028 already built for its mocked validation
   failures — do not change the visual error pattern, only its data
   source.
3. Remove the mock adapter entirely; no dead mock code remains reachable
   from the real pages.

# Non-goals

- No new screen or field beyond what F028 already built.
- No change to the storefront-facing pages (F030/F031).

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
