# B019 — Create a living IAM knowledge base and update gate

**Status:** planned

**Owner:** backend/docs-workflow

**Source:** S21

**Depends on:** B018

**Source slice:** [S21](../slices/021-iam-knowledge-base.md)

## Goal

Create `docs/modules/IAM.md` as the one current, searchable starting point for
questions about TenantForge IAM, then wire the backend workflow so future IAM
tasks automatically read it and either update it or explicitly prove that their
change has no documentation impact.

This task is documentation and agent-workflow only. It must not change
production C#, migrations, database schema, HTTP contracts or frontend code.

The learning goal is the difference between:

- executable truth: code, migrations and tests;
- current operational knowledge: `docs/modules/IAM.md`;
- historical reasoning: source slices and `docs/learning/**`.

## Dependency

Start only after B018 is delivered. The handbook must document the final
BuildingBlocks locations:

- `TenantForge.BuildingBlocks.Modules.IModuleConfig`;
- `TenantForge.BuildingBlocks.Identifiers.TsidId`;
- dependency direction API → IAM → BuildingBlocks.

Do not document the pre-B018 `IamId` or IAM-owned `IModuleConfig` paths.

## Deliverables

Create/update exactly these documentation/workflow areas:

- create `docs/modules/IAM.md`;
- update root `README.md` with one prominent “IAM module handbook” link;
- update `docs/architecture.md` to link the handbook, without duplicating it;
- update `AGENTS.md` with the mandatory IAM knowledge/update policy;
- update `.opencode/commands/backend-task.md`;
- update `.opencode/commands/review-slice.md`;
- update `.opencode/agents/backend-mentor.md`;
- update `.opencode/skills/vertical-slice-delivery/SKILL.md`;
- add the B019 learning note required by project rules;
- update only B019 task lifecycle files during final delivery.

Do not create another skill unless the existing delivery skill cannot enforce
the policy. The normal backend command, mentor and delivery skill are already
the entry points and must not require the user to remember a new command.

## IAM.md design

Write in concise technical English so code symbols remain searchable and agents
can quote exact terms. A short Persian sentence may explain the document's
purpose, but do not translate route names, claims, permission keys, class names
or configuration keys.

Start with:

- a one-paragraph purpose;
- “Read this first for IAM questions”;
- current-code disclaimer;
- a compact table of contents using stable explicit headings.

Use tables for inventories and bullets for invariants. Avoid a chronological
story, task IDs in headings, release notes, huge code copies and generic ASP.NET
documentation.

### Required section 1 — Purpose and non-goals

State IAM ownership: accounts, platform administration, authentication/JWT,
tenant discovery/membership, tenant roles/permissions, invitations and IAM
audit.

State non-ownership: frontend presentation, non-IAM business modules,
deployment secret management, email delivery/acceptance workflow and future
refresh-token/session hardening unless current code proves otherwise.

### Required section 2 — Fast facts

Include a compact lookup table for:

- module assembly/path;
- public composition seam;
- database/context;
- identifier representations;
- auth mechanism and subject claim;
- permission model;
- test project;
- local SDK/runtime notes;
- primary handbook update rule.

### Required section 3 — Dependency and composition boundary

Document the post-B018 graph:

```text
TenantForge.Api
    -> TenantForge.Modules.Iam
        -> TenantForge.BuildingBlocks
```

Document only the two host calls:

```csharp
builder.Services.AddIamModule(builder.Environment);
var app = builder.Build();
await app.UseIamModuleAsync();
```

Explain registration versus activation and the exact activation order:
configuration validation → authentication → authorization → migrations →
idempotent seed → endpoint mapping. Link `Program.cs`, `IamModule.cs`,
`IAMConfig.cs` and BuildingBlocks' `IModuleConfig.cs`.

State that features are internal and the host must not call module internals.

### Required section 4 — Source-code map

Provide a table with concern, owning path and what to inspect. Cover:

- BuildingBlocks module contract and TSID seam;
- IAM domain;
- each feature folder;
- IAM configuration/composition;
- persistence context/maps/migrations;
- integration fixtures and focused test classes;
- architecture, learning history and source slices.

Use exact case-sensitive repository paths verified after B018.

### Required section 5 — Configuration and secrets

Derive the final table from `IAMConfig`, `AuthOptions` and
`SeedAdminOptions`; at minimum inspect:

- `IAM:IamDb`;
- `IAM:Auth:SigningKey`;
- `IAM:SeedAdmin:Email`;
- `IAM:SeedAdmin:Password`;
- `IAM:SeedAdmin:DisplayName`;
- `AllowedOrigins` only as a linked host-owned setting, clearly not IAM-owned.

For each key record owner, required/optional conditions, environment behavior,
secret classification and failure mode. Never include configured values or demo
credentials. Explain late configuration visibility and Production fail-closed
rules.

### Required section 6 — Identity/TSID contract

Use one boundary table:

| Boundary | Representation |
| --- | --- |
| PostgreSQL identity/FK | `bigint` |
| .NET domain/EF | `Tsid` |
| HTTP route/request/response, Location, JWT sub | canonical 13-character string |

