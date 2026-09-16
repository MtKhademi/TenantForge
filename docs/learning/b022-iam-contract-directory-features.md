# B022 — IAM Contract directory types: users, tenants and tenant members

## 1. Files changed and why

- `src/modules/iam/TenantForge.Modules.Iam.Contract/Requests/CreateUserRequest.cs`,
  `Requests/CreateTenantRequest.cs`, `Responses/UsersListResponse.cs`,
  `Responses/UserResponse.cs`, `Responses/TenantListResponse.cs`,
  `Responses/TenantSummaryResponse.cs`, `Responses/TenantMembersResponse.cs`,
  `Responses/TenantContextResponse.cs`, `Responses/TenantMemberResponse.cs`
  (new) — the second batch of S23, nine more HTTP contract records, each now
  a `public sealed record` with the exact same members, names and order as
  before. None needs a `using` inside the Contract project: every cross-type
  reference (`UserResponse` inside `UsersListResponse`, `TenantSummaryResponse`
  inside `TenantListResponse`, `TenantContextResponse`/`TenantMemberResponse`
  inside `TenantMembersResponse`) is same-namespace, and `IReadOnlyList`/
  `PaginationMetadata` come from ImplicitUsings / the B021 `Responses` record.
- `features/users/UsersFeature.cs` — deleted the `CreateUserRequest`,
  `UsersListResponse` and `UserResponse` declarations. `UserResponse` was the
  one type that was *not* pure data: it carried a
  `public static UserResponse FromAccount(Domain.Account)` factory that
  references the **internal** domain entity `Account`. That method cannot move
  (the Contract project may not reference an internal domain type, and the
  type would not even compile there), so the data-only record moved and the
  mapping became a `private static UserResponse FromAccount(Account)` method
  inside `UsersFeature`; both call sites (`accounts.Select(FromAccount)`,
  `FromAccount(account)`) were updated to the unqualified method. Added
  `using TenantForge.Modules.Iam.Contract.Requests;`.
- `features/tenants/TenantsFeature.cs` — deleted the `CreateTenantRequest`,
  `TenantListResponse` and `TenantSummaryResponse` declarations; handler logic
  unchanged. Added `using ...Contract.Requests;` (the `Responses` using was
  already present from B021).
- `features/tenantmembers/TenantMembersFeature.cs` — deleted the
  `TenantMembersResponse`, `TenantContextResponse` and `TenantMemberResponse`
  declarations; handler logic unchanged. No `using` change — the existing
  `using ...Contract.Responses;` (from B021) already resolves the three moved
  records.
- `docs/modules/IAM.md` (Section 4 source-code map — the Platform users /
  Platform tenants / Tenant members rows now name the Contract project as the
  records' home, mirroring B021's Pagination row; the users row also records
  that `FromAccount` stays module-owned).

No `.csproj` or `TenantForge.sln` change: B022 reuses B021's single
`ProjectReference` (IAM → Contract) and the solution entry. Excluded exactly
as the slice requires: `roles/`, `invitations/`, `audit/` (B023) and
`pagination/`, `login/`, `account/`, `dashboard/` (already moved in B021).

## 2. Request flow from endpoint to response

`GET /api/platform/users` → `UsersFeature` binds pagination (module-internal
`PaginationSupport.TryBind`) → EF query over `IamDbContext.Accounts` →
`accounts.Select(FromAccount)` maps each internal `Account` to the now
contract-owned `UserResponse` → `Results.Ok(new UsersListResponse(users,
pagination))`. `POST /api/platform/users` binds the contract-owned
`CreateUserRequest`, validates, creates the `Account`, and returns
`Results.Created(..., FromAccount(account))` — a `201` with the same six
`UserResponse` fields. `GET /api/platform/tenants` and `POST
/api/platform/tenants` are the same shape for `TenantSummaryResponse` (note
`MemberCount` is computed by the handler, so it is a plain `int` member on the
contract record, not something the contract computes). `GET
/api/tenants/{tenantId}/members` builds `TenantContextResponse` for the tenant
and `TenantMemberResponse` per row, wrapped in `TenantMembersResponse`. In
every case only the type's home assembly changed; the mapping, ordering and
serialization are byte-for-byte the same.

## 3. Backend concepts introduced

- **Admission is judged per *member*, not just per *type*.** The slice's rule
  is "a contract type carries no reference to an internal domain type."
  `UserResponse` satisfied the *shape* half (six plain string/bool members, a
  real delivered response) but failed the *reference* half because of its
  `FromAccount(Domain.Account)` factory. The resolution is to split along that
  line: move the data shape, keep the mapping. A static factory that takes a
  domain entity is a *mapper*, not a *contract*; it belongs where the entity is
  visible.
