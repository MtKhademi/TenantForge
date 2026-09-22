# B039 — Tenant storefront identity and policy content

Backend learning note for slice S35. This slice gives a tenant a public
storefront identity (store name, tagline, support phone, Instagram) and five
customer policy pages (about, shipping, payment, returns, privacy), stored as
one plain-text `ShopProfile` row per tenant and exposed through two
authenticated admin routes and one anonymous public route.

## 1. Files changed and why

**Production**
- `domain/ShopProfile.cs` (new) — one row per tenant; every text field is
  trimmed on the outside only (internal newlines preserved), `InstagramUrl`
  normalizes empty → `null`, `Version` is a client-managed optimistic-concurrency
  counter starting at `1` (not a DB rowversion), `UpdatedAtUtc`/`CreatedAtUtc`
  from an injected `TimeProvider`. `Create`/`Update` are plain state holders —
  all validation runs in the feature first.
- `infrastructure/ShopProfileMap.cs` (new) — `shop_profiles` table, column
  lengths per the Spec (name 100, tagline 180, support_phone 30, instagram_url
  300?, about_text 4000, four policies 6000), and the **unique**
  `ix_shop_profiles_tenant_id` index — the constraint that makes one profile
  per tenant a database invariant.
- `infrastructure/ShopDbContext.cs` — `DbSet<ShopProfile> Profiles` + the map.
- `infrastructure/Migrations/…_AddShopProfile.cs` (+ Designer) and
  `ShopDbContextModelSnapshot.cs` — the generated schema change (table + unique
  index only; inspected line by line).
- `features/authorization/ShopAuthorization.cs` — new `SettingsManagePermission`
  (`Shop.Settings.Manage`) constant, added to `KnownKeys` (now three keys).
- `features/authorization/ShopPermissionCatalogContributor.cs` — contributes
  the third key to the `"shop"` catalog group.
- `features/profiles/ProfileContracts.cs` (new) — the four HTTP records
  (`SaveShopProfileRequest`, `ShopProfileDto`, `ShopProfileResponse`,
  `PublicShopProfileResponse`). The public DTO deliberately has no
  `id`/`tenantId`/`version`/`updatedAtUtc`.
- `features/profiles/ProfilesFeature.cs` (new) — the three endpoints, the
  `FOR UPDATE` single-row lock, the `stale_version` 409, and the
  phone/Instagram-URL validators.
- `ShopModule.cs` — `MapProfilesFeature()` appended to the fixed mapping order.

**Tests**
- `ShopProfileIntegrationTests.cs` (new) + a dedicated `ShopProfileDbFixture`
  — twelve one-scenario facts.
- `ShopModuleIntegrationTests.cs` — `shop_profiles` added to the exact
  `ExpectedShopTables` roster (required, or the startup test fails).
- `RolePermissionIntegrationTests.cs` — the two hand-maintained exact key
  rosters (aggregated catalog + Owner union) updated to include
  `Shop.Settings.Manage`.

**Docs**
- `docs/modules/SHOP.md`, `docs/design/shop/http-contracts.md` (S35), this
  note, and the agent/human knowledge files.

## 2. Request flow

**Admin read** — `GET /api/tenants/{tenantId}/shop/profile`:
`ShopAuthorization.AuthorizeTenantAccessAsync` (membership-only overload) →
query `shop_profiles` by the server-derived `TenantId` → `200 { profile: null }`
when absent, else the DTO. The empty state is a 200, never a 404.

**Admin save** — `PUT …/shop/profile`: authorize with
`Shop.Settings.Manage` → field validation (lengths, phone allowlist, Instagram
URL) → open a transaction → lock the tenant's single row
(`SELECT * FROM shop_profiles WHERE tenant_id = {0} FOR UPDATE`, via
`FromSqlRaw` on the raw bigint) → if `expectedVersion` is null, create (409 if
a row already exists) else require an exact version match (409 otherwise) →
`SaveChangesAsync` → commit. If the unique `tenant_id` constraint fires during
the save (two racing first-creates), the caught `DbUpdateException` with
PostgreSQL SQLSTATE `23505` is rolled back and surfaced as the same
`stale_version` 409 — the constraint, not the earlier read, is what actually
closes the create race.

