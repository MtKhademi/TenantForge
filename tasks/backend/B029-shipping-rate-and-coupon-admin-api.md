---
id: B029
slice: S28
title: Shipping-rate and coupon admin API
agent: backend-mentor
source: tasks/slices/028-shop-checkout.md
---

# Objective

Give an authenticated tenant admin endpoints to set/list per-province
shipping cost rows and to create/list/deactivate coupons, under the same
`/api/tenants/{tenantId}/shop/...` admin prefix and tenant-membership
authorization B026 already established.

# Context

Read `tasks/slices/028-shop-checkout.md` completely. Read B026's
delivered `src/modules/shop/TenantForge.Modules.Shop/features/authorization/ShopAuthorization.cs`
and `features/categories/CategoriesFeature.cs` — this task reuses
`ShopAuthorization.AuthorizeTenantAccessAsync` exactly as-is and follows
the same endpoint style (no new authorization concept).

This task's ledger dependency is `B026` only (not `B028`), so it may be
implemented before or after B028 — do not assume `CartsFeature` exists
when editing `ShopModule.cs` in step 5 below; the instructions there are
written to work either way.

# Scope — every file, in order

## 1. Domain entities

**`src/modules/shop/TenantForge.Modules.Shop/domain/ShopShippingRate.cs`:**

```csharp
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopShippingRate
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public string ProvinceName { get; private set; } = string.Empty;
    public decimal Cost { get; private set; }

    private ShopShippingRate()
    {
    }

    public static ShopShippingRate Create(Tsid tenantId, string provinceName, decimal cost)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(provinceName))
        {
            throw new ArgumentException("Province name is required.", nameof(provinceName));
        }

        return new ShopShippingRate
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            ProvinceName = provinceName.Trim(),
            Cost = cost
        };
    }

    public void UpdateCost(decimal cost) => Cost = cost;
}
```

**`src/modules/shop/TenantForge.Modules.Shop/domain/ShopCoupon.cs`:**

```csharp
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal enum ShopDiscountType
{
    Percentage,
    FixedAmount
}

internal sealed class ShopCoupon
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NormalizedCode { get; private set; } = string.Empty;
    public ShopDiscountType DiscountType { get; private set; }
    public decimal DiscountValue { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    private ShopCoupon()
    {
    }

    public static ShopCoupon Create(
        Tsid tenantId,
        string code,
        ShopDiscountType discountType,
        decimal discountValue,
        DateTimeOffset? expiresAtUtc)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Coupon code is required.", nameof(code));
        }

        var trimmedCode = code.Trim();
        return new ShopCoupon
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            Code = trimmedCode,
            NormalizedCode = trimmedCode.ToUpperInvariant(),
            DiscountType = discountType,
            DiscountValue = discountValue,
            IsActive = true,
            ExpiresAtUtc = expiresAtUtc?.ToUniversalTime()
        };
    }

    public void Deactivate() => IsActive = false;
}
```

## 2. EF Core maps

