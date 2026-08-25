# TenantForge delivery roadmap

Only one slice may be active. A slice is unblocked when every dependency is complete on `main`.

| ID | Task | Primary owner | Depends on | Status |
|---|---|---|---|---|
| S00 | [UI foundation](000-ui-foundation.md) | UI | None | Planned |
| S01 | [Hardcoded admin login](001-hardcoded-admin-login.md) | Backend → UI | S00 | Planned |
| S02 | [Authenticated application shell](002-authenticated-shell.md) | Backend → UI | S01 | Planned |
| S03 | [Dashboard summary](003-dashboard-summary.md) | UI → Backend → UI | S02 | Planned |
| S04 | [IAM account persistence](004-iam-account-persistence.md) | Backend | S03 | Planned |
| S05 | [Seeded platform administrator](005-seeded-platform-admin.md) | Backend | S04 | Planned |
| S06 | [User management](006-user-management.md) | UI → Backend → UI | S05 | Planned |
| S07 | [Tenant membership](007-tenant-membership.md) | UI → Backend → UI | S06 | Planned |
| S08 | [Tenant isolation](008-tenant-isolation.md) | Backend → UI | S07 | Planned |
| S09 | [Role and permission matrix](009-role-permission-matrix.md) | UI → Backend → UI | S08 | Planned |
| S10 | [Audit log and invitations](010-audit-and-invitations.md) | UI → Backend → UI | S09 | Planned |

## Milestone gates

### Demo 1 — Authentication understood

S00–S02: a development administrator signs in, survives navigation, sees their identity and signs out.

### Demo 2 — Authentication persisted

S03–S05: dashboard data is real and the administrator is seeded idempotently in PostgreSQL.

### Demo 3 — Multi-tenant administration

S06–S08: users and tenants exist, membership is visible and cross-tenant access is denied.

### Demo 4 — Reusable SaaS IAM

S09–S10: tenant roles, permissions, invitations and sensitive audit events are usable through the UI.

## Global completion rule

A status changes to `Done` only after browser evidence, automated verification, task review and merge to a runnable `main`. Planning or partial backend implementation is not progress to `Done`.
