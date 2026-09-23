namespace TenantForge.Modules.Shop.Features.Orders;

/// <summary>
/// B042: the tenant operator's order list/detail wire shapes. These are
/// read-only views of the order snapshot — nothing here joins live product
/// names or prices. The anonymous guest lookup
/// (<c>OrderLookupResponse</c>) keeps its own flat shape; the admin records
/// carry the <c>Admin</c> prefix so the two routes stay unambiguous.
/// </summary>
public sealed record AdminOrderSummaryResponse(
    string Id, string OrderNumber, string CustomerName, string CustomerPhone,
    string Status, decimal GrandTotal, DateTimeOffset CreatedAtUtc);

public sealed record AdminOrderListResponse(
    IReadOnlyList<AdminOrderSummaryResponse> Orders,
    TenantForge.Modules.Shop.Features.Pagination.PaginationMetadata Pagination);

/// <summary>
/// The point-in-time customer + address snapshot. Mirrors the customer
/// fields the anonymous <c>OrderLookupResponse</c> already exposes
/// (province/city/address/postal), per B042's "copy their exact field
/// lists" rule — the lookup has no separate customer record to copy.
/// </summary>
public sealed record AdminOrderCustomerResponse(
    string Name, string Phone, string ShippingProvince, string ShippingCity,
    string ShippingAddressLine, string ShippingPostalCode);

/// <summary>The order's frozen totals — the same four values the lookup returns.</summary>
public sealed record AdminOrderTotalsResponse(
    decimal SubTotal, decimal ShippingCost, decimal DiscountAmount, decimal GrandTotal);

/// <summary>
/// One payment-attempt summary. B042 deliberately exposes the minimal
/// id/status/created triple — the payment-lifecycle task (B044) owns a
/// richer attempt contract and a smaller cap.
/// </summary>
public sealed record AdminPaymentAttemptResponse(
    string Id, string Status, DateTimeOffset CreatedAtUtc);

public sealed record AdminOrderDetailResponse(
    string Id, string OrderNumber, string TrackingCode, string Status,
    AdminOrderCustomerResponse Customer, AdminOrderTotalsResponse Totals,
    IReadOnlyList<OrderLookupItemResponse> Items,
    IReadOnlyList<AdminPaymentAttemptResponse> PaymentAttempts,
    int Version, DateTimeOffset CreatedAtUtc);

/// <summary>
/// B043: the request for <c>PATCH …/orders/{orderId}/status</c>. <see
/// cref="Action"/> is nullable on the wire so the handler can distinguish an
/// absent value (a field error) from a present-but-unknown one, and must name
/// exactly <c>Fulfill</c> or <c>Cancel</c>. <see cref="ExpectedVersion"/> is the
/// optimistic-concurrency token the client last saw for this order's
/// <c>Version</c> — it is compared, never trusted, against the stored value.
/// </summary>
public sealed record ChangeOrderStatusRequest(string? Action, int ExpectedVersion);
