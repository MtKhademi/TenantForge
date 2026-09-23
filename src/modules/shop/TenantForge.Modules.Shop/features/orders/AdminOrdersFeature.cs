using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Authorization;
using TenantForge.Modules.Shop.Features.Pagination;
using TenantForge.Modules.Shop.Infrastructure;
using TSID.Creator.NET;

namespace TenantForge.Modules.Shop.Features.Orders;

/// <summary>
/// B042: the tenant operator's read-only order surface (list + detail).
/// Both routes are gated by <see cref="ShopAuthorization.OrdersViewPermission"/>
/// — unlike the membership-only admin routes, reading a tenant's guest orders
/// is a permission decision, not just a membership one. The detail route
/// answers malformed, foreign-tenant and missing order ids with one identical
/// generic 404 so the caller cannot tell which case it hit.
/// </summary>
internal static class AdminOrdersFeature
{
    private const int MaxSearchLength = 100;
    private static readonly TimeSpan MaxDateRange = TimeSpan.FromDays(366);

    /// <summary>
    /// B042: temporary cap on the payment-attempt history an admin order
    /// detail returns. The gateway-neutral payment-lifecycle task (B044)
    /// enforces a smaller, lifecycle-aware bound; until then the 20 newest
    /// keeps a churning order's payload bounded without inventing that policy.
    /// </summary>
    private const int MaxPaymentAttempts = 20;

