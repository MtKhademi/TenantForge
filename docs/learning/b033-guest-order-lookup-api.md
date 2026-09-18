# B033 — Order lookup API

## 1. Files changed and why

| File | Why |
| --- | --- |
| `src/modules/shop/.../features/orders/OrderLookupContracts.cs` | Public HTTP records: `OrderLookupRequest`, `OrderLookupItemResponse`, `OrderLookupResponse`. Module-local — no `TenantForge.Modules.Shop.Contract` project exists yet, so the record stays in the owning module. |
| `src/modules/shop/.../features/orders/OrderLookupFeature.cs` | The one anonymous endpoint, `POST /api/shop/{tenantId}/orders/lookup`. One query matches (tenantId, TrackingCode, CustomerPhone) together; the shared private `NotFoundProblem()` is the single source of the not-found body. |
| `src/modules/shop/.../ShopModule.cs` | One line: `endpoints.MapOrderLookupFeature();` in the activation phase, alongside the other `Map*Feature` calls. |
| `tests/.../IamDbFixture.cs` | `ShopOrderLookupDbFixture` (dedicated `tenantforge_shop_order_lookup_tests` DB) + collection, matching the per-task-database pattern. |
| `tests/.../ShopOrderLookupIntegrationTests.cs` | 7 facts: happy path, the three-way identical-404 proof, missing-field 400s, the snapshot-isolation proof (live product renamed after the order), the paid-status reflection, a raw-SQL read-only guard, and the malformed/unknown-tenant 404s. |

No migration, no new table, no `DbContext` change: the endpoint reads B031's `shop_orders`/`shop_order_items` and B032 only changes what a lookup *reports* (the live status column).

## 2. Request flow

`POST /api/shop/{tenantId}/orders/lookup` `{trackingCode, customerPhone}` — anonymous, no `Authorization` header.

1. `TsidId.TryParse(tenantId)` fails → bare `404` (empty body). This is the routing-edge convention B027–B032 use for malformed ids: nothing was looked up, so there is no problem to report.
2. `trackingCode` and/or `customerPhone` blank → `400` `Results.ValidationProblem(errors)` naming the field(s). This is deliberately a *different* shape from the not-found — a client that sent nothing wrong can still be told what to fix.
3. One `AsNoTracking` query on `Orders` for `TenantId == tenantTsid && TrackingCode == code && CustomerPhone == phone`. All three values are in the predicate, in one round trip.
4. No row → the single `NotFoundProblem()`: `Results.Problem(title: "Order not found", detail: "No order was found for this tracking code and phone number.", statusCode: 404)`.
5. Row found → one projected `AsNoTracking` query over `OrderItems` for that order id (snapshot fields only) → `200` with the `OrderLookupResponse`: the **live** `Status` column plus the snapshot item lines, address, and the stored totals.

No transaction, no `SaveChanges` anywhere in the handler — the endpoint is read-only.

## 3. Backend concepts introduced

