using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TenantForge.Modules.Shop.Features.Carts;

internal sealed class ShopCartCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ShopCartCleanupWorker> logger) : BackgroundService
{
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(ShopConfig.GetCartCleanupIntervalSeconds(configuration));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                var expiryService = scope.ServiceProvider.GetRequiredService<IShopCartExpiryService>();
                var expired = await expiryService.ExpireDueAsync(BatchSize, stoppingToken);
                if (expired > 0)
                {
                    logger.LogInformation("Expired {ExpiredCartCount} abandoned Shop carts.", expired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Shop cart cleanup failed.");
            }
        }
    }
}
