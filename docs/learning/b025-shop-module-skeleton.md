# B025 — Shop module persistence and skeleton

## 1. Files changed and why

- `src/modules/shop/TenantForge.Modules.Shop/` (new project) — TenantForge's
  second business module, created field-for-field on the shape of the IAM
  module: `domain/` entities, `infrastructure/` EF maps + `ShopDbContext`,
  and the two-method composition seam. It exists because S26 needs a home for
  the catalog (categories, products, variants, per-product size guide) that is
  **independent** of IAM.
- `domain/ShopCategory.cs`, `ShopProduct.cs`, `ShopProductVariant.cs`,
  `ShopSizeGuideColumn.cs`, `ShopSizeGuideRow.cs`, `ShopSizeGuideCell.cs` —
  the six catalog entities. All `internal sealed` with private setters, a
  private constructor and a validating static `Create` factory that stamps a
  fresh `Tsid` — the same shape as `Tenant`.
- `infrastructure/ShopTsidValueConverter.cs` — Shop's **own** copy of the
  `Tsid ↔ long` EF bridge. Copied, not referenced: Shop must never take a
  `ProjectReference` on the IAM module, and IAM's converter is `internal`.
- `infrastructure/Shop*Map.cs` — one `IEntityTypeConfiguration<T>` per
  entity: `ToTable`, `HasKey`, explicit `HasConversion(ShopTsidValueConverter
  .Shared)` on every `Tsid` column, `numeric(12,2)` prices, the unique
  per-tenant slug indexes and the FK delete behaviors (product→category
  `Restrict`, everything else `Cascade`).
- `infrastructure/ShopDbContext.cs` — the Shop context; six `DbSet`s applied
  via the maps.
- `ShopConfig.cs` — `IModuleConfig` implementation (`SectionName = "Shop"`):
  registers the scoped `ShopDbContext` on Npgsql with
  `MigrationsHistoryTable("__ShopMigrationsHistory")`, and fail-closed
  `ValidateConfiguration` that throws if `Shop:ShopDb` is blank.
- `ShopModule.cs` — the public seam: `AddShopModule` (registration, before
  `Build`, no I/O) and `UseShopModuleAsync` (activation, after `Build`:
  validate config, then `MigrateAsync`). No endpoint is mapped yet.
- `src/api/TenantForge.Api/Program.cs` — +1 using, +`AddShopModule` before
  `Build`, +`await app.UseShopModuleAsync()` after `UseIamModuleAsync`.
- `src/api/TenantForge.Api/TenantForge.Api.csproj` — new `ProjectReference`
  to the Shop module (the API still references BuildingBlocks only
  transitively, through the modules).
- `src/api/TenantForge.Api/appsettings.Development.json` — sibling `"Shop"`
  section with `ShopDb` pointing at the **same** connection string as
  `IAM:IamDb`.
- `src/modules/shop/.../infrastructure/Migrations/` — the generated
  `InitialShopCatalog` migration + designer + snapshot (generated, not
  hand-written).
- `tests/integration/TenantForge.Api.IntegrationTests/ShopModuleIntegrationTests.cs`
  (new) — fail-closed startup, exactly-six-tables, and restart-idempotency
  tests, plus a hermetic `ShopFailClosedApiFactory`.
- `ApiFactory.cs`, `IamDbFixture.cs`, `ProductionFailClosedTests.cs`,
  `PlatformAdminSeederTests.cs` — added `Shop:ShopDb` (and a dedicated
  `ShopDbFixture`/`ShopIsolatedCollection`) so every test host that now runs
  `UseShopModuleAsync` can start. No existing IAM test was weakened; all pass
  unmodified.
- `tasks/TASKS.md` — the `B025` row moved `planned → in_progress`.

## 2. Request flow from endpoint to response

B025 adds **no** endpoint, so the "request flow" is the **startup flow** the
host now performs, in deterministic order (this is what `UseShopModuleAsync`
guarantees):

1. `Program` builds the `WebApplicationBuilder`.
2. `AddIamModule` then `AddShopModule` register each module's services into
   the container — registration only, no I/O, no pass/fail decision
   (configuration is not fully assembled yet).
3. `app = builder.Build()`.
4. `UseIamModuleAsync` runs (unchanged): validate IAM config → auth/authorization
   middleware → migrate → seed → map IAM endpoints.
5. `UseShopModuleAsync` runs: validate `Shop:ShopDb` (throw → host never
   serves traffic if it is missing) → resolve a scoped `ShopDbContext` →
   `MigrateAsync()` applies `InitialShopCatalog` if pending.
6. `MapHealth()` + `app.Run()` — only `/health` and the existing IAM routes
   respond; no Shop route exists yet.

