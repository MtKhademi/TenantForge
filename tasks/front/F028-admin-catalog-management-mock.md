---
id: F028
slice: S26
title: Admin catalog management mock
agent: ui-engineer
source: tasks/slices/026-shop-catalog.md
---

# Objective

Build the admin catalog management screens — category list/create/edit,
and product list/create/edit with inline variant rows and a size-guide
table editor — against mocked data, matching `docs/design-system.md`
exactly. This is the first Shop frontend task; it has no Shop backend
dependency yet since it is mock-only.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md` completely
before making any visual decision. Read the complete
`tasks/slices/026-shop-catalog.md` — it is the authoritative contract for
this task and its "connect" counterpart, F029.

This task depends on `F025` (the shared shell/brand foundations fix),
which is `done` — the admin catalog screens render inside the existing
authenticated `DashboardShell`, exactly like `UsersPage.tsx`/`TenantsPage.tsx`
already do, and must reuse its already-corrected single-brand-mark header
and `SecondaryButton` icon-control convention. It does not depend on
`F026`/`F027` (the shared `StatePanel` and login-mobile tasks); if
`StatePanel` (F026) is `done` by the time this task starts, use it for
this task's error/empty states — check the ledger before writing a new
one-off panel.

Read `src/web/src/pages/UsersPage.tsx` and `src/web/src/pages/RolesPage.tsx`
for the existing list/create/edit page structure (header pattern, table,
create form, inline field validation) this task's category/product
screens should match — Shop introduces a new feature area, not a new
interaction pattern.

# Scope

1. `src/web/src/pages/shop/admin/CategoriesPage.tsx`: a table of
   categories (name, slug, display order, active toggle) with a
   create/edit form (name, slug, display order, active). Mocked data
   only — a local in-memory mock adapter under
   `src/web/src/features/shop/` (mirroring the mock-adapter pattern
   already used for other features before their "connect" task, e.g.
   `src/web/src/features/users/` before F009).
2. `src/web/src/pages/shop/admin/ProductsPage.tsx`: a table of products
   (name, slug, category, base price, variant count, active toggle) and a
   create/edit form with:
   - the product's own fields (name, slug, description, category picker,
     base price, compare-at price);
   - an inline, repeatable variant-row editor (color, size, SKU, stock
     quantity, price override) with add/remove row actions;
   - a size-guide table editor: a repeatable column-name editor (e.g.
     "دور سینه") and a repeatable row editor (a size label plus one input
     per current column) — adding a column must add a matching empty
     input to every existing row; removing a column must remove that
     column's value from every row.
3. Both pages follow the existing admin list-page layout (header with
   title/description/primary action, table, create/edit as an inline
   form or dialog — match whichever existing pattern `UsersPage.tsx`/
   `RolesPage.tsx` uses for consistency, not a new layout idiom).
4. Add the two new routes to the app's router and a Shop entry point in
   the tenant-scoped admin navigation (`ShellNav.tsx`), reachable the same
   way Users/Tenants/Roles already are.

# Non-goals

- No connection to a real API — B026 does not exist yet (F029 connects
  this page once it does).
- No storefront-facing page (that is F030) — this task is the
  authenticated admin side only.
- No image upload UI for product photos — out of scope for this task and
  this slice (B026 carries no image field).

# Acceptance

- Category and product list/create/edit work fully against mocked data,
  including the size-guide table editor's column/row synchronization
  described above.
- Every required state (idle, loading, empty, validation failure,
  success feedback) is implemented for both pages.
- The pages render inside `DashboardShell` exactly like other admin
  pages, reusing its header, `SecondaryButton` icon controls and, if
  already delivered, `StatePanel`.
- No new design token; every visual choice reuses existing tokens/
  components.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900, 1024×768 and 390×844:

- Create a category, create a product with at least two variants and a
  two-column size guide, add a third column and confirm every existing
  row gains a matching empty cell, remove a column and confirm its
  values disappear from every row, edit an existing product and confirm
  the form loads its current variants/size guide correctly.
- No browser console error.

# Lifecycle

Add row `F028` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F025`, and Spec link
`tasks/front/F028-admin-catalog-management-mock.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/026-shop-catalog.md` is the permanent record and is
never deleted.