- **A `private static` mapper in the feature is the module-owned equivalent of
  a static factory.** Before the move, `UserResponse.FromAccount` was a public
  static method on the record. After the move it is a `private static` method
  on the feature that *returns* the contract record. Callers change from
  `UserResponse.FromAccount(x)` to `FromAccount(x)`; the serialized output is
  identical because a static factory is never part of the JSON.
- **Same-namespace references keep the Contract project reference-free.**
  Moving several records that reference each other into one namespace
  (`Responses`) means no new `using` is needed inside the Contract project,
  preserving B021's zero-reference invariant.
- **Batched mechanical moves.** B022 is the second of three dependency-chained
  batches (B021 foundation, B022 directory, B023 roles/invitations/audit). Each
  batch is small enough to review on its own and reuses the one
  `ProjectReference` from the first — no per-type project wiring.

## 4. Security decisions

- No authentication, authorization, configuration or fail-closed behavior
  changed. The `PlatformAdmin` claim policy on the platform users/tenants
  routes, the active-membership gate on `/api/tenants/{tenantId}/members`, and
  the 401/403 non-disclosure rule are all untouched — proven by the full suite.
- The visibility change (`internal sealed` → `public sealed`) is a boundary
  re-drawing, not a security change: these types were always serialized to HTTP
  callers. `UserResponse` now exposes only the six data fields publicly; the
  internal `Account` entity remains `internal` and is reachable only through
  the module's own mapper.
- No secrets, credentials or tokens are logged; nothing new is logged.

## 5. Alternatives deliberately postponed

- **Moving `FromAccount` into the Contract project.** Rejected: it references
  the internal `Account` entity and would force the reference-free Contract
  project to know about a domain type. It would also require either making
  `Account` public (leaking the domain model) or adding a `ProjectReference`
  from Contract → IAM (an illegal cycle). Keeping it module-owned is the only
  option that respects the admission rule.
- **Introducing a shared `Account → UserResponse` mapper interface or
  extension method.** No second consumer needs it; a `private static` method is
  the smallest, most readable form.
- **The exported-surface architecture test** (`IamContractArchitectureTests`)
  and the **Contract handbook** (`docs/contracts/iam.md`) — still B023 and
  B024 respectively, so they lock the *final* 32-type roster rather than an
  intermediate state.

## 6. Verification

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
# Expected: build succeeded; Passed: 112, Failed: 0, Skipped: 0
```

Grep proof (only usages remain inside the implementation project; the nine
declarations exist only in the Contract project):

```bash
grep -rnE '(record|class) (CreateUserRequest|UsersListResponse|UserResponse|CreateTenantRequest|TenantListResponse|TenantSummaryResponse|TenantMembersResponse|TenantContextResponse|TenantMemberResponse)\b' src/modules/iam/TenantForge.Modules.Iam/
# -> no matches
```

Manual demo (API on `http://0.0.0.0:5000`, curl through the WSL gateway IP,
seeded admin):

1. `POST /api/auth/login` → `200` with a JWT.
2. With the token, `GET /api/platform/users?pageSize=3` → `200`
   `{users[ {id,email,displayName,status,isPlatformAdmin,createdAtUtc} ],
   pagination}`.
3. `POST /api/platform/users` `{email,displayName,password}` → `201` + the six
   `UserResponse` fields.
4. `GET /api/platform/tenants?pageSize=2` → `200`
   `{tenants[ {id,name,slug,status,memberCount,createdAtUtc} ], pagination}`.
5. `POST /api/platform/tenants` `{name,slug,ownerUserId}` → `201` +
   `TenantSummaryResponse` with `memberCount: 1`.
6. `GET /api/tenants/{tenantId}/members` as that tenant's owner → `200`
   `{tenant{id,name,slug,status}, members[ {id,userId,email,displayName,role,
   createdAtUtc} ], pagination}`.
7. Stop and restart the API against the same database → startup completes,
   "No migrations were applied" + "seeding already-present" (idempotent).

## 7. Three review questions

1. `UserResponse.FromAccount` was `public static` on the record before the
   move and is a `private static` method on `UsersFeature` after it. What
   exactly does the reference-free Contract project forbid, why can't the
   mapper simply be moved too, and why is a `private static` feature method the
   correct "home" rather than a new public mapper type?
2. All three moved record groups live in the single `Responses` namespace, so
   `TenantMembersResponse` can name `TenantContextResponse` and
   `TenantMemberResponse` without any `using`. What would break in the
   Contract project's zero-reference invariant if one of those cross-references
   instead crossed into a different namespace or a different project?
3. Why is `UserResponse`'s public surface now *smaller* than the internal
   `Account` entity's, even though both describe "a user" — and how does that
   difference (six plain string/bool fields vs. a persisted entity) keep the
   domain model from leaking into the HTTP contract?