    public static IEndpointRouteBuilder MapAdminOrdersFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenants/{tenantId}/shop/orders", async (
            string tenantId,
            HttpRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db,
            CancellationToken ct) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.OrdersViewPermission);
            if (access.Result is not null) return access.Result;

            if (!PaginationSupport.TryBind(request, out var page, out var errors))
            {
                return Results.ValidationProblem(errors);
            }

            if (!TryBindFilters(request, errors, out var search, out var status, out var fromUtc, out var toUtc))
            {
                return Results.ValidationProblem(errors);
            }

            // TenantId is the first predicate on every tenant-owned query.
            var query = db.Orders.AsNoTracking()
                .Where(order => order.TenantId == access.TenantId);

            if (search is not null)
            {
                // Case-insensitive contains on all four searchable columns; a
                // row matches when any one of them contains `q`. The captured
                // pattern is a bound parameter — never string-built SQL.
                var pattern = $"%{search}%";
                query = query.Where(order =>
                    EF.Functions.ILike(order.OrderNumber, pattern)
                    || EF.Functions.ILike(order.TrackingCode, pattern)
                    || EF.Functions.ILike(order.CustomerPhone, pattern)
                    || EF.Functions.ILike(order.CustomerName, pattern));
            }

            if (status is not null)
            {
                var matchedStatus = status.Value;
                query = query.Where(order => order.Status == matchedStatus);
            }

            // Inclusive start, exclusive end — both in UTC.
            if (fromUtc is not null)
            {
                var from = fromUtc.Value;
                query = query.Where(order => order.CreatedAtUtc >= from);
            }

            if (toUtc is not null)
            {
                var to = toUtc.Value;
                query = query.Where(order => order.CreatedAtUtc < to);
            }

            // Stable order always: newest first, Id breaking CreatedAtUtc
            // ties so a page walk cannot reshuffle between requests.
            var ordered = query
                .OrderByDescending(order => order.CreatedAtUtc)
                .ThenByDescending(order => order.Id);

            var (rows, pagination) = await PaginationSupport.PageAsync(ordered, page, ct);

            var response = new AdminOrderListResponse(
                rows.Select(order => new AdminOrderSummaryResponse(
                    TsidId.Format(order.Id),
                    order.OrderNumber,
                    order.CustomerName,
                    order.CustomerPhone,
                    order.Status.ToString(),
                    order.GrandTotal,
                    order.CreatedAtUtc)).ToList(),
                pagination);

            return Results.Ok(response);
        }).RequireAuthorization();

        endpoints.MapGet("/api/tenants/{tenantId}/shop/orders/{orderId}", async (
            string tenantId,
            string orderId,
            ClaimsPrincipal principal,
            ShopDbContext db,
            CancellationToken ct) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.OrdersViewPermission);
            if (access.Result is not null) return access.Result;

            // After authorization, malformed, foreign-tenant and missing ids
            // collapse into one identical 404: a malformed id never reaches
            // the database, and the lookup is tenant-scoped so a well-formed
            // id from another tenant simply cannot match.
            if (!TsidId.TryParse(orderId, out var orderTsid))
            {
                return OrderNotFoundProblem();
            }

            var order = await db.Orders.AsNoTracking()
                .SingleOrDefaultAsync(order => order.Id == orderTsid && order.TenantId == access.TenantId, ct);

            if (order is null)
            {
                return OrderNotFoundProblem();
            }

            // Item values come from the snapshot frozen at order-creation
            // time — never a live join to product names or prices.
            var items = await db.OrderItems.AsNoTracking()
                .Where(item => item.OrderId == order.Id)
                .Select(item => new OrderLookupItemResponse(
                    item.ProductNameSnapshot, item.VariantLabelSnapshot, item.UnitPrice, item.Quantity))
                .ToListAsync(ct);

            var paymentAttempts = await db.PaymentAttempts.AsNoTracking()
                .Where(attempt => attempt.OrderId == order.Id)
                .OrderByDescending(attempt => attempt.CreatedAtUtc)
                .ThenByDescending(attempt => attempt.Id)
                .Take(MaxPaymentAttempts)
                .Select(attempt => new AdminPaymentAttemptResponse(
                    TsidId.Format(attempt.Id),
                    attempt.Status.ToString(),
                    attempt.CreatedAtUtc))
                .ToListAsync(ct);

            var response = new AdminOrderDetailResponse(
                TsidId.Format(order.Id),
                order.OrderNumber,
                order.TrackingCode,
                order.Status.ToString(),
                new AdminOrderCustomerResponse(
                    order.CustomerName,
                    order.CustomerPhone,
                    order.ShippingProvince,
                    order.ShippingCity,
                    order.ShippingAddressLine,
                    order.ShippingPostalCode),
                new AdminOrderTotalsResponse(
                    order.SubTotal,
                    order.ShippingCost,
                    order.DiscountAmount,
                    order.GrandTotal),
                items,
                paymentAttempts,
                order.Version,
                order.CreatedAtUtc);

            return Results.Ok(response);
        }).RequireAuthorization();

        return endpoints;
    }

    /// <summary>
    /// The one, identical 404 for malformed, cross-tenant and missing order
    /// ids — reused, never rebuilt inline, so the three cases cannot drift
    /// into different bodies by accident. Deliberately does not reuse the
    /// anonymous tracking-code lookup's validation or response.
    /// </summary>
    private static IResult OrderNotFoundProblem() => Results.Problem(
        title: "Order not found",
        detail: "No order was found.",
        statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// Binds and validates the optional list filters, appending any problem
    /// to the shared <paramref name="errors"/> dictionary. `q` is trimmed and
    /// capped at 100 characters before it ever reaches the database;
    /// `status` must name a defined order status exactly (case-sensitive,
    /// matching the wire values); the date range must be well-formed,
    /// ordered and at most 366 days wide. A blank `q` is ignored (same as
    /// absent).
    /// </summary>
    private static bool TryBindFilters(
        HttpRequest request,
        Dictionary<string, string[]> errors,
        out string? search,
        out ShopOrderStatus? status,
        out DateTimeOffset? fromUtc,
        out DateTimeOffset? toUtc)
    {
        search = null;
        status = null;
        fromUtc = null;
        toUtc = null;

        var rawSearch = request.Query["q"].ToString().Trim();
        if (rawSearch.Length > MaxSearchLength)
        {
            errors["q"] = [$"q must be at most {MaxSearchLength} characters."];
        }
        else if (rawSearch.Length > 0)
        {
            search = rawSearch;
        }

        var rawStatus = request.Query["status"].ToString().Trim();
        if (rawStatus.Length > 0)
        {
            if (!Enum.TryParse<ShopOrderStatus>(rawStatus, ignoreCase: false, out var parsedStatus))
            {
                errors["status"] = ["status must be one of the defined order statuses."];
            }
            else
            {
                status = parsedStatus;
            }
        }

        var rawFrom = request.Query["fromUtc"].ToString().Trim();
        if (rawFrom.Length > 0)
        {
            if (!DateTimeOffset.TryParse(rawFrom, out var parsedFrom))
            {
                errors["fromUtc"] = ["fromUtc must be a parseable date-time."];
            }
            else
            {
                fromUtc = parsedFrom.ToUniversalTime();
            }
        }

        var rawTo = request.Query["toUtc"].ToString().Trim();
        if (rawTo.Length > 0)
        {
            if (!DateTimeOffset.TryParse(rawTo, out var parsedTo))
            {
                errors["toUtc"] = ["toUtc must be a parseable date-time."];
            }
            else
            {
                toUtc = parsedTo.ToUniversalTime();
            }
        }

        if (fromUtc is not null && toUtc is not null && toUtc.Value - fromUtc.Value > MaxDateRange)
        {
            errors["toUtc"] = ["The date range may span at most 366 days."];
        }

        return errors.Count == 0;
    }
}
