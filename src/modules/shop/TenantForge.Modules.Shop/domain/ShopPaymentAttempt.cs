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

    /// <summary>
    /// B043: called inside a cancel's transaction for every attempt still in
    /// <see cref="ShopPaymentAttemptStatus.Initiated"/>. Moving the attempt to
    /// <see cref="ShopPaymentAttemptStatus.Failed"/> is what makes it
    /// un-completable — a late payment callback for the (now cancelled) order
    /// is already answered <c>409</c> by the order-status guard, and even if a
    /// reference were presented, <see cref="TryResolve"/> would refuse to touch
    /// an attempt that is no longer <c>Initiated</c>. Already-resolved attempts
    /// return false and change nothing. No history row is ever deleted.
    /// </summary>
    public bool Invalidate(DateTimeOffset nowUtc)
    {
        if (Status != ShopPaymentAttemptStatus.Initiated)
        {
            return false;
        }

        Status = ShopPaymentAttemptStatus.Failed;
        CallbackReceivedAtUtc = nowUtc.ToUniversalTime();
        return true;
    }
}
