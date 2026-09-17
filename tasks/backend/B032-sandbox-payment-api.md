---
id: B032
slice: S29
title: Sandbox payment API
agent: backend-mentor
source: tasks/slices/029-shop-order-and-sandbox-payment.md
---

# Objective

Add the `IShopPaymentGateway` abstraction and its one real implementation,
`SandboxPaymentGateway`, plus an endpoint to initiate payment for a
`PendingPayment` order (returning a redirect target the frontend renders
as a fake bank page) and a callback endpoint that verifies the attempt
and transitions the order to `Paid` or back to `PendingPayment`/`Failed`.

# Context

Read `tasks/slices/029-shop-order-and-sandbox-payment.md` completely, in
particular its "Payment abstraction" section and the explicit, named
non-goal: no real ZarinPal integration, no stored card data, no webhook
signature scheme beyond what `SandboxPaymentGateway` needs to demonstrate
the seam. Read B031's delivered `domain/ShopOrder.cs` (specifically
`MarkPaid`/`MarkPaymentFailed`) — this task calls those methods, it does
not add a third way to change `ShopOrder.Status`.

# Scope — every file, in order

## 1. Domain entity

**`src/modules/shop/TenantForge.Modules.Shop/domain/ShopPaymentAttempt.cs`:**

```csharp
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal enum ShopPaymentAttemptStatus
{
    Initiated,
    Succeeded,
    Failed
}

internal sealed class ShopPaymentAttempt
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid OrderId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public ShopPaymentAttemptStatus Status { get; private set; } = ShopPaymentAttemptStatus.Initiated;
    public string GatewayReference { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CallbackReceivedAtUtc { get; private set; }

    private ShopPaymentAttempt()
    {
    }

    public static ShopPaymentAttempt Create(Tsid orderId, string provider, string gatewayReference, DateTimeOffset nowUtc)
    {
        if (TsidId.IsDefault(orderId))
        {
            throw new ArgumentException("Order id is required.", nameof(orderId));
        }

        return new ShopPaymentAttempt
        {
            Id = TsidId.NewId(),
            OrderId = orderId,
            Provider = provider,
            Status = ShopPaymentAttemptStatus.Initiated,
            GatewayReference = gatewayReference,
            CreatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    /// <summary>Returns false (no change) when the attempt was already resolved.</summary>
    public bool TryResolve(bool succeeded, DateTimeOffset nowUtc)
    {
        if (Status != ShopPaymentAttemptStatus.Initiated)
        {
            return false;
        }

        Status = succeeded ? ShopPaymentAttemptStatus.Succeeded : ShopPaymentAttemptStatus.Failed;
        CallbackReceivedAtUtc = nowUtc.ToUniversalTime();
        return true;
    }
}
```

## 2. EF Core map