A later Shop endpoint will follow the same per-module pattern (feature handler
inside `features/`, mapped inside that module's activation seam), never
reaching across into IAM.

## 3. Backend concepts introduced

- **Second-module composition.** The same `IModuleConfig` + two-call seam
  (`Add<Module>Module` before `Build`, `Use<Module>ModuleAsync` after) now
  composes **two** modules into one host. Registration stays I/O-free;
  activation owns validation + migration, and is genuinely `await`ed.
- **Modules are independent assemblies.** Shop cannot see IAM's `internal`
  types, so it carries its **own** `TsidValueConverter`. Modules never
  reference each other; the only shared home is BuildingBlocks.
- **Shared database, per-module migration history.** Both `IamDbContext` and
  `ShopDbContext` point at the same PostgreSQL database. EF would otherwise
  track both modules' applied migrations in one default
  `__EFMigrationsHistory` table and collide — so Shop explicitly uses
  `__ShopMigrationsHistory`. This is what lets B026 read IAM membership rows
  with raw SQL while keeping the two contexts separate.
- **Fail-closed activation.** A module's required configuration is validated
  after `Build` (so all configuration sources, including test-host
  overrides, are visible); a missing key throws and stops startup rather than
  serving with an unconfigured database.
- **Npgsql identifier case.** The maps name tables/columns lowercase, so
  they are stored lowercase; but the history table name and EF's
  `MigrationId`/`ProductVersion` columns keep case and must be quoted in raw
  SQL.

## 4. Important security decisions

- **Fail closed, never silent.** Absent/blank `Shop:ShopDb` aborts startup
  with a message that names the key but never echoes a connection string or
  credential.
- **Default-deny boundary preserved.** B025 introduces no route, so there is
  nothing new to authorize; IAM's authentication/authorization middleware and
  tenant-isolation rules are untouched and still enforced server-side.
- **No cross-module data access yet.** Shop holds no `DbSet` for any `iam_*`
  table; the raw-SQL membership read is deliberately deferred to B026, and it
  only becomes possible because both modules share one physical database.
- **Identifier boundary unchanged.** TSIDs are stored as `bigint` and never
  leak to JSON in this slice (there is no Shop HTTP surface yet); the same
  `Tsid`/`bigint` convention applies through Shop's own converter.
- **No secrets added.** The only new configuration value reuses the existing
  development connection string already present in the repo.

## 5. Alternatives deliberately postponed

- **A `TenantForge.Modules.Shop.Contract` project** — per the
  `module-contract-project` admission rule it is created only once Shop has a
  delivered, exercised HTTP shape; B025 has none, so the request/response
  types will live inside the module for now.
- **A `docs/modules/Shop.md` living handbook** — Shop earns it in a later
  dedicated task once it has enough delivered surface, matching how IAM's
  handbook was created.
- **Cart/coupon/order/payment entities** — those belong to S27–S30; creating
  them now would be speculative persistence with no named consumer.
- **Endpoint + seeding** — no endpoint or seed data until a later slice has a
  visible consumer.
- **A shared (BuildingBlocks) TSID value converter** — the admission bar is
  not yet met; Shop keeps its own copy for now rather than promoting it
  speculatively.

## 6. Commands and manual steps to verify the slice

Automated (run in this task):

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj \
  --no-restore --filter "FullyQualifiedName~ShopModuleIntegrationTests" --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --no-restore --nologo
```

Expected results from this slice:

- solution build: `0 Warning(s)`, `0 Error(s)`;
- focused Shop tests: 3 passing (fail-closed startup, exactly the six catalog
  tables + own history table, restart idempotency);
- full integration suite: **119 passing** (116 pre-existing IAM, unmodified +
  3 new Shop) — the zero-regression proof.

Manual browser/API demo outline (Development, real Postgres on 5432):

1. Start PostgreSQL for the backend (db/user `tenantforge`).
2. `dotnet.exe run --project src/api/TenantForge.Api` (forces Development).
3. Confirm `GET /health` returns `ok` and `POST /api/auth/login` with the
   development admin still returns `200` (IAM unaffected).
4. `psql ... -c "\dt shop_*"` → exactly six `shop_*` tables.
5. `psql ... -c 'select "MigrationId" from "__ShopMigrationsHistory";'` → one
   `…InitialShopCatalog` row; IAM's `"__EFMigrationsHistory"` coexists in the
   same database.
6. Restart the API against the same migrated database — activation is a no-op
   (no second history row).
7. Fail-closed: start a host **without** `Shop:ShopDb` and confirm startup
   aborts with `The 'Shop:ShopDb' configuration value is required for Shop
   persistence.` (proven by the `MissingShopConnection_FailsClosedAtStartup`
   integration test, which the Spec designates as the fail-closed check).

## 7. Three review questions

1. Why must `Shop:ShopDb` point at the *same* physical database as
   `IAM:IamDb`, and why does that force Shop onto its own
   `__ShopMigrationsHistory` table instead of sharing EF's default one?
2. Shop cannot reference the IAM module, so why does it ship its **own**
   `TsidValueConverter` instead of sharing one — and what would have to be
   true before that converter could be promoted into BuildingBlocks?
3. Why does `UseShopModuleAsync` validate configuration and run migrations
   *after* `Build` rather than in `AddShopModule` before `Build`, and what
   would break if a module validated its configuration at registration time?
