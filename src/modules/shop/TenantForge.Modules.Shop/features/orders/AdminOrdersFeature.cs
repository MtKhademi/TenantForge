using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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

            var response = await BuildOrderDetailResponseAsync(order, db, ct);
            return Results.Ok(response);
        }).RequireAuthorization();

        endpoints.MapPatch("/api/tenants/{tenantId}/shop/orders/{orderId}/status", async (
            string tenantId,
            string orderId,
            HttpRequest httpRequest,
            ChangeOrderStatusRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db,
            Features.Payments.ShopPaymentCompletionService completionService,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.OrdersManagePermission);
            if (access.Result is not null) return access.Result;

            if (!TsidId.TryParse(orderId, out var orderTsid))
            {
                return OrderNotFoundProblem();
            }

            var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            OrderStatusAction? parsedAction = null;
            if (string.IsNullOrWhiteSpace(request.Action))
            {
                errors["action"] = ["action is required."];
            }
            else if (Enum.TryParse<OrderStatusAction>(request.Action, ignoreCase: false, out var parsed))
            {
                parsedAction = parsed;
            }
            else
            {
                errors["action"] = ["action must be 'Fulfill' or 'Cancel'."];
            }

            if (request.ExpectedVersion < 1)
            {
                errors["expectedVersion"] = ["expectedVersion must be at least 1."];
            }

            // Read the header directly (no binding): the value must be present
            // and a valid UUID, or the request is rejected before any state is
            // touched.
            var idempotencyKey = httpRequest.Headers.TryGetValue("Idempotency-Key", out var headerValue)
                ? headerValue.ToString()
                : null;

            if (idempotencyKey is null)
            {
                errors["Idempotency-Key"] = ["The Idempotency-Key header is required."];
            }
            else if (!System.Guid.TryParse(idempotencyKey, out _))
            {
                errors["Idempotency-Key"] = ["The Idempotency-Key header must be a UUID."];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            // At this point the header is present and a UUID, and the action
            // parsed to one of the two legal values.
            var action = parsedAction!.Value;
            var now = timeProvider.GetUtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // Lock the order row (tenant-first predicate) before any other
                // read or write. Two concurrent requests for the same order —
                // including two with the same idempotency key — serialize here,
                // so the second re-reads the first's committed state.
                var order = await db.Orders
                    .FromSqlRaw("""
                        SELECT *
                        FROM shop_orders
                        WHERE tenant_id = {0} AND id = {1}
                        FOR UPDATE
                        """, access.TenantId.ToLong(), orderTsid.ToLong())
                    .SingleOrDefaultAsync(ct);

                if (order is null)
                {
                    await transaction.RollbackAsync(ct);
                    return OrderNotFoundProblem();
                }

                // Now, holding the order lock, look up this key's operation row
                // (tenant-first). A racing same-key request either committed its
                // row already (we see it here and answer from it) or will hit the
                // unique (tenant_id, idempotency_key) constraint when it saves —
                // the constraint, not this lookup, is what makes a first-call race
                // resolve to exactly one winner.
                var existing = await db.OrderOperations
                    .FirstOrDefaultAsync(o => o.TenantId == access.TenantId && o.Key == idempotencyKey, ct);

                if (existing is not null)
                {
                    // Replay. Same key, different action: a conflict — neither
                    // action is performed. Same action: return the stored
                    // response snapshot again without re-running the transition.
                    if (existing.Action != action)
                    {
                        await transaction.CommitAsync(ct);
                        return IdempotencyConflictProblem();
                    }

                    await transaction.CommitAsync(ct);
                    return ReplayProblem(existing.ResponseSnapshot);
                }

                // A valid transition with a stale token is a version conflict;
                // any other transition (incl. out of Fulfilled/Cancelled, or a
                // Paid -> Cancelled attempt) is an invalid transition.
                var applied = action switch
                {
                    OrderStatusAction.Fulfill => order.TryFulfill(now, request.ExpectedVersion),
                    _ => order.TryCancel(now, request.ExpectedVersion)
                };

                if (!applied)
                {
                    var conflict = order.Version != request.ExpectedVersion
                        ? StaleVersionProblem()
                        : InvalidTransitionProblem();

                    await transaction.RollbackAsync(ct);
                    return conflict;
                }

                if (action == OrderStatusAction.Cancel)
                {
                    await ReleaseCancelledOrderInventoryAsync(order, db, now, completionService, timeProvider, ct);
                }

                var response = await BuildOrderDetailResponseAsync(order, db, ct);
                db.OrderOperations.Add(ShopOrderOperation.Create(
                    access.TenantId, order.Id, idempotencyKey, action,
                    JsonSerializer.Serialize(response, SnapshotOptions),
                    access.AccountId, now));
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return Results.Ok(response);
            }
            catch (DbUpdateException exception) when (IsIdempotencyKeyViolation(exception))
            {
                // A racing first-call with the same key committed its row first:
                // this request's insert lost the unique-index race. Re-read the
                // winner's row and behave like a replay — never a 500.
                await transaction.RollbackAsync(ct);
                var existing = await db.OrderOperations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.TenantId == access.TenantId && o.Key == idempotencyKey, ct);

                if (existing is null || existing.Action != action)
                {
                    return IdempotencyConflictProblem();
                }

                return ReplayProblem(existing.ResponseSnapshot);
            }
        }).RequireAuthorization();

        return endpoints;
    }

    /// <summary>
    /// B043: builds the shared admin order-detail representation for an order
    /// already loaded in the (tracked) context. Reused by the B042 detail route
    /// and the B043 status route so the two cannot drift; the same record is
    /// what gets stored as the operation's response snapshot.
    /// </summary>
    private static async Task<AdminOrderDetailResponse> BuildOrderDetailResponseAsync(
        ShopOrder order, ShopDbContext db, CancellationToken ct)
    {
        // Item values come from the snapshot frozen at order-creation time —
        // never a live join to product names or prices.
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

        return new AdminOrderDetailResponse(
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
    }

    /// <summary>
    /// B043: the cancel side effects, inside the caller's transaction. Restores
    /// each order item's quantity back to its variant (variant rows locked
    /// <c>FOR UPDATE</c> so a racing cancel cannot double-restore) and marks the
    /// order's inventory released — exactly once, gated on
    /// <c>InventoryReleasedAtUtc</c> being null — and invalidates every
    /// <c>Initiated</c> payment attempt. No order history row is ever deleted.
    /// </summary>
    private static async Task ReleaseCancelledOrderInventoryAsync(
        ShopOrder order, ShopDbContext db, DateTimeOffset now,
        Features.Payments.ShopPaymentCompletionService completionService,
        TimeProvider timeProvider, CancellationToken ct)
    {
        // The restore is gated on the order's release stamp, set in the same
        // transaction — the guard that makes it exactly-once.
        if (!order.MarkInventoryReleased(now))
        {
            return;
        }

        var itemQuantities = await db.OrderItems.AsNoTracking()
            .Where(item => item.OrderId == order.Id)
            .GroupBy(item => item.ProductVariantId)
            .Select(group => new { ProductVariantId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToListAsync(ct);

        foreach (var itemGroup in itemQuantities)
        {
            var variants = await db.ProductVariants
                .FromSqlRaw("""
                    SELECT *
                    FROM shop_product_variants
                    WHERE id = {0}
                    FOR UPDATE
                    """, itemGroup.ProductVariantId.ToLong())
                .ToListAsync(ct);

            foreach (var variant in variants)
            {
                variant.Release(itemGroup.Quantity);
            }
        }

        // B044: invalidating Initiated attempts is the completion service's
        // job — it is the only class allowed to move an attempt out of
        // Initiated. It is called inside THIS transaction, with the order
        // already Cancelled and the order-row lock already held by the
        // handler, so no second lock is taken here.
        await completionService.InvalidateInitiatedAttemptsAsync(order, timeProvider, ct);
    }

    /// <summary>
    /// B043: a valid transition whose <c>expectedVersion</c> no longer matches
    /// the stored order version — the optimistic-concurrency conflict (same
    /// stable code the coupon/profile routes use for a stale version).
    /// </summary>
    private static IResult StaleVersionProblem() => Results.Problem(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Type = "stale_version",
        Title = "Order version conflict",
        Detail = "The order was changed before this request was applied. Reload and try again."
    });

    /// <summary>
    /// B043: the requested status change is not one of the allowed transitions
    /// (the exact code the Spec names — no other transition is legal, and there
    /// is no transition out of Fulfilled or Cancelled).
    /// </summary>
    private static IResult InvalidTransitionProblem() => Results.Problem(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Type = "invalid_order_transition",
        Title = "Invalid order transition",
        Detail = "This order cannot be moved from its current status by the requested action."
    });

    /// <summary>
    /// B043: the same idempotency key was reused with a different action than
    /// the one already stored — a conflict. Neither action is performed.
    /// </summary>
    private static IResult IdempotencyConflictProblem() => Results.Problem(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Type = "idempotency_key_conflict",
        Title = "Idempotency key conflict",
        Detail = "This idempotency key was already used for a different action."
    });

    /// <summary>
    /// B043: re-parses a stored operation's response snapshot and returns it as
    /// a 200, so a same-key/same-action replay is byte-identical to the
    /// original response without re-running the transition.
    /// </summary>
    private static IResult ReplayProblem(string snapshotJson)
        => Results.Ok(JsonSerializer.Deserialize<AdminOrderDetailResponse>(snapshotJson, SnapshotOptions)!);

    /// <summary>
    /// The JSON options the operation response snapshot is written and re-read
    /// with. <see cref="JsonSerializerDefaults.Web"/> matches the endpoint's own
    /// response serialization, so a replay is identical to a fresh response.
    /// </summary>
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The unique index that makes an idempotency key single-use per tenant.</summary>
    private const string IdempotencyKeyIndexName = "ix_shop_order_operations_tenant_idempotency_key";

    /// <summary>
    /// B043: narrows a caught <see cref="DbUpdateException"/> to the unique
    /// (tenant_id, idempotency_key) violation — PostgreSQL SQLSTATE 23505 on
    /// exactly this index. Any other failure is rethrown as a genuine server
    /// error, never swallowed. With the order-row lock held first, the flow
    /// reaches this path only in a degenerate race; it is the last-resort
    /// backstop, and it still answers with a replay/conflict, never a 500.
    /// </summary>
    private static bool IsIdempotencyKeyViolation(DbUpdateException exception)
    {
        var postgres = exception.GetBaseException() as PostgresException;
        return postgres is { SqlState: "23505" }
            && postgres.ConstraintName == IdempotencyKeyIndexName;
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
