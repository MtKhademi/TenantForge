# B043 — Fulfil or cancel orders with inventory-safe transitions

Slice S39. A tenant operator (a member holding `Shop.Orders.Manage`, or the
Owner via bypass) can now move an order along exactly two legal paths —
`Paid → Fulfilled` and `PendingPayment → Cancelled` — with optimistic
concurrency, idempotent replay, and a cancel that restores reserved stock
exactly once. B042 registered `Shop.Orders.Manage` and left it reserved; this
slice is the one that enforces it.

## 1. Files changed and why

- `domain/ShopOrder.cs` — three new nullable timestamps
  (`FulfilledAtUtc`, `CancelledAtUtc`, `InventoryReleasedAtUtc`), the
  `OrderStatusAction` enum (`Fulfill`/`Cancel`), and the two mutation methods
  `TryFulfill` / `TryCancel` (each: status guard + `expectedVersion == Version`
  guard → move status → stamp timestamp → `BumpVersion`). Also
  `MarkInventoryReleased` — the exactly-once guard that stamps
  `InventoryReleasedAtUtc` only when still null.
- `domain/ShopPaymentAttempt.cs` — `Invalidate(nowUtc)`: moves an
  `Initiated` attempt to `Failed` (idempotent; already-resolved attempts
  return false), so a cancelled order's un-paid attempts can no longer be
  completed.
- `domain/ShopProductVariant.cs` — `Release(quantity)`: the exact inverse of
  `TryReserve`, used by the cancel restore.
- `domain/ShopOrderOperation.cs` (new) — the idempotency record: `TenantId`,
  `OrderId`, `Key` (the client UUID), canonical `Action`, `ResponseSnapshot`
  (JSON), `ActorId`, `CreatedAtUtc`.
- `infrastructure/ShopOrderMap.cs` — maps the three new timestamp columns.
- `infrastructure/ShopOrderOperationMap.cs` (new) — `shop_order_operations`
  with the **unique** `(tenant_id, idempotency_key)` index (the single-use
  guarantee) and a cascade FK to `shop_orders`.
- `infrastructure/ShopDbContext.cs` — the new `OrderOperations` `DbSet` +
  configuration registration.
- `infrastructure/Migrations/20260923060810_AddShopOrderOperations.{cs,Designer.cs}`
  (generated) + `ShopDbContextModelSnapshot.cs` — only the three `shop_orders`
  columns and the new table; nothing else.
- `features/orders/AdminOrderContracts.cs` — `ChangeOrderStatusRequest
  (string? Action, int ExpectedVersion)` (the Spec's exact shape; `Action`
  nullable on the wire so an absent value is a field error, not a parse
  failure).
