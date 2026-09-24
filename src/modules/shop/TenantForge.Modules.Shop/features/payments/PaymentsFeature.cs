using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Payments.ZarinPal;
using TenantForge.Modules.Shop.Features.RateLimiting;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Payments;

/// <summary>
/// B044: the anonymous, gateway-neutral payment surface.
///
///   POST /api/shop/{tenantId}/orders/{orderId}/payments/initiate
///       Idempotency-Key (UUID) header, no body — every value the initiation
///       needs is server-derived. 200 InitiatePaymentResponse
///       { provider, redirectUrl, resultToken }.
///
///   GET /api/shop/{tenantId}/orders/{orderId}/payments/status?token=…
///       Requires the raw callback token; 200 PaymentStatusResponse
///       { orderNumber, status, providerReference } — and nothing else.
///
///   POST /api/shop/{tenantId}/orders/{orderId}/payments/sandbox/resolve
///       Development-only mapping; ResolveSandboxPaymentRequest
///       { authority, approved } → 200 PaymentStatusResponse. The only
///       browser-driven simulation in the module.
///
/// The browser never declares success: initiation carries no body at all,
/// and verification goes through the gateway's VerifyAsync into
/// ShopPaymentCompletionService, which re-checks tenant, order, amount and
/// provider under row locks before anything moves.
/// </summary>
internal static class PaymentsFeature
{
    /// <summary>
    /// B044: the maximum number of payment attempts a single order may have.
    /// The 11th initiation is rejected — a churning order's attempt history
    /// stays bounded, and the cap is a property of the payment lifecycle, not
    /// of the order list that displays it.
    /// </summary>
    private const int MaxAttemptsPerOrder = 10;

    /// <summary>The unique index that makes an idempotency key single-use per tenant.</summary>
    private const string InitiationKeyIndexName = "ix_shop_payment_initiations_tenant_idempotency_key";

