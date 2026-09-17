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

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/026-shop-catalog.md`. Read B027's delivered
endpoint shapes before writing the real adapter.

# Scope

1. Add a real, unauthenticated HTTP adapter under
   `src/web/src/features/shop/` calling
   `/api/shop/{tenantId}/categories`,
   `/api/shop/{tenantId}/categories/{categorySlug}/products` and
   `/api/shop/{tenantId}/products/{productSlug}`. No `Authorization`
   header is ever sent from this adapter — it must work identically in a
   fully signed-out browser session.
2. Replace F030's mocked data source in `CategoryPage.tsx` and
   `ProductDetailPage.tsx` with this adapter, including live stock per
   variant and the real size-guide table.
3. Remove the mock data path entirely.

# Non-goals

- No change to the cart page (F032/F033 handle the real cart).
- No new screen or interaction beyond what F030 already built.

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
