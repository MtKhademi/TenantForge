---
id: B030
slice: S28
title: Checkout API
agent: backend-mentor
source: tasks/slices/028-shop-checkout.md
---

# Objective

Add `POST /api/shop/{tenantId}/checkout/summary`: given a cart id, a
shipping address and an optional coupon code, validate the coupon and the
shipping province and return a priced checkout summary — without creating
an order or any other persisted record.

# Context

Read `tasks/slices/028-shop-checkout.md` completely, in particular the
explicit rule that an unshippable province must produce a plain, distinct
"not shippable here" response, never a silent zero shipping cost. Read
B028's delivered `features/carts/CartsFeature.cs` (specifically its
`BuildCartResponseAsync` helper — this task computes the subtotal the
same way) and B029's delivered `domain/ShopShippingRate.cs`/
`domain/ShopCoupon.cs`.

This task defines no new table and no new domain type — it only reads
existing ones.

# Scope — every file, in order

## 1. Request/response records

**`src/modules/shop/TenantForge.Modules.Shop/features/checkout/CheckoutContracts.cs`:**

```csharp
namespace TenantForge.Modules.Shop.Features.Checkout;

public sealed record CheckoutSummaryRequest(
    string? CartId,
    string? ShippingProvince,
    string? ShippingCity,
    string? ShippingAddressLine,
    string? ShippingPostalCode,
    string? CouponCode);

public sealed record CheckoutSummaryResponse(
    decimal SubTotal,
    decimal DiscountAmount,
    decimal ShippingCost,
    decimal GrandTotal);
```

## 2. `src/modules/shop/TenantForge.Modules.Shop/features/checkout/CheckoutFeature.cs`

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Checkout;

internal static class CheckoutFeature
{
    public static IEndpointRouteBuilder MapCheckoutFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/shop/{tenantId}/checkout/summary", async (
            string tenantId,
            CheckoutSummaryRequest request,
            ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(request.CartId, out var cartTsid)) return Results.NotFound();

            var cartExists = await db.Carts.AnyAsync(cart => cart.Id == cartTsid && cart.TenantId == tenantTsid);
            if (!cartExists) return Results.NotFound();

            var subTotal = await db.CartItems.AsNoTracking()
                .Where(item => item.CartId == cartTsid)
                .SumAsync(item => item.UnitPriceSnapshot * item.Quantity);

            if (subTotal <= 0) return Results.NotFound();

            var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            decimal shippingCost = 0;
            if (string.IsNullOrWhiteSpace(request.ShippingProvince))
            {
                errors["shippingProvince"] = ["Shipping province is required."];
            }
            else
            {
                var province = request.ShippingProvince.Trim();
                var rate = await db.ShippingRates.AsNoTracking()
                    .SingleOrDefaultAsync(rate => rate.TenantId == tenantTsid && rate.ProvinceName == province);
                if (rate is null)
                {
                    errors["shippingProvince"] = ["This tenant does not ship to the selected province."];
                }
                else
                {
                    shippingCost = rate.Cost;
                }
            }

            decimal discountAmount = 0;
            if (!string.IsNullOrWhiteSpace(request.CouponCode))
            {
                var normalizedCode = request.CouponCode.Trim().ToUpperInvariant();
                var coupon = await db.Coupons.AsNoTracking()
                    .SingleOrDefaultAsync(coupon => coupon.TenantId == tenantTsid && coupon.NormalizedCode == normalizedCode);

                if (coupon is null || !coupon.IsActive || coupon.ExpiresAtUtc < DateTimeOffset.UtcNow)
                {
                    errors["couponCode"] = ["This coupon code is not valid."];
                }
                else
                {
                    discountAmount = coupon.DiscountType == ShopDiscountType.Percentage
                        ? Math.Round(subTotal * coupon.DiscountValue / 100m, 2)
                        : Math.Min(coupon.DiscountValue, subTotal);
                }
            }

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var grandTotal = subTotal - discountAmount + shippingCost;
            return Results.Ok(new CheckoutSummaryResponse(subTotal, discountAmount, shippingCost, grandTotal));
        });

        return endpoints;
    }
}
```

## 3. Wire the feature into the composition seam

Edit `src/modules/shop/TenantForge.Modules.Shop/ShopModule.cs`. Add this
one line inside `MapShopModule`, alongside the existing `Map*Feature`
calls:

```csharp
        endpoints.MapCheckoutFeature();
```

Add the matching `using`:

```csharp
using TenantForge.Modules.Shop.Features.Checkout;
```

# Non-goals

- No order/payment creation (B031/B032).
- No coupon usage-count or minimum-order-value rule beyond active/expiry.

# If you get stuck

```bash
curl -X POST http://localhost:5080/api/shop/<tenantId>/checkout/summary \
  -H "Content-Type: application/json" \
  -d '{
        "cartId": "<cartId>",
        "shippingProvince": "تهران",
        "shippingCity": "تهران",
        "shippingAddressLine": "خیابان ولیعصر",
        "shippingPostalCode": "1234567890",
        "couponCode": "WELCOME10"
      }'
```

Expected (using the shipping rate and coupon created in B029's manual
test, and a cart with one item priced 890000): `200 OK` with
`{"subTotal":890000,"discountAmount":89000,"shippingCost":50000,"grandTotal":851000}`.

Try the same call with `"shippingProvince": "خارج از پوشش"` (a province
with no configured rate): expect `400` with
`{"errors":{"shippingProvince":["This tenant does not ship to the selected province."]}}`.

# Acceptance

- A valid cart, shippable province and no coupon returns the correct
  `subTotal`/`shippingCost`/`grandTotal` with `discountAmount = 0`.
- A valid coupon reduces `grandTotal` by the correct amount for both
  `Percentage` and `FixedAmount` types.
- An expired or inactive coupon code returns a clear validation error, not
  a summary that silently ignores it.
- An unshippable province returns a clear validation error, never a `200`
  with a zero shipping cost.
- An empty or nonexistent cart returns `404`, never a summary with a zero
  subtotal that looks like a valid empty order.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover: valid summary without a coupon, valid summary
with a percentage coupon, valid summary with a fixed-amount coupon capped
at the subtotal, expired-coupon rejection, inactive-coupon rejection,
unshippable-province rejection, and empty-cart rejection. The full
existing IAM suite continues to pass unmodified.

Manual: the `curl` sequence under "If you get stuck" above.

# Lifecycle

Add row `B030` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependencies `B028, B029`, and Spec link
`tasks/backend/B030-checkout-summary-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/028-shop-checkout.md` is the permanent record and
is never deleted.