**`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopShippingRateMap.cs`:**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopShippingRateMap : IEntityTypeConfiguration<ShopShippingRate>
{
    public void Configure(EntityTypeBuilder<ShopShippingRate> builder)
    {
        builder.ToTable("shop_shipping_rates");

        builder.HasKey(rate => rate.Id);

        builder.Property(rate => rate.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(rate => rate.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(rate => rate.ProvinceName)
            .HasColumnName("province_name")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(rate => rate.Cost)
            .HasColumnName("cost")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.HasIndex(rate => new { rate.TenantId, rate.ProvinceName })
            .IsUnique()
            .HasDatabaseName("ix_shop_shipping_rates_tenant_province");
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopCouponMap.cs`:**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopCouponMap : IEntityTypeConfiguration<ShopCoupon>
{
    public void Configure(EntityTypeBuilder<ShopCoupon> builder)
    {
        builder.ToTable("shop_coupons");

        builder.HasKey(coupon => coupon.Id);

        builder.Property(coupon => coupon.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(coupon => coupon.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(coupon => coupon.Code)
            .HasColumnName("code")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(coupon => coupon.NormalizedCode)
            .HasColumnName("normalized_code")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(coupon => coupon.DiscountType)
            .HasColumnName("discount_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(coupon => coupon.DiscountValue)
            .HasColumnName("discount_value")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(coupon => coupon.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(coupon => coupon.ExpiresAtUtc)
            .HasColumnName("expires_at_utc");

        builder.HasIndex(coupon => new { coupon.TenantId, coupon.NormalizedCode })
            .IsUnique()
            .HasDatabaseName("ix_shop_coupons_tenant_normalized_code");
    }
}
```

## 3. Register the two entities in `ShopDbContext`

Edit `src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopDbContext.cs`.
Add these two lines beside the existing `DbSet<T>` properties:

```csharp
    internal DbSet<ShopShippingRate> ShippingRates => Set<ShopShippingRate>();
    internal DbSet<ShopCoupon> Coupons => Set<ShopCoupon>();
```

And these two lines inside `OnModelCreating`, beside the existing
`ApplyConfiguration` calls:

```csharp
        modelBuilder.ApplyConfiguration(new ShopShippingRateMap());
        modelBuilder.ApplyConfiguration(new ShopCouponMap());
```

## 4. Migration

```bash
dotnet ef migrations add AddShopShippingRatesAndCoupons --project src/modules/shop/TenantForge.Modules.Shop --startup-project src/api/TenantForge.Api --output-dir infrastructure/Migrations
```

## 5. Request/response records

**`src/modules/shop/TenantForge.Modules.Shop/features/shipping/ShippingRateContracts.cs`:**

```csharp
namespace TenantForge.Modules.Shop.Features.Shipping;

public sealed record SetShippingRateRequest(string? ProvinceName, decimal Cost);

public sealed record ShippingRateResponse(string Id, string ProvinceName, decimal Cost);

public sealed record ShippingRateListResponse(IReadOnlyList<ShippingRateResponse> Rates);
```

**`src/modules/shop/TenantForge.Modules.Shop/features/coupons/CouponContracts.cs`:**

```csharp
namespace TenantForge.Modules.Shop.Features.Coupons;

public sealed record CreateCouponRequest(string? Code, string? DiscountType, decimal DiscountValue, DateTimeOffset? ExpiresAtUtc);

public sealed record CouponResponse(
    string Id,
    string Code,
    string DiscountType,
    decimal DiscountValue,
    bool IsActive,
    DateTimeOffset? ExpiresAtUtc);

public sealed record CouponListResponse(
    IReadOnlyList<CouponResponse> Coupons,
    TenantForge.Modules.Shop.Features.Pagination.PaginationMetadata Pagination);
```

## 6. `src/modules/shop/TenantForge.Modules.Shop/features/shipping/ShippingRatesFeature.cs`

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Authorization;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Shipping;

internal static class ShippingRatesFeature
{
    public static IEndpointRouteBuilder MapShippingRatesFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenants/{tenantId}/shop/shipping-rates", async (
            string tenantId,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            var rates = await db.ShippingRates.AsNoTracking()
                .Where(rate => rate.TenantId == access.TenantId)
                .OrderBy(rate => rate.ProvinceName)
                .Select(rate => new ShippingRateResponse(TsidId.Format(rate.Id), rate.ProvinceName, rate.Cost))
                .ToListAsync();

            return Results.Ok(new ShippingRateListResponse(rates));
        }).RequireAuthorization();

        endpoints.MapPost("/api/tenants/{tenantId}/shop/shipping-rates", async (
            string tenantId,
            SetShippingRateRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            if (string.IsNullOrWhiteSpace(request.ProvinceName))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["provinceName"] = ["Province name is required."],
                });
            }

            var provinceName = request.ProvinceName.Trim();
            var existing = await db.ShippingRates.SingleOrDefaultAsync(rate =>
                rate.TenantId == access.TenantId && rate.ProvinceName == provinceName);

            if (existing is null)
            {
                var rate = ShopShippingRate.Create(access.TenantId, provinceName, request.Cost);
                db.ShippingRates.Add(rate);
                await db.SaveChangesAsync();
                return Results.Ok(new ShippingRateResponse(TsidId.Format(rate.Id), rate.ProvinceName, rate.Cost));
            }

            existing.UpdateCost(request.Cost);
            await db.SaveChangesAsync();
            return Results.Ok(new ShippingRateResponse(TsidId.Format(existing.Id), existing.ProvinceName, existing.Cost));
        }).RequireAuthorization();

        return endpoints;
    }
}
```

## 7. `src/modules/shop/TenantForge.Modules.Shop/features/coupons/CouponsFeature.cs`

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Authorization;
using TenantForge.Modules.Shop.Features.Pagination;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Coupons;

internal static class CouponsFeature
{
    public static IEndpointRouteBuilder MapCouponsFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/tenants/{tenantId}/shop/coupons", async (
            string tenantId,
            CreateCouponRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                errors["code"] = ["Coupon code is required."];
            }

            if (!Enum.TryParse<ShopDiscountType>(request.DiscountType, ignoreCase: true, out var discountType))
            {
                errors["discountType"] = ["discountType must be 'Percentage' or 'FixedAmount'."];
            }
            else if (request.DiscountValue <= 0 || (discountType == ShopDiscountType.Percentage && request.DiscountValue > 100))
            {
                errors["discountValue"] = discountType == ShopDiscountType.Percentage
                    ? ["A percentage discount must be between 1 and 100."]
                    : ["Discount value must be positive."];
            }

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var normalizedCode = request.Code!.Trim().ToUpperInvariant();
            var duplicate = await db.Coupons.AnyAsync(coupon =>
                coupon.TenantId == access.TenantId && coupon.NormalizedCode == normalizedCode);
            if (duplicate) return DuplicateCouponProblem();

            var coupon = ShopCoupon.Create(access.TenantId, request.Code!, discountType, request.DiscountValue, request.ExpiresAtUtc);
            db.Coupons.Add(coupon);
            await db.SaveChangesAsync();

            return Results.Created($"/api/tenants/{tenantId}/shop/coupons/{TsidId.Format(coupon.Id)}", ToResponse(coupon));
        }).RequireAuthorization();

        endpoints.MapGet("/api/tenants/{tenantId}/shop/coupons", async (
            string tenantId,
            HttpRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            if (!PaginationSupport.TryBind(request, out var page, out var errors))
            {
                return Results.ValidationProblem(errors);
            }

            var query = db.Coupons.AsNoTracking()
                .Where(coupon => coupon.TenantId == access.TenantId)
                .OrderBy(coupon => coupon.Code)
                .ThenBy(coupon => coupon.Id);

            var (coupons, pagination) = await PaginationSupport.PageAsync(query, page);
            return Results.Ok(new CouponListResponse(coupons.Select(ToResponse).ToList(), pagination));
        }).RequireAuthorization();

        endpoints.MapPatch("/api/tenants/{tenantId}/shop/coupons/{couponId}/deactivate", async (
            string tenantId,
            string couponId,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            if (!TsidId.TryParse(couponId, out var couponTsid)) return Results.NotFound();

            var coupon = await db.Coupons.SingleOrDefaultAsync(coupon =>
                coupon.TenantId == access.TenantId && coupon.Id == couponTsid);
            if (coupon is null) return Results.NotFound();

            coupon.Deactivate();
            await db.SaveChangesAsync();

            return Results.Ok(ToResponse(coupon));
        }).RequireAuthorization();

        return endpoints;
    }

    private static IResult DuplicateCouponProblem() => Results.Problem(
        title: "Duplicate coupon code",
        detail: "A coupon with this code already exists in this tenant.",
        statusCode: StatusCodes.Status409Conflict);

    private static CouponResponse ToResponse(ShopCoupon coupon) => new(
        TsidId.Format(coupon.Id),
        coupon.Code,
        coupon.DiscountType.ToString(),
        coupon.DiscountValue,
        coupon.IsActive,
        coupon.ExpiresAtUtc);
}
```

