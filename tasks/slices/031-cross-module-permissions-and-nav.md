# S31 — Cross-module permission catalog and modular navigation

## Outcome

The user reviewed the real, already-merged Shop admin screens (F028/F029,
B025–B031 on `main`) and found two problems himself:

1. `src/web/src/components/shell/ShellNav.tsx`'s `navItems` is one flat
   array mixing IAM destinations (`اعضای مستأجر`, `نقش‌ها`, `دعوت‌ها`,
   `گزارش فعالیت`, `کاربران پلتفرم`, …) and the two Shop destinations
   (`دسته‌بندی‌های فروشگاه`, `محصولات فروشگاه`) with no visual grouping by
   module — confirmed by reading the real current file (quoted in full in
   F042 below).
2. Every mutating Shop admin endpoint —
   `src/modules/shop/TenantForge.Modules.Shop/features/categories/CategoriesFeature.cs`,
   `features/products/ProductsFeature.cs`,
   `features/shipping/ShippingRatesFeature.cs`,
   `features/coupons/CouponsFeature.cs` — calls
   `ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db)`,
   which only checks active tenant **membership**, never a permission key.
   IAM's own admin endpoints
   (`src/modules/iam/TenantForge.Modules.Iam/features/roles/RolesFeature.cs`)
   require a specific granted key via a second overload,
   `AuthorizeTenantAccessAsync(tenantId, principal, db, RolesManagePermission)`.
   Today, any tenant member — not just an Owner or a role-holder — can
   create or edit Shop categories, products, shipping rates and coupons.

This is not a missing check Shop can add on its own. IAM's own
`RolesFeature.ValidateRoleRequest` (rejecting any key outside its
module-private `PermissionKeys` HashSet, itself built from its own
`CatalogGroups`) and the permission catalog endpoint
(`GET /api/permissions/catalog`, serving that same hardcoded
`CatalogGroups`) both only ever know about IAM's own 3 groups
(`roles`/`invitations`/`audit`). A tenant owner cannot save a role with a
`Shop.*` key today — the server answers "Select only known permission
keys." Confirmed by reading the real, current
`RolesFeature.cs` (425 lines) in full before writing this slice: the
catalog (lines 25–47), the endpoint (line 51), and every place that reads
or filters through the hardcoded set (`ValidateRoleRequest` line 389,
`ResolvePermissionsAsync` lines 306 and 315 — see the note in B034's Spec
about why this second call site matters too, beyond what was first
proposed).

`RolesPage.tsx`'s `PermissionMatrix` component (line 524) already renders
whatever `groups` the server catalog returns with a plain `.map()` — it
does not hardcode IAM's 3 groups (confirmed by reading it before writing
F043). Once the server catalog includes a Shop group, the existing role
editor renders it with **no frontend change there**.

## The admission decision

This is the same "second real consumer earns the abstraction" bar already
used for `TenantForge.BuildingBlocks` itself (B018/S20) and the
`Module.Contract` pattern (B021/S23): IAM is consumer #1 of a new shared
permission-catalog contract, migrated onto it with a provably
behavior-preserving refactor (B034); Shop, in the very next task (B035),
is a concrete, already-planned consumer #2, not a hypothetical one. Per
`docs/building-blocks/README.md` Section 7's admission checklist, this
is sufficient evidence — the bar is "a type both modules actually use
today", and B035 makes that literally true within this same slice.

## New BuildingBlocks types (5 — B034)

| Type | Namespace/path | Purpose |
| --- | --- | --- |
| `PermissionDescriptor` | `TenantForge.BuildingBlocks.Permissions.PermissionDescriptor` | One permission key + label + description + kind, mirroring IAM Contract's `PermissionResponse` field-for-field |
| `PermissionGroup` | `TenantForge.BuildingBlocks.Permissions.PermissionGroup` | One named group of `PermissionDescriptor`s, mirroring IAM Contract's `PermissionGroupResponse` |
| `IPermissionCatalogContributor` | `TenantForge.BuildingBlocks.Permissions.IPermissionCatalogContributor` | One method, `GetPermissionGroups()`; implemented once per module that owns permission keys |
| `IAggregatedPermissionCatalog` | `TenantForge.BuildingBlocks.Permissions.IAggregatedPermissionCatalog` | The union of every registered contributor's groups and known keys |
| `AggregatedPermissionCatalog` | `TenantForge.BuildingBlocks.Permissions.AggregatedPermissionCatalog` | The one, trivial, eager-flattening implementation, built from every registered `IPermissionCatalogContributor` |

