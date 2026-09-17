---
id: B033
slice: S30
title: Order lookup API
agent: backend-mentor
source: tasks/slices/030-guest-order-tracking.md
---

# Objective

Add `POST /api/shop/{tenantId}/orders/lookup`: an unauthenticated
endpoint that requires both a tracking code and a phone number together
(never tracking code alone) and returns that order's current status,
items and totals when the pair matches, or a plain, generic not-found
response otherwise.

# Context

Read `tasks/slices/030-guest-order-tracking.md` completely, in particular
its explicit security reasoning for requiring both values together and
for returning one generic not-found response regardless of which half
was wrong. Read B031's delivered `domain/ShopOrder.cs`/`ShopOrderItem.cs`
— this endpoint reads them, it defines no new table.

# Scope — every file, in order

## 1. Request/response records

**`src/modules/shop/TenantForge.Modules.Shop/features/orders/OrderLookupContracts.cs`:**

```csharp
namespace TenantForge.Modules.Shop.Features.Orders;

public sealed record OrderLookupRequest(string? TrackingCode, string? CustomerPhone);

public sealed record OrderLookupItemResponse(string ProductNameSnapshot, string VariantLabelSnapshot, decimal UnitPrice, int Quantity);

public sealed record OrderLookupResponse(
    string OrderNumber,
    string Status,
    DateTimeOffset CreatedAtUtc,
    string ShippingProvince,
    string ShippingCity,
    string ShippingAddressLine,
    string ShippingPostalCode,
    decimal SubTotal,
    decimal ShippingCost,
    decimal DiscountAmount,
    decimal GrandTotal,
    IReadOnlyList<OrderLookupItemResponse> Items);
```

## 2. `src/modules/shop/TenantForge.Modules.Shop/features/orders/OrderLookupFeature.cs`

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Orders;

internal static class OrderLookupFeature
{
    public static IEndpointRouteBuilder MapOrderLookupFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/shop/{tenantId}/orders/lookup", async (
            string tenantId,
            OrderLookupRequest request,
            ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();

            var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(request.TrackingCode))
            {
                errors["trackingCode"] = ["Tracking code is required."];
            }

            if (string.IsNullOrWhiteSpace(request.CustomerPhone))
            {
                errors["customerPhone"] = ["Phone number is required."];
            }

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var trackingCode = request.TrackingCode!.Trim();
            var customerPhone = request.CustomerPhone!.Trim();

            // Exact match on BOTH values together, in one query — never
            // resolve by trackingCode alone and compare the phone
            // separately, which would make it observable (by response
            // timing or shape) which half was wrong.
            var order = await db.Orders.AsNoTracking()
                .SingleOrDefaultAsync(order =>
                    order.TenantId == tenantTsid
                    && order.TrackingCode == trackingCode
                    && order.CustomerPhone == customerPhone);

            if (order is null)
            {
                return NotFoundProblem();
            }

            var items = await db.OrderItems.AsNoTracking()
                .Where(item => item.OrderId == order.Id)
                .Select(item => new OrderLookupItemResponse(
                    item.ProductNameSnapshot, item.VariantLabelSnapshot, item.UnitPrice, item.Quantity))
                .ToListAsync();

            var response = new OrderLookupResponse(
                order.OrderNumber,
                order.Status.ToString(),
                order.CreatedAtUtc,
                order.ShippingProvince,
                order.ShippingCity,
                order.ShippingAddressLine,
                order.ShippingPostalCode,
                order.SubTotal,
                order.ShippingCost,
                order.DiscountAmount,
                order.GrandTotal,
                items);

            return Results.Ok(response);
        });

        return endpoints;
    }

    /// <summary>
    /// The one, identical not-found shape for both "tracking code does not
    /// exist" and "tracking code exists, phone does not match" — reused
    /// exactly, never rebuilt inline, so the two cases can never drift into
    /// two different bodies by accident.
    /// </summary>
    private static IResult NotFoundProblem() => Results.Problem(
        title: "Order not found",
        detail: "No order was found for this tracking code and phone number.",
        statusCode: StatusCodes.Status404NotFound);
}
```

## 3. Wire the feature into the composition seam

Edit `src/modules/shop/TenantForge.Modules.Shop/ShopModule.cs`. Add this
one line inside `MapShopModule`, alongside the existing `Map*Feature`
calls:

```csharp
        endpoints.MapOrderLookupFeature();
```

Add the matching `using` (if `TenantForge.Modules.Shop.Features.Orders`
is not already imported from B031's `OrderCreationFeature`, it already
is — this file lives in the same namespace, so no new `using` line is
needed).

# Non-goals

- No account/login of any kind.
- No rate-limiting/CAPTCHA.
- No order mutation (cancel, edit address, etc.) from this endpoint — it
  is read-only.

# If you get stuck

```bash
curl -X POST http://localhost:5080/api/shop/<tenantId>/orders/lookup \
  -H "Content-Type: application/json" \
  -d '{"trackingCode":"<trackingCode>","customerPhone":"09121234567"}'
```

Expected: `200 OK` with the full `OrderLookupResponse` shape (status,
order number, address, totals, items with their snapshot fields).

```bash
curl -X POST http://localhost:5080/api/shop/<tenantId>/orders/lookup \
  -H "Content-Type: application/json" \
  -d '{"trackingCode":"<trackingCode>","customerPhone":"09000000000"}'
```

Expected: `404` with
`{"title":"Order not found","detail":"No order was found for this tracking code and phone number.",...}`
— run the same call with a completely made-up `trackingCode` and confirm
the body is byte-for-byte identical (same title, same detail, same
status code).

# Acceptance

- A correct tracking-code + phone pair returns the order's status, items
  (from the snapshot fields) and totals.
- A real tracking code with a mismatched phone number returns the exact
  same generic not-found response as a wholly nonexistent tracking code.
- A request missing either field returns a normal validation error,
  distinct from the not-found response.
- The response never includes a live catalog lookup — verified by an
  integration test that edits the product's name via B026's admin API
  after the order was placed and confirms the lookup response still shows
  the original `ProductNameSnapshot`.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover the happy-path lookup, the mismatched-phone
vs. nonexistent-code identical-response proof, the missing-field
validation error, and the snapshot-isolation proof described above. The
full existing IAM suite continues to pass unmodified.

Manual: the `curl` sequence under "If you get stuck" above.

# Lifecycle

Add row `B033` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B032`, and Spec link
`tasks/backend/B033-guest-order-lookup-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/030-guest-order-tracking.md` is the permanent
record and is never deleted.
