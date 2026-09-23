using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal enum ShopOrderStatus
{
    PendingPayment,
    Paid,
    Cancelled,
    Fulfilled
}

/// <summary>
/// B043: the exact, closed set of operator order-status actions the
/// <c>PATCH …/orders/{orderId}/status</c> route accepts. Nothing else — the
/// route maps each value to one of the two allowed transitions and rejects
/// any other request as an invalid transition.
/// </summary>
internal enum OrderStatusAction
{
    Fulfill,
    Cancel
}

internal sealed class ShopOrder
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public string OrderNumber { get; private set; } = string.Empty;
    public string TrackingCode { get; private set; } = string.Empty;
    public ShopOrderStatus Status { get; private set; } = ShopOrderStatus.PendingPayment;
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerPhone { get; private set; } = string.Empty;
    public string ShippingProvince { get; private set; } = string.Empty;
    public string ShippingCity { get; private set; } = string.Empty;
    public string ShippingAddressLine { get; private set; } = string.Empty;
    public string ShippingPostalCode { get; private set; } = string.Empty;
    public decimal SubTotal { get; private set; }
    public decimal ShippingCost { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal GrandTotal { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// B042: optimistic-concurrency token for B043's order status actions.
    /// Starts at 1 and is bumped by exactly the mutation that changes the
    /// order (B043); B042 only persisted and returned it, never bumped it.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>B043: set exactly once, when the order moves <c>Paid → Fulfilled</c>.</summary>
    public DateTimeOffset? FulfilledAtUtc { get; private set; }

    /// <summary>B043: set exactly once, when the order moves <c>PendingPayment → Cancelled</c>.</summary>
    public DateTimeOffset? CancelledAtUtc { get; private set; }

    /// <summary>
    /// B043: set exactly once, in the same transaction as a cancel's inventory
    /// restore. A non-null value is the guard that makes the restore happen
    /// exactly once even if cancel were somehow triggered twice.
    /// </summary>
    public DateTimeOffset? InventoryReleasedAtUtc { get; private set; }

    private ShopOrder()
    {
    }

    public static ShopOrder Create(
        Tsid tenantId,
        string orderNumber,
        string trackingCode,
        string customerName,
        string customerPhone,
        string shippingProvince,
        string shippingCity,
        string shippingAddressLine,
        string shippingPostalCode,
        decimal subTotal,
        decimal shippingCost,
        decimal discountAmount,
        DateTimeOffset nowUtc)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new ShopOrder
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            OrderNumber = orderNumber,
            TrackingCode = trackingCode,
            Status = ShopOrderStatus.PendingPayment,
            CustomerName = customerName.Trim(),
            CustomerPhone = customerPhone.Trim(),
            ShippingProvince = shippingProvince.Trim(),
            ShippingCity = shippingCity.Trim(),
            ShippingAddressLine = shippingAddressLine.Trim(),
            ShippingPostalCode = shippingPostalCode.Trim(),
            SubTotal = subTotal,
            ShippingCost = shippingCost,
            DiscountAmount = discountAmount,
            GrandTotal = subTotal - discountAmount + shippingCost,
            CreatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    /// <summary>
    /// B044: moves the order to <c>Paid</c>. Called only by
    /// <c>ShopPaymentCompletionService</c>, inside its transaction and only
    /// after it has verified the attempt, its tenant, its order, its amount
    /// and its provider — a gateway result can never reach this method
    /// directly.
    /// </summary>
    public void MarkPaid() => Status = ShopOrderStatus.Paid;

    /// <summary>B043's mutation path calls this exactly once per successful change.</summary>
    public void BumpVersion() => Version++;

    /// <summary>
    /// B043: the only path out of <c>Paid</c>. Succeeds only when the order is
    /// currently <c>Paid</c> AND <paramref name="expectedVersion"/> matches the
    /// stored <see cref="Version"/>; then sets <c>Fulfilled</c>, stamps
    /// <see cref="FulfilledAtUtc"/> and bumps <see cref="Version"/> exactly once.
    /// Never touches stock or inventory — fulfilment is a status change only.
    /// </summary>
    public bool TryFulfill(DateTimeOffset nowUtc, int expectedVersion)
    {
        if (Status != ShopOrderStatus.Paid || Version != expectedVersion)
        {
            return false;
        }

        Status = ShopOrderStatus.Fulfilled;
        FulfilledAtUtc = nowUtc.ToUniversalTime();
        BumpVersion();
        return true;
    }

    /// <summary>
    /// B043: the only path out of <c>PendingPayment</c> an operator may take.
    /// Succeeds only when the order is currently <c>PendingPayment</c> AND
    /// <paramref name="expectedVersion"/> matches the stored <see cref="Version"/>;
    /// then sets <c>Cancelled</c>, stamps <see cref="CancelledAtUtc"/> and bumps
    /// <see cref="Version"/> exactly once. There is no <c>Paid → Cancelled</c>
    /// path — a paid order cannot be cancelled or refunded until a refund slice
    /// exists (Spec step 16). Inventory release is the feature's job, in the
    /// same transaction (Spec step 13); this method only moves the status.
    /// </summary>
    public bool TryCancel(DateTimeOffset nowUtc, int expectedVersion)
    {
        if (Status != ShopOrderStatus.PendingPayment || Version != expectedVersion)
        {
            return false;
        }

        Status = ShopOrderStatus.Cancelled;
        CancelledAtUtc = nowUtc.ToUniversalTime();
        BumpVersion();
        return true;
    }

    /// <summary>
    /// B043: the exactly-once inventory-release guard. Stamps
    /// <see cref="InventoryReleasedAtUtc"/> only when it is still null — a
    /// second call (the same key replayed through a race, or cancel triggered
    /// twice) sees the stamp and does nothing, so stock is restored at most
    /// once for the life of the order.
    /// </summary>
    public bool MarkInventoryReleased(DateTimeOffset nowUtc)
    {
        if (InventoryReleasedAtUtc is not null)
        {
            return false;
        }

        InventoryReleasedAtUtc = nowUtc.ToUniversalTime();
        return true;
    }
}