These are deliberately **not** the same types as IAM Contract's existing
`PermissionResponse`/`PermissionGroupResponse`/`PermissionCatalogResponse`
(the HTTP wire shape) — those stay exactly as they are, in
`TenantForge.Modules.Iam.Contract`, and `RolesFeature.cs`'s catalog
endpoint now maps the BuildingBlocks shape onto them at the boundary. See
B034's Spec for the exact mapping code and the reasoning: a wire response
type is a module-owned HTTP contract, never a cross-module primitive
(`docs/building-blocks/README.md` Section 8 already excludes IAM's whole
`RolesFeature.cs`/`AuthorizationPolicyNames` for exactly this reason —
B034 amends that exclusion row to name only the now-admitted shared shape,
not IAM's endpoint/authorization code, which remains excluded).

## Correction to the originally proposed design

The coordinator's original design named only `RolesFeature.cs`'s own
three internal call sites as needing the injected
`IAggregatedPermissionCatalog`. Reading the real current file surfaced a
real gap that must be fixed in the same task, not deferred:

`RolesFeature.ResolvePermissionsAsync` (the function behind
`GET /api/tenants/{tenantId}/me/permissions`, which is exactly the
endpoint `src/web/src/features/roles/tenantPermissions.ts`'s
`useTenantPermissions` hook calls, which is exactly what
`ShellNav.tsx`'s `requires`-gating reads) currently filters every
resolved key — both the Owner-bypass union (line 306) and the
assigned-role-keys union (line 315) — through IAM's own module-private
`PermissionKeys` HashSet. If this stayed as-is, a Shop permission key
assigned to a tenant role (or granted to an Owner) would be silently
dropped before it ever reached the frontend, and F043's nav gating for
`دسته‌بندی‌های فروشگاه`/`محصولات فروشگاه` would never work — not even for
an Owner. B034 therefore also threads `IAggregatedPermissionCatalog` into
`ResolvePermissionsAsync` itself (using `catalog.AllKnownKeys` in place
of `PermissionKeys`), and, because that function is reached through
`AuthorizeTenantAccessAsync`'s permission-checking path, into every
endpoint that calls `AuthorizeTenantAccessAsync` with a non-null
permission key — which, read via
`grep -rn "AuthorizeTenantAccessAsync" src/modules/iam/`, is not only
`RolesFeature.cs`'s own three call sites but also one in
`src/modules/iam/TenantForge.Modules.Iam/features/audit/AuditFeature.cs`
and two in
`src/modules/iam/TenantForge.Modules.Iam/features/invitations/InvitationsFeature.cs`.
B034's Spec enumerates and changes all of these — this is a wider, but
still entirely mechanical and behavior-preserving, diff than first
proposed. At B034 time (only IAM's contributor registered),
`catalog.AllKnownKeys` is set-equal to the old `PermissionKeys`, so every
existing IAM integration test still passes unmodified.

## Scope

**B034 — depends on B023 (IAM Contract project exists):**
- Add the 5 BuildingBlocks types above.
- Refactor `RolesFeature.cs`'s hardcoded catalog into
  `IamPermissionCatalogContributor : IPermissionCatalogContributor`,
  registered in `IAMConfig.RegisterServices`.
- Register the aggregator in `Program.cs`.
- Thread `IAggregatedPermissionCatalog` into every endpoint/helper that
  needs it (enumerated above and in full in B034's own Spec), converting
  the catalog endpoint's response at the BuildingBlocks/Contract
  boundary.
- Update `BuildingBlocksArchitectureTests.BuildingBlocks_ExportsOnlyTheApprovedProductionTypes`
  and `docs/building-blocks/README.md` (fast facts, exported-type
  catalog, the amended exclusion row, admission checklist evidence,
  change-impact declaration).
- Zero visible behavior change: the catalog endpoint's content, and every
  existing role/audit/invitation integration test, are unaffected.

**B035 — depends on B034:**
- Add `Shop.Catalog.Manage` and `Shop.Shipping.Manage` to
  `ShopAuthorization.cs`, plus a permission-checking overload of
  `AuthorizeTenantAccessAsync` that resolves the caller's effective Shop
  keys via raw SQL against `iam_tenant_memberships`,
  `iam_tenant_member_role_assignments` and `iam_tenant_roles` (Owner
  bypass, otherwise the member's assigned roles' `permission_keys`
  intersected with Shop's own known keys).
- Add `ShopPermissionCatalogContributor`, registered in
  `ShopConfig.RegisterServices`.
- Retrofit every mutating Shop admin endpoint (enumerated by
  `grep -rn "ShopAuthorization.AuthorizeTenantAccessAsync" src/modules/shop/`
  in B035's own Spec) to require the matching key; read-only/anonymous
  endpoints are untouched.
- New integration tests in the existing
  `ShopCatalogAdminIntegrationTests.cs`/`ShopShippingCouponAdminIntegrationTests.cs`
  files proving the 403/granted-role/Owner-bypass behavior for both keys.

**F042 — depends on F029 (Shop admin nav already exists):**
- Pure presentation restructure: `ShellNav.tsx`'s flat `navItems` becomes
  two labelled sections ("هویت و دسترسی" for the existing IAM items,
  "فروشگاه" for the two Shop items). No new item, no href change, no
  permission gating. Verified by before/after screenshot comparison.

**F043 — depends on F042, B035:**
- `roleTypes.ts` gains the two new `PermissionKey` union members and
  constants.
- `ShellNav.tsx`'s two Shop items gain `requires: [SHOP_CATALOG_MANAGE_KEY]`
  (both destinations are gated on the catalog-authoring key, since both
  are catalog screens — see F043's Spec for why `Shop.Shipping.Manage`
  has no nav item of its own yet: no shipping/coupon admin page exists
  in the nav today; F034/F035 build those screens but did not add a nav
  entry for them in this repository's actual history — confirmed by
  reading the real, current `ShellNav.tsx`, which has no
  `shop-shipping`/`shop-coupons` item to gate. This Spec does not invent
  one; adding that nav entry is out of scope here).
- No `RolesPage.tsx` change — confirmed by reading its generic
  `groups.map()` rendering.

## Non-goals

- No third Shop permission key.
- No change to any anonymous/read-only Shop endpoint (public storefront,
  cart, checkout, order creation/payment/lookup — all deliberately
  anonymous by S26–S30's own design).
- No nav entry added for the shipping-rate/coupon admin pages (F034/F035
  built pages that are reachable today only by direct URL, not from
  `ShellNav.tsx` — that gap, if it is one, is pre-existing and out of
  scope for this slice, which only re-groups and gates what is already
  in the nav).
- No change to `RolesPage.tsx`.

## Verification

- `dotnet build TenantForge.sln --nologo` and the full integration suite
  pass after B034 with **zero** test-content change required for any
  existing IAM test, and after B035 with the new Shop-permission tests
  passing.
- `npm run build` and `npm run lint` in `src/web/` pass after F042/F043.
- Real browser demo at 1440×900 and 390×844: the nav shows two labelled
  sections before/after F042 (screenshot pair); after F043, a
  `Member`-role account with no Shop role grant sees the two Shop nav
  items rendered inert, and a role granted `Shop.Catalog.Manage` (saved
  through the now-unblocked role editor, which shows a "فروشگاه" group
  automatically) makes them active; an Owner always sees them active.
- No new browser console error.
