---
id: B035
slice: S31
title: Shop permission enforcement (Shop.Catalog.Manage / Shop.Shipping.Manage)
agent: backend-mentor
source: tasks/slices/031-cross-module-permissions-and-nav.md
---

# Objective

Close the real gap B034 only made fixable: today **any active tenant
member** — not just an Owner or a role-holder — can create or edit Shop
categories, products, shipping rates and coupons, because every mutating
Shop admin endpoint calls `ShopAuthorization.AuthorizeTenantAccessAsync`
with no permission key at all. This task adds two real permission keys,
a permission-checking overload mirroring IAM's own pattern (see B034's
rewritten `RolesFeature.AuthorizeTenantAccessAsync`), a Shop contributor
to the shared catalog, and retrofits every one of the real mutating call
sites — enumerated below by reading the actual current files, not
assumed from an earlier Spec.

This Spec gives you every file's exact path and exact full code. Follow
it literally.

# Context

**Depends on B034** — the shared `IPermissionCatalogContributor` /
`IAggregatedPermissionCatalog` seam must exist first; this task is its
second, concrete consumer (not hypothetical — see
`docs/building-blocks/README.md` Section 7 evidence B034 already
recorded naming this task).

Read the full, current
`src/modules/shop/TenantForge.Modules.Shop/features/authorization/ShopAuthorization.cs`
before changing it (reproduced in full below as it stands today) — it
only checks active tenant **membership** via a raw `COUNT(*)` query
against `iam_tenant_memberships`/`iam_accounts`/`iam_tenants`, exactly
mirroring `RolesFeature`'s own active/active/active join, for the same
reason B026's Spec gives: Shop has no EF entities for IAM's tables, so
this cannot be a normal EF navigation — it must stay raw SQL.

Every real mutating call site was found by running, against the actual
current repository, not an earlier Spec's assumption:

```bash
grep -rn "ShopAuthorization.AuthorizeTenantAccessAsync" src/modules/shop/
```

which returns exactly 12 call sites across 4 files. Reading each file in
full (not just the grep line) shows which are mutating and which are
read-only:

| File | Line | Endpoint | Verb | Mutating? |
| --- | --- | --- | --- | --- |
| `features/categories/CategoriesFeature.cs` | 25 | `/api/tenants/{tenantId}/shop/categories` | POST create | **Yes** |
| `features/categories/CategoriesFeature.cs` | 50 | `/api/tenants/{tenantId}/shop/categories` | GET list | No |
| `features/categories/CategoriesFeature.cs` | 74 | `/api/tenants/{tenantId}/shop/categories/{categoryId}` | PUT update | **Yes** |
| `features/products/ProductsFeature.cs` | 25 | `/api/tenants/{tenantId}/shop/products` | POST create | **Yes** |
| `features/products/ProductsFeature.cs` | 64 | `/api/tenants/{tenantId}/shop/products` | GET list | No |
| `features/products/ProductsFeature.cs` | 96 | `/api/tenants/{tenantId}/shop/products/{productId}` | GET single | No |
| `features/products/ProductsFeature.cs` | 112 | `/api/tenants/{tenantId}/shop/products/{productId}` | PUT update | **Yes** |
| `features/shipping/ShippingRatesFeature.cs` | 23 | `/api/tenants/{tenantId}/shop/shipping-rates` | GET list | No |
| `features/shipping/ShippingRatesFeature.cs` | 41 | `/api/tenants/{tenantId}/shop/shipping-rates` | POST set | **Yes** |
| `features/coupons/CouponsFeature.cs` | 25 | `/api/tenants/{tenantId}/shop/coupons` | POST create | **Yes** |
| `features/coupons/CouponsFeature.cs` | 65 | `/api/tenants/{tenantId}/shop/coupons` | GET list | No |
| `features/coupons/CouponsFeature.cs` | 88 | `/api/tenants/{tenantId}/shop/coupons/{couponId}/deactivate` | PATCH deactivate | **Yes** |

**7 mutating call sites gain a permission key; 5 read-only call sites
are untouched** — a tenant member can still *see* the catalog, shipping
rates and coupons without a granted key, only *changing* them is gated.
This mirrors IAM's own split exactly (`RolesFeature`'s own `GET
/api/tenants/{tenantId}/roles` needs no `RolesManagePermission`, only
the mutating role endpoints do).

Categories and products share one key (`Shop.Catalog.Manage`); shipping
rates and coupons share the other (`Shop.Shipping.Manage`) — this
follows the existing test-file grouping
(`ShopCatalogAdminIntegrationTests.cs` vs.
`ShopShippingCouponAdminIntegrationTests.cs`, both already real files)
and keeps the key count at exactly 2, per this slice's Non-goals (no
third key — coupons do not get their own key).

