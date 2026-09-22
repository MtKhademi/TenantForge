using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Carts;

internal interface IShopCartExpiryService
{
    Task<CartLeaseResult> EnsureActiveAsync(Tsid tenantId, Tsid cartId, CancellationToken ct);
    Task<int> ExpireDueAsync(int batchSize, CancellationToken ct);
}

internal enum CartLeaseOutcome
{
    StillActive,
    JustExpired,
    AlreadyExpired,
    Converted
}

internal sealed record CartLeaseResult(CartLeaseOutcome Outcome, ShopCart? Cart);

internal sealed class ShopCartExpiryService(ShopDbContext db, TimeProvider timeProvider) : IShopCartExpiryService
{
    public async Task<CartLeaseResult> EnsureActiveAsync(Tsid tenantId, Tsid cartId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var cart = await LockCartAsync(tenantId, cartId, ct);
        if (cart is null)
        {
            await transaction.CommitAsync(ct);
            return new CartLeaseResult(CartLeaseOutcome.StillActive, null);
        }

        var result = cart.Status switch
        {
            ShopCartStatus.Expired => new CartLeaseResult(CartLeaseOutcome.AlreadyExpired, cart),
            ShopCartStatus.Converted => new CartLeaseResult(CartLeaseOutcome.Converted, cart),
            _ when now >= cart.ExpiresAtUtc => new CartLeaseResult(
                await ExpireLockedCartAsync(cart, now, ct) ? CartLeaseOutcome.JustExpired : CartLeaseOutcome.AlreadyExpired,
                cart),
            _ => new CartLeaseResult(CartLeaseOutcome.StillActive, cart)
        };
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<int> ExpireDueAsync(int batchSize, CancellationToken ct)
    {
        if (batchSize <= 0)
        {
            return 0;
        }

        var now = timeProvider.GetUtcNow();
        var dueCarts = await db.Carts.AsNoTracking()
            .Where(cart => cart.Status == ShopCartStatus.Active && cart.ExpiresAtUtc <= now)
            .OrderBy(cart => cart.ExpiresAtUtc)
            .ThenBy(cart => cart.Id)
            .Select(cart => new { cart.TenantId, cart.Id })
            .Take(batchSize)
            .ToListAsync(ct);

        var expired = 0;
        foreach (var dueCart in dueCarts)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var cart = await LockCartAsync(dueCart.TenantId, dueCart.Id, ct);
            if (cart is null || cart.Status != ShopCartStatus.Active || cart.ExpiresAtUtc > now)
            {
                await transaction.CommitAsync(ct);
                continue;
            }

            if (await ExpireLockedCartAsync(cart, now, ct))
            {
                expired++;
            }

            await transaction.CommitAsync(ct);
        }

        return expired;
    }

    private async Task<ShopCart?> LockCartAsync(Tsid tenantId, Tsid cartId, CancellationToken ct)
        => await db.Carts
            .FromSqlRaw("""
                SELECT *
                FROM shop_carts
                WHERE tenant_id = {0} AND id = {1}
                FOR UPDATE
                """, tenantId.ToLong(), cartId.ToLong())
            .SingleOrDefaultAsync(ct);

    private async Task<bool> ExpireLockedCartAsync(ShopCart cart, DateTimeOffset now, CancellationToken ct)
    {
        if (cart.Status != ShopCartStatus.Active)
        {
            return false;
        }

        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        cart.MarkExpired(now);

        var itemGroups = await db.CartItems
            .Where(item => item.CartId == cart.Id)
            .GroupBy(item => item.ProductVariantId)
            .Select(group => new { ProductVariantId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToListAsync(ct);

        foreach (var itemGroup in itemGroups)
        {
            await db.ProductVariants
                .Where(variant => variant.Id == itemGroup.ProductVariantId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(variant => variant.StockQuantity, variant => variant.StockQuantity + itemGroup.Quantity),
                    ct);
        }

        var items = await db.CartItems.Where(item => item.CartId == cart.Id).ToListAsync(ct);
        db.CartItems.RemoveRange(items);
        await db.SaveChangesAsync(ct);

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return true;
    }
}
