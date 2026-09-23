using Microsoft.EntityFrameworkCore;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Payments;

/// <summary>
/// B044: the resolved outcome of one verification, as seen by the caller.
/// <see cref="Applied"/> is true only when this call performed a write
/// (resolving a live attempt); a duplicate verification for an
/// already-resolved attempt returns <c>Applied: false</c> with the outcome
/// already computed, so the caller answers from state without re-applying
/// side effects. <see cref="OrderNotPayable"/> is true when a SUCCESS
/// verification arrived for an order that is no longer
/// <c>PendingPayment</c> — the attempt was recorded as failed and the order
/// never moved; the caller answers the B043 late-callback conflict (409).
/// <see cref="OrderNumber"/> is null when the order or the attempt behind
/// the authority could not be found — the caller answers its one generic
/// 404.
/// </summary>
internal sealed record PaymentCompletionResult(
    bool Applied, bool OrderNotPayable, string? OrderNumber,
    ShopPaymentAttemptStatus AttemptStatus, string? ProviderReference, string OrderStatus);

/// <summary>
/// B044: the ONLY place in the module that moves a payment attempt out of
/// <see cref="ShopPaymentAttemptStatus.Initiated"/> (to <c>Succeeded</c>,
/// <c>Failed</c> or <c>Invalidated</c>) and the ONLY place that moves an order
/// from <c>PendingPayment</c> to <c>Paid</c>. A gateway's
/// <see cref="GatewayVerification"/> is a hint, never a command: this service
/// re-checks the attempt and the order under row locks, validates that the
/// tenant, order, amount and provider all agree, and applies the transition
/// exactly once.
///
/// Concurrency is closed the same way the module closes every other
/// check-then-write race: both rows are locked <c>FOR UPDATE</c> at the start
/// of one transaction (tenant-first on the order, through its id, since the
/// attempt carries no tenant column of its own), and the write itself is a
/// guarded, version-checked change — a racing duplicate blocks on the lock,
/// re-reads the winner's committed state and returns the already-computed
/// outcome instead of re-applying it.
/// </summary>
internal sealed class ShopPaymentCompletionService(ShopDbContext db)
{
    /// <summary>
    /// Resolves the attempt behind <paramref name="authority"/> against
    /// <paramref name="verification"/>. Validates, in order, before changing
    /// anything: the order exists for the route's tenant; the attempt belongs
    /// to that order; the attempt's provider matches the gateway's; the
    /// attempt's frozen <c>AmountSnapshot</c> matches the order's total; the
    /// attempt is still <c>Initiated</c>; and — for a success — the order is
    /// still <c>PendingPayment</c> (a <c>Cancelled</c>/<c>Fulfilled</c> order
    /// can never become <c>Paid</c>, even for a late or duplicate success).
    /// </summary>
    public async Task<PaymentCompletionResult> VerifyAsync(
        string tenantId, string orderId, string authority,
        string provider, GatewayVerification verification,
        TimeProvider timeProvider, CancellationToken ct)
    {
        if (!TsidId.TryParse(tenantId, out var tenantTsid) || !TsidId.TryParse(orderId, out var orderTsid))
        {
            // The feature answers 404 — malformed ids are indistinguishable
            // from missing ones.
            return new PaymentCompletionResult(false, false, null, ShopPaymentAttemptStatus.Initiated, null, "PendingPayment");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Lock the order row first (tenant-first predicate). The attempt
            // has no TenantId column, so tenant scoping reaches it only
            // through this join — the rule from the Spec's transaction notes.
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
                await transaction.RollbackAsync();
                // The feature answers 404 for this — the same body as a
                // wrong/unknown authority, so the caller cannot tell which
                // half was wrong.
                return new PaymentCompletionResult(false, false, null, ShopPaymentAttemptStatus.Initiated, null, "PendingPayment");
            }

            // Lock the attempt row (the unique gateway_reference is its
            // natural key — a provider can reference an attempt only by it).
            var attempt = await db.PaymentAttempts
                .FromSqlRaw("""
                    SELECT *
                    FROM shop_payment_attempts
                    WHERE order_id = {0} AND gateway_reference = {1} AND provider = {2}
                    FOR UPDATE
                    """, orderTsid.ToLong(), authority, provider)
                .SingleOrDefaultAsync(ct);

            if (attempt is null)
            {
                await transaction.RollbackAsync();
                return new PaymentCompletionResult(false, false, null, ShopPaymentAttemptStatus.Initiated, null, order.Status.ToString());
            }

            // Already resolved (or invalidated by a cancel): this is a
            // duplicate or late callback. Return the already-computed outcome
            // without re-running the transition — the attempt row itself is
            // the record of what happened.
            if (attempt.Status != ShopPaymentAttemptStatus.Initiated)
            {
                await transaction.RollbackAsync();
                return new PaymentCompletionResult(
                    Applied: false,
                    OrderNotPayable: false,
                    OrderNumber: order.OrderNumber,
                    AttemptStatus: attempt.Status,
                    ProviderReference: attempt.ProviderReference,
                    OrderStatus: order.Status.ToString());
            }

            var now = timeProvider.GetUtcNow();

            // The amount must match the frozen snapshot — the server can
            // derive it, so a mismatch is never a caller question to ask.
            // Record it as a failed attempt with a stable code; the attempt
            // must never be left Initiated after a verification was made for
            // it, and the order is never paid on a mismatch.
            if (attempt.AmountSnapshot != order.GrandTotal)
            {
                attempt.TryResolve(false, null, "amount_mismatch", now);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new PaymentCompletionResult(
                    Applied: true,
                    OrderNotPayable: false,
                    OrderNumber: order.OrderNumber,
                    AttemptStatus: attempt.Status,
                    ProviderReference: attempt.ProviderReference,
                    OrderStatus: order.Status.ToString());
            }

            if (verification.Outcome == GatewayOutcome.Succeeded)
            {
                // A success can only ever pay an order that is still waiting
                // for payment. A late/duplicate success for a Cancelled or
                // Fulfilled order records the attempt as Failed with a stable
                // reason; the order is never moved.
                if (order.Status != ShopOrderStatus.PendingPayment)
                {
                    if (attempt.TryResolve(false, null, "order_not_payable", now))
                    {
                        await db.SaveChangesAsync(ct);
                    }

                    await transaction.CommitAsync(ct);
                    return new PaymentCompletionResult(
                        Applied: true,
                        OrderNotPayable: true,
                        OrderNumber: order.OrderNumber,
                        AttemptStatus: attempt.Status,
                        ProviderReference: attempt.ProviderReference,
                        OrderStatus: order.Status.ToString());
                }

                if (!attempt.TryResolve(true, verification.ReferenceId, null, now))
                {
                    await transaction.RollbackAsync();
                    return new PaymentCompletionResult(
                        Applied: false,
                        OrderNotPayable: false,
                        OrderNumber: order.OrderNumber,
                        AttemptStatus: attempt.Status,
                        ProviderReference: attempt.ProviderReference,
                        OrderStatus: order.Status.ToString());
                }

                // The only call to ShopOrder.MarkPaid in the module. Deliberately
                // NO order.BumpVersion(): B043's contract is that the order's
                // version is bumped only by operator mutations (TryCancel /
                // TryFulfill, which are the expectedVersion-gated ones). A
                // payment is a gateway-driven transition applied under the row
                // lock above — it must not silently invalidate an operator's
                // in-flight expectedVersion (the B043 Fulfil flow pays an order
                // at version 1, then fulfils it with expectedVersion 1).
                order.MarkPaid();
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new PaymentCompletionResult(
                    Applied: true,
                    OrderNotPayable: false,
                    OrderNumber: order.OrderNumber,
                    AttemptStatus: attempt.Status,
                    ProviderReference: attempt.ProviderReference,
                    OrderStatus: order.Status.ToString());
            }

            // A declined/failed verification: the attempt records the stable
            // error code and the order stays PendingPayment — payable again
            // with a fresh initiation.
            if (!attempt.TryResolve(false, null, verification.ErrorCode ?? "verification_failed", now))
            {
                await transaction.RollbackAsync();
                return new PaymentCompletionResult(
                    Applied: false,
                    OrderNotPayable: false,
                    OrderNumber: order.OrderNumber,
                    AttemptStatus: attempt.Status,
                    ProviderReference: attempt.ProviderReference,
                    OrderStatus: order.Status.ToString());
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new PaymentCompletionResult(
                Applied: true,
                OrderNotPayable: false,
                OrderNumber: order.OrderNumber,
                AttemptStatus: attempt.Status,
                ProviderReference: attempt.ProviderReference,
                OrderStatus: order.Status.ToString());
        }
        catch
        {
            // An exception here means the transaction has not committed
            // (nothing above awaits past the CommitAsync calls), so the
            // rollback is always valid and the write is never half-applied.
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Called by the cancel transition inside ITS transaction (the caller
    /// already holds the order row lock and has moved it to
    /// <c>Cancelled</c>): invalidates every attempt on that order still in
    /// <see cref="ShopPaymentAttemptStatus.Initiated"/> so a late verification
    /// can no longer complete it. Already-resolved attempts are untouched and
    /// no row is ever deleted.
    /// </summary>
    public async Task InvalidateInitiatedAttemptsAsync(
        ShopOrder order, TimeProvider timeProvider, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var initiated = await db.PaymentAttempts
            .Where(attempt => attempt.OrderId == order.Id && attempt.Status == ShopPaymentAttemptStatus.Initiated)
            .ToListAsync(ct);

        foreach (var attempt in initiated)
        {
            attempt.TryInvalidate(now);
        }

        if (initiated.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