Link `TsidId`, the IAM `TsidValueConverter`, maps and migration. Document
validation rules: lower-case accepted then normalized, malformed/GUID/decimal/
non-ASCII/default rejected. Document unique `TSIDCREATOR_NODE` requirement for
multiple writers. State that invitation tokens are secrets, not entity IDs.

### Required section 7 — Domain model and invariants

Inventory these current entities and their important relationships/invariants:

- `Account`;
- `Tenant`;
- `TenantMembership`;
- `TenantRole`;
- `TenantMemberRoleAssignment`;
- `TenantInvitation`;
- `AuditEvent`.

For each, link its file and summarize identity, ownership keys, statuses and
factory/mutation invariants. Do not paste every property. Cross-check normalized
email/slug/name behavior, active status, owner/member semantics, permission
replacement, invitation expiry/token hashing and audit immutability against
current code.

### Required section 8 — Persistence

Inventory the seven IAM tables and their primary/foreign keys, including the
composite assignment key and nullable/non-null audit actor reality as current
code proves. Link `IamDbContext`, every map and migrations.

Document:

- PostgreSQL provider;
- explicit TSID value conversion and `ValueGeneratedNever`;
- tenant-local unique constraints;
- delete behavior;
- migration-before-seed;
- irreversible UUID→TSID migration history without copying its SQL;
- no direct cross-module table access.

Do not claim nullable fields or cascades from memory: verify map and migration
files.

### Required section 9 — Authentication and JWT

Document login flow from endpoint → credential checker → password hash verify →
JWT issuer. Inventory current claims from code, including `sub`, email, name
and `isPlatformAdmin`; document issuer/audience/lifetime/clock-skew behavior.

Explain 401 versus authenticated-but-forbidden 403, invalid/legacy subject
failure, non-Development signing-key rules and the prohibition on logging
passwords/tokens/hashes.

### Required section 10 — Authorization and tenant isolation

Explain separately:

- platform-admin policy;
- authenticated account lookup;
- active tenant/account/membership checks;
- owner semantics;
- tenant role assignments and resolved permission union;
- last-effective-administrator protection;
- cross-tenant default deny;
- UI hiding is not authorization.

Inventory the current permission catalog directly from code:

- `IAM.Roles.Manage`;
- `IAM.Invitations.View`;
- `IAM.Invitations.Create`;
- `IAM.Audit.View`.

For each permission, link the endpoints/actions that consume it. Verify whether
each read is membership-only or permission-gated; do not infer.

### Required section 11 — Endpoint catalog

Create one row for every current route mapped by `IamModule`:

- `POST /api/auth/login`;
- `GET /api/auth/me`;
- `GET /api/auth/me/tenants`;
- `GET /api/platform/dashboard-summary`;
- `GET|POST /api/platform/users`;
- `GET|POST /api/platform/tenants`;
- `GET /api/tenants/{tenantId}/members`;
- `GET /api/permissions/catalog`;
- `GET|POST /api/tenants/{tenantId}/roles`;
- `PUT /api/tenants/{tenantId}/roles/{roleId}`;
- `PUT|DELETE /api/tenants/{tenantId}/members/{memberId}/roles/{roleId}`;
- `GET /api/tenants/{tenantId}/me/permissions`;
- `GET|POST /api/tenants/{tenantId}/invitations`;
- `GET /api/tenants/{tenantId}/audit`.

Columns must include method/path, purpose, authentication/policy/permission,
input/query, success shape/status, important failure statuses and owning feature
file. Derive all facts from current endpoint mapping; do not invent example
payload fields.

At the end, record the expected route-row count and explain how to update it
when a route is added/removed. Every literal route in the feature files must map
to exactly one table row.

### Required section 12 — Pagination and errors

Document the shared current pagination query `pageNumber`/`pageSize`,
defaults/max, metadata fields, deterministic ordering expectation,
beyond-last-page behavior and validation failure. List exactly which endpoints
are paged and which catalog/aggregate/resolved-permission endpoints are
deliberately unpaged.

Create an error semantics table for 400/401/403/404/409 with current IAM
examples and non-disclosure rules. Verify actual endpoints before filling it.

### Required section 13 — Invitations, audit, seeding and startup

Document:

- invitation creation/list behavior, duplicate conflict, role validation,
  expiry, opaque hashed token and current missing email/acceptance lifecycle;
- audit ownership, supported filters/order and permission;
- seed section all-or-none validation, password safety, idempotent admin
  creation and no-secret logging;
- migration/seed/restart ordering and behavior.

### Required section 14 — Test map and commands

Map every integration-test class to the IAM contract it protects, including
composition, configuration fail-closed, login/current account, persistence,
TSID, migration, users, tenants/discovery/isolation, roles/permissions,
invitations/audit and pagination.

Record current test count only if generated by the final verification run and
label it “last verified”, otherwise omit the count to avoid stale trivia.

Use repository commands:

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

Include the WSL/Windows host binding note by linking architecture instead of
duplicating the full troubleshooting guide.

### Required section 15 — Current limitations