No anonymous/read-only endpoint changes at all: the public storefront
catalog (`ShopStorefrontCatalogIntegrationTests.cs`), cart, checkout and
order endpoints stay exactly as they are (S26–S30's own design — all
deliberately anonymous).

# Scope — every file, in order

## 1. Rewrite `ShopAuthorization.cs`

Replace
`src/modules/shop/TenantForge.Modules.Shop/features/authorization/ShopAuthorization.cs`
entirely with:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Authorization;

/// <summary>
/// The result of checking whether the caller may act on a tenant's Shop
/// data. Mirrors the shape of TenantForge.Modules.Iam.Features.Roles.TenantAccess
/// (a Result slot the caller returns directly when non-null).
/// </summary>
internal sealed record ShopTenantAccess(Tsid TenantId, Tsid AccountId, IResult? Result)
{
    public static ShopTenantAccess Forbidden { get; } = new(default, default, Results.Forbid());
}

internal static class ShopAuthorization
{
    /// <summary>
    /// B035: gates every mutating category/product endpoint. Granted to a
    /// tenant Owner always, or to a member holding a tenant role whose
    /// permission_keys array contains this key.
    /// </summary>
    internal const string CatalogManagePermission = "Shop.Catalog.Manage";

    /// <summary>
    /// B035: gates every mutating shipping-rate/coupon endpoint. Same
    /// Owner-bypass/assigned-role-key rule as CatalogManagePermission.
    /// </summary>
    internal const string ShippingManagePermission = "Shop.Shipping.Manage";

    /// <summary>The two keys Shop owns — see ShopPermissionCatalogContributor.</summary>
    internal static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        CatalogManagePermission,
        ShippingManagePermission
    };

    /// <summary>
    /// Membership-only overload — every read-only Shop endpoint keeps
    /// calling exactly this, unchanged from before this task.
    /// </summary>
    internal static Task<ShopTenantAccess> AuthorizeTenantAccessAsync(
        string tenantId,
        ClaimsPrincipal principal,
        ShopDbContext db) =>
        AuthorizeTenantAccessAsync(tenantId, principal, db, null);

    /// <summary>
    /// Permission-checking overload. Every mutating Shop endpoint (Scope
    /// table in this Spec) now calls this with CatalogManagePermission or
    /// ShippingManagePermission.
    /// </summary>
    internal static Task<ShopTenantAccess> AuthorizeTenantAccessAsync(
        string tenantId,
        ClaimsPrincipal principal,
        ShopDbContext db,
        string permissionKey) =>
        AuthorizeTenantAccessAsync(tenantId, principal, db, (string?)permissionKey);

    private static async Task<ShopTenantAccess> AuthorizeTenantAccessAsync(
        string tenantId,
        ClaimsPrincipal principal,
        ShopDbContext db,
        string? permissionKey)
    {
        if (principal.Identity is not { IsAuthenticated: true })
        {
            return ShopTenantAccess.Forbidden;
        }

        if (!TsidId.TryParse(tenantId, out var tenantTsid))
        {
            return ShopTenantAccess.Forbidden;
        }

        var accountTsid = TsidId.TryParseNullable(principal.FindFirstValue("sub"));
        if (accountTsid is null)
        {
            return ShopTenantAccess.Forbidden;
        }

        // Same active/active/active join RolesFeature.AuthorizeTenantAccessAsync
        // uses, now selecting the membership's role string instead of a bare
        // count, so a permission check can tell Owner apart from Member
        // without a second round trip.
        var roles = await db.Database.SqlQueryRaw<string>(
            """
            SELECT m.role AS "Value"
            FROM iam_tenant_memberships m
            JOIN iam_accounts a ON a.id = m.account_id AND a.status = 'Active'
            JOIN iam_tenants t ON t.id = m.tenant_id AND t.status = 'Active'
            WHERE m.tenant_id = {0} AND m.account_id = {1}
            """,
            tenantTsid.ToLong(),
            accountTsid.Value.ToLong())
            .ToListAsync();

        if (roles.Count == 0)
        {
            return ShopTenantAccess.Forbidden;
        }

        if (permissionKey is not null)
        {
            var granted = roles[0] == "Owner"
                ? KnownKeys.Contains(permissionKey)
                : (await ResolveAssignedShopKeysAsync(db, tenantTsid, accountTsid.Value)).Contains(permissionKey);
            if (!granted)
            {
                return ShopTenantAccess.Forbidden;
            }
        }

        return new ShopTenantAccess(tenantTsid, accountTsid.Value, null);
    }

    /// <summary>
    /// Every Shop permission key the caller holds through an assigned
    /// tenant role, intersected with Shop's own KnownKeys (a role's
    /// permission_keys array may also hold IAM keys, which are irrelevant
    /// here). Raw SQL for the same cross-database reason as the membership
    /// check above: iam_tenant_member_role_assignments and iam_tenant_roles
    /// are IAM's own tables, unreachable from ShopDbContext as EF entities.
    /// </summary>
    private static async Task<HashSet<string>> ResolveAssignedShopKeysAsync(ShopDbContext db, Tsid tenantId, Tsid accountId)
    {
        var keys = await db.Database.SqlQueryRaw<string>(
            """
            SELECT DISTINCT unnest(r.permission_keys) AS "Value"
            FROM iam_tenant_memberships m
            JOIN iam_tenant_member_role_assignments asg ON asg.tenant_membership_id = m.id
            JOIN iam_tenant_roles r ON r.id = asg.tenant_role_id
            WHERE m.tenant_id = {0} AND m.account_id = {1}
            """,
            tenantId.ToLong(),
            accountId.ToLong())
            .ToListAsync();

        return keys.Where(KnownKeys.Contains).ToHashSet(StringComparer.Ordinal);
    }
}
```

## 2. `src/modules/shop/TenantForge.Modules.Shop/features/authorization/ShopPermissionCatalogContributor.cs` (new file)

```csharp
using TenantForge.BuildingBlocks.Permissions;

