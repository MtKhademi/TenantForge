# Architecture direction

This document records the intended destination. Each capability is introduced only when an active task needs it.

## System shape

TenantForge is a modular monolith with a separate React single-page application.

```text
Browser
  → React application
  → ASP.NET Core API host
  → IAM module
  → PostgreSQL
```

The first milestone contains one deployable API and one deployable web application. Module boundaries prepare the system for growth without paying the operational cost of microservices.

## Backend organization

Features are organized as vertical slices inside a module. A slice keeps its endpoint, request, validation, handler and response close together.

```text
src/modules/iam/
├── domain/
├── infrastructure/
└── features/
    └── login/
```

Rules:

- Domain concepts belong to the module that owns them.
- The API host composes modules but does not contain IAM business rules.
- Modules do not query another module's tables directly.
- Contracts are introduced only for demonstrated cross-module needs.
- Infrastructure remains replaceable but is not abstracted prematurely.

## Authentication evolution

Authentication intentionally evolves in visible steps:

1. A development-only hardcoded administrator proves the end-to-end login flow.
2. A protected current-user endpoint proves authentication on navigation and refresh.
3. Account persistence replaces the hardcoded credential source without replacing the UI contract.
4. An idempotent seed creates the platform administrator when absent.
5. Session hardening is introduced in a dedicated future task after the basic flows are understood.

The development stub must not be accepted in production configuration.

## Multi-tenancy

The first milestone uses one shared database and explicit `TenantId` ownership for tenant-scoped data.

Tenant context is resolved from authenticated membership and an explicit tenant selection. The API validates that the user is an active member before executing tenant-scoped behavior.

Defense-in-depth expectations:

- tenant-scoped reads require tenant context;
- writes set tenant ownership on the server;
- unique constraints include tenant identity when uniqueness is tenant-local;
- integration tests attempt cross-tenant access;
- missing or invalid tenant context fails closed;
- background work must carry explicit tenant context when it is eventually introduced.

Database row-level security and separate databases are not first-milestone requirements.

## Authorization

Authorization is evaluated on the API using platform role, tenant membership, tenant roles and catalog permissions. UI visibility is not a security boundary.

The first RBAC milestone supports role-based allows. Direct per-user allow/deny overrides and permission caching are postponed until a visible product need justifies them.

## Testing strategy

- Unit tests cover important domain rules with no infrastructure.
- Integration tests execute API behavior against real PostgreSQL through Testcontainers.
- Playwright verifies a small number of critical browser journeys.
- Tenant isolation and unauthorized access are integration-test requirements.

Tests are added with the slice that creates the behavior. The project does not build a large test harness before the first usable flow.

## Observability and operations

Structured logs, health checks and correlation identifiers are introduced when the runnable host exists. Metrics, tracing, background jobs and production deployment are later slices, not bootstrap prerequisites.
