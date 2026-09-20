# B042 — Expose tenant order list and detail for operators

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S38`.
- Depends on: `B035, B033`.
- Immediate browser consumer: `F060`. Do not widen this API for an unnamed future screen.
- Visible outcome: Authorized tenant operators list and inspect guest orders without using the public tracking secret.

## Do this in order

Follow this checklist top to bottom. It restates every requirement below as
literal steps. The prose sections after it are the same requirements in
full — read them too, they are not optional extra scope, just the same
facts written a different way. Before step 1, follow the branch-naming,
ledger-update and approval-gate rules already described in `AGENTS.md` and
in the "Ownership and dependency" section above (do not repeat them here,
just follow them).

1. Read `AGENTS.md`, the `B042` row in `tasks/TASKS.md`, this whole Spec
   file, `docs/modules/SHOP.md` (only if it already exists), and
   `tasks/slices/038-shop-admin-orders.md`. Also read the matching section
   of `docs/design/shop/http-contracts.md` — you will update this section
   later, before your Spec file is deleted at delivery.
2. Confirm the current code still matches this baseline (commit `34dc44e`):
   Shop is one module project, EF entities are internal, IDs are TSIDs
   (TSID = "a sortable numeric string ID — see `TenantForge.BuildingBlocks`"),
   migration history is tracked in a separate table, IAM membership/role
   checks use raw SQL, storefront/cart/checkout/order/payment/lookup routes
   are anonymous, and integration tests live in files named
   `Shop*IntegrationTests.cs`. Do not change any of these conventions —
   this Spec does not ask you to.
3. Add the permission key `Shop.Orders.View` to the module's permission
   catalog. Concretely: add an `OrdersViewPermission` constant to
   `src/modules/shop/TenantForge.Modules.Shop/features/authorization/ShopAuthorization.cs`
   next to the existing `CatalogManagePermission` (`Shop.Catalog.Manage`) and
   `ShippingManagePermission` (`Shop.Shipping.Manage`), add it to that class's
   `KnownKeys` set, and contribute it from `ShopPermissionCatalogContributor.cs`
   the same way the other two are contributed. This backend task owns the
   server-side keys; the matching TypeScript constants are added by the
   frontend task `F050`.
4. In the same three places, also add `Shop.Orders.Manage`. Do not
   enforce or check `Shop.Orders.Manage` anywhere in this task — it is
   reserved for task `B043`. Leave the existing "Owner" role bypass
   (owners pass every permission check) exactly as it already works.
5. Add a new property `Version` (type `int`, default value `1`) to the
   `ShopOrder` entity. This is an optimistic-concurrency token: it lets a
   later task (`B043`) detect when two requests try to change the same
   order at once. `B043` needs it, and `F060` (the list/detail screen) must
   already be returning it before `F061` (the action screen) can use it, so
   add it now even though this task does not mutate orders.
6. Generate one EF migration (EF migration = "a generated script that
   changes the database schema; create it by running
   `dotnet ef migrations add <Name>` inside the Shop module project") for
   the new `Version` column only. Give it a clear name such as
   `AddShopOrderVersion`. After generating it, open the migration file and
   confirm it only adds that one column — no unrelated schema changes.
7. Add a new Minimal API endpoint:
   `GET /api/tenants/{tenantId}/shop/orders?pageNumber=&pageSize=&status=&q=&fromUtc=&toUtc=`.
   Require a valid JWT and the `Shop.Orders.View` permission. Anonymous
   requests must get `401`.
8. Implement the query exactly as described in "Required implementation"
   step 2 below:
   - Filter by the route's `tenantId` first, before any other filter.
   - If `q` is present, it must be at most 100 characters. Trim it first.
     Match it with a case-insensitive "contains" comparison against all four
     of: order number, tracking code, customer phone, customer name — a row
     matches if any one of the four contains `q`. There is no shared text-search
     helper in this repository today, so do not go looking for one and do not
     create a general-purpose one; write the `Where` clause inline in this
     endpoint. Let EF Core translate it to parameterized SQL; never build SQL
     by string concatenation.
   - If `status` is present, it must be one of the defined order-status
     enum values; reject anything else with the repo's standard validation
     error.
   - If `fromUtc`/`toUtc` are present, treat them as UTC, inclusive start,
     exclusive end, and reject a range spanning more than 366 days.
   - Sort results by `CreatedAtUtc desc, Id desc` — always, so pagination is
     stable across pages.
9. Use the existing pagination helper already used elsewhere in the
   codebase to bound `pageNumber`/`pageSize` and build `PaginationMetadata`.
   Do not write a new pagination mechanism.
10. Define the response records exactly as shown in "Required code shape"
    below: `AdminOrderSummaryResponse`, `AdminOrderListResponse`,
    `AdminOrderDetailResponse`. Copy the field names and types exactly.
11. Map each `ShopOrder` row to `AdminOrderSummaryResponse` with: `Id`,
    `OrderNumber`, `CustomerName`, `CustomerPhone`, `Status` (as a string),
    `GrandTotal`, `CreatedAtUtc`. Wrap the list plus `PaginationMetadata`
    in `AdminOrderListResponse`.
12. Add a second Minimal API endpoint:
    `GET /api/tenants/{tenantId}/shop/orders/{orderId}`. Same auth
    requirement as step 7 (JWT + `Shop.Orders.View`).
13. Build `AdminOrderDetailResponse` with: `Id`, `OrderNumber`,
    `TrackingCode`, `Status`, `Customer`, `Totals`, `Items`,
    `PaymentAttempts`, `Version`, `CreatedAtUtc`.
    - `Customer` is an `AdminOrderCustomerResponse` and `Totals` is an
      `AdminOrderTotalsResponse`. These two record types are not fully
      spelled out in this Spec's code shape — the rule for filling them in
      is: if the codebase already has response records used by the
      anonymous order-lookup/tracking endpoints that carry customer and
      totals data, copy their exact field lists into these new `Admin`
      records (same fields, just renamed with the `Admin` prefix so the
      route is unambiguous). If no such existing records are found, use
      `AdminOrderCustomerResponse(string Name, string Phone, string
      ShippingAddress)` and `AdminOrderTotalsResponse(decimal Subtotal,
      decimal ShippingTotal, decimal TaxTotal, decimal GrandTotal)` as the
      safe default — do not invent additional fields beyond these.
    - `Items` reuses the existing `OrderLookupItemResponse` type (already
      used by the anonymous lookup feature). Item values must come from the
      snapshot stored on the order at checkout time — never join to live
      product name/price data, since a product's current name or price may
      have changed since the order was placed.
    - `PaymentAttempts` is a list of `AdminPaymentAttemptResponse`
      (payment attempt summaries). Same rule as `Customer`/`Totals` above:
      reuse an existing payment-attempt response shape if the codebase
      already has one for a similar admin/lookup view; otherwise use a
      minimal `AdminPaymentAttemptResponse(string Id, string Status,
      DateTimeOffset CreatedAtUtc)`. Return only the 20 newest attempts per
      order (see step 15).
14. For a malformed order ID, a well-formed ID belonging to a different
    tenant, or a missing order ID, return the exact same generic `404`
    response after the authorization check runs — do not let timing or
    response-body differences reveal which case it was. Do not call into,
    or reuse validation from, the anonymous public tracking-code lookup
    code path.
15. Cap `PaymentAttempts` at the 20 newest attempts per order. Document in a
    code comment that this cap is temporary until a later payment-lifecycle
    task enforces a smaller cap.
16. Apply the "Security and transaction rules" section below to both
    endpoints: put `TenantId` first in every database predicate; never take
    `TenantId`, totals, prices, stock deltas, permission keys, or payment
    success from client input when the server already knows them; thread a
    `CancellationToken` (an object that lets a request be cancelled if the
    client disconnects — pass it through every new async database/I/O call)
    through all new async calls; use `TimeProvider` (the repo's
    injectable clock, instead of calling `DateTime.UtcNow` directly) if you
    add any time-dependent logic; never log credentials, bearer tokens,
    phone numbers, tracking codes, coupon codes, gateway authority values,
    or customer addresses — only stable IDs and reason codes.
17. Write the integration tests listed in "Integration tests required"
    below, one test (or a small group of tests) per bullet. Put them in the
    closest existing `Shop*IntegrationTests.cs` file, or create a new file
    named after this feature if none fits. Use the real PostgreSQL test
    fixture already used by other Shop tests. Assert on response bodies and
    on what was actually persisted — a test that only checks the HTTP
    status code is not sufficient.
18. Update the matching section of `docs/design/shop/http-contracts.md` so
    it exactly matches the routes, records, nullability, enums, and problem
    codes you actually delivered.
19. Update `docs/modules/SHOP.md` (create it if this is the first task to
    touch it) so it matches the routes/entities/config/auth/tests you
    delivered, and write or update the `docs/learning/<task-id>-<slug>.md`
    learning note.
20. Run every command in "Validation" below, in order, and fix anything
    that fails before asking for approval.
21. Go through "Acceptance checklist" below and confirm each box with real
    evidence (test output, screenshots via `F060`, etc.) before requesting
    final approval.

## Read before editing

Read `AGENTS.md`, the `B042` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/038-shop-admin-orders.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Baseline as of commit `34dc44e` (later commits changed only `src/web/**` and
`tasks/**`, so this still describes the backend you will find): Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

order domain/map, order admin contracts/feature, authorization/catalog, module mapping, one migration for `Version`, integration tests, SHOP handbook and learning note.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

| Method | Route | Auth | Result |
|---|---|---|---|
| GET | `/api/tenants/{tenantId}/shop/orders?pageNumber=&pageSize=&status=&q=&fromUtc=&toUtc=` | JWT + `Shop.Orders.View` | paged summaries |
| GET | `/api/tenants/{tenantId}/shop/orders/{orderId}` | same | full detail |

All errors use the repository's existing Minimal API/RFC7807 shapes (RFC7807 = "the repo's standard JSON error body shape for HTTP errors — reuse the existing helper, do not invent a new error format"). Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation

1. Add permissions `Shop.Orders.View` and `Shop.Orders.Manage`; this task enforces View only and reserves Manage for B043. Owner bypass remains.
2. List filters by tenant before all other predicates. `q<=100` exact/contains matches normalized order number, tracking code, phone or customer name. Status must be a defined enum. Date range is UTC, inclusive start/exclusive end, max 366 days. Stable order `CreatedAtUtc desc, Id desc`.
3. Add `Version int` (default 1) to `ShopOrder` now because the immediately dependent B043 mutation requires an optimistic token and F060 must receive it before F061 binds B043 actions. Summary: order ID/number, customer name/phone, status, grand total, created time. Detail includes Version, address, totals, item snapshots and payment attempt summaries; never joins live product names/prices.
4. Return generic 404 for malformed/foreign/missing order after authorization. Do not expose public lookup behavior or tracking-code validation through timing/body differences.
5. Add bounded pagination through existing helper; payment attempts are bounded by a documented maximum of 20 newest attempts per order until the payment lifecycle task enforces a smaller cap.

## Required code shape

```csharp
public sealed record AdminOrderSummaryResponse(
    string Id, string OrderNumber, string CustomerName, string CustomerPhone,
    string Status, decimal GrandTotal, DateTimeOffset CreatedAtUtc);
public sealed record AdminOrderListResponse(
    IReadOnlyList<AdminOrderSummaryResponse> Orders, PaginationMetadata Pagination);
public sealed record AdminOrderDetailResponse(
    string Id, string OrderNumber, string TrackingCode, string Status,
    AdminOrderCustomerResponse Customer, AdminOrderTotalsResponse Totals,
    IReadOnlyList<OrderLookupItemResponse> Items,
    IReadOnlyList<AdminPaymentAttemptResponse> PaymentAttempts,
    int Version, DateTimeOffset CreatedAtUtc);
```

`AdminOrderCustomerResponse`, `AdminOrderTotalsResponse`, and
`AdminPaymentAttemptResponse` are not spelled out above because the
original Spec left their exact fields to be completed from existing
codebase conventions. Step 13 of the "Do this in order" checklist gives
the exact rule and safe-default field lists to use — follow it instead of
inventing your own shapes. `OrderLookupItemResponse` already exists in the
codebase (used by the anonymous lookup feature) — reuse it as-is.

The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O. Use `TimeProvider` where this task adds time-dependent behavior.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

Write one test (or small group of tests) per scenario below. Each must assert on response bodies and persisted data, not status code alone.

- [ ] Write a test for each of View/Manage/Owner/no-permission, then assert the correct status code and body for each.
- [ ] Write a test that requests another tenant's orders, then assert they never appear and a cross-tenant order ID 404s.
- [ ] Write a test for every filter (`q`, `status`, `fromUtc`/`toUtc`) with both valid and invalid values, then assert correct filtering or the correct validation error.
- [ ] Write a test that pages through results twice with the same filters, then assert the ordering and page contents are stable.
- [ ] Write a test that edits a product's name/price after an order is placed, then fetches that order's detail, then assert the snapshot values are unchanged.
- [ ] Write a test that requests a malformed or missing order ID, then assert a generic `404`.
- [ ] Write a test with more than 20 payment attempts on one order, then assert only the 20 newest are returned.
- [ ] Write a test with no auth token, then assert `401`.

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body give `F060` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

## Validation

Run these from the repository root, in this order, and fix every failure
before moving to the next command.

On the reference WSL setup there is no Linux `dotnet` binary — use `dotnet.exe`
instead of `dotnet` in every command below. See
`docs/architecture.md#local-development-environment-wsl--windows-net-sdk`.

The integration tests start PostgreSQL through Testcontainers, so Docker must
be running before you run any test command. Start it with `docker compose up -d postgres`
if Docker Desktop is not already up (the compose service is not what the tests
connect to, but it confirms the Docker daemon is reachable).

1. Build everything:

   ```bash
   dotnet build TenantForge.sln --nologo
   ```

2. Run only this task's Shop integration tests first (replace
   `<ShopTestClass>` with the exact class name you added or extended, for
   example `ShopCatalogAdminIntegrationTests`):

   ```bash
   dotnet test TenantForge.sln --nologo --filter FullyQualifiedName~<ShopTestClass>
   ```

3. Run the full test suite and confirm it is green:

   ```bash
   dotnet test TenantForge.sln --nologo
   ```

4. Open the migration file you generated under
   `src/modules/shop/TenantForge.Modules.Shop/infrastructure/Migrations/` and
   read it line by line. Confirm it contains only the schema changes this Spec
   asked for and nothing else. Confirm `ShopDbContextModelSnapshot.cs` was
   updated in the same change.

5. Re-read `docs/modules/SHOP.md` and check every routes/entities/config/auth/tests
   statement against the code you actually delivered. Then finish the
   `docs/learning/B042-<slug>.md` learning note.

6. Confirm the frontend was not touched:

   ```bash
   git diff --name-only origin/main... -- src/web
   ```

   This must print nothing.

## Non-goals

Status mutation, export, customer account, invoice PDF, refund or shipment tracking provider.

## Completion report

When the task is finished, report exactly these six things — no more, no less.
Do not skip a heading because you think it is obvious.

1. **Files changed.** The full list of paths you created, edited or deleted,
   grouped as: production code, EF migration (generated), tests,
   documentation. Say which files are generated rather than hand-written.
2. **Implementation decisions.** Every decision this Spec left to you, with
   the option you picked and one sentence of why. If you followed an "if
   unsure, do X" default from this Spec, say so and name it.
3. **Commands executed.** Every command from "Validation" above, copied
   verbatim in the order you ran them.
4. **Results of those checks.** For each command: pass or fail, and for the
   test commands the actual passed/failed/skipped counts. If you had to re-run
   something after a fix, say that and give the final result. Never report a
   command as passing if you did not run it.
5. **Risks, blockers and follow-up.** Anything you could not verify, any
   scenario from "Integration tests required" you could not cover and why, any
   contract detail that differed from this Spec, and anything the next task
   (F060) must know. Write "None." if there is genuinely nothing.
6. **Documentation impact statement.** The exact line
   `SHOP.md impact: <what you updated>` or
   `SHOP.md impact: none — <specific reason>`, plus the same line for
   `IAM.md`, `BuildingBlocks docs` and `IAM Contract docs` if your diff touched
   any of them (see `AGENTS.md`). A vague "docs not needed" is not accepted.

## Acceptance checklist

- [ ] The visible outcome works through `F060` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
