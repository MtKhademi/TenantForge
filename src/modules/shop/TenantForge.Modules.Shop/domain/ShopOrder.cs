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

    public void MarkPaid() => Status = ShopOrderStatus.Paid;

    public void MarkPaymentFailed()
    {
        if (Status == ShopOrderStatus.PendingPayment) return;
        Status = ShopOrderStatus.PendingPayment;
    }
}
