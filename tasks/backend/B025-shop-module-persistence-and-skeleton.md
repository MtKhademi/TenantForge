---
id: B025
slice: S26
title: Shop module persistence and skeleton
agent: backend-mentor
source: tasks/slices/026-shop-catalog.md
---

# Objective

Create the new `TenantForge.Modules.Shop` project skeleton — its own
`domain/`, `features/`, `infrastructure/` folders, a `ShopDbContext`, and
the two-method composition seam (`AddShopModule` before `Build`,
`UseShopModuleAsync` after `Build`) mirroring IAM's current
`AddIamModule`/`UseIamModuleAsync` shape — plus one EF Core migration for
the six catalog entities: `ShopCategory`, `ShopProduct`,
`ShopProductVariant`, `ShopSizeGuideColumn`, `ShopSizeGuideRow`,
`ShopSizeGuideCell`. No HTTP endpoint is added in this task.

# Context

Read `tasks/slices/026-shop-catalog.md` completely — it is the
authoritative contract for this task and the two front tasks that build
on it. `docs/modules/IAM.md` is IAM's living handbook; Shop does not get
its own handbook yet (per the slice's Non-goals and `AGENTS.md`'s "Living
module knowledge" precedent, that arrives once Shop has real delivered
surface), but read `docs/modules/IAM.md` Section 3 (dependency/composition
diagram) and Section 4 (source-code map) for the exact shape to mirror.

Read the real IAM files this task's structure is modeled on before writing
anything:
- `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs` — the
  `AddIamModule`/`UseIamModuleAsync` seam: registration does no I/O and no
  pass/fail decision; activation validates configuration (fail closed),
  installs auth middleware, migrates, seeds, then maps endpoints, in that
  order.
- `src/modules/iam/TenantForge.Modules.Iam/IAMConfig.cs` — the
  `IModuleConfig` implementation: `RegisterServices` registers the
  `DbContext` against a configuration-bound connection string plus any
  module services; `ValidateConfiguration` fails startup with a clear
  message when a required setting is missing.
- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/IamDbContext.cs`
  — a plain `internal sealed class ... : DbContext` with one `DbSet<T>`
  property per entity and `OnModelCreating` applying one `IEntityTypeConfiguration<T>`
  per entity.
- `src/building-blocks/TenantForge.BuildingBlocks/Identifiers/TsidId.cs`
  and `Modules/IModuleConfig.cs` — the identifier and module-registration
  contracts this task depends on (both already `done`, B017/B018; no
  change to either file in this task).
- `src/api/TenantForge.Api/Program.cs` — where `AddIamModule`/
  `UseIamModuleAsync` are called; this task adds the matching
  `AddShopModule`/`await app.UseShopModuleAsync();` calls beside them.

There is no dependency row to add for B020 (BuildingBlocks) or B016–B019
(the composition-seam/TSID/knowledge-base tasks) in `tasks/TASKS.md` — all
of them are already `done`, and their conventions (the seam shape, TSID
identifiers, `IModuleConfig`) are simply used as-is by this new module.
This task's only real ledger dependency is none (it is the first Shop
task); its Depends-on column is left as none in `tasks/TASKS.md`.

# Scope

1. `src/modules/shop/TenantForge.Modules.Shop/TenantForge.Modules.Shop.csproj`:
   `net10.0`, `Nullable`/`ImplicitUsings` enabled, a `ProjectReference` to
   `TenantForge.BuildingBlocks` (for `TsidId`/`IModuleConfig`) and the
   `Npgsql.EntityFrameworkCore.PostgreSQL`/`Microsoft.EntityFrameworkCore.Design`
   package references IAM's own `.csproj` already uses (copy the same
   versions). Add the project to `TenantForge.sln`.
2. `domain/ShopCategory.cs`, `ShopProduct.cs`, `ShopProductVariant.cs`,
   `ShopSizeGuideColumn.cs`, `ShopSizeGuideRow.cs`, `ShopSizeGuideCell.cs`
   — plain domain entities with private setters and named factory methods,
   matching IAM's domain-entity shape (compare
   `src/modules/iam/TenantForge.Modules.Iam/domain/Tenant.cs`). Fields per
   entity exactly as listed in `tasks/slices/026-shop-catalog.md`'s entity
   table. Every `Id`/foreign-key member is a `Tsid`.
3. `infrastructure/ShopDbContext.cs`: `internal sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)`
   with one `DbSet<T>` per entity and one `IEntityTypeConfiguration<T>`
   map class per entity under `infrastructure/` (mirroring
   `AccountMap`/`TenantMap` etc.), configuring the `Tsid` value converter
   the same way IAM's maps already do for every `Tsid` column (grep
   `ValueConverter` or the TSID conversion helper in an existing IAM map
   file and reuse the identical pattern — do not invent a second
   conversion approach).
4. `ShopConfig.cs` implementing `IModuleConfig`: `SectionName => "Shop"`;
   `RegisterServices` registers `ShopDbContext` against
   `Shop:ShopDb` (a new, separate configuration key and, in
   `appsettings.Development.json`/compose environment, a separate
   connection string value — do not reuse `IAM:IamDb`, even if it points
   at the same physical PostgreSQL server; each module owns its own
   configuration key exactly as IAM does); `ValidateConfiguration` fails
   startup with a clear `InvalidOperationException` message when
   `Shop:ShopDb` is missing, mirroring `IAMConfig.ValidateConfiguration`'s
   connection-string check.
5. `ShopModule.cs`: `AddShopModule(this IServiceCollection, IHostEnvironment)`
   (registration only) and `UseShopModuleAsync(this WebApplication)`
   (validate configuration, then `await db.Database.MigrateAsync();` — no
   seed step exists yet, no endpoint mapping call yet since there are no
   endpoints in this task).
6. One EF Core migration (`dotnet ef migrations add InitialShopCatalog
   --project src/modules/shop/TenantForge.Modules.Shop --startup-project
   src/api/TenantForge.Api`) creating exactly the six catalog tables. No
   table for `ShopCart`, `ShopCartItem`, `ShopCoupon`,
   `ShopShippingRate`, `ShopOrder`, `ShopOrderItem` or
   `ShopPaymentAttempt` — those are out of scope per the slice's
   Non-goals.
7. `src/api/TenantForge.Api/Program.cs`: add
   `builder.Services.AddShopModule(builder.Environment);` beside the
   existing `AddIamModule` call, and `await app.UseShopModuleAsync();`
   beside the existing `await app.UseIamModuleAsync();` call. Add the
   `Shop:ShopDb` connection string to `appsettings.Development.json` and
   any Docker Compose/test-host configuration source that already
   supplies `IAM:IamDb`, pointing at a distinct database (or distinct
   schema, matching whatever the existing Postgres compose service already
   supports for a second database) — never the same database name as
   IAM's, so the two modules' migrations can never collide.

# Acceptance

- The solution builds with `TenantForge.Modules.Shop` included and
  referenced from `TenantForge.Api`.
- `AddShopModule`/`UseShopModuleAsync` exist with exactly the same
  two-call shape IAM uses; the API host calls both, in the same
  registration-before-`Build`/activation-after-`Build` order as IAM.
- Starting the API with `Shop:ShopDb` unset fails startup with a clear
  message (fail closed, same as IAM's own missing-connection-string
  check) — verified with an integration test that boots a test host
  without the setting and asserts a startup failure.
- Starting the API with `Shop:ShopDb` set applies the new migration and
  creates exactly the six listed tables, verified by a migration-only
  integration test (compare to how IAM's own equivalent test, if any,
  or a fresh test-host boot proves the schema exists).
- Restarting the API against the same already-migrated database is a
  no-op (idempotent) — the same startup-safety property every existing
  IAM migration already has.
- No HTTP endpoint is added or reachable from this module yet.
- `docs/modules/IAM.md` is unaffected by this task (it touches no
  `src/modules/iam/**` file and no shared BuildingBlocks contract);
  state `IAM.md impact: none — this task only creates the new,
  unrelated TenantForge.Modules.Shop project` in self-review and the PR
  body. `docs/building-blocks/README.md` impact: none — `TsidId` and
  `IModuleConfig` are consumed as-is, with no change to either type or
  its consumer list beyond adding Shop as a second, expected consumer
  (no admission-rule question is raised, since both types already exist
  precisely to support a second module).

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

The existing IAM-focused suite must continue to pass unmodified,
proving zero regression from adding the second module.

Manual:

- Boot the API locally with `Shop:ShopDb` configured; confirm the log
  shows the Shop migration applying (or already applied) and the process
  starts and serves the existing IAM endpoints unaffected.
- Boot the API with `Shop:ShopDb` unset; confirm it fails to start with
  the expected clear error and does not silently continue.
- Inspect the Shop database with `psql`/a database tool and confirm
  exactly the six catalog tables exist, with `Tsid` columns stored as
  `bigint`.

# Lifecycle

Add row `B025` to the Backend queue in `tasks/TASKS.md` with status
`planned`, no dependency (the first Shop task), and Spec link
`tasks/backend/B025-shop-module-persistence-and-skeleton.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/026-shop-catalog.md` is the permanent record and is
never deleted.
