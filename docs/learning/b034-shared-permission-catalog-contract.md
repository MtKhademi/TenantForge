# B034 — Shared permission catalog contract (BuildingBlocks) + IAM migration

## 1. Files changed and why

| File | Change | Why |
| --- | --- | --- |
| `src/building-blocks/TenantForge.BuildingBlocks/Permissions/PermissionDescriptor.cs` | new | One permission key a module owns (key/label/description/kind) — the cross-module in-memory shape. Mirrors IAM Contract's `PermissionResponse` field-for-field, but the wire type stays IAM-owned. |
| `src/building-blocks/TenantForge.BuildingBlocks/Permissions/PermissionGroup.cs` | new | One named group of descriptors; mirrors `PermissionGroupResponse`. |
| `src/building-blocks/TenantForge.BuildingBlocks/Permissions/IPermissionCatalogContributor.cs` | new | One method `GetPermissionGroups()`. Implemented once per module that owns permission keys; the seam that lets the host discover every contributor without any module referencing another. |
| `src/building-blocks/TenantForge.BuildingBlocks/Permissions/IAggregatedPermissionCatalog.cs` | new | The union of every contributor's groups (`AllGroups`) and keys (`AllKnownKeys`), computed once at startup. |
| `src/building-blocks/TenantForge.BuildingBlocks/Permissions/AggregatedPermissionCatalog.cs` | new | The single trivial implementation: flattens all contributors exactly once in the constructor. |
| `src/modules/iam/.../features/roles/IamPermissionCatalogContributor.cs` | new | IAM's own 3 groups (`roles`/`invitations`/`audit`), byte-for-byte what `RolesFeature.CatalogGroups` hardcoded before, now in the shared shape. |
| `src/modules/iam/.../features/roles/RolesFeature.cs` | rewritten | Catalog endpoint maps the aggregate onto the Contract wire shape; `ValidateRoleRequest` and `ResolvePermissionsAsync` check `catalog.AllKnownKeys` instead of the module-private `PermissionKeys` set; removed `CatalogGroups`/`PermissionKeys`/`KnownPermissionKeys`; added `ToResponseGroups` boundary mapper. |
| `src/modules/iam/.../features/audit/AuditFeature.cs` | 1 call site | The audit endpoint now injects and passes the catalog. |
| `src/modules/iam/.../features/invitations/InvitationsFeature.cs` | 2 call sites | Both invitation endpoints now inject and pass the catalog. |
| `src/modules/iam/TenantForge.Modules.Iam/IAMConfig.cs` | +1 registration | `services.AddSingleton<IPermissionCatalogContributor, IamPermissionCatalogContributor>()`. |
| `src/api/TenantForge.Api/Program.cs` | +1 factory registration | Builds the `IAggregatedPermissionCatalog` singleton from every registered contributor, after both modules' `RegisterServices`. |
| `tests/integration/.../BuildingBlocksArchitectureTests.cs` | 1 assertion array | Approved exported-type surface grows 2 → 7 (order-sensitive). |
| `docs/building-blocks/README.md` | §2, §4, §7, §8, §12 | Real admission: 5 new rows, completed evidence block, narrowed exclusion, declaration. |
| `docs/modules/IAM.md` | §3, §4, §10, §16 | Catalog provenance, composition snippet, owner-semantics wording, declaration. |

## 2. Request flow

`GET /api/permissions/catalog` (authenticated):

1. Minimal API resolves `IAggregatedPermissionCatalog` from DI — the host
   factory in `Program.cs` builds it once at first resolution from
   `sp.GetServices<IPermissionCatalogContributor>()` (today: only
   `IamPermissionCatalogContributor`, registered by `IAMConfig.RegisterServices`).
2. `AggregatedPermissionCatalog`'s constructor has already flattened that
   contributor's groups and keys exactly once (no per-call recomputation).
3. `RolesFeature.ToResponseGroups` maps each `PermissionGroup`/
   `PermissionDescriptor` onto the IAM Contract wire records
   `PermissionGroupResponse`/`PermissionResponse` — the wire shape is
   module-owned and is unchanged.
4. `Results.Ok(new PermissionCatalogResponse(groups))` → `200 {groups[]}`.