namespace TenantForge.Modules.Shop.Features.Authorization;

/// <summary>
/// Shop's own contribution to the shared permission catalog (B034/B035) —
/// the second real consumer of the BuildingBlocks contributor/aggregator
/// seam, exactly as B034's admission evidence recorded.
/// </summary>
internal sealed class ShopPermissionCatalogContributor : IPermissionCatalogContributor
{
    public IReadOnlyList<PermissionGroup> GetPermissionGroups() =>
    [
        new("shop", "فروشگاه", "مدیریت دسته‌بندی‌ها، محصولات، نرخ‌های ارسال و کدهای تخفیف فروشگاه.",
        [
            new(ShopAuthorization.CatalogManagePermission, "مدیریت دسته‌بندی‌ها و محصولات", "اجازه ایجاد و ویرایش دسته‌بندی‌ها و محصولات فروشگاه.", "write"),
            new(ShopAuthorization.ShippingManagePermission, "مدیریت ارسال و تخفیف‌ها", "اجازه تعیین نرخ‌های ارسال و ایجاد یا غیرفعال‌کردن کدهای تخفیف.", "write")
        ])
    ];
}
```

## 3. `ShopConfig.cs`: register the contributor

Add one `using` and one line inside `RegisterServices`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenantForge.BuildingBlocks.Modules;
using TenantForge.BuildingBlocks.Permissions;
using TenantForge.Modules.Shop.Features.Authorization;
using TenantForge.Modules.Shop.Infrastructure;
```

`RegisterServices` currently ends with the `AddDbContext<ShopDbContext>` call's closing brace. Add immediately after it, still inside `RegisterServices`:

```csharp
        // B035: Shop's own contribution to the shared permission catalog
        // (the second real contributor, after IAM's — see B034).
        services.AddSingleton<IPermissionCatalogContributor, ShopPermissionCatalogContributor>();
```

## 4. Retrofit the 7 mutating call sites

Every change below is the same one-line-in, one-line-out shape: add the
permission key as a fourth argument. Nothing else in any of these
handlers changes.

**`features/categories/CategoriesFeature.cs` line 25** (POST create) —
current:

```csharp
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
```

New:

```csharp
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.CatalogManagePermission);
```

**`features/categories/CategoriesFeature.cs` line 74** (PUT update) —
same change, same new line, inside the `MapPut` handler.

**`features/products/ProductsFeature.cs` line 25** (POST create) — same
new line, inside the `MapPost` handler.

**`features/products/ProductsFeature.cs` line 112** (PUT update) — same
new line, inside the second `MapPut` handler (the one taking
`UpdateProductRequest`).

