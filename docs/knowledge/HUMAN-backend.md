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

- tenant-scoped category and product administration, including one level of
  subcategories: a category can be a root or a direct child of a root (never
  deeper), and the public storefront groups children under their root;
- product image galleries: upload, reorder and delete, with real image
  decoding/validation and safe local storage;
- a public storefront catalog, including each product's ordered image
  gallery;
- storefront discovery: an anonymous all-products list with name search,
  category/sale filtering and four deterministic sort orders, showing a
  per-product "card price" (lowest in-stock variant price), an on-sale flag
  and a sold-out state — without ever exposing SKUs or raw stock counts;
- persistent carts with server-owned reservation leases: abandoned carts expire, release reserved stock back to variants, and return a stable `410 shop_cart_expired` problem so the storefront can ask the shopper to start again;
- shipping rates and coupons, where a coupon can now set a minimum subtotal, a
  maximum discount cap and a total redemption limit; the checkout preview shows
  what would be discounted without consuming a redemption, and order creation
  spends a redemption exactly once (two shoppers racing for the last slot get
  one discount and one "limit reached", and the shopper who lost keeps their
  cart);
- a checkout summary, order creation and guest order lookup;
- admin order reading for tenant operators: a permission-gated, filterable
  list and a per-order detail showing the customer, the frozen item snapshots,
  the totals and the (capped) payment history — so staff can inspect a guest
  order without ever knowing its public tracking-code secret;
- operator order operations: a tenant operator with the `Shop.Orders.Manage`
  key can mark a paid order **fulfilled** or cancel an unpaid order. Cancelling
  returns the reserved stock to the shelf exactly once and voids any payment
  that has not yet completed. A paid order cannot be cancelled — refunds are a
  separate, later capability.
- a sandbox payment gateway;
- a tenant storefront identity (store name, tagline, support phone, Instagram)
  and customer policy pages (about, shipping, payment, returns, privacy) that
  the storefront header/footer can publish, saved with an optimistic-concurrency
  version so two editors cannot silently overwrite each other;
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

**Reading is a permission, and a "not found" never leaks why.** Most Shop admin
reads only need tenant membership, but reading a tenant's guest orders is
gated by its own `Shop.Orders.View` key — an authenticated member without the
key gets a `403`, an anonymous caller a `401`. The order-detail route answers a
malformed id, another tenant's id and a missing id with the *identical* `404`,
so an outsider cannot even tell whether a given id exists. An order also stores
a `version` number: the operator status actions (fulfil/cancel) only apply when
the caller echoes back the version they last saw, so two operators changing the
same order at once cannot silently overwrite each other.

**BuildingBlocks has an admission rule.** A type only moves there once it has
proven cross-module value. This is deliberate friction — it stops the project
from growing a `Common` bucket that everything depends on.

**"Effective" visibility is one shared rule, and check-then-write is
race-safe.** A storefront category is only public while it *and* its root are
active — one rule, applied by every public read (category list, product
filtering, product detail, image bytes) so a deactivated root cannot leak a
child through one route while hiding it from another. The rule that a parent
which already has children can never itself become a child is enforced with a
row lock held across the check and the write, not a read-then-hope check, so
two simultaneous requests cannot both win.

**Integration tests over unit mocks.** Security-sensitive behavior is tested
against a real PostgreSQL instance through Testcontainers, covering both the
allowed and the denied path, because that is where tenant isolation bugs
actually appear.

**Concurrency is owned, not inferred.** The storefront profile (and product
galleries) use an explicit version number the client echoes back: a save is
applied only when it matches the row's current version, otherwise the client
gets a `409` saying to reload. The profile keeps exactly one row per tenant by
a database unique index, so two people saving the first draft at the same
moment resolve to one winner and one conflict — never a silent overwrite. The
conflict carries a stable `type` (`stale_version`) so the frontend can react
to it by name instead of parsing a message.

**Cart stock leases are explicit.** Adding an item reserves stock immediately so checkout cannot oversell. That reservation now has a server-owned expiry; a row lock makes expiry and order creation race safely, so either the cart becomes an order or the stock is restored exactly once — never both.

**Order status changes are idempotent and inventory-safe.** An operator's
fulfil/cancel request carries a UUID idempotency key and the order's current
version. The same key sent again returns the original result without re-doing
the work; the same key with a *different* action is a conflict rather than a
silent guess. Each key is stored once per tenant, backed by a database unique
index — not a "check first, then write" that two racing requests could both
pass. Cancelling an order locks the order and its product-variant rows, returns
the reserved stock in the same transaction, and records a release timestamp so
the stock comes back exactly once even if the action is somehow triggered
twice. A late payment callback for a cancelled order is rejected, so a cancelled
order can never be paid by accident.

**Coupon rules have one owner, and spending capacity is race-safe.** Every
coupon rule (minimum subtotal, discount cap, redemption limit, expiry, active
flag) lives in a single pure function that reads a coupon and returns a verdict
plus the discount to apply — it never writes. The checkout summary and the order
creation both call it, so the price a shopper previews and the price they are
charged can never disagree. Spending a redemption is different, though: the
order path re-checks the rules while holding a database row lock on the coupon
and bumps the redeemed count in the same transaction that writes the order. That
lock is what stops two concurrent orders from both claiming the last allowed
redemption, and because the bump is in the same transaction, a failure later in
the order also undoes the redemption.

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