- `features/orders/AdminOrdersFeature.cs` — the `PATCH …/status` route, a
  shared `BuildOrderDetailResponseAsync` (reused by B042's detail route so the
  two can't drift), the cancel side-effect helper, and the four new RFC 7807
  problem helpers.
- Tests: `ShopOrderOperationsIntegrationTests.cs` (new), a dedicated
  `ShopOrderOperationsDbFixture`/collection in `IamDbFixture.cs`, and
  `shop_order_operations` added to the hand-maintained `ExpectedShopTables`
  roster in `ShopModuleIntegrationTests.cs`.

## 2. Request flow, endpoint to response

`PATCH /api/tenants/{tenantId}/shop/orders/{orderId}/status`.

1. `RequireAuthorization()` — anonymous → `401` (JWT challenge).
2. `ShopAuthorization.AuthorizeTenantAccessAsync(…, OrdersManagePermission)` —
   no key (and not Owner) → `403`.
3. Parse `orderId` (malformed → generic `404`).
4. Validate `action` (`Fulfill`/`Cancel`), `expectedVersion` (`≥ 1`), and the
   `Idempotency-Key` header (present + a UUID). Any problem → `400` naming the
   field.
5. Begin a database transaction.
   1. **Lock the order row** `FOR UPDATE` (tenant-first predicate). Two
      concurrent requests for the same order — even with the same key —
      serialize here. Missing/foreign order → `404`.
   2. **Look up the operation row** by `(TenantId, Key)` (tenant-first). If
      found: same action → return its stored `ResponseSnapshot` as a `200`
      (byte-identical to the original) without re-running the transition;
      different action → `409 idempotency_key_conflict`.
   3. Apply the transition via `TryFulfill`/`TryCancel`. If it fails:
      `Version != expectedVersion` → `409 stale_version`; otherwise (no legal
      transition from the current status) → `409 invalid_order_transition`.
      Roll back, nothing changed.
   4. On a successful **cancel**: lock the referenced variant rows `FOR UPDATE`,
      restore each order item's quantity to its variant, and — only if
      `InventoryReleasedAtUtc` is still null — stamp it (exactly-once).
      Invalidate every `Initiated` payment attempt to `Failed`.
   5. Build the `AdminOrderDetailResponse`, insert the operation row (JSON
      snapshot of the response), `SaveChangesAsync`, commit, return `200`.
6. A `DbUpdateException` that is a PostgreSQL `23505` on exactly
   `ix_shop_order_operations_tenant_idempotency_key` is caught as a last-resort
   same-key race: roll back, re-read the winner's row, and answer replay or
   conflict — never a `500`.

## 3. Backend concepts introduced

- **Optimistic concurrency via a client token**: `expectedVersion` is
  compared (never trusted) against the stored `Version` *inside the aggregate*,
  and only a matching, legal transition applies — `Version` is bumped exactly
  once per successful change.
- **Idempotency keyed by a client UUID, backed by a unique constraint**: the
  same key is stored once per tenant; a replay returns the stored response
  instead of re-doing the side effect. The **unique index**, not an earlier
  `AnyAsync`, is what makes a first-call race resolve to one winner.
- **Row-lock + guarded-update for exactly-once writes**: the order and variant
  rows are locked `FOR UPDATE` across the write, and the restore is additionally
  gated on a nullable stamp — two mechanisms, each sufficient on its own.
- **Aggregate-owned transitions vs feature-owned transactions**: the domain
  methods mutate state and report success; the feature owns the transaction,
  the locks, the inventory restore and the payment-attempt invalidation.
- **Response snapshots for idempotent replay**: the returned record is
  serialized with the endpoint's own JSON options and re-parsed on replay, so a
  replay is byte-identical to the original.

## 4. Security decisions

- **Server-side authorization**: `Shop.Orders.Manage` is checked through
  `ShopAuthorization` (raw-SQL membership + role-key resolution); Owner bypass
  is preserved; the default is deny. UI visibility is never authorization.
- **Tenant isolation**: every tenant-owned query (order lock, operation
  lookup) puts `TenantId` in the first predicate; a cross-tenant order id is
  indistinguishable from a missing one (generic `404`).
- **Late payment safety**: a cancelled order's `Initiated` attempts are
  invalidated, and the callback route already rejects any non-`PendingPayment`
  order, so a late gateway callback can never move a cancelled order to `Paid`.
  There is no `Paid → Cancelled` transition, so a paid order cannot be
  cancelled or refunded until a refund slice exists.
- **No sensitive data in logs or the snapshot**: the operation row stores stable
  ids and the response; nothing here logs bearer tokens, gateway references,
  phone numbers or addresses.

## 5. Alternatives deliberately postponed

- A generic, reusable idempotency middleware/table for *all* endpoints — the
  Spec asks for this one route, so the mechanism stays local to the orders
  feature rather than becoming a platform abstraction.
- A `Shop.Contract` project — still no second .NET consumer of a Shop shape.
- Paid-order refunds, returns, carrier integration, partial fulfilment or
  cancellation, and rate limiting — each belongs to its own future slice
  (refunds to a later one, rate limiting to B046).
- Storing a structured (typed) snapshot instead of a JSON string — a JSON
  column is the simplest byte-identical replay; a typed row would add mapping
  surface this slice does not need.

## 6. Commands and manual steps

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test TenantForge.sln --nologo --filter FullyQualifiedName~ShopOrderOperationsIntegrationTests
dotnet.exe test TenantForge.sln --nologo
git diff --name-only origin/main... -- src/web   # prints nothing
```

Manual demo (with the API running):

1. Create a tenant + a single-variant product, then an order through the
   anonymous flow (it starts `PendingPayment`, `version 1`).
2. Cancel it: `PATCH …/orders/{id}/status` with
   `{"action":"Cancel","expectedVersion":1}` and a fresh
   `Idempotency-Key: <uuid>` → `200`, status `Cancelled`, `version 2`; the
   variant's stock is back to its full initial quantity.
3. Replay the same request → `200` byte-identical, no stock change, one
   `shop_order_operations` row.
4. Send the same key with `{"action":"Fulfill"…}` → `409
   idempotency_key_conflict`.
5. For a separate `Paid` order, send `{"action":"Fulfill","expectedVersion":1}`
   → `200`, status `Fulfilled`, stock untouched.

## 7. Three review questions

1. The cancel locks the order row *and* checks `InventoryReleasedAtUtc` before
   restoring stock. Which failure mode does each mechanism prevent, and what
   would happen to the stock total if only one of them existed?
2. Why is the idempotency guarantee backed by the unique `(tenant_id,
   idempotency_key)` index rather than by "check the row exists, then insert"?
   In what concrete race does the `AnyAsync`-then-insert approach go wrong?
3. A caller sends a correct `expectedVersion` but the wrong action for the
   order's current status (e.g. `Cancel` on a `Paid` order). Which of
   `stale_version` / `invalid_order_transition` does it receive, and why is
   telling those two apart useful to the frontend?