**Public read** — `GET /api/shop/{tenantId}/profile` (no auth): parse the
tenant TSID (malformed → 404) → load only a **published** profile → 404 when
missing or unpublished (the storefront renders one neutral fallback for both) →
else the public DTO, which carries no `version`.

## 3. Backend concepts introduced

- **Client-managed optimistic concurrency.** `Version` is an `int` the client
  echoes back as `expectedVersion`; the server compares and bumps it. This is
  different from product media's `GalleryVersion` (same idea) and different
  from a database `rowversion`/`ConcurrencyToken` — nothing here is
  EF-managed.
- **Unique index as the concurrency authority.** A bare "does a row exist?"
  `AnyAsync` before an insert does *not* close a create race. The unique index
  is what makes exactly one concurrent insert win; the app then maps the loser's
  `23505` to a typed 409 instead of leaking a raw exception.
- **Typed RFC 7807 via `ProblemDetails`.** `Results.Problem(new ProblemDetails
  { Type = "stale_version", … })` sets the `type` member — the module's first
  use of a non-default `type`, giving the frontend a stable machine-readable
  conflict code rather than only a title.
- **Two different authorizations on two different routes for one entity.** The
  admin read is membership-only (like every other read-only admin route); the
  admin write and nothing else is gated by `Shop.Settings.Manage`; the public
  read is anonymous. Same table, three trust levels.

## 4. Security decisions

- **Tenant always from the route context.** `TenantId` is taken from the
  authenticated route segment and `ShopAuthorization`; the request body has no
  `tenantId` field, so a caller can never write into another tenant's profile
  by typing one in.
- **Default-deny + fail closed.** No membership → 403, no permission → 403,
  unauthenticated → 401, malformed/cross-tenant public id → the same 404 as an
  unknown one (no existence leak).
- **Plain text end to end.** No field is rendered as HTML; the Instagram URL is
  validated to `instagram.com` / `*.instagram.com` (the suffix check anchors on
  the dot, so `instagram.com.evil.example` is rejected), and the phone is a
  conservative character allowlist — the server never trusts the client's claim
  about what the text is.
- **No secrets in logs.** The feature logs nothing; only stable IDs and reason
  codes are relevant here, and none are written.

## 5. Alternatives deliberately postponed

- **EF `ConcurrencyToken`/`rowversion`.** Would make the counter
  EF-managed and change the wire shape to an opaque token; the Spec asks for an
  explicit integer `version` the client controls, so a plain int + manual
  compare was chosen.
- **A database advisory lock or `GET_LOCK`** for the create race. The unique
  index already guarantees one winner without a second locking mechanism.
- **Catalog gating on `IsPublished`.** The Spec is explicit that publication
  gates only the profile/policy pages; no storefront route reads `IsPublished`,
  so existing storefront URLs keep working during rollout. A test pins this.

## 6. Commands and manual verification

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test TenantForge.sln --nologo --filter FullyQualifiedName~ShopProfileIntegrationTests
dotnet.exe test TenantForge.sln --nologo
```

Manual (dev): sign in as a platform admin, create a tenant, and as its owner
`GET /api/tenants/{tenantId}/shop/profile` (expect `{ profile: null }`), then
`PUT` it with `expectedVersion: null` (expect `version: 1`), then a second
`PUT` with `expectedVersion: 1` (expect `version: 2`), and a third with
`expectedVersion: 1` (expect `409 stale_version`). Confirm the public route
`GET /api/shop/{tenantId}/profile` returns `404` while `isPublished` is false
and the full public body (no `version`) once published. F057 binds the admin
settings form and the storefront header/footer/policy pages to these exact
routes.

## 7. Three review questions

1. The create path locks the single tenant row with `FOR UPDATE`, but a `SELECT
   … FOR UPDATE` on a row that does not yet exist locks nothing. What actually
   stops two concurrent first-creates from both inserting — and why is the
   `FOR UPDATE` lock still worth having for the update path?
2. `Version` is a plain `int` compared and incremented by the feature, not an
   EF `ConcurrencyToken`. What would change in the response shape and the
   concurrency guarantee if we let EF own it with a database rowversion
   instead?
3. The public route returns the same `404` for "no profile", "unpublished" and
   "malformed tenant id". Is that the right trade-off, and what would a client
   need to distinguish them (and why is that distinction dangerous to expose)?