List only limitations verified in current code/docs. At minimum verify:

- invitation email delivery;
- invitation acceptance/registration;
- refresh-token/session hardening;
- any current multi-module limitations.

Never turn planned ideas into implemented claims.

### Required section 16 — Change-impact checklist

End IAM.md with a checklist mapping code changes to sections:

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

Add the exact self-review declaration formats:

```text
IAM.md impact: updated — <sections>
IAM.md impact: none — <specific reason>
```

## Automatic agent/workflow changes

### AGENTS.md

Add a “Living module knowledge” rule:

- IAM questions/tasks read `docs/modules/IAM.md` first, then verify relevant
  facts in code;
- any production IAM diff triggers the impact checklist;
- update IAM.md in the same task when a documented fact changes;
- otherwise require the exact no-impact declaration;
- historical learning notes never override current handbook/code.

### backend-task command

During discovery, after reading the active Spec:

1. detect IAM ownership when expected paths include `src/modules/iam/**` or
   the task changes IAM contracts in BuildingBlocks/API;
2. read the complete IAM handbook;
3. include “expected IAM.md sections” in the plan.

Before moving to review:

1. inspect the actual diff;
2. classify it with IAM.md's impact table;
3. update and validate IAM.md, or record exact no-impact reason;
4. keep this as a separate visible todo—never hide it inside implementation.

### review-slice command

Add a blocking review check:

- if production IAM/config/composition/BuildingBlocks contract files changed,
  require either IAM.md in the diff or the exact no-impact declaration;
- reject vague reasons;
- verify edited handbook claims against code and route/config/permission
  inventories;
- report stale or missing documentation as a review finding.

### backend-mentor and vertical-slice skill

Teach the same read-first and impact-gate rule concisely. Remove stale wording
that points to completed live Specs. Do not duplicate the whole IAM handbook in
agent files.

## Documentation validation

Create a lightweight, dependency-free validation approach inside this task.
Prefer a small script under `scripts/` only if the repository already accepts
scripts; otherwise use a documented PowerShell/bash command in the learning
note. It must check:

- every Markdown relative link in IAM.md resolves;
- every literal route found in current IAM feature `MapGet/MapPost/MapPut/
  MapDelete/MapPatch` calls appears in the endpoint catalog;
- there are no duplicate method/path rows;
- required configuration and permission keys appear;
- forbidden secret-looking values are not embedded;
- obsolete symbols `IamId` and the old IAM-owned `IModuleConfig` path do not
  appear after B018.

Do not create a full documentation generator. Generated API docs are not a
substitute for the curated security/ownership handbook.

## Question drill

Before review, use a fresh IAM question set and answer each from IAM.md first,
citing its heading and owning code path. Include at least:

1. Where is `IModuleConfig` defined and who may reference it?
2. What exactly happens inside `UseIamModuleAsync`, and in what order?
3. Which configuration values are secrets?
4. Why is a TSID a string in JSON but bigint in PostgreSQL?
5. Which endpoints require `IAM.Invitations.Create`?
6. Can a platform admin read a tenant without membership?
7. What protects the last effective tenant administrator?
8. What happens on a duplicate active invitation?
9. Which lists are paginated and what is the maximum page size?
10. Where is legacy UUID→TSID migration behavior tested?
11. What must change in IAM.md when a new claim is added?
12. Which invitation capabilities are not implemented?

If any answer requires scanning the whole repository because IAM.md lacks a
path or fact, improve the handbook and rerun the drill.

## Verification and demo

Run documentation checks plus:

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
git diff --check
```

Manual truth check:

1. use IAM.md to locate login and current-account contracts;
2. start the real API/database;
3. sign in and call current account, tenant discovery and one tenant-scoped
   endpoint;
4. compare method/path/auth/status/ID/pagination facts to the handbook;
5. restart against the same database and verify documented startup behavior;
6. ask the question drill without repository-wide searching.

No frontend files/tests are touched. Browser/API smoke exists only to prove
documentation accuracy.

## Acceptance checklist

- [ ] B018 is done before B019 starts.
- [ ] `docs/modules/IAM.md` contains all sixteen required sections.
- [ ] Route, config, permission, domain, persistence and test inventories match code.
- [ ] Every handbook path/link resolves and no secret value appears.
- [ ] README and architecture link the handbook without duplicating it.
- [ ] AGENTS, backend-task, review-slice, backend-mentor and delivery skill enforce the impact gate.
- [ ] Future IAM tasks must update IAM.md or provide the exact justified no-impact declaration.
- [ ] The twelve-question drill is answerable from IAM.md with headings and code paths.
- [ ] Build, full backend tests, documentation validation and live truth check pass.
- [ ] No runtime, migration, schema, API or frontend change is included.

## Delivery discipline

Read S21 and this entire Spec. Inventory current IAM from code after B018 rather
than copying historical task prose. Present the document outline, source paths
and automatic update-gate plan, then wait for approval. Work only on B019.
During final delivery set B019 to `done`, replace its Spec with `—`, delete
exactly this Spec, preserve S21 and open one PR to `main`.
