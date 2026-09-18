# B035 — Shop permission enforcement

## 1. Files changed and why

| File | Why |
| --- | --- |
| `src/modules/shop/TenantForge.Modules.Shop/features/authorization/ShopAuthorization.cs` | Was a membership-only gate (raw `COUNT(*)` against IAM's tables). Now also gates on a permission key: added the two key constants, `KnownKeys`, a permission-checking overload, and `ResolveAssignedShopKeysAsync`. The membership query now selects the membership's `role` string instead of counting, so the Owner bypass needs no second round trip. |
| `src/modules/shop/TenantForge.Modules.Shop/features/authorization/ShopPermissionCatalogContributor.cs` (new) | Shop's `IPermissionCatalogContributor` — the second real consumer of the B034 BuildingBlocks catalog seam. Advertises one `shop` group with the two keys so `GET /api/permissions/catalog` and `AllKnownKeys` now include them. |
| `src/modules/shop/TenantForge.Modules.Shop/ShopConfig.cs` | Registers the contributor in `RegisterServices`, exactly where B034's own Spec said the second contributor would land. The host's `IAggregatedPermissionCatalog` factory picks it up automatically — no `Program.cs` change. |
| `features/categories/CategoriesFeature.cs`, `features/products/ProductsFeature.cs`, `features/shipping/ShippingRatesFeature.cs`, `features/coupons/CouponsFeature.cs` | The 7 mutating call sites pass a key (Catalog ×4, Shipping ×3). The 5 read-only GETs keep the 3-arg membership-only overload. |
| `tests/.../ShopCatalogAdminIntegrationTests.cs` | +2 tests (member-no-grant 403 / role-granted success) + 2 small setup helpers, matching the file's existing helper style. |
| `tests/.../ShopShippingCouponAdminIntegrationTests.cs` | +2 tests (same pair) + the same 2 helpers. |
| `tests/.../RolePermissionIntegrationTests.cs` | The catalog test's expected key set grew from 4 to 6 (renamed accordingly) — a planned deviation: the Spec said "no test-content change required in IAM's own catalog tests", but that test asserts an *exact* key list, which B035 deliberately changes. It still pins the exact complete set. |
| `docs/building-blocks/README.md`, `docs/modules/IAM.md` | Handbook impact classification (see §5). |

## 2. Request flow (e.g. `POST /api/tenants/{tenantId}/shop/categories`)

1. `RequireAuthorization()` middleware rejects unauthenticated callers with 401 before the handler runs.
2. The handler calls `ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.CatalogManagePermission)`.
3. The private core: parse the route's `tenantId` as a TSID (else `Forbid`), read the account from the JWT `sub` claim (else `Forbid`).
4. One raw-SQL query — `SELECT m.role FROM iam_tenant_memberships m JOIN iam_accounts … AND a.status='Active' JOIN iam_tenants … AND t.status='Active' WHERE …` — returns the membership's role string, or zero rows (no active/active/active link → `Forbid`).
5. If a key was requested: role `"Owner"` ⇒ bypass (any Shop key is granted); otherwise `ResolveAssignedShopKeysAsync` runs a second raw-SQL query (`unnest(r.permission_keys)` over the member's role assignments), intersected with `KnownKeys`; the key must be in that set or `Forbid` (403).
6. On success the handler proceeds exactly as before; the `ShopTenantAccess.Result` slot is the same "return this `IResult` directly when non-null" shape IAM uses.

## 3. Backend concepts introduced

- **Cross-database authorization without cross-module references.** Shop cannot reference IAM's EF entities (module→module dependency is forbidden), and BuildingBlocks must not reference EF Core. The gate therefore stays raw SQL against IAM's *physical* tables, mirroring IAM's own active/active/active join. The trade-off: Shop hardcodes IAM's table/column names in SQL strings; they are verified by the full integration suite, not compile-time checks.
- **String vs enum at the SQL boundary.** The `role` column is persisted as text (`HasConversion<string>()` on `TenantMembershipRole`), which is why the raw SQL compares `m.role = 'Owner'` directly.
- **Owner bypass as a policy decision, encoded in one line:** `roles[0] == "Owner" ? KnownKeys.Contains(key) : assignedKeys.Contains(key)`.
- **Contributor/aggregator pattern (B034's seam, now with two real consumers).** Each module registers its own `IPermissionCatalogContributor`; the host builds one `AggregatedPermissionCatalog` from all of them. Adding Shop's contributor changed zero host code — that is the seam working as designed.
- **Intersecting, not trusting, the role's key list.** A role's `permission_keys` may contain keys from other modules; Shop only acts on keys in its own `KnownKeys`. Likewise IAM's `ResolvePermissionsAsync` filters through the aggregate's `AllKnownKeys`.

## 4. Security decisions

- **Default deny at every step:** unauthenticated → 401 (middleware); bad TSID / missing `sub` / no active membership / no grant → 403. There is no path that returns success without an active membership row.
- **Membership ≠ permission.** The 5 read-only endpoints stay membership-only on purpose (mirrors IAM's own split), but every mutation now requires a granted key.
- **Owner bypass is broad but bounded:** it grants Shop's own two keys, never any key outside Shop's `KnownKeys` — and a non-Owner's keys are also intersected with `KnownKeys`, so a role cannot "grant" itself a key Shop doesn't know.
- **No UI reliance:** everything is enforced server-side; the frontend gating (F043) is presentation only.

## 5. Alternatives deliberately postponed

- **EF entities/migrations for IAM's tables in Shop's context** — would couple the modules' migration histories (they deliberately track separate history tables in the same physical database).
- **A shared authorization service in BuildingBlocks** — rejected: BuildingBlocks must stay free of EF/ASP.NET; the raw-SQL gate is module-owned (the guide's exclusion table says exactly this for IAM's own authorization code).
- **A third Shop key for coupons** — the slice's Non-goals keep the count at 2.
- **Caching resolved permissions** — premature; the queries are two indexed lookups per mutating request.

## 6. Verification

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

Manual (live host, Owner token + a fresh Member token):
1. Owner: all 7 mutating endpoints succeed (unchanged behavior).
2. New member (no role): all 7 mutating endpoints → 403; the 5 read-only GETs → 200.
3. Assign a role containing `Shop.Catalog.Manage` to the member: category/product mutations succeed, shipping/coupon mutations still 403.
4. Add `Shop.Shipping.Manage` to the role: shipping-rate POST and coupon create/deactivate succeed.
5. `GET /api/permissions/catalog` now returns a `shop` group with both keys (6 keys total).

## 7. Three review questions

1. The membership query changed from `COUNT(*)` to selecting the role string. What would break if the column were one day mapped as an integer instead of text — and how could the code have been written to survive that change?
2. `ResolveAssignedShopKeysAsync` re-checks the tenant/account pair in its own `WHERE` clause even though the caller already verified membership. Is that redundant, defensive, or necessary for correctness — and what is the cost?
3. If a third module (e.g. `TenantForge.Modules.Invoice`) needed permission keys, which files would you touch and which would you *not* have to? What would prove the contributor/aggregator seam generalizes (or fails)?