## 8. Wire both features into the composition seam

Edit `src/modules/shop/TenantForge.Modules.Shop/ShopModule.cs`. Add these
two lines inside `MapShopModule`, alongside whichever `Map*Feature` calls
are already there (the exact preceding set depends on whether B028 has
landed yet — either way, just add these two lines to the same method):

```csharp
        endpoints.MapShippingRatesFeature();
        endpoints.MapCouponsFeature();
```

Add the matching `using` statements at the top of the file:

```csharp
using TenantForge.Modules.Shop.Features.Shipping;
using TenantForge.Modules.Shop.Features.Coupons;
```

# Non-goals

- No coupon usage tracking/redemption count.
- No shipping-rate delete endpoint — an unwanted province rate is
  corrected by setting a new cost via the same upsert `POST`.

# If you get stuck

```bash
curl -X POST http://localhost:5080/api/tenants/<tenantId>/shop/shipping-rates \
  -H "Authorization: Bearer <accessToken>" -H "Content-Type: application/json" \
  -d '{"provinceName":"تهران","cost":50000}'
```

Expected: `200 OK`, `{"id":"...","provinceName":"تهران","cost":50000}`.
Run it again with a different `cost` for the same province and confirm
the same `id` comes back with the new cost (upsert, not a new row).

```bash
curl -X POST http://localhost:5080/api/tenants/<tenantId>/shop/coupons \
  -H "Authorization: Bearer <accessToken>" -H "Content-Type: application/json" \
  -d '{"code":"WELCOME10","discountType":"Percentage","discountValue":10,"expiresAtUtc":null}'
```

Expected: `201 Created` with the coupon's `id`, `code`, `discountType`,
`discountValue`, `isActive: true`. Repeating the exact same call is
expected to return `409 Conflict`.

# Acceptance

- All four endpoints work, tenant-scoped and authorization-checked
  exactly like B026's endpoints.
- Posting a shipping rate for a province that already has one updates its
  cost rather than creating a second row for that province.
- Creating a coupon with a duplicate code (case-insensitive) within the
  same tenant is rejected; the same code in a different tenant is
  allowed.
- Deactivating a coupon is reflected immediately in the list endpoint and
  (once B030 exists) makes that coupon fail validation at checkout.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover the shipping-rate upsert, coupon creation
including the duplicate-code rejection, list, and deactivate. The full
existing IAM suite continues to pass unmodified.

Manual: the two `curl` sequences under "If you get stuck" above.

# Lifecycle

Add row `B029` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B026`, and Spec link
`tasks/backend/B029-shipping-rate-and-coupon-admin-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/028-shop-checkout.md` is the permanent record and
is never deleted.
