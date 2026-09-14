# B017 — Replace persisted IAM GUID identifiers with TSIDs

**Status:** planned

**Owner:** backend

**Source:** S19

**Depends on:** B016

**Source slice:** [S19](../slices/019-tsid-identifiers.md)

## Goal

Replace every persisted IAM entity `Guid` with `TSID.Creator.NET.Tsid`, store it
as PostgreSQL `bigint`, and expose/accept only canonical 13-character strings at
HTTP and JWT boundaries. Preserve all existing database rows and relationships.

The learning goal is to understand representation changes at explicit
boundaries: value generation in the domain, EF Core value conversion, safe
relational migration, and transport formatting that avoids JavaScript's integer
precision limit.

## Fixed decisions — do not redesign

1. Add `TSID.Creator.NET` pinned to exact version `1.0.0` in the IAM module.
2. Application identity type is `Tsid`; database provider type is `long`;
   request/response/JWT representation is the canonical TSID string.
3. Public clients never send or receive the backing decimal long. GUID-shaped
   input and decimal-long input are not compatibility formats.
4. Use `TsidCreator.GetTsid()` for new IDs, `Tsid.From(long)` for persistence,
   `Tsid.From(string)` behind a non-throwing validation helper for input, and
   `Tsid.ToString()` for transport output.
5. Centralize these operations in a small IAM-owned `IamId` helper and one
   `Tsid`↔`long` EF converter/convention. Do not invent per-entity wrappers,
   generators, repositories or a cross-module ID framework.
6. IDs are assigned in process before `SaveChanges`; configure identity keys
   with `ValueGeneratedNever()`.
7. Multi-instance deployments must set a distinct `TSIDCREATOR_NODE` (0–1023)
   per writer. Document this operational requirement; do not build allocation
   infrastructure in this task.
8. The migration preserves data and is intentionally irreversible. Its `Down`
   throws a clear `NotSupportedException` explaining that original UUIDs were
   removed.
9. Old JWT GUID subjects and bookmarked GUID paths are intentionally invalid.
   Document the rollout requirement to sign in again after deployment.
10. Preserve current authorization, tenancy, pagination, validation and error
    disclosure behavior. This task changes identifier representation only.

## Owned paths

- `src/modules/iam/**`
- `src/api/**` only where composition or tests require it after B016
- `tests/backend/**`
- the new EF migration and `IamDbContextModelSnapshot`
- `docs/architecture.md` and the required `docs/learning/**` note
- this active ledger row/Spec during final delivery

Do not edit `src/web/**`. Its API identifiers are already typed as strings.
Do not clean up unrelated GUIDs used for unique test data, filesystem paths or
opaque security tokens.

## Required production changes

### 1. Package and one identifier seam

- Pin `TSID.Creator.NET` `1.0.0` in the IAM project; restore and commit any
  repository-managed lockfile change if one is produced by the established
  package workflow.
- Add a small internal `IamId` helper beside the IAM domain/shared code. It
  generates a new `Tsid`, formats canonical strings, identifies the default
  value, and offers `TryParse(string?, out Tsid)` so invalid user input cannot
  escape as `ArgumentException`.
- Parsing may accept lower-case Crockford text if the package does, but every
  output is normalized by `Tsid.ToString()`. Require exactly 13 characters and
  reject blank, GUID-shaped and decimal-only values before/package parsing.
- Unit-test generation, long round-trip, canonical formatting, invalid input
  and default-value behavior.

### 2. Domain model conversion

Change the following properties and all constructors/factories/method
parameters that carry them from `Guid` to `Tsid`:

| Entity | Properties |
| --- | --- |
| `Account` | `Id` |
| `Tenant` | `Id` |
| `TenantMembership` | `Id`, `TenantId`, `AccountId` |
| `TenantRole` | `Id`, `TenantId` |
| `TenantMemberRoleAssignment` | `TenantMembershipId`, `TenantRoleId` |
| `TenantInvitation` | `Id`, `TenantId` |
| `AuditEvent` | `Id`, `TenantId`, nullable `ActorAccountId` |

Replace identity defaults such as `Guid.NewGuid()` with the centralized TSID
generator. Replace `Guid.Empty` checks with the helper/default `Tsid` check.
Preserve equality, aggregate rules, invitation-token generation and timestamps.

Search after editing for `Guid`, `Guid?`, `Guid.Parse`, `Guid.TryParse`,
`GetGuid`, `uuid` and `HasColumnType` under IAM production code. Classify every
remaining match in the learning note; an entity identity match is a defect.

