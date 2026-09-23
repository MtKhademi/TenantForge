using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace TenantForge.Modules.Shop.Features.Payments;

/// <summary>
/// B044: picks the single <see cref="IShopPaymentGateway"/> implementation
/// whose <see cref="IShopPaymentGateway.Provider"/> matches the
/// <c>Shop:Payments:Provider</c> configuration value.
///
/// The gateway is NEVER resolved by registration order: both implementations
/// (Sandbox today, ZarinPal in B045) are registered as scoped services, and
/// this resolver selects exactly one of them by the explicit configuration
/// string. An absent value defaults to <c>Sandbox</c> only in Development
/// (the same default <c>ShopConfig</c> validation applies); outside
/// Development an absent value fails closed. A configured provider with no
/// registered implementation — for example <c>ZarinPal</c> before B045
/// delivers its gateway — is a configuration error and fails closed (an
/// <see cref="InvalidOperationException"/> at the first resolution) rather
/// than silently falling back to the sandbox.
/// </summary>
internal interface IShopPaymentGatewayResolver
{
    IShopPaymentGateway Resolve();
}

internal sealed class ShopPaymentGatewayResolver(
    IEnumerable<IShopPaymentGateway> gateways,
    IConfiguration configuration,
    IHostEnvironment environment) : IShopPaymentGatewayResolver
{
    internal const string ProviderKey = "Shop:Payments:Provider";

    public IShopPaymentGateway Resolve()
    {
        var configured = configuration[ProviderKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"The '{ProviderKey}' configuration value is required and must be one of the registered payment providers.");
            }

            configured = SandboxPaymentGateway.ProviderName;
        }

        var match = gateways.FirstOrDefault(gateway =>
            string.Equals(gateway.Provider, configured, StringComparison.Ordinal));

        if (match is null)
        {
            throw new InvalidOperationException(
                $"No payment gateway is registered for the configured provider '{configured}'.");
        }

        return match;
    }
}
