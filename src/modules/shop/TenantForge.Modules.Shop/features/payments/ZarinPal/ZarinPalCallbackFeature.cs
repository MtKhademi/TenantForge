using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Payments;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Payments.ZarinPal;

/// <summary>
/// B045: the anonymous ZarinPal callback route.
///
///   GET /api/shop/{tenantId}/payments/zarinpal/callback?Authority=&amp;Status=&amp;state=
///
/// ZarinPal redirects the customer's browser here after the hosted payment
/// page. The query string the browser hands back is NOT trusted: the only
/// server-authored value it may carry is the signed, expiring <c>state</c>
/// (a Data Protection token that binds the tenant id, order id, attempt id
/// and the B044 raw callback token). The order, attempt, authority, amount
/// and token are all read back from that signed state and the database —
/// never from the <c>Authority</c> or <c>Status</c> query values, which are
/// provider-echoed hints at best.
///
/// On <c>Status != OK</c> the attempt is resolved as declined and the verify
/// endpoint is NOT called at all (a decline is a business outcome, not a
/// provider failure to re-check). On <c>Status == OK</c> the server calls the
/// provider's verify endpoint server-to-server with the server-stored
/// authority and amount, and the outcome flows through
/// <see cref="ShopPaymentCompletionService"/> — the only place an attempt or
/// an order ever moves. Code <c>101</c> ("already verified") is reconciled
/// against the attempt's already-stored success reference before anything is
/// trusted: a 101 for an attempt this server never recorded as a success
/// (or one whose stored reference does not match) fails closed.
///
/// Every terminal path answers a <c>302</c> to the configured frontend
/// payment-result route carrying only the opaque result token and the route
/// context — no order number, amount, provider payload or error detail is
/// echoed into the redirect. A forged, tampered, expired or absent state
/// answers the one generic 404, indistinguishable from a missing order.
/// </summary>
internal static class ZarinPalCallbackFeature
{
    public static IEndpointRouteBuilder MapZarinPalCallbackFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/shop/{tenantId}/payments/zarinpal/callback", async (
            string tenantId,
            string? authority,
            string? status,
            string? state,
            HttpContext httpContext,
            IShopPaymentGatewayResolver gatewayResolver,
            ShopPaymentCompletionService completionService,
            ZarinPalCallbackStateProtector stateProtector,
            ZarinPalOptions options,
            ShopDbContext db,
            TimeProvider timeProvider,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            // An explicit category: the feature type is static and cannot be
            // used as ILogger<T>'s type argument, and logging under the
            // gateway's category would misattribute callback decisions.
            var logger = loggerFactory.CreateLogger(
                "TenantForge.Modules.Shop.Features.Payments.ZarinPal.Callback");
            try
            {
                // B046: bound the callback's query-string length before any
                // model-binding work. A query string above the bound is
                // rejected with a generic 413 — the forged or truncated
                // state is never parsed, and no request-specific value is
                // echoed.
                var queryLength = httpContext.Request.QueryString.Value?.Length ?? 0;
                if (queryLength > RateLimiting.ShopRateLimitOptions.MaxCallbackQueryLength)
                {
                    return CallbackTooLargeProblem();
                }

                return await HandleCallbackAsync(
                    tenantId, status, state, gatewayResolver, completionService,
                    stateProtector, options, db, timeProvider, logger, ct);
            }
            catch (ZarinPalProviderUnavailableException exception)
            {
                // The provider's verify endpoint was unreachable or returned a
                // malformed body. This is NOT a payment outcome: the attempt
                // is left untouched (still Initiated) so a later callback or
                // reconciliation can still complete it — a network hiccup
                // must never mark a payment Paid or Failed. A safe 503
                // carrying only the stable code.
                return Results.Problem(new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Type = exception.StableCode,
                    Title = "Payment provider unavailable",
                    Detail = "The payment provider could not be verified right now. The payment was not recorded; try again."
                });
            }
        });

        return endpoints;
    }

    private static async Task<IResult> HandleCallbackAsync(
        string tenantId, string? status, string? state,
        IShopPaymentGatewayResolver gatewayResolver,
        ShopPaymentCompletionService completionService,
        ZarinPalCallbackStateProtector stateProtector,
        ZarinPalOptions options,
        ShopDbContext db, TimeProvider timeProvider,
        ILogger logger, CancellationToken ct)
    {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return PaymentNotFoundProblem();

            // The signed state is the ONLY trusted input. A missing, tampered
            // or expired token is a miss, exactly like a wrong one.
            var callbackState = stateProtector.Unprotect(state);
            if (callbackState is null)
            {
                return PaymentNotFoundProblem();
            }

            // The state names the tenant and order it was minted for; a
            // callback that does not line up with its own route is a miss
            // (the state can only have been created by this server, so a
            // mismatch means the query string was spliced).
            if (callbackState.TenantId != tenantId)
            {
                return PaymentNotFoundProblem();
            }

            if (!TsidId.TryParse(callbackState.OrderId, out var orderTsid)) return PaymentNotFoundProblem();

            // Load the order (tenant-first) and the exact attempt the state
            // names. The attempt is looked up by its own id — NOT by the
            // browser-echoed authority — so a forged authority can never
            // select a different attempt.
            var order = await db.Orders.AsNoTracking()
                .SingleOrDefaultAsync(o => o.Id == orderTsid && o.TenantId == tenantTsid, ct);

            if (order is null)
            {
                return PaymentNotFoundProblem();
            }

            Tsid attemptTsid;
            if (!TsidId.TryParse(callbackState.AttemptId, out attemptTsid)) return PaymentNotFoundProblem();

            var attempt = await db.PaymentAttempts.AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == attemptTsid && a.OrderId == orderTsid, ct);

            if (attempt is null)
            {
                return PaymentNotFoundProblem();
            }

            // The gateway is the configured provider; a host that is not on
            // ZarinPal (or whose provider configuration is broken) refuses the
            // callback with the same generic 404 — the route's existence never
            // reveals which provider a host runs.
            IShopPaymentGateway gateway;
            try
            {
                gateway = gatewayResolver.Resolve();
            }
            catch (InvalidOperationException)
            {
                return PaymentNotFoundProblem();
            }

            if (!string.Equals(gateway.Provider, ZarinPalPaymentGateway.ProviderName, StringComparison.Ordinal))
            {
                return PaymentNotFoundProblem();
            }

            // The authoritative authority and amount for verification are the
            // attempt's stored values — never the browser-echoed query values.
            var verification =
                string.Equals(status, "OK", StringComparison.Ordinal)
                    ? await VerifyServerStoredValuesAsync(gateway, attempt.GatewayReference, attempt.AmountSnapshot, ct)
                    : // Status != OK (or absent/malformed): a decline. The
                      // verify endpoint is NOT called at all.
                      new GatewayVerification(GatewayOutcome.Failed, null, "payment_declined");

            // Code 101 = "already verified". It is trusted ONLY when it refers
            // to a success this server already recorded for this exact attempt
            // (the attempt is already Succeeded AND the returned reference
            // matches the stored success reference). That case is a benign
            // duplicate: the completion service replays the stored outcome and
            // the browser is told the payment succeeded. Every other 101 —
            // a reference this server never stored, or one arriving for an
            // attempt that is not a recorded success — fails closed: it is
            // converted to a failure outcome, and the browser is told the
            // payment did not succeed. Nothing is ever double-applied.
            if (verification.VerifyCode == 101
                && !(attempt.Status == ShopPaymentAttemptStatus.Succeeded
                     && string.Equals(verification.ReferenceId, attempt.ProviderReference, StringComparison.Ordinal)))
            {
                // Structured identifiers only — no secrets, no authority.
                logger.LogWarning(
                    "ZarinPal callback 101 rejected (no matching stored success): tenantId={TenantId} orderId={OrderId} attemptId={AttemptId}.",
                    tenantTsid.ToLong(), orderTsid.ToLong(), attemptTsid.ToLong());
                verification = new GatewayVerification(GatewayOutcome.Failed, null, "already_verified_mismatch");
            }

            // The completion service is the ONLY place the verified outcome
            // becomes an attempt/order transition. It re-checks tenant,
            // order, amount and provider under row locks and applies the
            // change exactly once.
            var completion = await completionService.VerifyAsync(
                tenantId, callbackState.OrderId, attempt.GatewayReference,
                gateway.Provider, verification, timeProvider, ct);

            if (completion.OrderNumber is null)
            {
                return PaymentNotFoundProblem();
            }

            // The order's own status decides the browser-facing outcome — not
            // the raw gateway outcome. A success for an order that is no
            // longer payable (late/duplicate) leaves the order where it is
            // (Cancelled, …), which is a declined redirect.
            var approved = completion.OrderStatus == nameof(ShopOrderStatus.Paid);
            return RedirectResult(options, tenantId, orderTsid, callbackState.CallbackToken, approved);
    }

    private static async Task<GatewayVerification> VerifyServerStoredValuesAsync(
        IShopPaymentGateway gateway, string storedAuthority, decimal storedAmount, CancellationToken ct)
    {
        try
        {
            // Both the authority and the amount come from the server's stored
            // attempt — the browser-echoed values are never used here.
            return await gateway.VerifyAsync(
                new PaymentVerificationRequest(storedAuthority, new Dictionary<string, string>(), storedAmount),
                ct);
        }
        catch (ZarinPalProviderException exception)
        {
            // The provider could not be verified right now (timeout, 5xx,
            // malformed body). This is NOT a payment outcome: the attempt is
            // left untouched (still Initiated) so a later callback or
            // reconciliation can still complete it. A safe 503 carrying only
            // the stable code — never the provider's response text.
            throw new ZarinPalProviderUnavailableException(exception.StableCode);
        }
    }

    /// <summary>
    /// The 302 to the configured frontend payment-result route. Only the
    /// opaque result token (the B044 raw callback token, re-derived from the
    /// signed state) and the route context travel in the URL — no order
    /// number, amount, provider payload or error detail.
    /// </summary>
    private static IResult RedirectResult(
        ZarinPalOptions options, string tenantId, Tsid orderId, string callbackToken, bool approved)
    {
        var location = new Uri(
            options.FrontendResultBaseUrl,
            $"/shop/{tenantId}/payment-result?outcome={(approved ? "approved" : "declined")}&token={callbackToken}");
        return Results.Redirect(location.ToString(), permanent: false, preserveMethod: false);
    }

    private static IResult PaymentNotFoundProblem() => Results.Problem(
        title: "Payment not found",
        detail: "No payment was found.",
        statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// B046: the one generic 413 for a callback whose query string exceeds
    /// the configured bound. It names only the size bound — never the tenant,
    /// the (forged) state, or any other request value.
    /// </summary>
    private static IResult CallbackTooLargeProblem() => Results.Problem(
        type: "shop_callback_too_large",
        title: "Callback too large",
        detail: "The callback request exceeds the permitted size.",
        statusCode: StatusCodes.Status413PayloadTooLarge);
}

/// <summary>
/// B045: raised by the callback route when the provider's verify endpoint
/// could not be reached or parsed. The endpoint catches it and answers a safe
/// 503 that leaves the attempt untouched — a provider outage is not a payment
/// outcome.
/// </summary>
internal sealed class ZarinPalProviderUnavailableException(string stableCode) : Exception(stableCode)
{
    public string StableCode { get; } = stableCode;
}
