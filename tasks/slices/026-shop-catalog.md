# S26 — Shop catalog: persistence, admin authoring, storefront browsing

## Outcome

TenantForge gains its first slice of a brand-new module,
`TenantForge.Modules.Shop`, mirroring IAM's real structure from day one
(`domain/`, `features/<feature>/`, `infrastructure/` with EF Core
migrations, a `ShopDbContext`, and the composition seam IAM only reached
after B016/S18 — `AddShopModule` before `Build`, `UseShopModuleAsync` after
`Build`). This slice delivers the catalog: categories, products, their
color/size variants and their per-product size-guide table, with an admin
authoring surface and a public, anonymous storefront read surface.

The product/design reference is a set of screenshots of the real site
darnoshop.com the user and the assistant already reviewed together
(no fetch is performed as part of this or any later Shop task — the
screenshots are the cited evidence, the same way S25 cited browser
screenshots it had already taken):

| Screenshot | Maps to |
| --- | --- |
| Category grid / storefront home | F030 (category grid) |
| Product listing within a category, paginated cards | F030 (product cards) |
| Product detail page: image gallery, color swatches, size buttons, a size-guide table below the fold, a quantity stepper and an add-to-cart button | F030 (product detail) |
| Boutique-owner-facing product form implied by the richness of one listing (many colors/sizes, one measurement table per product) | F028/F029 (admin authoring), B026 |

`docs/design-system.md` tokens/components govern the actual visual
execution everywhere; darnoshop's screenshots are cited only for *which UI
elements exist and how they relate* (color/size selection, a size-guide
table, a gallery, a stepper), never for darnoshop's own colors, type or
spacing.

## New entities (all new; none exist before this slice)

| Entity | Fields |
| --- | --- |
| `ShopCategory` | `Id` (Tsid), `TenantId` (Tsid), `Name`, `Slug`, `DisplayOrder` (int), `IsActive` (bool) |
| `ShopProduct` | `Id` (Tsid), `TenantId` (Tsid), `CategoryId` (Tsid), `Name`, `Slug`, `Description`, `BasePrice` (decimal), `CompareAtPrice` (decimal?), `IsActive` (bool) |
| `ShopProductVariant` | `Id` (Tsid), `ProductId` (Tsid), `Color`, `Size`, `Sku`, `StockQuantity` (int), `PriceOverride` (decimal?) |
| `ShopSizeGuideColumn` | `Id` (Tsid), `ProductId` (Tsid), `Name` (e.g. "دور سینه"), `DisplayOrder` (int) |
| `ShopSizeGuideRow` | `Id` (Tsid), `ProductId` (Tsid), `SizeLabel`, `DisplayOrder` (int) |
| `ShopSizeGuideCell` | `Id` (Tsid), `RowId` (Tsid), `ColumnId` (Tsid), `Value` |

Every entity's `TenantId`/tenant-reachable ownership follows IAM's existing
tenant-scoping pattern (a plain `Tsid` column, no foreign key into IAM's
own `iam_tenants` table — Shop is a separate `DbContext`/module and never
takes a `ProjectReference` on `TenantForge.Modules.Iam`; the tenant id in
the route is trusted the same way every existing tenant-scoped IAM
endpoint already trusts it). Every primary key is a TSID `bigint`, per
B017/S19. The size guide is a genuine relational table set, not a
speculative generic key-value system: darnoshop's own size-guide table has
a variable number of named measurement columns per product (a shirt might
measure "دور سینه"/"دور کمر"; a scarf might not have a size guide at all),
and `ShopSizeGuideCell` is exactly the join between one row (a size label)
and one column (a measurement) that this variability requires — nothing
more general is built.

## Route convention introduced in this slice (reused by every later Shop task)

IAM has no anonymous, tenant-scoped route today — every existing IAM route
is either platform-admin (`/api/platform/...`) or requires an authenticated
tenant member (`/api/tenants/{tenantId}/...` with `.RequireAuthorization()`).
This slice introduces the first anonymous route family, so it fixes one
explicit, consistent shape used by every Shop task from here on:

- **Admin (authenticated, tenant member)**: `/api/tenants/{tenantId}/shop/...`
  — identical shape to IAM's existing tenant-scoped routes, so the same
  `{tenantId}` route segment and `ClaimsPrincipal` tenant-membership check
  IAM already uses applies unchanged.
- **Public storefront (anonymous)**: `/api/shop/{tenantId}/...` — a
  distinct top-level `shop` prefix (not nested under `/api/tenants/...`)
  makes it visually and structurally obvious, at the route-table level,
  that everything under `/api/shop/` never requires authentication and
  never reads a `ClaimsPrincipal`. `{tenantId}` is the tenant's TSID, the
  same public identifier already used everywhere else — no separate public
  slug/subdomain concept is introduced.

