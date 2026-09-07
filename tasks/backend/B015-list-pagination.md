---
id: B015
slice: S15
title: Add consistent pagination to collection APIs and query filters
agent: backend-mentor
source: tasks/slices/015-list-pagination.md
---

# Objective

Expose real server pagination and filtered totals for all seven business
collection reads so F022 can page tables and selectors beyond the current cap.

## Context

Read tasks/slices/015-list-pagination.md in full, docs/architecture.md and the
current docs/design contracts for S06 through S13 that cover these endpoints.
The S15 source is authoritative for query fields, response metadata, ordering,
empty-page semantics and the explicit security-snapshot exclusions.

## Scope and files

- Own src/modules/iam/TenantForge.Modules.Iam/features/users/UsersFeature.cs,
  features/tenants/TenantsFeature.cs, features/tenantmembers/TenantMembersFeature.cs,
  features/roles/RolesFeature.cs, features/invitations/InvitationsFeature.cs,
  features/audit/AuditFeature.cs and the my-tenants feature introduced by B012.
- Add small IAM-local pagination query/metadata/validation primitives and bind
  them through explicit endpoint filter DTOs. Preserve action/fromUtc on audit.
  Avoid a repository framework or generic query engine.
- Implement the seven GETs from S15, preserving their array keys, row payloads,
  members' tenant context, existing scope rules and mutation response contracts.
- Count after authorization/scope/business filters, then apply deterministic
  ordering and Skip/Take before materializing. Retain unique tie-breakers.
  Do not page BuildRoleResponsesAsync callers used for mutation responses or
  truncate authorization/administrator-protection queries as a side effect.
- Update affected backend integration tests and add focused pagination coverage
  under tests/integration/TenantForge.Api.IntegrationTests/ as appropriate.
- Sole owner of docs/design/s15-list-pagination.md, documenting the final request
  and response contracts, endpoint inventory, curl examples, validation and
  intermediate UI limitation. Add docs/learning/b015-list-pagination.md per
  AGENTS.md, including Count-before-Skip and tenant-scoped totals.
- F022 is the immediate UI consumer. Add no endpoint and do not edit src/web.

## Acceptance

- Each of the seven GETs accepts pageNumber/pageSize through its filter DTO and
  returns all six metadata fields. Omitting both yields page 1 of size 50;
  omitting either alone applies only its own default. pageSize 100 is accepted.
- Each endpoint has real PostgreSQL integration evidence with >50 matching
  rows: the default cap is navigable, adjacent pages are disjoint on unchanged
  data, and traversing pages yields exactly the expected authorized IDs.
- Cover first, middle, last partial, zero-match and beyond-last pages, duplicate
  sort values, accurate totalCount/totalPages/flags, and retained members context.
- Cover invalid numeric/empty/overflow inputs and offset overflow as field-level
  400s. No invalid size becomes an unbounded query or an unexpected 500.
- Audit action/fromUtc combine with paging; totals exclude nonmatching actions,
  older events and other tenants. Invitation totals exclude expired/nonpending
  rows consistently with data. My tenants excludes others' memberships and
  inactive tenants, without duplicate tenants.
- Security tests prove ordinary users cannot obtain platform rows OR counts;
  other-tenant, missing-membership and missing-permission cases remain denied
  on later pages. Authentication failures remain 401; valid forbidden reads
  remain 403 under the existing contract.
- Paged roles preserve complete per-role permissionKeys/memberIds; permission
  resolution and last-administrator protection continue to consider all roles.
- Review generated query shape/evidence that paging runs in SQL; no ToList
  before paging and no fetching all roles/assignments just to return one page.

## Verification and browser demo

Run from the repository root with .NET 10 and Docker for PostgreSQL Testcontainers:

- dotnet build src/api/TenantForge.Api/TenantForge.Api.csproj
- dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj

Demonstrate existing browser consumers still parse first-page data. Use
authenticated same-origin browser requests to show page 2, filtered totals,
an empty page, invalid input and denial; do not expose tokens or add a temporary
endpoint. Record that visible controls and complete selector navigation belong
to F022. Document repeatable development-only fixture steps in the learning
note. Record environment blockers without claiming checks passed.

## Lifecycle

Status and dependencies live only in tasks/TASKS.md. Follow the owning command,
plan approval and final review gates. After final delivery approval, mark only
B015 done, replace its Spec with — and delete this exact executable Spec in
the same commit. Preserve the source slice and learning/design documents.