    public static IEndpointRouteBuilder MapPaymentsFeature(this IEndpointRouteBuilder endpoints)
    {
        // B045: the provider-specific callback route (ZarinPal today) is mapped
        // alongside the gateway-neutral routes.
        endpoints.MapZarinPalCallbackFeature();

        endpoints.MapPost("/api/shop/{tenantId}/orders/{orderId}/payments/initiate", async (
            string tenantId,
            string orderId,
            HttpRequest httpRequest,
            IShopPaymentGatewayResolver gatewayResolver,
            ShopDbContext db,
            TimeProvider timeProvider,
            ZarinPalCallbackStateProtector callbackStateProtector,
            IOptions<ZarinPalOptions> zarinPalOptions,
            CancellationToken ct) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return PaymentNotFoundProblem();
            if (!TsidId.TryParse(orderId, out var orderTsid)) return PaymentNotFoundProblem();

            // The Idempotency-Key header is required and a UUID — read
            // directly, never bound, so a missing or malformed key is a clean
            // 400 naming the header before any state is touched.
            var idempotencyKey = httpRequest.Headers.TryGetValue("Idempotency-Key", out var headerValue)
                ? headerValue.ToString()
                : null;

            if (idempotencyKey is null)
            {
                return IdempotencyKeyProblem("The Idempotency-Key header is required.");
            }

            if (!System.Guid.TryParse(idempotencyKey, out _))
            {
                return IdempotencyKeyProblem("The Idempotency-Key header must be a UUID.");
            }

            var gateway = gatewayResolver.Resolve();
            var now = timeProvider.GetUtcNow();

            // The provider's callback URL is the server-owned status endpoint
            // for this order (an absolute URL derived from the request; B045's
            // real provider uses it as its return/failure target).
            var callbackUri = new UriBuilder
            {
                Scheme = httpRequest.Scheme,
                Host = httpRequest.Host.Value,
                Path = $"/api/shop/{tenantId}/orders/{orderId}/payments/status"
            }.Uri;

            // Canonical request fingerprint: the tenant and order ids the
            // request is bound to (initiation carries no body). Declared
            // before the transaction so the unique-index race recovery in
            // the catch block can compare against it too. A retry with the
            // same key but a different canonical request is a conflict,
            // never a silent replay.
            var fingerprint = ComputeFingerprint(tenantId, orderId);

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // Lock the order row (tenant-first predicate) before any
                // other read or write. Two concurrent initiations for the
                // same order — including two with the same idempotency key —
                // serialize here, so the second re-reads the first's
                // committed state.
                var order = await db.Orders
                    .FromSqlRaw("""
                        SELECT *
                        FROM shop_orders
                        WHERE tenant_id = {0} AND id = {1}
                        FOR UPDATE
                        """, tenantTsid.ToLong(), orderTsid.ToLong())
                    .SingleOrDefaultAsync(ct);

                if (order is null)
                {
                    await transaction.RollbackAsync(ct);
                    return PaymentNotFoundProblem();
                }

                // Only an order waiting for payment may start a new attempt.
                if (order.Status != ShopOrderStatus.PendingPayment)
                {
                    await transaction.RollbackAsync(ct);
                    return OrderNotAwaitingPaymentProblem(order.Status);
                }

                // Same key already used by this tenant: replay or conflict.
                var existing = await db.PaymentInitiations
                    .FirstOrDefaultAsync(i => i.TenantId == tenantTsid && i.IdempotencyKey == idempotencyKey, ct);

                if (existing is not null)
                {
                    await transaction.CommitAsync(ct);
                    return existing.RequestFingerprint == fingerprint
                        ? BuildReplayResponse(gateway, existing)
                        : IdempotencyConflictProblem();
                }

                // An order has at most one live Initiated attempt at a time:
                // a fresh key for an order that already has one returns that
                // attempt's response, never a second live attempt (no orphan
                // rows). The latest stored initiation for the attempt is
                // what carries its seed and redirect URL.
                var live = await db.PaymentAttempts
                    .FirstOrDefaultAsync(
                        a => a.OrderId == orderTsid && a.Status == ShopPaymentAttemptStatus.Initiated, ct);

                if (live is not null)
                {
                    var liveRecord = await db.PaymentInitiations
                        .Where(i => i.AttemptId == live.Id)
                        .OrderByDescending(i => i.CreatedAtUtc)
                        .ThenByDescending(i => i.Id)
                        .FirstAsync(ct);

                    db.PaymentInitiations.Add(ShopPaymentInitiation.Create(
                        tenantTsid, orderTsid, live.Id, idempotencyKey, fingerprint,
                        liveRecord.RedirectUrl, liveRecord.TokenSeed, now));
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return BuildReplayResponse(gateway, liveRecord);
                }

                // The hard cap: ten attempts per order, the 11th rejected.
                var total = await db.PaymentAttempts
                    .CountAsync(a => a.OrderId == orderTsid, ct);

                if (total >= MaxAttemptsPerOrder)
                {
                    await transaction.RollbackAsync(ct);
                    return TooManyAttemptsProblem();
                }

                // Mint the attempt: amount frozen from the order, the token
                // hash stored (never the raw token), the provider fixed by
                // the resolver. The gateway only contributes the authority
                // and the redirect URL — it never sees the order, the amount
                // or the database.
                var tokenSeed = RandomNumberGenerator.GetBytes(32);
                var token = DeriveToken(tokenSeed);
                var tokenHash = HashToken(token);

                // B045: the attempt id is pre-minted so a real provider can
                // bind its signed callback state to the attempt BEFORE the row
                // exists: the state is what the provider hands back in the
                // callback query string, and it must name the very attempt
                // this initiation mints. The Sandbox path (whose "callback" is
                // the status route above) never names an attempt, so the
                // pre-minting is invisible to it. The row is persisted only
                // after the gateway call succeeds — a provider failure leaves
                // no orphan attempt row.
                var attemptId = TsidId.NewId();
                var providerCallbackUri = callbackUri;
                if (string.Equals(gateway.Provider, ZarinPalPaymentGateway.ProviderName, StringComparison.Ordinal))
                {
                    // The callback URL the provider is told to redirect the
                    // browser back to: the server's own ZarinPal callback
                    // route, carrying ONLY the signed state. The state binds
                    // tenant, order, attempt and the raw token — the callback
                    // route trusts none of those from the query string — and
                    // it is deliberately never persisted.
                    var stateToken = callbackStateProtector.Protect(new ZarinPalCallbackState(
                        TsidId.Format(tenantTsid), TsidId.Format(orderTsid),
                        TsidId.Format(attemptId), token));
                    providerCallbackUri = new Uri(
                        zarinPalOptions.Value.PublicApiBaseUrl,
                        $"/api/shop/{tenantId}/payments/zarinpal/callback?state={Uri.EscapeDataString(stateToken)}");
                }

                GatewayInitiation initiation;
                try
                {
                    initiation = await gateway.InitiateAsync(
                        new PaymentContext(tenantTsid, orderTsid, order.GrandTotal, providerCallbackUri), ct);
                }
                catch (ZarinPalProviderException exception)
                {
                    // The provider could not start the payment (unavailable,
                    // rejected, an empty authority, or an unpayable amount).
                    // No attempt row is persisted: the order stays
                    // PendingPayment and payable with a fresh key. The
                    // response carries only the stable code — never the
                    // provider's response text, which may echo request
                    // fields.
                    await transaction.RollbackAsync(ct);
                    return ProviderUnavailableProblem(exception.StableCode);
                }

                var attempt = ShopPaymentAttempt.Create(
                    order.Id, gateway.Provider, initiation.Authority,
                    order.GrandTotal, tokenHash, now, preMintedId: attemptId);
                db.PaymentAttempts.Add(attempt);

                var record = ShopPaymentInitiation.Create(
                    tenantTsid, orderTsid, attempt.Id, idempotencyKey, fingerprint,
                    initiation.RedirectUri.ToString(), tokenSeed, now);
                db.PaymentInitiations.Add(record);

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return Results.Ok(new InitiatePaymentResponse(
                    gateway.Provider, initiation.RedirectUri.ToString(), token));
            }
            catch (DbUpdateException exception) when (IsInitiationKeyViolation(exception))
            {
                // A racing same-key first-call committed its row first: this
                // request's insert lost the unique-index race. Re-read the
                // winner's row and behave like a replay — never a 500.
                await transaction.RollbackAsync(ct);
                var winner = await db.PaymentInitiations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(i => i.TenantId == tenantTsid && i.IdempotencyKey == idempotencyKey, ct);

                if (winner is null || winner.RequestFingerprint != fingerprint)
                {
                    return IdempotencyConflictProblem();
                }

                return BuildReplayResponse(gateway, winner);
            }
        }).RequireRateLimiting(ShopRateLimitPolicies.Payment);

        endpoints.MapGet("/api/shop/{tenantId}/orders/{orderId}/payments/status", async (
            string tenantId,
            string orderId,
            string? token,
            ShopDbContext db,
            CancellationToken ct) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return PaymentNotFoundProblem();
            if (!TsidId.TryParse(orderId, out var orderTsid)) return PaymentNotFoundProblem();

            // A missing or blank token is a miss, exactly like a wrong one:
            // the caller must never be able to distinguish the cases.
            if (string.IsNullOrWhiteSpace(token))
            {
                return PaymentNotFoundProblem();
            }

            // The order scopes the tenant (the attempt has no tenant column
            // of its own — the rule from the Spec's transaction notes); the
            // token identifies the attempt. Wrong tenant, wrong order, wrong
            // token and no-attempt all collapse into the same generic 404
            // below.
            var order = await db.Orders.AsNoTracking()
                .SingleOrDefaultAsync(o => o.Id == orderTsid && o.TenantId == tenantTsid, ct);

            if (order is null)
            {
                return PaymentNotFoundProblem();
            }

            // Compare the presented token's hash to the stored hash with a
            // fixed-time comparison — a comparison whose running time does
            // not depend on where the two values first differ, so the correct
            // token cannot be learned one byte at a time through timing. A
            // normal == / string.Equals is never used for this check.
            var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var attempts = await db.PaymentAttempts.AsNoTracking()
                .Where(a => a.OrderId == orderTsid)
                .ToListAsync(ct);

            var attempt = attempts.FirstOrDefault(a =>
                CryptographicOperations.FixedTimeEquals(
                    presentedHash, Convert.FromHexString(a.CallbackTokenHash)));

            if (attempt is null)
            {
                return PaymentNotFoundProblem();
            }

            return Results.Ok(new PaymentStatusResponse(
                order.OrderNumber, order.Status.ToString(), attempt.ProviderReference));
        });

        // Development-only: the browser-driven sandbox simulation. The route
        // is mapped only when the running environment is Development —
        // outside it the endpoint does not exist at all — and a Production
        // host configured with Shop:Payments:Provider=Sandbox additionally
        // refuses to start (ShopConfig.ValidateConfiguration), so the
        // simulation can never be enabled silently in production.
        var environment = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (environment.IsDevelopment())
        {
            endpoints.MapPost("/api/shop/{tenantId}/orders/{orderId}/payments/sandbox/resolve", async (
                string tenantId,
                string orderId,
                ResolveSandboxPaymentRequest request,
                IShopPaymentGatewayResolver gatewayResolver,
                ShopPaymentCompletionService completionService,
                ShopDbContext db,
                TimeProvider timeProvider,
                CancellationToken ct) =>
            {
                if (!TsidId.TryParse(tenantId, out _)) return PaymentNotFoundProblem();
                if (!TsidId.TryParse(orderId, out _)) return PaymentNotFoundProblem();

                if (string.IsNullOrWhiteSpace(request.Authority))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["authority"] = ["authority is required."]
                    });
                }

                var gateway = gatewayResolver.Resolve();
                if (!string.Equals(gateway.Provider, SandboxPaymentGateway.ProviderName, StringComparison.Ordinal))
                {
                    return Results.Problem(
                        title: "Sandbox resolve unavailable",
                        detail: "The sandbox resolve route is only available when the provider is 'Sandbox'.",
                        statusCode: StatusCodes.Status409Conflict);
                }

                // The fake bank page hands back its single simulated input.
                // This is the only browser-authored payment value in the
                // module, and it can only reach a Development host.
                var verification = await gateway.VerifyAsync(new PaymentVerificationRequest(
                    request.Authority,
                    new Dictionary<string, string> { ["approved"] = request.Approved ? "true" : "false" }), ct);

                // The completion service is the only place a verified outcome
                // may turn into an attempt/order transition — the gateway's
                // result is a hint, never a command.
                var completion = await completionService.VerifyAsync(
                    tenantId, orderId, request.Authority, gateway.Provider, verification, timeProvider, ct);

                if (completion.OrderNumber is null)
                {
                    return PaymentNotFoundProblem();
                }

                if (completion.OrderNotPayable)
                {
                    return OrderNotPayableProblem();
                }

                return Results.Ok(new PaymentStatusResponse(
                    completion.OrderNumber, completion.OrderStatus, completion.ProviderReference));
            });
        }

        return endpoints;
    }

    /// <summary>
    /// Rebuilds an initiation response from a stored idempotency row — used
    /// by the ordinary replay path, the live-attempt path and the
    /// unique-index race recovery. The response is identical to the first
    /// call's: same provider (stable per configuration), same redirect URL
    /// (the stored one) and the same result token (re-derived from the
    /// stored seed — the seed is not the token, and the token is never
    /// persisted).
    /// </summary>
    private static IResult BuildReplayResponse(IShopPaymentGateway gateway, ShopPaymentInitiation record)
        => Results.Ok(new InitiatePaymentResponse(
            gateway.Provider, record.RedirectUrl, DeriveToken(record.TokenSeed)));

    /// <summary>
    /// The raw callback token is the one-way SHA-256 of the stored 32-byte
    /// seed — a 32-byte value the Spec calls "random". It is returned to the
    /// client exactly once per initiation (and re-derived, identically, on a
    /// same-key replay); only its SHA-256 hash is ever stored, on the
    /// attempt. The seed is not a credential: it sits in a row the caller
    /// can already address by its own idempotency key and, without the
    /// token-hash it derives, it authenticates nothing.
    /// </summary>
    private static string DeriveToken(byte[] seed)
        => Convert.ToHexString(SHA256.HashData(seed)).ToLowerInvariant();

    /// <summary>SHA-256 of a raw callback token, hex lowercase (64 chars).</summary>
    private static string HashToken(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

    /// <summary>
    /// SHA-256 over the canonical request: the tenant and order id strings
    /// exactly as they appeared in the route (initiation has no body). A
    /// same-key retry for a different canonical request produces a different
    /// fingerprint and is answered as an idempotency conflict.
    /// </summary>
    private static string ComputeFingerprint(string tenantId, string orderId)
        => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}|{orderId}"))).ToLowerInvariant();

    private static bool IsInitiationKeyViolation(DbUpdateException exception)
    {
        var postgres = exception.GetBaseException() as PostgresException;
        return postgres is { SqlState: "23505" }
            && postgres.ConstraintName == InitiationKeyIndexName;
    }

    private static IResult IdempotencyKeyProblem(string detail)
        => Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Idempotency-Key"] = [detail]
        });

    private static IResult OrderNotAwaitingPaymentProblem(ShopOrderStatus status)
        => Results.Problem(
            title: "Order is not awaiting payment",
            detail: $"This order's current status is '{status}'.",
            statusCode: StatusCodes.Status409Conflict);

    /// <summary>
    /// B043's late-payment rule, carried through B044: a success verification
    /// for an order that is no longer PendingPayment is a conflict, and the
    /// order never moves. (The completion service has already recorded the
    /// attempt as Failed with a stable code.)
    /// </summary>
    private static IResult OrderNotPayableProblem() => Results.Problem(
        title: "Order already resolved",
        detail: "This order can no longer be paid.",
        statusCode: StatusCodes.Status409Conflict);

    private static IResult IdempotencyConflictProblem() => Results.Problem(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Type = "idempotency_key_conflict",
        Title = "Idempotency key conflict",
        Detail = "This idempotency key was already used for a different request."
    });

    /// <summary>
    /// B045: the safe response to a ZarinPal provider failure during
    /// initiation. 503 — the provider is unavailable to complete the payment
    /// right now — carrying only the stable code as the RFC 7807 type. The
    /// merchant id, the provider's message and the raw authority never reach
    /// this body; a network timeout is one of these codes and leaves the
    /// order payable (no attempt row was persisted).
    /// </summary>
    private static IResult ProviderUnavailableProblem(string stableCode) => Results.Problem(new ProblemDetails
    {
        Status = StatusCodes.Status503ServiceUnavailable,
        Type = stableCode,
        Title = "Payment provider unavailable",
        Detail = "The payment provider could not start this payment. The order is still payable; try again."
    });

    private static IResult TooManyAttemptsProblem() => Results.Problem(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Type = "too_many_payment_attempts",
        Title = "Too many payment attempts",
        Detail = $"This order already has {MaxAttemptsPerOrder} payment attempts."
    });

    /// <summary>
    /// The one, identical 404 for every initiation/status miss: malformed
    /// ids, an unknown order, an order under another tenant, a wrong token
    /// and an unknown authority are all indistinguishable.
    /// </summary>
    private static IResult PaymentNotFoundProblem() => Results.Problem(
        title: "Payment not found",
        detail: "No payment was found.",
        statusCode: StatusCodes.Status404NotFound);
}