### 3. EF Core model

- Add one `ValueConverter<Tsid,long>` (and nullable handling through EF's normal
  null behavior) in IAM infrastructure.
- Register the conversion centrally in `IamDbContext.ConfigureConventions` for
  every `Tsid` property, then retain entity-specific constraints/indexes in the
  existing mapping classes.
- Map the fourteen columns listed below to `bigint`. Configure entity primary
  keys as `ValueGeneratedNever()` and retain current composite keys, uniqueness,
  indexes, maximum lengths and delete behaviors.
- Update seed values and design-time model/snapshot so no seed or model metadata
  uses a GUID identity.

Required column inventory:

```text
iam_accounts.id
iam_tenants.id
iam_tenant_memberships.id
iam_tenant_memberships.tenant_id
iam_tenant_memberships.account_id
iam_tenant_roles.id
iam_tenant_roles.tenant_id
iam_tenant_member_role_assignments.tenant_membership_id
iam_tenant_member_role_assignments.tenant_role_id
iam_tenant_invitations.id
iam_tenant_invitations.tenant_id
iam_audit_events.id
iam_audit_events.tenant_id
iam_audit_events.actor_account_id
```

### 4. Hand-authored data-preserving migration

Scaffold the migration to update the snapshot, then replace unsafe generated
UUID-to-bigint `AlterColumn` operations. PostgreSQL has no valid direct cast for
this change.

Within the migration transaction:

1. create one temporary mapping table keyed by entity kind plus old UUID for the
   six identity roots: accounts, tenants, memberships, roles, invitations and
   audit events;
2. assign every mapped row a globally distinct valid TSID long. A direct,
   reviewable PostgreSQL approach is one migration-time UTC millisecond since
   the TSID epoch (`2020-01-01T00:00:00Z`) shifted left 22 bits, plus a stable
   `row_number()` over all six root sets. Check the maximum before addition and
   enforce uniqueness; do not hash or truncate a UUID. Values beyond one
   22-bit counter window naturally advance the timestamp portion and remain
   valid TSIDs;
3. add temporary bigint primary/foreign-key columns;
4. backfill primary values through their table mapping and all foreign keys by
   joining to the referenced mapping, including nullable audit actors;
5. assert before swapping that non-null counts match and no required FK lacks a
   mapping; abort the migration on any mismatch;
6. drop dependent foreign keys, primary keys and indexes in a reviewed order;
7. drop/rename columns and recreate every constraint/index with the same names
   where practical and the same cascade/restrict/nullability semantics; and
8. remove all temporary objects.

Do not reset the database, delete rows, create unrelated schema, expose mapping
tables permanently, or synthesize UUIDs in `Down`. Make `Down` immediately
throw the explicit irreversible-migration exception.

### 5. Commands, queries, authorization and endpoints

Follow each identity through Login, CurrentAccount, Users, Tenants,
TenantDiscovery, TenantMembers, Roles, Invitations and Audit. Update:

- MediatR command/query records and handler locals;
- tenant-owner input and every route-bound tenant/member/role/invitation ID;
- repositories, projections, joins and comparisons;
- response records, nested DTOs and all ID collections;
- authorization resources/requirements, membership access records,
  dictionaries and hash sets;
- JWT issuing so `sub = account.Id.ToString()` and authentication parsing so a
  non-TSID subject fails authentication without an exception; and
- created-resource `Location` generation so paths contain canonical TSID text.

Keep endpoint input types as `string` until the handler/boundary validates and
parses them. Keep every response ID property explicitly `string` and map from
`Tsid` explicitly; never rely on a global JSON converter that could accidentally
serialize the struct as an object or its backing `long` as a number.

For malformed values, preserve the endpoint's current distinction between
validation failure and not-found. Never turn bad syntax into 500. The existing
frontend should require no change because it already treats identifiers as
strings.

### 6. Tests

Update domain/integration fixtures whose values are actual IAM identities to
use `IamId`/`Tsid`. Do not blindly convert `Guid.NewGuid()` used only inside a
unique email, slug or temporary directory. Replace relational `GetGuid()` reads
with `GetInt64()` plus `Tsid.From(long)` where identity values are asserted.

Add focused coverage for:

- `IamId` and EF converter round-trips;
- all domain identity defaults and relationships;
- JSON response tokens: every field named as an IAM ID is a JSON string with a
  canonical 13-character value, never a number/object;
