---
id: F043
slice: S31
title: Gate Shop nav items on the new Shop permission keys
agent: frontend-mentor
source: tasks/slices/031-cross-module-permissions-and-nav.md
---

# Objective

Connect the two Shop nav items to the two new permission keys B035
added server-side, using the exact same `requires` mechanism the
دعوت‌ها/گزارش فعالیت items already use (F019/F023). This is the smallest
possible change: two constants added to `roleTypes.ts`, and one
`requires` array added to each of the two Shop items in
`navSections`.

This Spec gives you the exact full diffs. Follow them literally.

# Context

**Depends on F042** (the `navSections` shape must exist first) **and
B035** (the server must resolve `Shop.Catalog.Manage`/
`Shop.Shipping.Manage` through `/api/tenants/{tenantId}/me/permissions`
before a `requires` gate on them means anything).

Read the real, current `src/web/src/features/roles/roleTypes.ts` (107
lines) before editing it — reproduced in full below, Section "1", is
exactly its current `PermissionKey` union and constants.

Read the real, current `src/web/src/pages/RolesPage.tsx` before
assuming it needs a change. Its `PermissionMatrix` component (around
line 524) renders whatever `groups` the server catalog response
contains with a plain, generic `.map()` over `groups` and, inside each
group, over `group.permissions` — it does not hardcode IAM's 3 groups
anywhere, does not special-case a group id, and reads every permission
item's `key`/`label`/`description`/`kind` generically from the response.
**Confirmed by reading the real file: no change is needed here.** Once
B035 ships and the server catalog includes the new `shop` group (added
by `ShopPermissionCatalogContributor`, aggregated by
`AggregatedPermissionCatalog`), `RolesPage.tsx` renders a "فروشگاه"
group and its two permissions automatically, with the exact same
checkbox-per-permission UI every existing group already gets — this is
the entire reason B034/B035 built a generic, contributor-based catalog
instead of anything IAM-specific.

No nav entry exists today for a shipping-rate/coupon admin screen —
confirmed by reading the real, current `ShellNav.tsx` (after F042): its
`shop` section has only `shop-categories` and `shop-products`, both
reachable through
`/t/{tenantId}/shop/categories`/`/t/{tenantId}/shop/products`. There is
no `shop-shipping`/`shop-coupons` item to gate on
`Shop.Shipping.Manage`, and this task does not add one — that would be
a new nav destination and screen, out of scope for a permission-gating
task (and, per this slice's Non-goals, out of scope for this whole
slice). Both existing Shop nav items are gated on
`Shop.Catalog.Manage` only, since both are catalog-authoring screens.

# Scope

## 1. `src/web/src/features/roles/roleTypes.ts`

Current (lines 11–20):

```ts
export type PermissionKey =
  | 'IAM.Roles.Manage'
  | 'IAM.Invitations.View'
  | 'IAM.Invitations.Create'
  | 'IAM.Audit.View'

export const ROLES_MANAGE_KEY = 'IAM.Roles.Manage'
export const INVITATIONS_VIEW_KEY = 'IAM.Invitations.View'
export const INVITATIONS_CREATE_KEY = 'IAM.Invitations.Create'
export const AUDIT_VIEW_KEY = 'IAM.Audit.View'
```

New:

```ts
export type PermissionKey =
  | 'IAM.Roles.Manage'
  | 'IAM.Invitations.View'
  | 'IAM.Invitations.Create'
  | 'IAM.Audit.View'
  | 'Shop.Catalog.Manage'
  | 'Shop.Shipping.Manage'

export const ROLES_MANAGE_KEY = 'IAM.Roles.Manage'
export const INVITATIONS_VIEW_KEY = 'IAM.Invitations.View'
export const INVITATIONS_CREATE_KEY = 'IAM.Invitations.Create'
export const AUDIT_VIEW_KEY = 'IAM.Audit.View'
export const SHOP_CATALOG_MANAGE_KEY = 'Shop.Catalog.Manage'
export const SHOP_SHIPPING_MANAGE_KEY = 'Shop.Shipping.Manage'
```

