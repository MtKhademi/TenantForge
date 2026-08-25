# Product definition

## One-line description

TenantForge is an understandable, reusable foundation for building secure multi-tenant SaaS applications with .NET and React.

## Primary users

### Application developer

Clones TenantForge to start a new SaaS product without rebuilding authentication, tenants, users, roles and permission-aware administration.

### Backend learner

Follows the ordered slices to understand how a production-minded .NET system grows from a simple login into multi-tenant IAM.

### SaaS operator

Uses the platform dashboard to manage tenants and inspect platform-level activity.

## Product boundaries

TenantForge provides a foundation, not a finished business application.

Included in the first public milestone:

- platform administrator authentication;
- users and account lifecycle basics;
- tenants and multi-membership;
- tenant switching and isolation;
- platform and tenant roles;
- permission catalog and role-permission assignment;
- permission-aware navigation and API authorization;
- invitations;
- audit log for sensitive IAM changes;
- responsive light and dark admin UI;
- local Docker-based setup and integration tests.

Not included in the first milestone:

- billing and subscriptions;
- social login;
- enterprise SSO;
- tenant impersonation;
- per-tenant databases;
- background-job platform;
- event bus or microservices;
- chat, matching or domain-specific features.

## Roles at the target milestone

### Platform scope

- `SuperAdmin` manages the whole installation and all tenants.

### Tenant scope

- `Owner` controls tenant membership and access.
- `Admin` manages day-to-day tenant administration.
- `Manager` has a limited operational role.
- `Member` has basic access.
- Custom tenant roles can combine catalog permissions.

A person can belong to multiple tenants and have different roles in each tenant.

## Permission shape

Permissions use a stable `Module.Resource.Action` key, for example:

```text
IAM.Users.View
IAM.Users.Create
IAM.Users.Update
IAM.Roles.ManagePermissions
Dashboard.View
AuditLogs.View
```

The server is the authority. The frontend uses the resolved permission set only to improve navigation and user experience.

## Success criteria for v0.1

A new contributor can clone the repository, start infrastructure and applications with documented commands, sign in as the seeded platform administrator, create two tenants, assign users and roles, prove tenant isolation, and inspect the resulting audit events.
