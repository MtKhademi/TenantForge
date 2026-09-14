# S19 — Use one safe identifier across database, domain and HTTP boundaries

## Outcome

Every persisted IAM entity identifier uses a TSID. PostgreSQL stores its signed
64-bit value as `bigint`, .NET domain and persistence code works with `Tsid`,
and every HTTP/JWT boundary uses the canonical 13-character TSID string. The
browser never receives a JSON number for an identifier, so JavaScript cannot
lose integer precision.

B017 delivers this slice after B016. This is an intentional breaking change at
the authentication and URL boundary, but not a data-reset migration.

## Boundary contract

| Boundary | Required representation | Example / rule |
| --- | --- | --- |
| PostgreSQL identity and foreign key | `bigint` | The value returned by `Tsid.ToLong()` |
| Domain, commands, queries and authorization | `Tsid` | No `Guid` or raw `long` entity IDs |
| EF Core conversion | `Tsid` ↔ `long` | One centralized converter/convention |
| Route and request JSON input | `string` | Parse a valid 13-character TSID; reject GUIDs and decimal longs |
| Response JSON and `Location` headers | `string` | Canonical uppercase `Tsid.ToString()` |
| JWT `sub` and internal authenticated account ID | canonical TSID string / parsed `Tsid` | Old GUID-subject tokens are invalid |
| Frontend models | `string` | Existing browser identifier types remain strings |

Use the exact stable NuGet package `TSID.Creator.NET` version `1.0.0`. Centralize
generation, parse/validation and formatting in the IAM module rather than
spreading package calls across features. Do not add a generic identifier
framework, strongly typed wrappers per entity, or an alternate public encoding.

## Persisted identifier inventory

The following fourteen columns are the complete migration inventory. Every one
must change from PostgreSQL `uuid` to `bigint`, including nullable foreign keys.

| Table | Columns |
| --- | --- |
| `iam_accounts` | `id` |
| `iam_tenants` | `id` |
| `iam_tenant_memberships` | `id`, `tenant_id`, `account_id` |
| `iam_tenant_roles` | `id`, `tenant_id` |
| `iam_tenant_member_role_assignments` | `tenant_membership_id`, `tenant_role_id` |
| `iam_tenant_invitations` | `id`, `tenant_id` |
| `iam_audit_events` | `id`, `tenant_id`, `actor_account_id` |

The corresponding domain properties on `Account`, `Tenant`,
`TenantMembership`, `TenantRole`, `TenantMemberRoleAssignment`,
`TenantInvitation` and `AuditEvent` use `Tsid`. New aggregate IDs are generated
in process before insertion and EF marks them as never database-generated.

## Data migration contract

PostgreSQL cannot safely cast a UUID to a bigint. The migration is therefore
hand-authored and data preserving:

1. create a unique old-UUID to new-TSID-long mapping for each identity table;
2. add temporary bigint columns and backfill primary keys;
3. backfill every foreign key by joining through the appropriate mapping;
4. drop and recreate affected constraints and indexes in dependency order;
5. replace and rename columns atomically, retaining nullability and the current
   cascade/restrict behavior; and
6. leave no UUID IAM identity/foreign-key column or temporary mapping object.

Existing accounts, tenants, memberships, roles, assignments, invitations and
audit events must remain connected and readable. A destructive database reset,
row deletion or guessed numeric conversion is forbidden. Because the original
UUID values are not retained after a successful migration, `Down` must fail
explicitly with a clear `NotSupportedException` instead of manufacturing new
UUID identities and pretending to restore the old state.

Existing access tokens contain GUID subjects and existing bookmarked routes
contain GUID values. They are expected to stop working after deployment; users
must sign in again and follow newly generated links. State this in architecture
and learning documentation. Invitation tokens themselves remain opaque strings
and are not converted to TSIDs.

## HTTP and feature inventory

Update identifier contracts across Login, CurrentAccount, Users, Tenants,
TenantDiscovery, TenantMembers, Roles, Invitations and Audit. This includes:

- route values, request records and validators;
- response records, nested items and identifier collections;
- created-resource `Location` headers;
- owner account input on tenant creation;
- JWT subject emission and authenticated-account parsing;
- authorization requirement/resource records, tenant-access lookup results,
  dictionaries, hash sets and comparisons; and
- repository/query projections and test fixtures.

Malformed, GUID-shaped and decimal-long public IDs must follow the endpoint's
existing validation/not-found policy without throwing an unhandled exception.
Authentication, tenant isolation, permission semantics, pagination envelopes,
status codes and all non-ID response fields remain unchanged.

Do not mechanically replace every `Guid.NewGuid()` in the repository. GUIDs
used only to make unique test emails, slugs or temporary directories are not
database entity identities. Password reset/invitation tokens and other secret
entropy are also outside this conversion.

## Verification

Automated coverage must prove:

- every entity can be saved and queried through `Tsid` while PostgreSQL uses
  `bigint` for all fourteen columns;
- a full legacy UUID relationship graph survives migration with the same row
  counts and foreign-key relationships;
- migrated/new longs are unique where required and round-trip through
  `Tsid.From(long)` and canonical strings;
- request IDs are strings, invalid GUID/decimal/malformed inputs fail safely,
  and all response IDs serialize as strings rather than JSON numbers;
- login emits a TSID `sub`, authenticated requests parse it, and a legacy GUID
  subject is rejected;
- created-resource locations, cross-tenant authorization, collection
  pagination and nullable audit actors still behave correctly; and
- the complete backend test suite passes.

The manual browser/database demo creates or signs in an account, creates a
tenant and role, exercises membership/invitation/audit screens, refreshes a
deep link, and inspects network payloads to show canonical 13-character string
IDs. A schema query shows `bigint` for all fourteen columns. An old GUID route
or old session fails safely and a fresh sign-in succeeds.

## Out of scope

- Frontend redesign or changing frontend IDs away from `string`.
- Re-encoding non-identity secrets, invitation tokens or pagination values.
- Backward-compatible GUID aliases, dual-read/dual-write or permanent mapping
  tables.
- Distributed-ID infrastructure beyond documenting that multi-instance
  deployments must assign unique `TSIDCREATOR_NODE` values.
- Generic module, repository or identifier abstractions.

## Acceptance

- The package version and three-boundary contract are explicit in code and docs.
- No IAM entity ID or entity foreign key remains a `Guid` in domain/runtime code.
- All fourteen database columns are `bigint`; no IAM identity column is `uuid`.
- No endpoint or JWT leaks a numeric identifier; public inputs and outputs are
  canonical TSID strings.
- Legacy data and relationships survive the migration; the irreversible down
  policy and required re-login are documented.
- Focused converter, API, auth, authorization and migration tests plus the full
  backend suite pass.
- The browser and database demo can be repeated from written instructions.