**`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopPaymentAttemptMap.cs`:**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopPaymentAttemptMap : IEntityTypeConfiguration<ShopPaymentAttempt>
{
    public void Configure(EntityTypeBuilder<ShopPaymentAttempt> builder)
    {
        builder.ToTable("shop_payment_attempts");

        builder.HasKey(attempt => attempt.Id);

        builder.Property(attempt => attempt.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(attempt => attempt.OrderId)
            .HasColumnName("order_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(attempt => attempt.Provider)
            .HasColumnName("provider")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(attempt => attempt.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(attempt => attempt.GatewayReference)
            .HasColumnName("gateway_reference")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(attempt => attempt.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(attempt => attempt.CallbackReceivedAtUtc)
            .HasColumnName("callback_received_at_utc");

        builder.HasIndex(attempt => attempt.GatewayReference)
            .IsUnique()
            .HasDatabaseName("ix_shop_payment_attempts_gateway_reference");

        builder.HasIndex(attempt => attempt.OrderId)
            .HasDatabaseName("ix_shop_payment_attempts_order_id");

        builder.HasOne<ShopOrder>()
            .WithMany()
            .HasForeignKey(attempt => attempt.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

## 3. Register the entity in `ShopDbContext`

Add this line beside the existing `DbSet<T>` properties:

```csharp
    internal DbSet<ShopPaymentAttempt> PaymentAttempts => Set<ShopPaymentAttempt>();
```

And this line inside `OnModelCreating`:

```csharp
        modelBuilder.ApplyConfiguration(new ShopPaymentAttemptMap());
```

## 4. Migration

```bash
dotnet ef migrations add AddShopPaymentAttempts --project src/modules/shop/TenantForge.Modules.Shop --startup-project src/api/TenantForge.Api --output-dir infrastructure/Migrations
```

## 5. `src/modules/shop/TenantForge.Modules.Shop/features/payments/IShopPaymentGateway.cs`

```csharp
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Features.Payments;

internal sealed record PaymentInitiation(string GatewayReference, string RedirectUrl);

internal sealed record PaymentVerification(bool Succeeded);

internal interface IShopPaymentGateway
{
    Task<PaymentInitiation> InitiateAsync(ShopOrder order, CancellationToken ct);

    Task<PaymentVerification> VerifyCallbackAsync(string gatewayReference, bool approved, CancellationToken ct);
}
```

## 6. `src/modules/shop/TenantForge.Modules.Shop/features/payments/SandboxPaymentGateway.cs`

```csharp
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Payments;

/// <summary>
/// The one real IShopPaymentGateway implementation in this module. It never
/// makes an outbound HTTP call to any external service — "initiating
/// payment" means minting a ShopPaymentAttempt row and a redirect URL to an
/// in-app frontend route (the fake bank page F038/F039 render), which then
/// calls the callback endpoint directly. See B032's Spec Context and
/// tasks/slices/029-shop-order-and-sandbox-payment.md for the explicit,
/// deliberate scope boundary this class sits inside.
/// </summary>
internal sealed class SandboxPaymentGateway(ShopDbContext db) : IShopPaymentGateway
{
    private const string ProviderName = "Sandbox";

    public async Task<PaymentInitiation> InitiateAsync(ShopOrder order, CancellationToken ct)
    {
        var gatewayReference = GenerateGatewayReference();
        var attempt = ShopPaymentAttempt.Create(order.Id, ProviderName, gatewayReference, DateTimeOffset.UtcNow);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);

        var redirectUrl = $"/shop/{Format(order.TenantId)}/payments/sandbox/{gatewayReference}";
        return new PaymentInitiation(gatewayReference, redirectUrl);
    }

    public async Task<PaymentVerification> VerifyCallbackAsync(string gatewayReference, bool approved, CancellationToken ct)
    {
        var attempt = await db.PaymentAttempts.SingleOrDefaultAsync(a => a.GatewayReference == gatewayReference, ct);
        if (attempt is null)
        {
            return new PaymentVerification(false);
        }

        var resolved = attempt.TryResolve(approved, DateTimeOffset.UtcNow);
        if (!resolved)
        {
            // Already resolved once — do not silently re-apply a second
            // callback for the same attempt (B032's Spec Non-goals).
            return new PaymentVerification(false);
        }

        await db.SaveChangesAsync(ct);
        return new PaymentVerification(approved);
    }

    private static string GenerateGatewayReference()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Format(TSID.Creator.NET.Tsid tenantId) => TenantForge.BuildingBlocks.Identifiers.TsidId.Format(tenantId);
}
```

## 7. Register the gateway in `ShopConfig`

Edit `src/modules/shop/TenantForge.Modules.Shop/ShopConfig.cs`. Add this
line at the end of `RegisterServices` (after the existing
`services.AddDbContext<ShopDbContext>(...)` call):

```csharp
        // Scoped, not singleton: SandboxPaymentGateway depends on the scoped
        // ShopDbContext, mirroring IAMConfig.RegisterServices' own
        // scoped-vs-singleton reasoning for its database-backed services.
        services.AddScoped<Features.Payments.IShopPaymentGateway, Features.Payments.SandboxPaymentGateway>();
```

Add the matching `using` at the top of `ShopConfig.cs` if you prefer the
unqualified names instead of the inline qualification above:

```csharp
using TenantForge.Modules.Shop.Features.Payments;
```

(then simplify the registration line to
`services.AddScoped<IShopPaymentGateway, SandboxPaymentGateway>();`).

## 8. Request/response records and endpoints

**`src/modules/shop/TenantForge.Modules.Shop/features/payments/PaymentContracts.cs`:**

```csharp
namespace TenantForge.Modules.Shop.Features.Payments;

public sealed record InitiatePaymentResponse(string GatewayReference, string RedirectUrl);

public sealed record PaymentCallbackRequest(string? GatewayReference, bool Approved);

public sealed record PaymentCallbackResponse(string OrderId, string Status);
```

**`src/modules/shop/TenantForge.Modules.Shop/features/payments/PaymentsFeature.cs`:**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Payments;

internal static class PaymentsFeature
{
    public static IEndpointRouteBuilder MapPaymentsFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/shop/{tenantId}/orders/{orderId}/payments/initiate", async (
            string tenantId,
            string orderId,
            IShopPaymentGateway gateway,
            ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(orderId, out var orderTsid)) return Results.NotFound();

            var order = await db.Orders.SingleOrDefaultAsync(order => order.Id == orderTsid && order.TenantId == tenantTsid);
            if (order is null) return Results.NotFound();

            if (order.Status != ShopOrderStatus.PendingPayment)
            {
                return Results.Problem(
                    title: "Order is not awaiting payment",
                    detail: $"This order's current status is '{order.Status}'.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var initiation = await gateway.InitiateAsync(order, CancellationToken.None);
            return Results.Ok(new InitiatePaymentResponse(initiation.GatewayReference, initiation.RedirectUrl));
        });

        endpoints.MapPost("/api/shop/{tenantId}/orders/{orderId}/payments/callback", async (
            string tenantId,
            string orderId,
            PaymentCallbackRequest request,
            IShopPaymentGateway gateway,
            ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(orderId, out var orderTsid)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(request.GatewayReference))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["gatewayReference"] = ["gatewayReference is required."],
                });
            }

            var order = await db.Orders.SingleOrDefaultAsync(order => order.Id == orderTsid && order.TenantId == tenantTsid);
            if (order is null) return Results.NotFound();

            if (order.Status != ShopOrderStatus.PendingPayment)
            {
                return Results.Problem(
                    title: "Order already resolved",
                    detail: $"This order's current status is '{order.Status}'.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var verification = await gateway.VerifyCallbackAsync(request.GatewayReference, request.Approved, CancellationToken.None);
            if (!verification.Succeeded && !request.Approved)
            {
                // A legitimate decline: the gateway resolved the attempt as
                // Failed. Distinguish this from an unknown/duplicate
                // gatewayReference (also "not succeeded") using the attempt
                // row directly.
                var attemptExists = await db.PaymentAttempts.AnyAsync(a => a.GatewayReference == request.GatewayReference);
                if (!attemptExists)
                {
                    return Results.NotFound();
                }

                order.MarkPaymentFailed();
                await db.SaveChangesAsync();
                return Results.Ok(new PaymentCallbackResponse(TsidId.Format(order.Id), order.Status.ToString()));
            }

            if (!verification.Succeeded)
            {
                // approved=true but the gateway could not resolve the
                // attempt (unknown reference, or already resolved once).
                return Results.Problem(
                    title: "Payment callback rejected",
                    detail: "The payment attempt could not be verified.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            order.MarkPaid();
            await db.SaveChangesAsync();
            return Results.Ok(new PaymentCallbackResponse(TsidId.Format(order.Id), order.Status.ToString()));
        });

        return endpoints;
    }
}
```

## 9. Wire the feature into the composition seam

Edit `src/modules/shop/TenantForge.Modules.Shop/ShopModule.cs`. Add this
one line inside `MapShopModule`, alongside the existing `Map*Feature`
calls:

```csharp
        endpoints.MapPaymentsFeature();
```

Add the matching `using`:

```csharp
using TenantForge.Modules.Shop.Features.Payments;
```

# Non-goals

- No real ZarinPal (or other) integration, credentials, or production
  webhook-signature verification.
- No `Cancelled`/`Fulfilled` order-status transition from this endpoint.
- No retry/idempotency-key handling beyond rejecting a callback for an
  order that is not `PendingPayment` (and rejecting a second callback for
  an already-resolved attempt).

# If you get stuck

```bash
curl -X POST http://localhost:5080/api/shop/<tenantId>/orders/<orderId>/payments/initiate
```

Expected: `200 OK`,
`{"gatewayReference":"...","redirectUrl":"/shop/<tenantId>/payments/sandbox/<gatewayReference>"}`.

```bash
curl -X POST http://localhost:5080/api/shop/<tenantId>/orders/<orderId>/payments/callback \
  -H "Content-Type: application/json" \
  -d '{"gatewayReference":"<gatewayReference>","approved":true}'
```

Expected: `200 OK`, `{"orderId":"<orderId>","status":"Paid"}`. Repeating
the exact same callback call a second time is expected to return `409`
(the attempt is already resolved).

# Acceptance

- Initiating payment for a `PendingPayment` order creates a
  `ShopPaymentAttempt` and returns a redirect target.
- Initiating payment for a non-`PendingPayment` order (e.g. already
  `Paid`) is rejected with a clear `409` conflict.
- An `approved: true` callback with a valid `gatewayReference` transitions
  the order to `Paid` and the attempt to `Succeeded`.
- An `approved: false` callback leaves the order `PendingPayment` and
  marks the attempt `Failed`.
- A callback with an unknown `gatewayReference`, or a second callback for
  an already-resolved attempt, is rejected with a clear error, not
  silently accepted.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover: initiate on a valid order, initiate on an
invalid-status order, approve callback reaching `Paid`, decline callback
staying `PendingPayment`, and rejection of an unknown/duplicate callback.
The full existing IAM suite continues to pass unmodified.

Manual: the `curl` sequence under "If you get stuck" above, plus repeating
the callback once more with `approved:false` on a fresh order to confirm
it stays `PendingPayment`.

# Lifecycle

Add row `B032` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B031`, and Spec link
`tasks/backend/B032-sandbox-payment-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/029-shop-order-and-sandbox-payment.md` is the
permanent record and is never deleted.