- string route/body parsing for valid lower/upper case, malformed, GUID-shaped,
  decimal-long and blank input;
- login JWT `sub`, successful authenticated parsing and rejection of a legacy
  GUID subject;
- `Location` headers containing the new string ID;
- cross-tenant and permission denial after type conversion;
- collection items and pagination metadata remaining intact; and
- nullable `AuditEvent.ActorAccountId` round-trip.

Create an isolated PostgreSQL `TsidIdentifierMigrationTests` fixture that:

1. migrates only to the migration immediately before B017;
2. inserts a connected legacy graph with fixed UUIDs covering all seven tables,
   both role assignments, an invitation, and audit events with null/non-null
   actor IDs;
3. migrates to latest;
4. asserts unchanged row counts and relationships;
5. queries `information_schema.columns` to assert all fourteen columns are
   `bigint` and no listed column remains `uuid`;
6. asserts generated values are positive, unique where constrained and each
   round-trips through `Tsid.From(long).ToString()`; and
7. proves constraints still reject orphaned references.

Keep `PermissionKeyMigrationTests` independent by targeting its intended
permission migration boundary; do not make it accidentally depend on B017.

## Documentation and operations

Update `docs/architecture.md` with the three-boundary representation, package
choice, EF conversion, migration policy, required post-deploy re-login and the
requirement for distinct `TSIDCREATOR_NODE` values in multi-instance writers.

Add the backend learning note required by `AGENTS.md`. It must explain why JSON
numbers are unsafe above 2^53−1, why TSID remains time-sortable when stored as a
signed long, how the converter keeps domain and provider types separate, and
why UUID-to-bigint needs an explicit relationship-preserving migration. Include
the classified search results for any deliberately retained GUID uses.

## Verification commands

Use the repository-prescribed Windows SDK through WSL:

```bash
dotnet.exe restore TenantForge.slnx
dotnet.exe build TenantForge.slnx --no-restore
dotnet.exe test tests/backend/TenantForge.Iam.UnitTests/TenantForge.Iam.UnitTests.csproj --no-build
dotnet.exe test tests/backend/TenantForge.Iam.IntegrationTests/TenantForge.Iam.IntegrationTests.csproj --no-build
rg -n "Guid|Guid\?|Guid\.Parse|Guid\.TryParse|GetGuid|uuid" src/modules/iam tests/backend docs
```

Also run the repository's task-ledger/link validation and the full backend test
command required by `AGENTS.md`. Review the generated migration and snapshot in
the diff; a green build is not evidence that legacy data is preserved.

## Repeatable manual demo

1. Start PostgreSQL and migrate a database that contains a legacy account,
   tenant, membership, role assignment, invitation and audit event.
2. Start the built API through the B016 module activation seam.
3. Confirm the old access token/old GUID deep link fails safely, then sign in
   again.
4. Create/read a tenant and role; exercise members, invitations and audit pages;
   refresh a tenant deep link.
5. In browser Network tools, show that route/body/response/JWT IDs are canonical
   13-character strings and that no identifier is a JSON number.
6. Query `information_schema.columns` for the fourteen-column inventory and
   show each is `bigint`; query joins to show the legacy graph remains connected.

## Acceptance checklist

- [ ] B016 is `done` and this task is dependency-ready before implementation.
- [ ] `TSID.Creator.NET` is pinned to `1.0.0`; generation/parsing/formatting is centralized.
- [ ] All listed domain identity and foreign-key properties use `Tsid`.
- [ ] EF centrally converts `Tsid` to `long`, keys are client-generated, and all fourteen columns are `bigint`.
- [ ] The hand-authored migration preserves the complete legacy graph and fails explicitly on `Down`.
- [ ] Every public identifier input/output and JWT subject is a canonical string; backing longs never reach JSON.
- [ ] Invalid, GUID and decimal input fail safely without weakening auth or tenant isolation.
- [ ] Migration, converter, API, auth, authorization and pagination tests pass with the full backend suite.
- [ ] Architecture and learning docs cover precision, operations, re-login and deliberately retained GUIDs.
- [ ] The browser/database demo is repeatable and its evidence is included in review.

## Delivery discipline

Read S19 and this entire Spec before editing. Present the boundary contract and
an ordered plan, then wait for user approval. Work only on B017. During final
delivery set B017 to `done`, replace its Spec link with `—`, delete exactly this
Spec, preserve the S19 source, validate the ledger graph, and open one PR to
`main`. Do not begin a later slice.
