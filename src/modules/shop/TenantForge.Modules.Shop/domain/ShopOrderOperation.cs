using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

/// <summary>
/// B043: the durable record of one order-status action, what made it
/// idempotent, and who did it. There is at most one row per
/// <c>(TenantId, Key)</c> — enforced by a unique database constraint, so the
/// same idempotency key can never be stored twice for the same tenant and two
/// racing first-calls resolve to exactly one winner. The row stores a snapshot
/// of the response that was returned, so a replay returns the same final
/// representation without re-running the transition.
/// </summary>
internal sealed class ShopOrderOperation
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public Tsid OrderId { get; private set; }

    /// <summary>The client-supplied idempotency key (a UUID string).</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>The canonical action that was performed (<c>Fulfill</c> or <c>Cancel</c>).</summary>
    public OrderStatusAction Action { get; private set; }

    /// <summary>
    /// The response that was returned for this action, serialized to JSON. A
    /// replay re-parses and returns this instead of re-mutating the order.
    /// </summary>
    public string ResponseSnapshot { get; private set; } = string.Empty;

    /// <summary>The account that performed the action (the acting operator's id).</summary>
    public Tsid ActorId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private ShopOrderOperation()
    {
    }

    public static ShopOrderOperation Create(
        Tsid tenantId,
        Tsid orderId,
        string key,
        OrderStatusAction action,
        string responseSnapshot,
        Tsid actorId,
        DateTimeOffset nowUtc)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (TsidId.IsDefault(orderId))
        {
            throw new ArgumentException("Order id is required.", nameof(orderId));
        }

        return new ShopOrderOperation
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            OrderId = orderId,
            Key = key,
            Action = action,
            ResponseSnapshot = responseSnapshot,
            ActorId = actorId,
            CreatedAtUtc = nowUtc.ToUniversalTime()
        };
    }
}
