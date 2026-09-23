# Backend agent knowledge

Operational memory for the agent running `/backend-task`. Read this file
completely before implementing any backend task. It describes the current
codebase, not its history. Keep it short — link to the detailed handbook
instead of copying it here.

Human-facing explanations live in [HUMAN-backend.md](HUMAN-backend.md) and
`docs/learning/`. Do **not** read those to implement a task.

## Fast facts

| Fact | Value |
| --- | --- |
| Runtime | .NET 10, ASP.NET Core Minimal APIs |
| Shape | Modular monolith, one visible vertical slice per task |
| Database | PostgreSQL 16 (Docker Compose), EF Core + Npgsql |
| Solution | `TenantForge.sln` |
| Modules today | `iam`, `shop` |
| Test project | `tests/integration/TenantForge.Api.IntegrationTests` (xUnit + Testcontainers) |
| CLI on this machine | `dotnet.exe`, **not** `dotnet` |

## Layout

```text
src/api/TenantForge.Api/                     # host: Program.cs, HealthEndpoints.cs, appsettings
src/building-blocks/TenantForge.BuildingBlocks/
  Identifiers/TsidId.cs                      # TSID formatting/parsing
  Modules/IModuleConfig.cs                   # module registration + validation contract
  Permissions/                               # IPermissionCatalogContributor, AggregatedPermissionCatalog, PermissionGroup, PermissionDescriptor
src/modules/iam/TenantForge.Modules.Iam/          # domain/ features/ infrastructure/ + IamModule.cs, IAMConfig.cs, AuthorizationPolicyNames.cs
src/modules/iam/TenantForge.Modules.Iam.Contract/ # Requests/ Queries/ Responses/ — IAM's public HTTP shapes, zero outgoing references
src/modules/shop/TenantForge.Modules.Shop/        # domain/ features/ infrastructure/ + ShopModule.cs, ShopConfig.cs
tests/integration/TenantForge.Api.IntegrationTests/
```

Shop has **no** `.Contract` project. Its DTOs live beside the feature in
`features/<area>/<Area>Contracts.cs`.

## Architecture boundaries

- Dependency direction is **API → module → BuildingBlocks**. BuildingBlocks
  never references the API or a module. Modules never reference each other's
  implementation project.
- `TenantForge.Modules.Iam` references `TenantForge.Modules.Iam.Contract`.
  Nothing else does.
- BuildingBlocks is not a `Common`/`Utils` bucket. A type is admitted only
  with proven cross-module or accepted system-wide evidence — see the
  admission checklist in `docs/building-blocks/README.md`.
- Cross-module communication today goes through BuildingBlocks contracts
  (`IPermissionCatalogContributor`) or raw SQL against the other module's
  tables when a task explicitly allows it (Shop reads IAM membership this
  way in `src/modules/shop/TenantForge.Modules.Shop/features/authorization/ShopAuthorization.cs`). Do not add a
  project reference between modules.

## Module composition seam

Every module exposes exactly two public methods, called from
`src/api/TenantForge.Api/Program.cs` and nowhere else:

```csharp
builder.Services.AddShopModule(builder.Environment);   // registration only, no I/O
await app.UseShopModuleAsync();                        // activation
```

Activation owns, in this order: configuration validation (fail closed) →
pending migrations → endpoint mapping. IAM additionally owns authentication
middleware, authorization middleware and idempotent platform-admin seeding.
Never validate at registration time, never expose those steps as separate host
calls, never add a hosted service to hide the await.

## Feature pattern

One folder per capability under `features/`:

- `<Area>Feature.cs` — a `MapXFeature(this IEndpointRouteBuilder)` extension
  plus its handlers. Types stay `internal`.
- `<Area>Contracts.cs` — request/response records (Shop), or the matching
  record in `TenantForge.Modules.Iam.Contract` (IAM).
- The module's `MapXModule` calls every `MapXFeature()` in a fixed order.

Copy the nearest existing feature in the same module before inventing a shape.

## Identifiers

- Database identity and foreign keys are PostgreSQL `bigint`.
- Domain and EF model values are `Tsid`.
- HTTP request/response IDs and JWT subjects are **canonical 13-character TSID
  strings**.
- Never serialize the backing integer to JSON and never accept a decimal ID
  from a client. Conversion lives in `TsidValueConverter.cs` /
  `ShopTsidValueConverter.cs`.

## API contract rules

- The backend contract is the source of truth. The frontend never invents a
  different permanent shape.
