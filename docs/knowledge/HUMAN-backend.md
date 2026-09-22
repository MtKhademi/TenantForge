# Backend knowledge for people

A plain-language description of what the TenantForge backend does today, how
the important pieces work and why they were built that way.

This file is **not** required reading for an implementing agent — that is
[AGENT-backend.md](AGENT-backend.md). Read this one when you want to
understand the system rather than change it.

Related: `docs/learning/<task-id>-<slug>.md` teaches one delivered backend
slice at a time and is written once, at delivery. This file is the living
overview and gets updated whenever a slice changes the picture below.

## What the backend is

A .NET 10 modular monolith behind ASP.NET Core Minimal APIs, storing everything
in one PostgreSQL database through EF Core. It is a single deployable process,
but the code is split into modules that only talk to each other through
explicit contracts — so a module can later be extracted without rewriting its
callers.

Two modules exist:

- **IAM** — accounts, tenants, memberships, roles, permissions, invitations and
  the audit log. It also owns authentication for the whole host.
- **Shop** — a tenant's product catalog, storefront, cart, checkout, orders and
  payments.

A shared `TenantForge.BuildingBlocks` project holds only the handful of things
every module must agree on: the TSID identifier type, the module configuration
contract and the permission catalog contracts.

## How a request flows

1. The host (`src/api/TenantForge.Api/Program.cs`) registers both modules
   before building the app, then activates them in order.
2. IAM activation validates its configuration, installs authentication and
   authorization middleware, applies pending migrations, seeds the development
   platform administrator and maps every IAM endpoint.
3. Shop activation does the same minus middleware and seeding.
4. An incoming request is authenticated by the IAM middleware, then routed to a
   feature handler.
5. A tenant-scoped handler checks that the caller is a member of that tenant
   and holds the required permission before touching data.
6. The handler returns a typed response, or an RFC 7807 Problem Details body on
   failure.

## What is delivered today

**IAM**

- development sign-in for the seeded platform administrator;
- session recovery in the browser;
- platform user listing and creation;
- tenant creation with an initial owner, and tenant switching;
- tenant membership with server-enforced isolation;
- tenant roles and a server-provided permission matrix;
- invitation creation and a pending-invitation list with expiry;
- an audit log for invitation and role changes;
- shared, paginated list responses.

**Shop**

- tenant-scoped category and product administration;
- product image galleries: upload, reorder and delete, with real image
  decoding/validation and safe local storage;
- a public storefront catalog, including each product's ordered image
  gallery;
- storefront discovery: an anonymous all-products list with name search,
  category/sale filtering and four deterministic sort orders, showing a
  per-product "card price" (lowest in-stock variant price), an on-sale flag
  and a sold-out state — without ever exposing SKUs or raw stock counts;
- persistent carts;
- shipping rates and coupons;
- a checkout summary, order creation and guest order lookup;
- a sandbox payment gateway;
- Shop permission keys published through the shared catalog and enforced on
  every tenant-scoped route.

Live status for everything else is in `tasks/TASKS.md`.

## Decisions worth knowing, and why

**Two-phase module composition.** Each module exposes exactly
`AddXModule(...)` and `await UseXModuleAsync(...)`. Registration does no I/O
and makes no pass/fail decision; activation owns validation, migration and
endpoint mapping. This keeps the host free of module internals and makes an
invalid configuration fail at startup rather than on the first request.

**Three-layer identifiers.** IDs are stored as PostgreSQL `bigint` (compact,
sortable, cheap to index), modelled as `Tsid` in .NET (type-safe), and exposed
over HTTP as 13-character strings (opaque to clients, no enumeration hints, no
JavaScript precision loss). The backing integer never appears in JSON.

**One error shape.** Every failure is RFC 7807 Problem Details, so the frontend
has exactly one parser for errors.

**Authorization is server-side, fail closed.** Missing tenant or permission
context is a denial, not a default-allow. Hiding a button in the UI is never
treated as a control.

**BuildingBlocks has an admission rule.** A type only moves there once it has
proven cross-module value. This is deliberate friction — it stops the project
from growing a `Common` bucket that everything depends on.

**Integration tests over unit mocks.** Security-sensitive behavior is tested
against a real PostgreSQL instance through Testcontainers, covering both the
allowed and the denied path, because that is where tenant isolation bugs
actually appear.

**No speculative endpoints.** An endpoint is added only when a current or
immediately dependent frontend task consumes it.

**Product media never trusts what the client claims.** An uploaded image's
actual bytes are decoded (not its filename or `Content-Type` header) to
confirm it really is JPEG/PNG/WebP, is a single still frame, and is within
size/dimension limits. The accepted image is always stripped of EXIF/GPS
metadata and re-encoded to WebP before it is stored. A file is only made
visible to readers after the database row that references it has committed
successfully, so the filesystem and the database can never disagree about
which images exist.

## Running and verifying it

```bash
docker compose up -d postgres
ASPNETCORE_ENVIRONMENT=Development dotnet.exe run \
  --project src/api/TenantForge.Api/TenantForge.Api.csproj --urls http://0.0.0.0:5000
curl http://localhost:5000/health
```

Migrations are applied and the development administrator is seeded at startup.

```bash
dotnet.exe build TenantForge.sln
dotnet.exe test          # requires Docker for Testcontainers
```

On the reference machine there is no Linux `dotnet` binary; use `dotnet.exe`.
Full environment notes are in `docs/architecture.md`.

## Current limitations

- Invitations are records only. Nothing is emailed, and acceptance,
  registration-from-invite, resend and revoke are not implemented.
- Sessions use a single access token; there is no refresh-token rotation.
- Payments run against a sandbox gateway; a real provider is a later slice.
- There is no metrics, tracing or background-job infrastructure yet.

## Where to look next

- `docs/architecture.md` — system shape and testing strategy
- `docs/modules/IAM.md` — the searchable IAM handbook
- `docs/modules/SHOP.md` — the searchable Shop handbook
- `docs/building-blocks/README.md` — BuildingBlocks handbook and admission rule
- `docs/contracts/iam.md` — IAM's public HTTP surface
- `docs/design/shop/` — Shop contracts and design notes
- `docs/learning/` — per-slice teaching notes

## Maintaining this file

Read it before editing so existing explanations are preserved. Add or revise
only the sections a delivered task actually changed, and describe the verified
implementation rather than the plan. Do not paste large code blocks, secrets or
task-specific debugging detail.