Lines 50/64/96/23/65 (the 5 read-only `GET` handlers listed in this
Spec's Context table) are **not** touched — they keep calling the
3-argument membership-only overload exactly as today.

**`features/shipping/ShippingRatesFeature.cs` line 41** (POST set) —
current:

```csharp
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
```

New:

```csharp
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.ShippingManagePermission);
```

**`features/coupons/CouponsFeature.cs` line 25** (POST create) — same
new line, inside the `MapPost` handler.

**`features/coupons/CouponsFeature.cs` line 88** (PATCH deactivate) —
same new line, inside the `MapPatch` handler.

After editing, re-run
`grep -rn "ShopAuthorization.AuthorizeTenantAccessAsync" src/modules/shop/`
and confirm: exactly 7 lines now carry a fourth argument
(`ShopAuthorization.CatalogManagePermission` ×4,
`ShopAuthorization.ShippingManagePermission` ×3) and exactly 5 lines
still carry only 3 arguments.

## 5. New integration tests

**In `ShopCatalogAdminIntegrationTests.cs`** (categories + products),
add tests proving, for both `POST /api/tenants/{tenantId}/shop/categories`
and `PUT /api/tenants/{tenantId}/shop/products/{productId}` (one
representative mutating endpoint per file section is enough; both files
already test create+update for their own resource, so add the
permission variants alongside the existing ones using the same
`PlatformAdminClientAsync`/`CreateOwnerAccountAsync`/
`CreateTenantWithOwnerAsync`/`SetMemberToken` helpers already in this
file):

1. **Member with no role grant gets 403.** Create the tenant with an
   Owner (existing helper), then create a second account, add it as a
   plain `Member` (open a `db.CreateContext()`, add a
   `TenantMembership` row directly with `TenantMembershipRole.Member` —
   mirror how `RolesIntegrationTests`-style setup adds a bare member, or
   if no such helper exists yet in this file, add a small private
   `CreateMemberAsync(Tsid tenantId, string email)` following the exact
   shape of the existing `CreateOwnerAccountAsync`, using
   `TenantMembership.Create(tenantId, accountId, TenantMembershipRole.Member, now)`),
   `SetMemberToken` for that account, call the mutating endpoint, assert
   `HttpStatusCode.Forbidden`.
2. **Member with a role granting `Shop.Catalog.Manage` gets through.**
   Same setup, but also create a `TenantRole` with
   `PermissionKeys = ["Shop.Catalog.Manage"]` and a
   `TenantMemberRoleAssignment` linking the membership to that role
   (both plain IAM domain entities, addable the same way
   `CreateOwnerAccountAsync` adds an `Account` — this test file already
   references `TenantForge.Modules.Iam.Domain`), call the same endpoint,
   assert success (`Created`/`OK`).
3. **Owner always gets through** — already implicitly covered by every
   existing create/update test in this file (all use the Owner token);
   add one explicit assertion comment noting this, no new test needed.

**In `ShopShippingCouponAdminIntegrationTests.cs`**, add the same two
new tests (no-grant 403, granted-role 200) for
`POST /api/tenants/{tenantId}/shop/shipping-rates`, using
`Shop.Shipping.Manage` in the granted role's `PermissionKeys`.

Read both existing test files in full before adding to them, and match
their exact existing helper method names/signatures — do not invent a
different shape for `CreateOwnerAccountAsync`/`SetMemberToken`/etc. if
the real file's names differ slightly from this Spec's description.

# Non-goals

- No third Shop permission key — coupons are gated by
  `Shop.Shipping.Manage`, not a key of their own.
- No change to any anonymous/read-only Shop endpoint: public storefront
  browsing, cart, checkout, order creation/payment/lookup stay exactly
  as anonymous as S26–S30 built them.
- No change to the 5 read-only Shop admin `GET` endpoints listed in this
  Spec's Context table — they stay membership-only.
- No frontend change (F042/F043 are separate tasks).

# Acceptance

- A tenant member with no Shop role grant gets `403 Forbidden` from
  every one of the 7 mutating endpoints listed in Scope §4.
- The same member, granted a tenant role containing the matching key,
  succeeds.
- A tenant Owner succeeds on every one of the 7 endpoints with no role
  grant at all (bypass), exactly as before this task.
- Every one of the 5 read-only endpoints stays reachable by a plain
  member with **no** Shop role grant (unchanged from before this task).
- `GET /api/permissions/catalog` now also returns a `shop` group with
  the two new keys, verifiable once F043 or a manual authenticated
  `curl` is run (no test-content change required in IAM's own catalog
  tests, since B034 already made the catalog generic).

# Verification

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

Manual: with an Owner token, all 7 mutating endpoints succeed unchanged;
with a fresh member token holding no role, each of the 7 returns 403;
after assigning a role with the matching key, each succeeds.

# Lifecycle

Add row `B035` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B034`, and Spec link
`tasks/backend/B035-shop-permission-enforcement.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/031-cross-module-permissions-and-nav.md` is the
permanent record and is never deleted.