- Errors use RFC 7807 Problem Details as the single JSON error body
  (`Results.Problem` / `ValidationProblem`). Do not add a second error shape.
- List endpoints return the shared pagination envelope. `PaginationMetadata`
  has **six** members: `pageNumber`, `pageSize`, `totalCount`, `totalPages`,
  `hasPreviousPage`, `hasNextPage`.
- Shop HTTP shapes are documented in `docs/design/shop/http-contracts.md`;
  IAM's in `docs/contracts/iam.md` and `docs/modules/IAM.md`.
- A slice normally adds no more than one or two endpoints, and only with a
  named current or immediately dependent frontend consumer.
- Do not change an accepted contract without updating the active task first.

## Authentication and authorization

- Tenant isolation and permission checks are **server-side**. Hiding UI is not
  authorization. Default to deny when tenant or permission context is missing.
- IAM registers the `PlatformAdmin` policy
  (`AuthorizationPolicyNames.PlatformAdmin`): unauthenticated → 401,
  authenticated but failing the `isPlatformAdmin` claim → 403.
- Tenant-scoped Shop routes check membership and permission through
  `ShopAuthorization`. Delivered permission keys today:
  `Shop.Catalog.Manage`, `Shop.Shipping.Manage`, `Shop.Settings.Manage`
  (gates `PUT …/shop/profile`), `Shop.Orders.View` (gates the admin order
  list/detail — the first Shop *read* gated by a permission key, not just
  membership) and `Shop.Orders.Manage` (registered in the catalog but enforced
  nowhere yet — reserved for B043's order mutations). The tenant Owner role
  bypasses the permission check. Every authenticated admin route chain-ends with
  `.RequireAuthorization()` so an anonymous caller gets `401` (JWT challenge),
  not the `403` that `ShopAuthorization`'s `Results.Forbid()` would give.
- Product media (`ShopProductImage`, `features/media/`) never trusts a
  client's filename or `Content-Type`: `ShopImageValidator` decodes the
  actual bytes with `SixLabors.ImageSharp` (pinned `3.1.11`), accepts only
  JPEG/PNG/WebP, rejects animated/multi-frame/oversized input, strips
  EXIF/ICC/XMP, and re-encodes to WebP before anything reaches disk.
  `IShopMediaStorage`/`LocalShopMediaStorage` stage a file, commit it to its
  final path only after the owning DB transaction succeeds, and require
  `Shop:MediaRoot` (an absolute, writable directory validated at activation —
  fail closed, same as `Shop:ShopDb`).
- New permission keys are registered by the module's
  `IPermissionCatalogContributor` (`IamPermissionCatalogContributor`,
  `ShopPermissionCatalogContributor`) and aggregated in `Program.cs`. The
  frontend reads the catalog from the server; keep the two lists in step.
- Development-only shortcuts must fail closed outside `Development`.
- Never log passwords, tokens or secrets.

## Persistence and migrations

- One `DbContext` per module: `IamDbContext`, `ShopDbContext`, each under
  `infrastructure/`.
- Every entity has a sibling `<Entity>Map.cs` `IEntityTypeConfiguration`. Put
  column names, lengths, indexes and conversions there, not in the entity.
- Migrations live in `infrastructure/Migrations/` and are applied at startup by
  the module's activation phase.
- Use `TimeProvider` as the injectable clock, never `DateTime.UtcNow` inline.
- Shop cart leases are server-owned: cart create/read responses include `expiresAtUtc`, successful add/update/delete extends the lease, reads do not, expired active carts return `410 shop_cart_expired`, and successful order creation marks the cart `Converted` instead of deleting the cart row.
- Handle races with a row lock plus a database constraint, not a bare
  `AnyAsync` check.

## Testing

- One integration test project covers everything:
  `tests/integration/TenantForge.Api.IntegrationTests`.
- `ApiFactory` / `IamDbFixture` boot the real host against a Testcontainers
  PostgreSQL instance. **Docker must be running.**
- Every security-sensitive behavior needs both the happy path and the
  relevant unauthorized/forbidden path.
- `BuildingBlocksArchitectureTests` and `IamContractArchitectureTests` lock the
  dependency direction and the exported type roster by hand. A surface change
  means editing those hand-written lists too.
- Never weaken a test or narrow a filter to obtain a pass.

## Commands

```bash
dotnet.exe build TenantForge.sln
dotnet.exe test                                     # whole integration suite (needs Docker)
dotnet.exe test --filter FullyQualifiedName~ShopOrderIntegrationTests
docker compose up -d postgres
ASPNETCORE_ENVIRONMENT=Development dotnet.exe run --project src/api/TenantForge.Api/TenantForge.Api.csproj --urls http://0.0.0.0:5000
```

EF migration (run from the repository root):

```bash
dotnet.exe ef migrations add <Name> \
  --project src/modules/shop/TenantForge.Modules.Shop/TenantForge.Modules.Shop.csproj \
  --startup-project src/api/TenantForge.Api/TenantForge.Api.csproj \
  --context ShopDbContext
```

## Known traps

1. There is no Linux `dotnet` binary on this machine. Every build/test/run
   command is `dotnet.exe`.
2. `dotnet.exe run` forces `Development` through `launchSettings.json`. To
   prove Production fail-closed behavior, run the built DLL directly with
   `ASPNETCORE_ENVIRONMENT=Production`.
3. A Windows process is not reachable from WSL on `127.0.0.1`. Bind
   `--urls http://0.0.0.0:<port>` and curl the WSL gateway IP from
   `ip route | head -1`.
4. `WebApplicationFactory` hosts must be hermetic: point the content root at an
   empty temp directory so real `appsettings.*.json` files cannot leak into
   test configuration.
5. `dotnet.exe test` fails with a container error, not a compile error, when
   Docker is not running. Start Docker before blaming the code.
6. Shop entities are not all tenant-scoped. `ShopPaymentAttempt` has no
   `TenantId` column — filter through `ShopOrder`.
7. Adding a permission key in the backend without adding it to the frontend
   catalog (or the reverse) silently breaks the permission matrix.
8. Adding a new *required* module config key (e.g. `Shop:MediaRoot`) breaks
   every narrow hand-built `WebApplicationFactory` fixture that sets its own
   in-memory config instead of using the shared `ApiFactory` — grep for other
   fixtures supplying `Shop:ShopDb`/`IAM:IamDb` directly and add the new key
   there too, then re-run the **full** suite (not just the new feature's
   filtered tests) before calling a slice done.
9. `ShopModuleIntegrationTests.ExpectedShopTables` is a hand-maintained exact
   table roster. Every new Shop table must be added there in the same task or
   the startup-contract tests fail.
10. EF Core will **not** translate a *method call* that returns an
    `IQueryable` as a correlated subquery — write the `Min`/`Max`/`Count`
    inline in the `Select`, or the request 500s with
    `InvalidOperationException` at query-translation time. Model an empty
    aggregate as nullable (`decimal?`): C# `Min` on a non-nullable sequence
    cannot express "no rows" (a sold-out product has no in-stock variant).
11. In LINQ, `OrderBy` after `OrderBy` **replaces** the first ordering; use
    `ThenBy`/`ThenByDescending` to keep a primary ordering (e.g. sold-out-last)
    stable across the chosen secondary sort.
12. To build a translatable `Where` predicate dynamically (a reusable rule
    applied to several routes), capture the source `IQueryable` in a
    normally-written lambda and return it as
    `Expression<Func<T, bool>>` (see `CategoryVisibility.For` in
    `features/categories/`) — EF Core translates the captured queryable into a
    correlated `EXISTS`. A hand-built `Expression.Constant`/`Expression.Call`
    tree is **not** translated and 500s at query-translation time.
13. Close check-then-write races with a row lock held across the write:
    `db.Set.FromSqlRaw("SELECT * FROM <table> WHERE tenant_id = {0} AND id =
    {1} FOR UPDATE", …)` inside the transaction, then the guard check and
    `SaveChangesAsync` (B038's reparent guard uses this for `shop_categories`).
    A bare `AnyAsync` before the write does not close the race.
14. Adding a Shop (or any module) permission key breaks two **hand-maintained
   exact rosters** in `RolePermissionIntegrationTests.cs` — the aggregated
   `/api/permissions/catalog` key list and the Owner's resolved
   `/me/permissions` union — because an Owner's union is *every* registered
   module's known keys, not just the module under test. Update both lists in
   the same task (they are ordered; insert the new key in sort order), and the
   `ShopModuleIntegrationTests.ExpectedShopTables` roster if a table was added
   too. A full-suite run is what catches a missed roster.
15. Shop categories are exactly two levels (root + one child, B038). Public
    visibility is "effective activity": a category shows on **every** public
    route (list, by-slug, all-products, detail, media bytes) only while it and
    its root are `IsActive` — always route public eligibility through
    `CategoryVisibility`; a root's slug includes its direct children's
    products, a child's slug only its own.
16. Every coupon rule (minimum subtotal, max-discount cap, redemption limit,
    expiry, active flag) is centralized in the pure `ShopCouponPolicy.Evaluate`
    (`features/coupons/ShopCouponPolicy.cs`) — a single owner so the checkout
    preview and the order-consumption path cannot drift. It is pure: it reads
    the loaded coupon and returns a `CouponEvaluation` (valid flag, capped
    discount, stable `ErrorCode`), never writes. `coupon_not_found` is produced
    by the caller (tenant-first lookup → null), not by `Evaluate`; the other
    four codes it returns are `coupon_inactive`, `coupon_expired` (strictly
    past `ExpiresAtUtc`), `coupon_minimum_not_met`, `coupon_limit_reached`. A
    rejected coupon surfaces on the anonymous routes as a `400` `couponCode`
    field error naming the exact code in parentheses (the first three keep the
    historical "not valid" phrasing so B030/B031 tests stay green). Order
    creation re-evaluates under a coupon `FOR UPDATE` row lock (lock order cart
    → coupon) and bumps `RedeemedCount` exactly once; checkout never increments.
17. The integration test assembly is a single namespace with `internal` DTO
    records defined per feature file (`CouponDto`, `CouponListDto`,
    `CheckoutSummaryDto`, `OrderCreatedDto`, …). Defining a record with an
    already-used name in a new test file is a CS0101 collision (the compiler
    also raises CS8863 "only a single partial type may have a parameter list").
    Prefix new DTO records with the task id (e.g. `B041CouponDto`) so they
    cannot collide with the existing per-feature shapes.
18. When a test builds a query string by interpolation, a
    `DateTimeOffset`'s `:O` round-trip form for a UTC value contains a literal
    `+00:00`, and in a **raw query string a `+` decodes to a space** — so the
    endpoint rejects it as an unparseable value. A real HTTP client sends
    `%2B`; tests must percent-encode the value (`Uri.EscapeDataString(...)`)
    before interpolating it, or the `400`/`400-where-200` failure looks like an
    endpoint bug when it is a test URL-construction bug (B042 hit this on its
    `fromUtc`/`toUtc` filter tests).

## Decisions future tasks must preserve

- Two-phase module composition (`AddXModule` / `UseXModuleAsync`), no third
  public entry point.
- `bigint` storage / `Tsid` domain / 13-char string wire for every identifier.
- RFC 7807 as the only error body.
- BuildingBlocks admission rule; `Module.Contract` holds only delivered HTTP
  shapes with no ASP.NET/EF/Npgsql dependency.
- Server-side authorization, fail closed by default.
- No speculative endpoint, abstraction, background service or empty project.

## Read-first gates (conditional, from `AGENTS.md`)

Read the whole handbook during discovery **only** when the task touches that
area, then classify the diff against its change-impact checklist before review:

| Task touches | Read first | Declaration if nothing changed |
| --- | --- | --- |
| `src/modules/iam/**` or an IAM contract in BuildingBlocks/the host | `docs/modules/IAM.md` | `IAM.md impact: none — <specific reason>` |
| `src/modules/shop/**` or a Shop contract in BuildingBlocks/the host | `docs/modules/SHOP.md` | `SHOP.md impact: none — <specific reason>` |
| `src/building-blocks/**`, a reference to it, or `IModuleConfig`/`TsidId` | `docs/building-blocks/README.md` | `BuildingBlocks docs impact: none — <specific reason>` |
| `src/modules/iam/TenantForge.Modules.Iam.Contract/**` or a reference to it | `docs/contracts/iam.md` | `IAM Contract docs impact: none — <specific reason>` |

A vague "docs not needed" does not satisfy these gates.

## More detail

- `AGENTS.md` — shared rules, ownership, ledger lifecycle, Git safety
- `docs/architecture.md` — system shape, testing strategy, WSL environment
- `docs/design/shop/` — Shop HTTP contracts, inventory, payments
- `.opencode/skills/vertical-slice-delivery/SKILL.md` — delivery protocol
- `.opencode/skills/module-contract-project/SKILL.md` — when a module earns a
  `.Contract` project

## Maintaining this file

Update it in the same task that changes a fact above. Add durable facts only:
no debugging notes, no speculation, no abandoned approaches, no secrets, no
large code blocks. If a task changed nothing here, say so in the completion
report instead of padding the file.
