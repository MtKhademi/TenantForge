# B042 — Expose tenant order list and detail for operators

Slice S38. A tenant operator (a member holding `Shop.Orders.View`, or the
Owner via bypass) can now list and inspect the tenant's guest orders — the
customer's name/phone, the frozen item snapshots, the totals, and the
payment-attempt history — without ever needing the public tracking-code
secret that the anonymous guest lookup requires.

## 1. Files changed and why

- `features/authorization/ShopAuthorization.cs` — added the two constants
  `OrdersViewPermission` (`Shop.Orders.View`) and `OrdersManagePermission`
  (`Shop.Orders.Manage`) and both to `KnownKeys`. Manage is registered but
  never checked here — B043 owns the mutations it gates.
- `features/authorization/ShopPermissionCatalogContributor.cs` — contributes
  both new keys to the `shop` catalog group (the frontend reads the catalog
  from the server, so the two keys must appear there even though only View is
  enforced yet).
- `domain/ShopOrder.cs` — new `Version` (int, default `1`) property + a
  `BumpVersion()` that only B043's mutation path will call. B042 persists and
  returns it but never bumps it.
- `infrastructure/ShopOrderMap.cs` — mapped the `version` column
  (`HasDefaultValue(1)`).
- `infrastructure/Migrations/20260922180030_AddShopOrderVersion.{cs,Designer.cs}`
  (generated) + `ShopDbContextModelSnapshot.cs` — adds **only** that one
  column, default `1`, backfilling existing orders.
- `features/orders/AdminOrderContracts.cs` (new) — the admin wire records
  (`AdminOrder*`), distinct from the anonymous lookup's flat records.
- `features/orders/AdminOrdersFeature.cs` (new) — the two `Shop.Orders.View`
  routes (list + detail).
- `ShopModule.cs` — `MapAdminOrdersFeature()` in the fixed mapping order.
- Tests: `ShopAdminOrdersIntegrationTests.cs` (new), a dedicated
  `ShopAdminOrdersDbFixture`/collection in `IamDbFixture.cs`, and the two
  hand-maintained permission rosters in `RolePermissionIntegrationTests.cs`.

## 2. Request flow, endpoint to response

**List (`GET /api/tenants/{tenantId}/shop/orders`).** The route
`RequireAuthorization()`s (JWT challenge → `401` when anonymous) →
`ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db,
OrdersViewPermission)` (Owner bypass, or a role carrying the key; otherwise
`403`) → bind `pageNumber`/`pageSize` and the optional `q`/`status`/
`fromUtc`/`toUtc` filters (any violation → `400` naming the field) → build the
query with `TenantId` as the **first** predicate → append `q` as a
case-insensitive `ILike` contains across all four searchable columns, a status
enum match, and the inclusive-start/exclusive-end date window → order
`CreatedAtUtc desc, Id desc` → `PaginationSupport.PageAsync` → map each row to
`AdminOrderSummaryResponse`.

**Detail (`GET …/orders/{orderId}`).** Same auth gate → parse the TSID
(malformed → generic 404, never hitting the DB) → a tenant-scoped lookup
(`Id == orderTsid && TenantId == access.TenantId`) that returns one identical
404 for a missing **and** a cross-tenant id → load the item snapshots and the
20 newest payment attempts → map to `AdminOrderDetailResponse` (including
`Version`).

## 3. Backend concepts introduced

- **Permission-gated reads.** Every prior Shop admin read was membership-only;
  B042 is the first Shop route that treats *reading* as a permission decision.
  It reuses the same 4-argument `ShopAuthorization` overload the mutating
  routes already used — no new mechanism, just a new key.
- **Seeding an optimistic-concurrency token before it is needed.** `Version`
  lands in B042 (read-only) because the immediately-dependent B043 mutation
  needs it, and the F060 list/detail screen must already be returning it
  before F061 can bind B043's actions. A read-only slice that is also a schema
  prerequisite for the next slice.
- **Snapshot reads, not live joins.** The detail's item values come from
  `ShopOrderItem`'s `*Snapshot` columns, frozen at order-creation time — never
  a join to the current product name/price.
- **A documented, temporary cap.** Payment attempts are `Take(20)` newest, with
  a comment naming B044 as the task that replaces this with a lifecycle-aware
  bound — a bounded payload without inventing that policy early.

## 4. Important security decisions

- **Tenant isolation is the first predicate, always.** Both the list and the
  detail lookup put `order.TenantId == access.TenantId` in the query before any
  other filter, so a well-formed id from another tenant simply cannot match —
  it collapses into the same generic `404` as a missing id. Timing and body are
  identical; nothing leaks which case was hit.
- **Anonymous → `401`, authenticated-but-unauthorized → `403`.** Both routes
  `RequireAuthorization()` so the JWT challenge answers anonymous callers
  before the handler runs; `ShopAuthorization` then enforces the permission.
  This is the same shape every other Shop admin route uses (discovered as a
  recovery fix: the first draft of this feature omitted `RequireAuthorization()`
  and anonymous callers fell through to `Results.Forbid()` → `403`).
- **Nothing tenant/totals/prices comes from the client.** `TenantId` is the
  route segment the auth check already verified; no request body supplies a
  price, total or permission key.
- **No reuse of the anonymous lookup path.** The 404 is a single
  `OrderNotFoundProblem()` helper, never the tracking-code lookup's
  validation/response, so the two routes cannot drift.

## 5. Alternatives deliberately postponed

- **Order mutations (fulfil/cancel) and `Shop.Orders.Manage` enforcement** —
  B043. Only the token and the catalog key are seeded here.
- **Export, customer accounts, invoice PDF, refund, shipment tracking, rate
  limiting** — explicit non-goals / later slices.
- **A general-purpose text-search helper** — the Spec says there is none today
  and not to create one; the `q` `Where` is written inline in this endpoint.
- **A `Shop.Contract` project** — still no second .NET consumer, so the admin
  records stay beside the feature.

## 6. Commands and manual steps to verify

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test TenantForge.sln --nologo --filter "FullyQualifiedName~ShopAdminOrdersIntegrationTests"
dotnet.exe test TenantForge.sln --nologo
```

Manual (Development): start Docker, run the API, sign in as a tenant owner,
place a couple of orders through the storefront. Then:
`GET /api/tenants/{tenantId}/shop/orders?q=<name-or-phone>`,
`?status=Paid`, `?fromUtc=&toUtc=` (percent-encode the UTC `+00:00`), and
`?pageNumber=2&pageSize=2`; `GET …/orders/{orderId}` to see the snapshot
items, totals, `paymentAttempts` and `version`. A member without
`Shop.Orders.View` gets `403`; no token gets `401`; another tenant's order id
404s identically to a missing one.

## 7. Three review questions for the learner

1. Why must the detail route return the *same* 404 body for a malformed id, a
   cross-tenant id and a missing id? What would it leak if the three cases
   returned different messages or different latencies?
2. `Shop.Orders.Manage` is registered in the catalog but enforced nowhere.
   What would break in the permission matrix / role authoring UI if it were
   **not** registered yet, versus leaving it reserved-and-unenforced?
3. The item detail reads `ShopOrderItem`'s snapshot columns instead of joining
   the live product. What goes wrong for the operator's view (and for the
   money totals) if a product's name or price changes after the order is
   placed?