The other changed flows: `POST/PUT /api/tenants/{tenantId}/roles*` validate
`permissionKeys` against `catalog.AllKnownKeys` (`ValidateRoleRequest`);
`GET /me/permissions` and every permission-checking
`AuthorizeTenantAccessAsync` call (audit, invitations, roles) resolve the
caller's keys through `ResolvePermissionsAsync`, which unions the owner
bypass from `catalog.AllKnownKeys` and filters assigned-role keys through it.

## 3. Concepts introduced

- **Contributor + aggregator DI pattern**: each module registers a
  singleton `IPermissionCatalogContributor`; the host builds one aggregate
  from all of them. No module ever references another module — the
  BuildingBlocks types are the only shared surface. This is what lets B035
  add Shop's keys by registering one more contributor.
- **Wire shape vs in-memory shape**: the Contract's `PermissionResponse`
  etc. stay IAM's HTTP contract; the new records are the in-memory shape.
  `ToResponseGroups` is the boundary mapper.
- **C# nullable-reference overloads (the one Spec adaptation)**: the Spec
  sketched an `internal` non-nullable shim plus a `private` nullable core
  overload. C# erases reference-type nullable annotations for overload
  resolution, so the two 5-parameter signatures were identical → **CS0111**.
  Fix: the nullable core is the single `internal` implementation; the 3-arg
  membership overload delegates with `(null, null)` and never dereferences
  the catalog. Same behavior, one fewer function.

## 4. Security decisions

- The aggregate only **unions** labels/keys for display and for
  `AllKnownKeys` membership checks. It never bypasses any module's own
  authorization: tenant membership, active account/tenant joins and the
  per-endpoint permission check are all unchanged.
- At B034 time the contributor set is `{IAM}`, so `AllKnownKeys` is
  set-equal to the old hardcoded `PermissionKeys` — every existing
  integration test passes unmodified, and no new key is grantable (a role
  request with `Shop.*` is still rejected with the same message).
- Registration order matters and is documented: the aggregator is built
  after both modules' `RegisterServices`, so every contributor is already
  in the container.

## 5. Alternatives deliberately postponed

- **Moving the wire response types into BuildingBlocks** — rejected: a wire
  response type is a module-owned HTTP contract, not a cross-module
  primitive (the handbook's Section 8 exclusion was narrowed, not deleted).
- **Letting Shop call IAM's catalog endpoint or reference the IAM module** —
  rejected: false dependency direction; the contributor pattern exists
  precisely to avoid it.
- **A generic "module contributes X" framework** — rejected: one concrete
  contract for one real, proven need (B035 is the second consumer).
- **Per-call recomputation of the aggregate** — rejected: contributors are
  startup-fixed singletons, so flattening once in the constructor is
  correct and cheaper.

## 6. Verification

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

- Build: 0 errors (only pre-existing environmental `NU1900` warnings).
- Full integration suite: 184/184, zero existing-test content edits.
- Live demo (built DLL, `--urls http://0.0.0.0:5080`, gateway
  `http://172.31.112.1:5080`, dev content root with a seeded admin):
  13/13 checks — login; catalog unauthenticated 401; catalog body
  field-for-field identical to the pre-refactor hardcoded output
  (order-sensitive JSON compare); DI resolved on roles/invitations/audit
  (200, no 500); owner `/me/permissions` = the 4 IAM keys; unknown key →
  400 "Select only known permission keys."; role creation 201.
  Evidence: `/tmp/opencode/b034-live-results.json`.

## 7. Review questions

1. Why does the permission-checking `AuthorizeTenantAccessAsync` need the
   catalog at all — what would break in B035 if it still filtered against
   IAM's own module-private key set?
2. The Spec's two-overload sketch failed to compile (CS0111). What
   exactly does C# do with `T` vs `T?` when comparing overload
   signatures, and why does the single nullable-internal overload remain
   safe for the 3-arg membership-only callers?
3. B035 registers a Shop contributor. What changes in
   `GET /api/permissions/catalog`, in `ValidateRoleRequest`, and in an
   Owner's `/me/permissions` — and which of those three is a **visible**
   behavior change for the UI?