Every other line in this file (the `PermissionItem`/`PermissionGroup`/
`PermissionCatalogResponse`/`TenantRole`/error-class types) is
unchanged — they are already generic over `PermissionKey` and need no
edit.

## 2. `src/web/src/components/shell/ShellNav.tsx`

After F042, the `shop` section reads:

```tsx
  {
    id: 'shop',
    label: 'فروشگاه',
    items: [
      { id: 'shop-categories', label: 'دسته‌بندی‌های فروشگاه', icon: ShoppingBag, href: '/t/', tenantScopedSuffix: '/shop/categories' },
      { id: 'shop-products', label: 'محصولات فروشگاه', icon: ShoppingBag, href: '/t/', tenantScopedSuffix: '/shop/products' },
    ],
  },
```

Change both items to add `requires`:

```tsx
  {
    id: 'shop',
    label: 'فروشگاه',
    items: [
      { id: 'shop-categories', label: 'دسته‌بندی‌های فروشگاه', icon: ShoppingBag, href: '/t/', tenantScopedSuffix: '/shop/categories', requires: [SHOP_CATALOG_MANAGE_KEY] },
      { id: 'shop-products', label: 'محصولات فروشگاه', icon: ShoppingBag, href: '/t/', tenantScopedSuffix: '/shop/products', requires: [SHOP_CATALOG_MANAGE_KEY] },
    ],
  },
```

Add `SHOP_CATALOG_MANAGE_KEY` to the existing import from
`@/features/roles/roleTypes`:

Current:

```tsx
import { AUDIT_VIEW_KEY, INVITATIONS_VIEW_KEY, type PermissionKey } from '@/features/roles/roleTypes'
```

New:

```tsx
import { AUDIT_VIEW_KEY, INVITATIONS_VIEW_KEY, SHOP_CATALOG_MANAGE_KEY, type PermissionKey } from '@/features/roles/roleTypes'
```

Nothing else in `ShellNav.tsx` changes — the `requires`-gating
mechanism itself (`permissionGated`/`permissionPending`/`inert` in the
render loop) already exists from F019/F023 and needs no edit; it reads
whatever `requires` array an item declares, exactly as it already does
for دعوت‌ها and گزارش فعالیت.

## 3. `RolesPage.tsx`

No change. See this Spec's Context section for why, verified by reading
the real file.

# Non-goals

- No new nav item for shipping-rate/coupon admin — no screen exists to
  link to; adding one is a different, larger task (a new admin page)
  that this slice does not include.
- No change to `RolesPage.tsx` or its `PermissionMatrix` component.
- No change to `useTenantPermissions` or any other permission-resolving
  hook — the existing hook already fetches whatever keys the server
  resolves; it needs no knowledge of which keys exist.
- No third Shop permission key, no `Shop.Shipping.Manage` nav gate
  anywhere (nothing to gate it on yet).

# Acceptance

- A signed-in tenant member with no Shop role grant sees both Shop nav
  items rendered inert (dimmed, `aria-disabled`, no navigation on
  click) — same behavior دعوت‌ها/گزارش فعالیت already have for a member
  missing those keys.
- A member granted a tenant role containing `Shop.Catalog.Manage` (saved
  through the role editor, which now shows a "فروشگاه" group
  automatically once B035 is live) sees both Shop nav items active.
- A tenant Owner always sees both Shop nav items active, with or without
  an explicit role grant (Owner bypass, unchanged from B034/B035).
- `npm run build` and `npm run lint` in `src/web/` pass.

# Verification

```bash
cd src/web && npm run lint && npm run build
```

Manual, real browser, after B035 is live: sign in as a plain tenant
member with no Shop role — both Shop nav items are visible but inert;
grant that member's role `Shop.Catalog.Manage` through the role editor
— both items become active without a page reload once
`useTenantPermissions` re-resolves; sign in as the tenant Owner — both
items are always active. No new browser console error.

# Lifecycle

Add row `F043` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F042, B035`, and Spec link
`tasks/front/F043-shop-nav-permission-gating.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/031-cross-module-permissions-and-nav.md` is the
permanent record and is never deleted.