## Scope, split into two backend tasks and four front tasks

**B025 — Shop module persistence and skeleton (first Shop task):**
- Create `TenantForge.Modules.Shop`/`TenantForge.Modules.Shop.Contract`-free
  (no Contract project yet — see Non-goals) project, `ShopDbContext`, the
  `AddShopModule`/`UseShopModuleAsync` seam, and one EF Core migration for
  `ShopCategory`, `ShopProduct`, `ShopProductVariant`,
  `ShopSizeGuideColumn`, `ShopSizeGuideRow`, `ShopSizeGuideCell` only.
- No HTTP endpoint (mirrors B004, IAM persistence, which shipped with none)
  except whatever minimal wiring `UseShopModuleAsync` needs to run
  (migration + registration only — no seed data, since nothing in
  `AGENTS.md`'s scope discipline calls for one yet).

**B026 — depends on B025 (category/product admin authoring):**
- Tenant-scoped admin endpoints: create/list/update categories; create,
  list and update one product together with its variants and size-guide
  rows in a single authoring payload, because a boutique owner naturally
  fills in one product form with all its color/size stock and its
  measurement table at once — forcing N separate calls for what one form
  submits is exactly the kind of indirection `AGENTS.md`'s "prefer direct,
  readable code" line warns against.

**B027 — depends on B026 (public storefront catalog reads):**
- Anonymous endpoints: list active categories, list active products within
  a category (paginated, reusing the S15/B015 pagination convention), and
  fetch one active product's full detail (variants with live stock,
  size-guide table) by slug.

**F028 — depends on F025 (admin catalog mock):**
- Category list/create/edit and product list/create/edit (inline variant
  rows, a size-guide table editor), against mocked data, matching
  `docs/design-system.md`.

**F029 — depends on F028, B026 (connect admin catalog):**
- Replace F028's mocks with real calls to B026.

**F030 — depends on F025 (storefront browsing mock):**
- Public, unauthenticated category grid, product cards, and a product
  detail page (gallery/lightbox, variant-aware color/size selectors with
  per-combination stock/sold-out state, size-guide table, quantity
  stepper, add-to-cart), against mocked data. This is a *separate,
  unauthenticated layout* from `DashboardShell` — it is not another
  authenticated admin page. It still reuses the shared design tokens
  (`src/web/src/index.css`) and primitive components (`Button`, `Input`,
  the shadcn/ui primitives already customized for TenantForge) rather than
  inventing a second design language.

**F031 — depends on F030, B027 (connect storefront browsing):**
- Replace F030's mocks with real calls to B027.

## Non-goals

- No `TenantForge.Modules.Shop.Contract` project. Per the
  `module-contract-project` Skill's admission rule ("create it the moment
  `Module` has at least one delivered, exercised HTTP request or response
  type — never earlier"), Shop's Contract project is only created once
  Shop's own endpoints are delivered and, per the Skill's stated intent, a
  second module has reason to reference them. This slice states the
  request/response types live directly inside `TenantForge.Modules.Shop`
  for now — exactly how IAM itself ran for 22 slices (S02–S22) before S23.
- No `ShopCart`, `ShopCartItem`, `ShopCoupon`, `ShopShippingRate`,
  `ShopOrder`, `ShopOrderItem`, `ShopPaymentAttempt` table — those belong
  to S27–S30 and creating them now would be speculative persistence ahead
  of a named consumer.
- No customer authentication/account of any kind. Every Shop endpoint in
  this and every later slice is anonymous by design (see S27's Non-goals
  for the full statement of this decision) — B026's admin endpoints are the
  one exception, and they reuse IAM's existing tenant-membership
  authentication/authorization, not a new Shop-specific login.
- No Telegram/social integration of any kind — that is a separate, later
  module, out of scope for every task in S26–S30.
- No `docs/modules/Shop.md` living handbook yet. Per `AGENTS.md`'s "Living
  module knowledge" section and the precedent of `docs/modules/IAM.md`
  (created only after several IAM slices), Shop earns its own handbook in
  a dedicated later task once the module has enough delivered surface to
  document usefully — not in S26.

## Verification

- `dotnet build TenantForge.sln --nologo` succeeds after each backend task.
- `dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo`
  passes in full after each backend task, including new Shop-specific
  integration tests each task adds and the full existing IAM suite
  unmodified (proving zero regression to IAM).
- `npm run build` and `npm run lint` in `src/web/` succeed after each front
  task.
- Real browser demo at 1440×900, 1024×768 and 390×844 for every page this
  slice touches (admin catalog screens, public storefront screens); no new
  browser console error.
- Registering this slice's rows in `tasks/TASKS.md` does not implement or
  run any of B025–B031/F028–F031; all rows are added as `planned`.