- **Enumeration-closure by pairing**: a secret a human has to type later must be long *or* must be paired with something only the real customer knows. B031's `TrackingCode` is 12 chars from a 32-symbol alphabet (unguessable on its own, but short and error-prone for people), so S30 closes the enumeration path by also requiring the phone number the order was placed under — no account, no login, matching the Shop module's guest-first design.
- **One query, not two**: resolving by tracking code first and comparing the phone in .NET would be observable — response shape and timing would differ between "no such code" and "wrong phone". One composite predicate makes both failures the same database result: no row.
- **One shared error factory**: `NotFoundProblem()` is a private static used by every not-found branch. Reusing the method (instead of rebuilding `Results.Problem(...)` inline per branch) is what makes "the two 404s are the same body" a code-level guarantee rather than a copy-paste habit.
- **Snapshot vs. live data in one response**: `Status`, `OrderNumber`, address, and totals come from the order row *as it is now*; `ProductNameSnapshot`/`VariantLabelSnapshot` come from the item rows written at order time. The lookup never joins the live catalog, so a later `PUT products/{id}` rename (B026's wholesale update) cannot change what a past order shows.
- **`AsNoTracking` twice**: both queries are read-only projections; tracking entities would only make EF maintain change-state for rows nothing in the handler will save.
- **Bare `Results.NotFound()` vs. `Results.Problem(...)`**: the module already uses the plain 404 for "malformed id at the routing edge" and the richer problem body for "lookup ran and found nothing". This endpoint uses both, and the tests pin the difference (empty body vs. the shared problem JSON).

## 4. Important security decisions

- **Never sufficient alone**: tracking code without phone (or phone without code) cannot resolve an order — the predicate requires both. A caller can only ever get a 404 or a 400 from guessing, never partial order data.
- **One identical not-found body**: wrong phone, fabricated code, another tenant's code, and an unknown well-formed tenant all return the same `ProblemDetails` (only the per-request `traceId` differs, which ASP.NET Core appends to every problem response). No branch may ever say "that code exists, wrong phone" — that phrasing itself would leak that the code was real.
- **Tenant-scoped by the route segment**: the query carries `TenantId == tenantTsid`, so the same tracking code + phone pair is a 404 under any other tenant — the cross-tenant fact locks this down.
- **Read-only, verified in the database**: the read-only fact takes raw-SQL counts of `shop_orders`, `shop_order_items`, and `shop_payment_attempts` before and after a successful *and* a failed lookup and asserts the order row is still `PendingPayment`. The endpoint cannot advance an order just because someone asked about it.
- **No secrets in responses**: the response exposes the order number, status, address, and totals the guest supplied — nothing derived from the tracking code's key space or the customer name is added.
- **No rate limiting / CAPTCHA by explicit non-goal**: the slice names stronger anti-enumeration hardening as a separate later task; adding it here would be speculative infrastructure.

## 5. Alternatives deliberately postponed

- **GET with query parameters** — rejected by the slice: a tracking code + phone number pair must not end up in server access logs, browser history, or a shareable URL. `POST` keeps it in the body.
- **Account-based tracking** — the entire Shop flow is guest-first (S27's non-goals); introducing login for "easier" tracking would break the product's design, not improve it.
- **Order actions from the tracking page** (cancel, re-order, edit address) — non-goal; the endpoint is a read and the response has no action affordance.
- **Rate limiting / CAPTCHA / exponential backoff** — no demonstrated need; a later task if real abuse shows up.
- **A `Shop.Contract` project for the lookup records** — admission rule not met: no second module or consumer references these types yet, so they stay module-local like every other Shop record.
- **Searching by order number instead** — an order number (`ORD-260918-6687`) is a smaller, more predictable namespace than the tracking code and would reopen the enumeration path it was designed to close.

## 6. How to verify

```bash
# build + full suite (dotnet.exe on this machine; no Linux dotnet CLI)
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo --filter "FullyQualifiedName~ShopOrderLookupIntegrationTests"
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

Manual (live API on port 5080; an order was placed through B026/B028/B031 first):

```bash
# correct pair
curl -X POST http://<host>:5080/api/shop/<tenantId>/orders/lookup \
  -H "Content-Type: application/json" \
  -d '{"trackingCode":"<trackingCode>","customerPhone":"09121234567"}'
# → 200 with orderNumber, status, shipping address, totals, items[] (snapshot fields)

# real code, wrong phone
# → 404 {"title":"Order not found","detail":"No order was found for this tracking code and phone number.",...}

# fabricated code
# → 404 — the body is identical to the wrong-phone one (only traceId differs)
```

Observed in this delivery: focused facts 7/7, full suite 184/184 (177 pre-existing + 7 new). The live demo created order `ORD-260918-8255` (tracking `R3WKQKNGEQEW`, phone `09121234567`) and exercised all nine checks — including paying the order through B032 (lookup then reports `Paid`) and renaming the live product through B026's admin `PUT` (lookup still reports the snapshot name `Shirt`).

## 7. Three review questions

1. Why does the not-found comparison in `ShopOrderLookupIntegrationTests` canonicalize the bodies to a string (after stripping `traceId`) instead of asserting `JsonNode` equality directly? What does ASP.NET Core append to every `ProblemDetails` response that makes raw body equality flaky?
2. The malformed-tenant branch returns a bare `Results.NotFound()` (empty body) while every lookup-miss branch returns the shared `ProblemDetails` body. Defend this asymmetry: is it safe for an attacker to tell "the tenant id was malformed" apart from "the lookup found nothing", and why does the code treat these as different moments?
3. The happy-path response mixes live data (`Status`) with snapshot data (`ProductNameSnapshot`). If a later task added "refund" or "re-ship" actions reachable from this endpoint, which fields would you trust for the decision, and which would you refuse to trust — and what would that say about where state changes belong in this module?
