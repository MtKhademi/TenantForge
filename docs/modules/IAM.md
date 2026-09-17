# IAM module handbook

این سند نقطه شروع برای هر پرسش درباره ماژول IAM است؛ ابتدا اینجا را بخوانید، سپس مسیر کد ذکرشده را برای تأیید نهایی بررسی کنید.

**Read this first for IAM questions.** This document describes current, merged
IAM behavior only. It is not a task diary: it contains no `planned`/task-status
language and no historical narrative. When it disagrees with code, migrations
or tests, the code is the source of truth — report the mismatch and verify
before trusting either.

## Table of contents

1. [Purpose and non-goals](#1-purpose-and-non-goals)
2. [Fast facts](#2-fast-facts)
3. [Dependency and composition boundary](#3-dependency-and-composition-boundary)
4. [Source-code map](#4-source-code-map)
5. [Configuration and secrets](#5-configuration-and-secrets)
6. [Identity/TSID contract](#6-identitytsid-contract)
7. [Domain model and invariants](#7-domain-model-and-invariants)
8. [Persistence](#8-persistence)
9. [Authentication and JWT](#9-authentication-and-jwt)
10. [Authorization and tenant isolation](#10-authorization-and-tenant-isolation)
11. [Endpoint catalog](#11-endpoint-catalog)
12. [Pagination and errors](#12-pagination-and-errors)
13. [Invitations, audit, seeding and startup](#13-invitations-audit-seeding-and-startup)
14. [Test map and commands](#14-test-map-and-commands)
15. [Current limitations](#15-current-limitations)
16. [Change-impact checklist](#16-change-impact-checklist)

## 1. Purpose and non-goals

IAM owns:

- accounts (platform administrators and ordinary users) and their credentials;
- platform administration (global user/tenant directories, admin-only reads);
- authentication and JWT issuance/validation;
- tenant discovery and tenant membership;
- tenant roles and the resolved permission model (owner union + assigned
  custom roles);
- tenant invitations (creation and listing only — see [Section 15](#15-current-limitations));
- IAM audit events.

IAM explicitly does **not** own:

- frontend presentation, routing or visual design;
- any non-IAM business module (Shop now exists as a separate module; IAM
  neither references it nor is referenced by it — see
  [Section 3](#3-dependency-and-composition-boundary));
- deployment secret storage/rotation (it only reads configuration values —
  see [Section 5](#5-configuration-and-secrets));
- email delivery or invitation-acceptance workflow;
- refresh-token rotation or session hardening beyond the current single
  short-lived access token, unless a later slice proves otherwise in code.

## 2. Fast facts

| Fact | Value |
| --- | --- |
| Module assembly/path | `src/modules/iam/TenantForge.Modules.Iam/` (`TenantForge.Modules.Iam.csproj`) |
| Public composition seam | `TenantForge.Modules.Iam.IamModule` — `AddIamModule` (before `Build`), `UseIamModuleAsync` (after `Build`) |
| Database/context | PostgreSQL via `TenantForge.Modules.Iam.Infrastructure.IamDbContext` |
| Identifier representations | PostgreSQL `bigint` ↔ .NET `Tsid` ↔ HTTP/JWT canonical 13-char string — see [Section 6](#6-identitytsid-contract) |
| Auth mechanism / subject claim | JWT bearer (HS256); subject (`sub`) is the account's canonical TSID string |
| Permission model | Tenant-scoped owner union + custom-role assignment; catalog of 4 keys — see [Section 10](#10-authorization-and-tenant-isolation) |
| Test project | `tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj` |
| Local SDK/runtime notes | No Linux `dotnet`; use `dotnet.exe` — see `docs/architecture.md#local-development-environment-wsl--windows-net-sdk` |
| Primary handbook update rule | Every IAM-touching backend task updates this file or states `IAM.md impact: none — <specific reason>` — see [Section 16](#16-change-impact-checklist) |

## 3. Dependency and composition boundary

Current dependency direction (post-B025):

```text
TenantForge.Api
    -> TenantForge.Modules.Iam
        -> TenantForge.Modules.Iam.Contract
        -> TenantForge.BuildingBlocks
    -> TenantForge.Modules.Shop
        -> TenantForge.BuildingBlocks
```

Shop is a separate module that never references IAM (or its Contract) — the
two business modules share only `TenantForge.BuildingBlocks`. This is what
the dependency-direction rule exists to prevent.

`TenantForge.Modules.Iam.Contract` holds IAM's HTTP-facing
request/query/response records (`public sealed`, one sub-namespace per kind:
`Requests`, `Queries`, `Responses`). It has **zero** outgoing references —
no `ProjectReference`, `PackageReference` or `FrameworkReference` — and is
referenced by `TenantForge.Modules.Iam` only. It is the only place a future
module may reference to consume an IAM request/response shape.

The API host composes IAM through exactly two calls
(`src/api/TenantForge.Api/Program.cs`); Shop is composed the same way, in the
same registration-before-`Build` / activation-after-`Build` order:

```csharp
builder.Services.AddIamModule(builder.Environment);
builder.Services.AddShopModule(builder.Environment);

var app = builder.Build();

await app.UseIamModuleAsync();
await app.UseShopModuleAsync();
```

- **`AddIamModule`** (registration, before `Build`) — adds every IAM-owned
  service to the container. It performs no I/O and makes no pass/fail
  decision, because configuration is not fully assembled yet (host and
  `WebApplicationFactory` test sources alike). See
  `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs`.
- **`UseIamModuleAsync`** (activation, after `Build`) — runs once, in this
  deterministic order:
  1. validate the fully assembled configuration (fail closed) —
     `IAMConfig.ValidateConfiguration`;
  2. install authentication middleware (`app.UseAuthentication()`);
  3. install authorization middleware (`app.UseAuthorization()`);
  4. apply pending migrations (`db.Database.MigrateAsync()`);
  5. seed the platform administrator idempotently (`PlatformAdminSeeder`);
  6. map every IAM endpoint (`MapIamModule`).

  Configuration validation, migration, seeding and endpoint mapping are
  `private` helpers inside `IamModule`; the host cannot call them separately.
  Database work is genuinely `await`ed — never `.Wait()`/`.Result`/hidden in a
  hosted service.

`IAMConfig` (`src/modules/iam/TenantForge.Modules.Iam/IAMConfig.cs`) implements
`TenantForge.BuildingBlocks.Modules.IModuleConfig`
(`src/building-blocks/TenantForge.BuildingBlocks/Modules/IModuleConfig.cs`):
`SectionName` (`"IAM"`), `RegisterServices`, `ValidateConfiguration`.

Every IAM feature class is `internal`; the host must not reach into feature
details — it only calls the two `IamModule` methods above.

## 4. Source-code map

| Concern | Path | Inspect for |
| --- | --- | --- |
| BuildingBlocks module contract | `src/building-blocks/TenantForge.BuildingBlocks/Modules/IModuleConfig.cs` | The registration/validation contract every module implements |
| BuildingBlocks TSID seam | `src/building-blocks/TenantForge.BuildingBlocks/Identifiers/TsidId.cs` | `NewId`, `Format`, `TryParse`, `TryParseNullable`, `IsDefault` |
| IAM composition seam | `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs` | The two public calls and activation order |
| IAM HTTP contract | `src/modules/iam/TenantForge.Modules.Iam.Contract/` | The `public sealed` request/query/response records (`Requests/`, `Queries/`, `Responses/`) — zero outgoing references, referenced by the IAM module only; the detailed per-type handbook is [`docs/contracts/iam.md`](../contracts/iam.md) |
| IAM configuration | `src/modules/iam/TenantForge.Modules.Iam/IAMConfig.cs` | Config keys, DI registrations, fail-closed validation |
| Authorization policy names | `src/modules/iam/TenantForge.Modules.Iam/AuthorizationPolicyNames.cs` | `PlatformAdmin` claim policy name |
| Domain entities | `src/modules/iam/TenantForge.Modules.Iam/domain/` | `Account`, `Tenant`, `TenantMembership`, `TenantRole`, `TenantMemberRoleAssignment`, `TenantInvitation`, `AuditEvent` and their enums |
| Login/JWT feature | `src/modules/iam/TenantForge.Modules.Iam/features/login/` | `LoginFeature`, `JwtIssuer`, `JwtConstants`, `AccountCredentialChecker`, `AuthOptions`, `JwtBearerSigningKeyOptions`, `PlatformAdminSeeder`, `SeedAdminOptions` |
| Current account / tenant discovery | `src/modules/iam/TenantForge.Modules.Iam/features/account/` | `CurrentAccountFeature`, `TenantDiscoveryFeature` |
| Dashboard summary | `src/modules/iam/TenantForge.Modules.Iam/features/dashboard/` | `DashboardSummaryFeature` |
| Platform users | `src/modules/iam/TenantForge.Modules.Iam/features/users/` | `UsersFeature` (the `CreateUserRequest`/`UsersListResponse`/`UserResponse` records now live in `TenantForge.Modules.Iam.Contract`; `UserResponse.FromAccount` stays here as a module-owned mapper because it references the internal `Account` entity) |
| Platform tenants | `src/modules/iam/TenantForge.Modules.Iam/features/tenants/` | `TenantsFeature` (the `CreateTenantRequest`/`TenantListResponse`/`TenantSummaryResponse` records now live in `TenantForge.Modules.Iam.Contract`) |
| Tenant members | `src/modules/iam/TenantForge.Modules.Iam/features/tenantmembers/` | `TenantMembersFeature` (the `TenantMembersResponse`/`TenantContextResponse`/`TenantMemberResponse` records now live in `TenantForge.Modules.Iam.Contract`) |
 | Roles/permissions | `src/modules/iam/TenantForge.Modules.Iam/features/roles/` | `RolesFeature` (catalog data, CRUD, assignment, resolved permissions, `AuthorizeTenantAccessAsync`; the handler-only records `TenantAccess`/`ActorSnapshot`/`AssignmentValidation`/`RemovedAssignment` stay here). The nine contract records (`CreateRoleRequest`/`UpdateRoleRequest`/`PermissionCatalogResponse`/`PermissionGroupResponse`/`PermissionResponse`/`TenantRolesResponse`/`PagedTenantRolesResponse`/`TenantRoleResponse`/`ResolvedPermissionsResponse`) now live in `TenantForge.Modules.Iam.Contract` |
 | Invitations | `src/modules/iam/TenantForge.Modules.Iam/features/invitations/` | `InvitationsFeature` (the `CreateInvitationRequest`/`InvitationListResponse`/`InvitationResponse` records now live in `TenantForge.Modules.Iam.Contract`) |
 | Audit | `src/modules/iam/TenantForge.Modules.Iam/features/audit/` | `AuditFeature` (the `AuditListResponse`/`AuditEventResponse` records now live in `TenantForge.Modules.Iam.Contract`) |
| Pagination | `src/modules/iam/TenantForge.Modules.Iam/features/pagination/` | `PaginationSupport` (binding/execution); the `PaginationQuery`/`PaginationMetadata` records now live in `TenantForge.Modules.Iam.Contract` |
| Persistence context/maps | `src/modules/iam/TenantForge.Modules.Iam/infrastructure/` | `IamDbContext`, `*Map.cs`, `TsidValueConverter` |
| Migrations | `src/modules/iam/TenantForge.Modules.Iam/infrastructure/Migrations/` | Chronological schema history, including the irreversible `20260914120008_TsidIdentifiers` |
| Integration test fixtures | `tests/integration/TenantForge.Api.IntegrationTests/ApiFactory.cs`, `IamDbFixture.cs` | `WebApplicationFactory` setup, `IamSeedMode`, per-suite Postgres fixtures |
| Focused test classes | `tests/integration/TenantForge.Api.IntegrationTests/*.cs` | See [Section 14](#14-test-map-and-commands) |
| Architecture decisions | `docs/architecture.md` | System-wide shape, BuildingBlocks admission rule, module seam rule |
| Learning history | `docs/learning/` | Why-narratives for delivered slices (not current-state truth) |
| Source slices | `tasks/slices/` | Frozen historical contracts (e.g. `019-tsid-identifiers.md`, `020-building-blocks.md`) |

## 5. Configuration and secrets

All keys are read by `IAMConfig` (`src/modules/iam/TenantForge.Modules.Iam/IAMConfig.cs`),
bound from `AuthOptions`/`SeedAdminOptions`
(`src/modules/iam/TenantForge.Modules.Iam/features/login/`).

| Key | Owner | Required? | Environment behavior | Secret? | Failure mode |
| --- | --- | --- | --- | --- | --- |
| `IAM:IamDb` | IAM | Always required | Same rule in every environment | Yes — connection string may carry credentials | Startup throws `InvalidOperationException` (fail closed) if blank |
| `IAM:Auth:SigningKey` | IAM | Optional at startup, required to issue tokens | If absent, `JwtIssuer.CanIssue` is false and `JwtBearerSigningKeyOptions` assigns a random never-matching key: login answers 401 instead of a 500, and every protected endpoint stays 401 | Yes | No startup crash; login/auth fail closed with 401 |
| `IAM:SeedAdmin:Email` | IAM | Required together with Password/DisplayName, or all three absent | Same rule in every environment | No (an identity, not a secret) — logged on successful seed | Startup throws if section is partially filled |
| `IAM:SeedAdmin:Password` | IAM | Required together with Email/DisplayName, or all three absent | Outside `Development`, must be ≥ 12 characters (checked by length only) | Yes — bootstrap password, never logged, only its length is validated | Startup throws if section incomplete or (non-Development) too short; message never echoes the value |
| `IAM:SeedAdmin:DisplayName` | IAM | Required together with Email/Password, or all three absent | Same rule in every environment | No | Startup throws if section is partially filled |
| `AllowedOrigins` | API host (not IAM) | Required only in `Development` CORS setup | Read by `Program.cs`, not by IAM | No | Linked here only because it lives beside IAM config in `appsettings.Development.json`; IAM does not own or validate it |

Late configuration visibility: `AuthOptions`/`SeedAdminOptions` are bound
lazily (`services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>()...)`),
so configuration added after `CreateBuilder` — including
`WebApplicationFactory` test configuration — is honored. Never read these
values eagerly at registration time.

Production fail-closed rules: `ValidateConfiguration` runs once, inside
`UseIamModuleAsync`, before authentication/authorization middleware. A missing
`IAM:IamDb`, a partially-filled `SeedAdmin` section, or a non-Development seed
password shorter than 12 characters all throw and stop startup. No configured
value or secret is ever included in an exception message or a log entry.

## 6. Identity/TSID contract

| Boundary | Representation |
| --- | --- |
| PostgreSQL identity/FK | `bigint` |
| .NET domain/EF | `Tsid` (from the `TSID.Creator.NET` package) |
| HTTP route/request/response, `Location`, JWT `sub` | canonical 13-character Crockford-base32 string |

The one public seam is `TenantForge.BuildingBlocks.Identifiers.TsidId`
(`src/building-blocks/TenantForge.BuildingBlocks/Identifiers/TsidId.cs`):
`NewId()`, `Format(Tsid)`, `TryParse(string?, out Tsid)`,
`TryParseNullable(string?)`, `IsDefault(Tsid)`. IAM code never calls
`TsidCreator`/`Tsid.From` directly outside this seam and
`TsidValueConverter`.

The EF bridge is IAM-owned:
`src/modules/iam/TenantForge.Modules.Iam/infrastructure/TsidValueConverter.cs`
— one shared `ValueConverter<Tsid, long>` instance, applied explicitly on
every `Tsid` property in every `*Map.cs` (EF strips convention-applied
converters from key properties, so explicit per-property application is
required).

`TsidId.TryParse` validation rules (all without throwing):

- null/blank → rejected;
- length must be exactly 13 characters → rejects GUID-shaped (36 or 32 chars)
  and most decimal input in one check;
- an all-digit 13-character string → rejected explicitly (still valid
  Crockford text at the byte level, but must not leak the bigint backing
  value);
- any character `> 127` (non-ASCII) → rejected before reaching the package;
- the all-zero "unset" sentinel → rejected;
- lower-case Crockford input is accepted and always re-emitted in canonical
  upper-case form by `Format`.

Multiple writer processes must set a unique `TSIDCREATOR_NODE` value (a TSID
generator operational requirement); this repository does not currently run
multiple writers, so no environment sets it yet.

Invitation tokens (`TenantInvitation.TokenHash`) are **not** entity
identifiers — they are opaque, hashed secrets (see
[Section 13](#13-invitations-audit-seeding-and-startup)) and must never be
treated as a TSID.

## 7. Domain model and invariants

All entities live in `src/modules/iam/TenantForge.Modules.Iam/domain/` and are
`internal` (not reachable outside the module).

| Entity | Identity | Key relationships | Invariants |
| --- | --- | --- | --- |
| `Account.cs` | `Tsid Id` | none (root aggregate) | Email is trimmed and normalized (`NormalizeEmail` = trim + upper-invariant) with a unique index; `IsPlatformAdmin` set only at creation (`CreatePlatformAdmin` vs `CreateUser`); `Status` (`Active`/`Disabled`) gates login and tenant-context checks |
| `Tenant.cs` | `Tsid Id` | referenced by membership/role/invitation/audit rows | `Slug`/`NormalizedSlug` derived via `NormalizeSlug` (lower-case, `[a-z0-9-]`, no leading/trailing/duplicate dashes) with a unique index; `Status` (`Active`/`Suspended`) excludes suspended tenants from discovery and access |
| `TenantMembership.cs` | `Tsid Id` | `TenantId` → Tenant (cascade delete), `AccountId` → Account (restrict delete) | One row per `(TenantId, AccountId)` (unique index); `Role` is `Owner` or `Member` — `Owner` implicitly holds every permission key (see [Section 10](#10-authorization-and-tenant-isolation)) |
| `TenantRole.cs` | `Tsid Id` | `TenantId` → Tenant (cascade delete) | `Name`/`NormalizedName` unique per tenant; `Kind` is `"custom"` for tenant-created roles (only custom roles can be updated — see `RolesFeature`); `PermissionKeys` stored de-duplicated and ordinally sorted |
| `TenantMemberRoleAssignment.cs` | Composite `(TenantMembershipId, TenantRoleId)` — no surrogate `Id` | → `TenantMembership`, → `TenantRole` (both cascade delete) | Assignment existence alone grants the role's permission keys to the member; removal is blocked when it would leave zero effective administrators for `IAM.Roles.Manage` (see [Section 10](#10-authorization-and-tenant-isolation)) |
| `TenantInvitation.cs` | `Tsid Id` | `TenantId` → Tenant (cascade delete) | `Email`/`NormalizedEmail` lower-cased; `TokenHash` is `SHA256(rawToken)` — the raw token is never persisted; `Status` starts `"Pending"`; `ExpiresAtUtc` = creation + 7 days |
| `AuditEvent.cs` | `Tsid Id` | `TenantId` → Tenant (cascade delete), `ActorAccountId` → Account (restrict delete) | Immutable — no mutation method exists after `Create`; `Actor`/`ActorEmail` are a point-in-time snapshot of the acting account, not a live join |

Cross-check before editing invariants: `AccountStatus.cs`, `TenantStatus.cs`,
`TenantMembershipRole.cs` hold the enum values referenced above.

## 8. Persistence

`IamDbContext`
(`src/modules/iam/TenantForge.Modules.Iam/infrastructure/IamDbContext.cs`) is
the single PostgreSQL-backed context (Npgsql provider, configured in
`IAMConfig.RegisterServices`). It exposes seven `DbSet`s, one per table below,
each configured by its own `*Map.cs` via `IEntityTypeConfiguration<T>`.

| Table | Primary key | Foreign keys | Delete behavior | Notable indexes |
| --- | --- | --- | --- | --- |
| `iam_accounts` | `id` | — | — | unique `normalized_email` |
| `iam_tenants` | `id` | — | — | unique `normalized_slug` |
| `iam_tenant_memberships` | `id` | `tenant_id` → tenants, `account_id` → accounts | tenant: cascade; account: restrict | unique `(tenant_id, account_id)`; `account_id` |
| `iam_tenant_roles` | `id` | `tenant_id` → tenants | cascade | unique `(tenant_id, normalized_name)` |
| `iam_tenant_member_role_assignments` | composite `(tenant_membership_id, tenant_role_id)` | both → their tables | both cascade | `tenant_role_id` |
| `iam_tenant_invitations` | `id` | `tenant_id` → tenants | cascade | `(tenant_id, normalized_email, status)` — non-unique, application code enforces the "no duplicate active" rule |
| `iam_audit_events` | `id` | `tenant_id` → tenants (cascade), `actor_account_id` → accounts (restrict) | mixed, see left | `(tenant_id, created_at_utc)`, `(tenant_id, action)` |

Every `Tsid`-typed column uses `TsidValueConverter.Shared` with
`.ValueGeneratedNever()` on primary keys (see [Section 6](#6-identitytsid-contract)).
`iam_tenant_roles.permission_keys` is a native PostgreSQL `text[]` column.

Migration order (`infrastructure/Migrations/`, chronological):
`InitialIamPersistence` → `AddTenantMemberships` → `AddTenantRoles` →
`AddInvitationsAndAudit` → `NormalizeTenantPermissionKeys` →
`TsidIdentifiers`. Activation always calls `db.Database.MigrateAsync()`
**before** seeding (`IamModule.SeedIamModuleAsync`) — migration-before-seed is
not optional.

`TsidIdentifiers` (`20260914120008_TsidIdentifiers.cs`) is the irreversible
UUID→bigint migration: it builds a temporary `old_id → new_id` map, backfills
every `*_tsid` shadow column, verifies zero missing/duplicate mappings, then
drops the old `uuid` columns and renames the shadow columns into place. Its
`Down` throws `NotSupportedException` — restoring from a backup is the only
rollback path. Do not copy its SQL elsewhere; link this file instead.

No IAM table is read or written directly by code outside this module — every
access goes through `IamDbContext`.

## 9. Authentication and JWT

Flow: `POST /api/auth/login`
(`src/modules/iam/TenantForge.Modules.Iam/features/login/LoginFeature.cs`) →
`ICredentialChecker.AuthenticateAsync` (implemented by
`AccountCredentialChecker.cs`, looks up by normalized email, then
`IPasswordHasher<Account>.VerifyHashedPassword`) → on success,
`JwtIssuer.Issue` (`JwtIssuer.cs`) mints an HS256 token.

A missing account, a disabled account (`Status != Active`) and a wrong
password are all deliberately indistinguishable — the checker returns `null`
for all three, and the endpoint answers one generic 401.

JWT claims (from `JwtIssuer.Issue`): `sub` (canonical TSID string), `email`,
`name` (display name), `isPlatformAdmin` (`"true"`/`"false"` string).
Issuer/audience are both the constant `"TenantForge"`
(`JwtConstants.Issuer`/`Audience`); lifetime is 30 minutes
(`JwtConstants.TokenLifetime`); `ClockSkew` is 30 seconds
(`IAMConfig.RegisterServices`).

401 vs 403: unauthenticated (missing/invalid/expired/wrong-signature token)
is always 401 (the JwtBearer challenge). An authenticated principal whose
`sub` no longer names a usable account (legacy GUID subject, disabled
account, account deleted) is denied at the endpoint with the endpoint's
fail-closed answer — typically `Results.Forbid()` (403) for a tenant-scoped
read, since the caller *is* authenticated but not authorized to act as that
subject. `CurrentAccountFeature` denies an invalid/non-TSID subject with 401
(no valid caller identity was ever established), since it is the pure
identity-echo endpoint.

Non-Development signing-key rule: if `IAM:Auth:SigningKey` is blank in any
environment, `JwtBearerSigningKeyOptions` assigns a process-local random key
that can never validate any token — every protected endpoint stays 401,
without a 500.

Never log passwords, tokens or password hashes. `LoginFeature` logs only the
attempted email and outcome; `PlatformAdminSeeder`/`IamModule` log only the
seeded email, never the password or its hash.

## 10. Authorization and tenant isolation

**Platform-admin policy** — `AuthorizationPolicyNames.PlatformAdmin`
(`IAMConfig.RegisterServices`): `RequireClaim("isPlatformAdmin", "true")`.
Unauthenticated → 401; authenticated without the claim → 403. Used by the
platform-only endpoints (dashboard summary, platform users, platform
tenants).

**Authenticated account lookup** — every tenant-scoped handler re-verifies
the JWT `sub` against the persisted `Account` (via
`TsidId.TryParseNullable`/`TryParse` then a DB check) instead of trusting the
token alone, because a stateless token can outlive row state (e.g. account
disabled after issuance). A legacy non-TSID subject or a disabled/missing
account is denied — never crashes.

**Active tenant/account/membership checks** —
`RolesFeature.AuthorizeTenantAccessAsync`
(`src/modules/iam/TenantForge.Modules.Iam/features/roles/RolesFeature.cs`) is
the shared tenant-access gate used by roles, invitations and audit endpoints:
it requires an active `TenantMembership` row joined to an active `Account`
and an active `Tenant`; any missing link returns `Results.Forbid()`.
`TenantMembersFeature` implements the same shape inline for its own route.

**Owner semantics** — `TenantMembershipRole.Owner` implicitly holds every
known permission key (`RolesFeature.ResolvePermissionsAsync` unions the full
catalog for an owner membership, in addition to any explicitly assigned
custom-role keys).

**Tenant role assignments and resolved permission union** — a member's
effective permissions are: (owner ? full catalog : ∅) ∪ (permission keys of
every `TenantRole` assigned to their membership via
`TenantMemberRoleAssignment`). `GET /api/tenants/{tenantId}/me/permissions`
exposes this resolved set.

**Last-effective-administrator protection** —
`RolesFeature.HasAnyEffectiveRoleAdministratorAsync` recomputes, across every
active member, whether at least one account would still hold
`IAM.Roles.Manage` after a hypothetical role-permission update or
assignment removal. If the answer is "no", the mutation is rejected with
`409 Conflict` — a tenant can never be left with zero accounts able to
manage roles.

**Cross-tenant default deny** — a tenant id that does not match an active
membership row for the caller is treated identically to "tenant does not
exist" (`Forbid`/`403`), whether the caller is an ordinary user or a platform
administrator. `TenantIsolationIntegrationTests.PlatformAdminWithoutMembership_IsStillDenied`
proves platform-admin status alone never substitutes for tenant membership.

**UI hiding is not authorization** — every rule above is enforced
server-side, independent of what the frontend displays or disables.

Permission catalog (from `RolesFeature.CatalogGroups`, exposed by
`GET /api/permissions/catalog`):

| Key | Consuming endpoints |
| --- | --- |
| `IAM.Roles.Manage` | `POST/PUT /api/tenants/{tenantId}/roles*`, `PUT`/`DELETE /api/tenants/{tenantId}/members/{memberId}/roles/{roleId}` |
| `IAM.Invitations.View` | `GET /api/tenants/{tenantId}/invitations` |
| `IAM.Invitations.Create` | `POST /api/tenants/{tenantId}/invitations` |
| `IAM.Audit.View` | `GET /api/tenants/{tenantId}/audit` |

Membership-only (no specific permission key required) reads: `GET
/api/tenants/{tenantId}/members`, `GET /api/tenants/{tenantId}/roles`, `GET
/api/tenants/{tenantId}/me/permissions` — verified directly in
`RolesFeature`/`TenantMembersFeature` (no `permissionKey` argument passed to
`AuthorizeTenantAccessAsync`, or the equivalent inline check).

## 11. Endpoint catalog

19 routes, one row per literal `Map*` call in
`src/modules/iam/TenantForge.Modules.Iam/features/**`. **Expected row count:
19.** When a route is added or removed in a feature file, add or remove
exactly one row here in the same change (see the documentation-validation
check in [Section 14](#14-test-map-and-commands), which counts literal
`MapGet/MapPost/MapPut/MapDelete/MapPatch` calls against this table).

| Method & path | Purpose | Auth / policy / permission | Input | Success | Key failures | Feature file |
| --- | --- | --- | --- | --- | --- | --- |
| `POST /api/auth/login` | Authenticate with email/password, issue JWT | None (public) | JSON `{email, password}` | `200` `{accessToken, expiresAtUtc, user}` | `400` missing fields; `401` invalid credentials or no signing key configured | `login/LoginFeature.cs` |
| `GET /api/auth/me` | Echo the caller's identity from validated claims | Authenticated | — | `200` `{id, email, displayName, isPlatformAdmin}` | `401` unauthenticated or non-TSID/invalid subject | `account/CurrentAccountFeature.cs` |
| `GET /api/auth/me/tenants` | List the caller's active tenant memberships | Authenticated (+ active account) | Pagination query | `200` `{tenants[], pagination}` | `401` unauthenticated; `403` disabled/missing account | `account/TenantDiscoveryFeature.cs` |
| `GET /api/platform/dashboard-summary` | Platform health/summary counts | `PlatformAdmin` policy | — | `200` `{environment, apiStatus, platformAdminCount, generatedAtUtc}` | `401`; `403` non-admin | `dashboard/DashboardSummaryFeature.cs` |
| `GET /api/platform/users` | List all accounts (global directory) | `PlatformAdmin` policy | Pagination query | `200` `{users[], pagination}` | `401`; `403` non-admin | `users/UsersFeature.cs` |
| `POST /api/platform/users` | Create an account | `PlatformAdmin` policy | JSON `{email, displayName, password}` | `201` created user | `400` validation; `401`; `403`; `409` duplicate email | `users/UsersFeature.cs` |
| `GET /api/platform/tenants` | List all tenants with member counts | `PlatformAdmin` policy | Pagination query | `200` `{tenants[], pagination}` | `401`; `403` non-admin | `tenants/TenantsFeature.cs` |
| `POST /api/platform/tenants` | Create a tenant with its first owner | `PlatformAdmin` policy | JSON `{name, slug, ownerUserId}` | `201` created tenant | `400` validation; `401`; `403`; `409` duplicate slug | `tenants/TenantsFeature.cs` |
| `GET /api/tenants/{tenantId}/members` | List members of one tenant | Authenticated + active membership | Route id, pagination query | `200` `{tenant, members[], pagination}` | `401`; `403` non-member/invalid tenant id | `tenantmembers/TenantMembersFeature.cs` |
| `GET /api/permissions/catalog` | Static permission catalog for UI rendering | Authenticated | — | `200` `{groups[]}` | `401` | `roles/RolesFeature.cs` |
| `GET /api/tenants/{tenantId}/roles` | List roles of one tenant with member assignments | Authenticated + active membership | Route id, pagination query | `200` `{roles[], pagination}` | `401`; `403` non-member | `roles/RolesFeature.cs` |
| `POST /api/tenants/{tenantId}/roles` | Create a custom tenant role | `IAM.Roles.Manage` | Route id, JSON `{name, permissionKeys}` | `201` created role | `400` validation; `401`; `403`; `409` duplicate name | `roles/RolesFeature.cs` |
| `PUT /api/tenants/{tenantId}/roles/{roleId}` | Replace a custom role's permissions | `IAM.Roles.Manage` | Route ids, JSON `{permissionKeys}` | `200` updated role | `400` validation; `401`; `403`; `404` unknown role; `409` non-custom role or would remove last administrator | `roles/RolesFeature.cs` |
| `PUT /api/tenants/{tenantId}/members/{memberId}/roles/{roleId}` | Assign a role to a member | `IAM.Roles.Manage` | Route ids only | `200` `{roles[]}` (current tenant role list) | `401`; `403`; `404` unknown member/role | `roles/RolesFeature.cs` |
| `DELETE /api/tenants/{tenantId}/members/{memberId}/roles/{roleId}` | Remove a role assignment | `IAM.Roles.Manage` | Route ids only | `200` `{roles[]}` (idempotent if already absent) | `401`; `403`; `404` unknown member/role; `409` would remove last administrator | `roles/RolesFeature.cs` |
| `GET /api/tenants/{tenantId}/me/permissions` | Resolve the caller's own effective permissions | Authenticated + active membership | Route id | `200` `{permissions[]}` | `401`; `403` non-member | `roles/RolesFeature.cs` |
| `GET /api/tenants/{tenantId}/invitations` | List active pending invitations | `IAM.Invitations.View` | Route id, pagination query | `200` `{invitations[], pagination}` | `401`; `403` missing permission | `invitations/InvitationsFeature.cs` |
| `POST /api/tenants/{tenantId}/invitations` | Create a pending invitation | `IAM.Invitations.Create` | Route id, JSON `{email, role}` | `201` created invitation | `400` validation/unknown role; `401`; `403`; `409` duplicate active invitation for the same email | `invitations/InvitationsFeature.cs` |
| `GET /api/tenants/{tenantId}/audit` | List tenant audit events, optionally filtered | `IAM.Audit.View` | Route id, `action`/`fromUtc` query, pagination query | `200` `{events[], pagination}` | `400` unknown action/bad timestamp; `401`; `403` missing permission | `audit/AuditFeature.cs` |

## 12. Pagination and errors

Shared query parameters (`PaginationSupport.TryBind`,
`src/modules/iam/TenantForge.Modules.Iam/features/pagination/PaginationSupport.cs`):
`pageNumber` (default `1`, min `1`, no upper bound beyond `int` overflow
protection) and `pageSize` (default `50`, max `100`). Response metadata
(`PaginationMetadata`): `pageNumber`, `pageSize`, `totalCount`, `totalPages`,
`hasPreviousPage`, `hasNextPage`. Ordering is always deterministic — every
paginated query ends with a stable tiebreaker (`.ThenBy(x => x.Id)` or
equivalent) so page boundaries never shuffle rows between requests. A page
requested beyond the last page returns an empty `items` array with accurate
metadata, not an error. An out-of-range `pageNumber`/`pageSize` returns
`400` via `Results.ValidationProblem`.

Paginated endpoints (7): `GET /api/auth/me/tenants`, `GET /api/platform/users`,
`GET /api/platform/tenants`, `GET /api/tenants/{tenantId}/members`,
`GET /api/tenants/{tenantId}/roles`, `GET /api/tenants/{tenantId}/invitations`,
`GET /api/tenants/{tenantId}/audit`.

Deliberately unpaged: `GET /api/permissions/catalog` (fixed static catalog),
`GET /api/tenants/{tenantId}/me/permissions` (small resolved-set aggregate),
`GET /api/platform/dashboard-summary` (single aggregate object), and every
mutation response (`POST`/`PUT`/`DELETE`) that returns a single created
row or the current small role list.

| Status | Meaning in IAM | Example |
| --- | --- | --- |
| `400` | Request validation failure (`Results.ValidationProblem`) | Missing login fields; invalid tenant slug; unknown permission key; bad pagination value |
| `401` | No valid authenticated principal | Missing/expired/malformed/wrong-signature JWT; invalid credentials; non-TSID subject on `/api/auth/me` |
| `403` | Authenticated but not authorized for this resource | Non-admin on a `PlatformAdmin` route; non-member on a tenant route; missing permission key |
| `404` | Referenced sub-resource does not exist within an already-authorized scope | Unknown `roleId`/`memberId` in role assignment |
| `409` | State conflict | Duplicate email/slug/role name; duplicate active invitation; would-remove-last-administrator |

Non-disclosure rule: failure responses never reveal whether an email/account
exists, whether a tenant exists (for a non-member), or any secret value —
`401`/`403` responses are content-identical across their differing causes
wherever the endpoint tables above list more than one cause per status.

## 13. Invitations, audit, seeding and startup

**Invitation creation** (`InvitationsFeature.MapInvitationsFeature`, `POST`):
requires `IAM.Invitations.Create`; validates email format and a known role
(`"Owner"`, `"Viewer"`, or an existing custom `TenantRole` name in that
tenant); takes a Postgres advisory transaction lock keyed on
`tenantId:normalizedEmail` before checking for a duplicate **active**
(`Status == "Pending" && ExpiresAtUtc > now`) invitation, so a concurrent
duplicate request reliably produces exactly one `201` and one `409`, never
two rows. The raw invitation token is generated, immediately hashed
(`TenantInvitation.HashToken`, SHA-256) and only the hash is persisted — the
raw token is not returned in the response and is not currently delivered
anywhere (see [Section 15](#15-current-limitations)). Every creation also
writes an `AuditEvent` (`"Invitation.Created"`) in the same transaction.

**Invitation listing** (`GET`): requires `IAM.Invitations.View`; returns only
currently pending, unexpired invitations, newest first.

**Audit ownership**: `AuditFeature` (`GET /api/tenants/{tenantId}/audit`)
requires `IAM.Audit.View`; supports optional `action` (must be one of the
five known values written by `RolesFeature`/`InvitationsFeature` —
`Role.Created`, `Role.Updated`, `Role.Assigned`, `Role.Unassigned`,
`Invitation.Created`) and `fromUtc` filters; always ordered newest-first.

**Seeding**: `PlatformAdminSeeder.SeedAsync`
(`features/login/PlatformAdminSeeder.cs`) is all-or-none — `SeedAdminOptions`
must have `Email`, `Password` and `DisplayName` all present or all absent
(enforced at startup by `ValidateConfiguration`, [Section 5](#5-configuration-and-secrets)).
When configured and no account with that normalized email exists, it hashes
the password (`IPasswordHasher<Account>`, never touching the plaintext after
hashing) and creates exactly one platform-administrator account inside a
transaction. If a concurrent process already inserted the same normalized
email, the unique index makes the second insert fail; that failure is caught
and treated as "already seeded," never as an error. No password or hash is
ever logged — only the seeded email and a created/already-present outcome.

**Startup ordering**: on every process start, `UseIamModuleAsync` first
migrates (`db.Database.MigrateAsync()`), then seeds — never the reverse — so
`iam_accounts` always exists before the seeder queries it. A restart against
the same already-migrated, already-seeded database is a no-op for both steps
(migrations are idempotent by EF Core's applied-migrations history table;
seeding is idempotent by the unique-email check above).

## 14. Test map and commands

| Test class | Protects |
| --- | --- |
| `IamModuleCompositionTests.cs` | The two-method public seam shape; full migrate→seed→login activation order; restart idempotency |
| `ProductionFailClosedTests.cs` | Fail-closed startup validation and login behavior outside Development |
| `LoginIntegrationTests.cs` | Login contract, JWT claims, generic invalid-credential response, no credential logging |
| `CurrentAccountIntegrationTests.cs` | `/api/auth/me` contract and every 401 cause (missing/expired/malformed/wrong-key/wrong-issuer/legacy-subject token) |
| `DashboardSummaryIntegrationTests.cs` | Platform-admin-only dashboard contract, 401/403 split |
| `IamPersistenceIntegrationTests.cs` | Raw schema/constraints (unique email, required columns, no plaintext password field) |
| `PermissionKeyMigrationTests.cs` | The historical permission-key normalization migration |
| `TsidIdTests.cs` | `TsidId` generation/format/parse rules from [Section 6](#6-identitytsid-contract) |
| `PlatformAdminSeederTests.cs` | Seeder idempotency, concurrent-seed race safety, disabled-account login denial |
| `UserManagementIntegrationTests.cs` | Platform user list/create, admin-only enforcement, tenant-scoped callers denied |
| `TenantMembershipIntegrationTests.cs` | Tenant creation with first owner, duplicate-slug/invalid-owner handling |
| `TenantDiscoveryIntegrationTests.cs` | `/api/auth/me/tenants` scoping (only own active memberships, not platform-admin-wide) |
| `TenantIsolationIntegrationTests.cs` | Cross-tenant denial, including platform-admin-without-membership |
| `RolePermissionIntegrationTests.cs` | Catalog contract, role CRUD/assignment, resolved permissions, last-administrator protection |
| `InvitationAuditIntegrationTests.cs` | Invitation creation/duplicate/concurrency/expiry, custom-role acceptance, audit query scoping/ordering |
| `PaginationIntegrationTests.cs` | Pagination metadata, security-safe denial pages, stable ordering across pages |
| `BuildingBlocksArchitectureTests.cs` | The BuildingBlocks admission rule and dependency-direction enforcement |
| `IamContractArchitectureTests.cs` | The IAM Contract project boundary: zero outgoing `ProjectReference`s, the compiled assembly-reference denylist (no `TenantForge.Modules.*`/`TenantForge.Api`/ASP.NET/EF Core/Npgsql), exactly one incoming reference from `TenantForge.Modules.Iam`, and the exact 32-type exported-surface roster |

Commands:

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

See `docs/architecture.md#local-development-environment-wsl--windows-net-sdk`
for the WSL/Windows host-binding notes (no Linux `dotnet`, `dotnet.exe run`
forces Development, `127.0.0.1` unreachable from WSL) instead of repeating
them here.

## 15. Current limitations

Verified against current code (not aspirational):

- **Invitation email delivery** — not implemented. Creating an invitation
  only persists a row and a hashed token; no email is sent.
- **Invitation acceptance/registration** — not implemented. There is no
  endpoint that consumes an invitation token to create an account or
  membership; invitations remain "Pending" until they expire.
- **Refresh-token/session hardening** — not implemented. `JwtIssuer` mints a
  single 30-minute access token per login; there is no refresh token,
  rotation or revocation list.
- **Multi-module limitations** — a second business module (Shop) now exists
  beside IAM. The dependency-direction rule
  ([Section 3](#3-dependency-and-composition-boundary)) is enforced by
  `BuildingBlocksArchitectureTests` and is now proven live against two
  modules: Shop references `TenantForge.BuildingBlocks` only, never IAM or
  its Contract. Shop still has no endpoints of its own (B025 shipped
  persistence only), so it consumes no IAM request/response shape yet.

## 16. Change-impact checklist

| Change | Mandatory IAM.md sections |
| --- | --- |
| route/request/response/status | Endpoint catalog; pagination/errors if relevant |
| config/secret/startup | Configuration; composition/startup |
| entity/invariant | Domain; persistence when mapped |
| table/map/migration | Persistence; identity if relevant |
| JWT/claims/auth policy | Authentication; authorization |
| permission/membership/tenant rule | Authorization; affected endpoint rows |
| BuildingBlocks ID/module contract | Dependency/composition; identity |
| test/verification path | Test map |
| implemented limitation | Current limitations |

Self-review declaration formats (use exactly one, in self-review and the PR
body, for every backend task that touches `src/modules/iam/**` or an IAM
contract in `TenantForge.BuildingBlocks`/the API host):

```text
IAM.md impact: updated — <sections>
IAM.md impact: none — <specific reason>
```
