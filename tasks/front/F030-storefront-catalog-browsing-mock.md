---
id: F030
slice: S26
title: Storefront catalog browsing and product detail mock
agent: ui-engineer
source: tasks/slices/026-shop-catalog.md
---

# Objective

Build the public-facing storefront: a category grid, product cards within
a category, and a product detail page with an image gallery/lightbox,
variant-aware color and size selectors (each combination shows its own
stock/sold-out state), a size-guide table, a quantity stepper and an
"add to cart" action — all against mocked data.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md` completely.
Read the complete `tasks/slices/026-shop-catalog.md`, in particular its
screenshot-to-task mapping table: this page family's visual shape (color/
size selection, a size-guide table below the gallery, a quantity stepper,
an add-to-cart action) is grounded in the darnoshop.com screenshots the
user and the assistant already reviewed together — this Spec cites that
evidence for *which elements exist and how they relate*, never for
darnoshop's own colors, type or spacing. Every actual visual decision
(color, spacing, radius, shadow, type) still follows `docs/design-system.md`
tokens and the existing shadcn/ui-based component set.

This task depends on `F025` (shared shell/brand foundations), but the
public storefront is a **separate, unauthenticated layout** from
`DashboardShell` — it is not another authenticated admin page and must
not be nested inside `DashboardShell`'s sidebar/header. State plainly
what minimal shared foundation it still reuses: the design tokens in
`src/web/src/index.css`, and the existing `Button`/`Input`/other
shadcn/ui-based primitive components under `src/web/src/components/ui/`.
It does not reuse `DashboardShell`, `ShellNav`, or any authenticated-shell
chrome — build a small, dedicated storefront layout wrapper instead
(header with the tenant's brand mark, no sidebar, no sign-out control,
since there is no signed-in session here at all).

# Scope

1. `src/web/src/pages/shop/storefront/StorefrontLayout.tsx`: minimal
   public layout (brand mark, category navigation, a cart-icon/count
   affordance wired to mocked cart state for now).
2. `src/web/src/pages/shop/storefront/CategoryPage.tsx`: category grid
   (or, when a category is selected, its paginated product-card grid).
   Mocked data.
3. `src/web/src/pages/shop/storefront/ProductDetailPage.tsx`:
   - an image gallery with a lightbox (mocked images);
   - color and size selectors that are variant-aware — selecting a
     color/size combination with no matching variant, or a variant with
     `StockQuantity = 0`, disables "add to cart" and shows a clear
     sold-out/unavailable state, never a silently-ignored selection;
   - the product's size-guide table (columns/rows from mocked data);
   - a quantity stepper, capped by the selected variant's mocked stock;
   - an "add to cart" action that, for this mock task, updates a local
     mocked cart state (F032 builds the real cart page; this task only
     needs the add-to-cart interaction and its own success feedback, not
     a full cart UI).
4. Add public routes for the storefront (distinct from the authenticated
   app's routes, reachable without signing in).

# Non-goals

- No connection to a real API (F031 connects this page once B027 exists).
- No cart page (F032) — only the add-to-cart interaction and its
  immediate feedback.
- No admin-facing element anywhere on these pages.

# Acceptance

- The category grid and product cards render against mocked data with
  the required states (loading, empty, error).
- The product detail page's variant selectors correctly reflect
  per-combination stock/sold-out state from mocked data, and the
  quantity stepper never allows exceeding the selected variant's mocked
  stock.
- The size-guide table renders correctly for a mocked product that has
  one, and the section is simply absent (not an empty table) for a
  mocked product that has none.
- The storefront layout has no authenticated-shell chrome (no sidebar,
  no sign-out) and no new design token.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900, 1024×768 and 390×844:

- Browse from the category grid into a product's detail page, select
  different color/size combinations and confirm stock/sold-out state
  updates correctly, open the gallery lightbox, and add an in-stock
  combination to the mocked cart with visible success feedback.
- No browser console error.

# Lifecycle

Add row `F030` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F025`, and Spec link
`tasks/front/F030-storefront-catalog-browsing-mock.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/026-shop-catalog.md` is the permanent record and is
never deleted.
