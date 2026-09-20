# B042 — Expose tenant order list and detail for operators

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S38`.
- Depends on: `B035, B033`.
- Immediate browser consumer: `F060`. Do not widen this API for an unnamed future screen.
- Visible outcome: Authorized tenant operators list and inspect guest orders without using the public tracking secret.

## Read before editing

Read `AGENTS.md`, the `B042` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/038-shop-admin-orders.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Current baseline is commit `34dc44e`: Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

order domain/map, order admin contracts/feature, authorization/catalog, module mapping, one migration for `Version`, integration tests, SHOP handbook and learning note.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

| Method | Route | Auth | Result |
        |---|---|---|---|
        | GET | `/api/tenants/{tenantId}/shop/orders?pageNumber=&pageSize=&status=&q=&fromUtc=&toUtc=` | JWT + `Shop.Orders.View` | paged summaries |
        | GET | `/api/tenants/{tenantId}/shop/orders/{orderId}` | same | full detail |

All errors use the repository's existing Minimal API/RFC7807 shapes. Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

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

The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O. Use `TimeProvider` where this task adds time-dependent behavior.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

View/Manage/Owner/no permission matrix; tenant isolation; all filters and invalid values; stable pagination; snapshot accuracy after catalog edit; foreign ID 404; attempts bounded; anonymous 401.

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body give `F060` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

## Validation

1. `dotnet build TenantForge.sln --nologo`
2. Targeted Shop integration test class.
3. Full `dotnet test TenantForge.sln --nologo` (or the repository's documented Windows `dotnet.exe` equivalent).
4. Inspect the generated migration for only intended schema changes.
5. Verify `docs/modules/SHOP.md` against routes/entities/config/auth/tests and update the Bxxx learning note.

## Non-goals

Status mutation, export, customer account, invoice PDF, refund or shipment tracking provider.

## Acceptance checklist

- [ ] The visible outcome works through `F060` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
