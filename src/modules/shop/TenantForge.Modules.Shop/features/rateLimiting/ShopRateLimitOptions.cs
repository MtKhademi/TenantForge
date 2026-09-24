using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace TenantForge.Modules.Shop.Features.RateLimiting;

/// <summary>
/// B046: per-policy, per-minute request limits for the anonymous Shop flows
/// (order lookup, cart mutations, checkout/order creation and payment
/// initiation) plus the request-size bounds this task enforces.
///
/// <c>QueueLength</c> is fixed at exactly zero: once a policy's limit is hit,
/// the next request is rejected immediately — it is never queued or delayed.
///
/// In <b>Development</b> an absent value falls back to its documented default;
/// in <b>Production</b> every value must be configured explicitly to a positive
/// number within its documented maximum (recorded in
/// <c>docs/modules/SHOP.md</c>), and a missing or out-of-range value fails
/// startup.
/// </summary>
public sealed class ShopRateLimitOptions
{
    public const string SectionName = "Shop:RateLimiting";

    public const string MaxRequestBodyBytesPath = SectionName + ":MaxRequestBodyBytes";
    public const string OrderLookupPerMinutePath = SectionName + ":OrderLookupPerMinute";
    public const string CartMutationPerMinutePath = SectionName + ":CartMutationPerMinute";
    public const string CheckoutOrderPerMinutePath = SectionName + ":CheckoutOrderPerMinute";
    public const string PaymentInitiationPerMinutePath = SectionName + ":PaymentInitiationPerMinute";

    // ── Per-minute limits: documented defaults (Development) and maxima ──────

    public const int DefaultOrderLookupPerMinute = 30;
    public const int DefaultCartMutationPerMinute = 120;
    public const int DefaultCheckoutOrderPerMinute = 30;
    public const int DefaultPaymentInitiationPerMinute = 20;

    public const int MinPerMinute = 1;
    public const int MaxPerMinute = 10_000;

    // ── Request-size bounds (bytes) ──────────────────────────────────────────
    // One source of truth for the media-upload endpoint (bound via
    // <c>RequireMaxRequestBodySize</c>), the host-level request-body guard, and
    // the callback query-length guard.

    /// <summary>Maximum total size of a product-image upload (5 MiB).</summary>
    public const long MaxMediaUploadBytes = 5L * 1024L * 1024L;

    /// <summary>Maximum total size of a JSON Shop request body (512 KiB).</summary>
    public const long MaxJsonRequestBodyBytes = 512L * 1024L;

    /// <summary>Maximum total length of the ZarinPal callback query string (16 KiB).</summary>
    public const int MaxCallbackQueryLength = 16_000;

    /// <summary>
    /// Always exactly zero — a non-zero value is refused by
    /// <see cref="Validate"/>.
    /// </summary>
    public int QueueLength { get; set; }

    /// <summary>The per-minute limit for the order-lookup policy.</summary>
    public int OrderLookupPerMinute { get; set; } = DefaultOrderLookupPerMinute;

    /// <summary>The per-minute limit for the cart-mutation policy.</summary>
    public int CartMutationPerMinute { get; set; } = DefaultCartMutationPerMinute;

    /// <summary>The per-minute limit for the checkout/order-creation policy.</summary>
    public int CheckoutOrderPerMinute { get; set; } = DefaultCheckoutOrderPerMinute;

    /// <summary>The per-minute limit for the payment-initiation policy.</summary>
    public int PaymentInitiationPerMinute { get; set; } = DefaultPaymentInitiationPerMinute;

    /// <summary>
    /// Maximum total request-body size (bytes) for the anonymous Shop surface.
    /// In Development an absent value falls back to
    /// <see cref="MaxJsonRequestBodyBytes"/>; in Production it is required and
    /// must be a positive integer.
    /// </summary>
    public long MaxRequestBodyBytes { get; set; } = MaxJsonRequestBodyBytes;

    /// <summary>
    /// Binds and validates the <c>Shop:RateLimiting</c> section into this
    /// instance. In Development an absent value falls back to its documented
    /// default; in Production every per-minute value and the request-body
    /// bound are required and must be within the documented ranges — otherwise
    /// the host refuses to start.
    /// </summary>
    public void Bind(IHostEnvironment environment, IConfiguration configuration)
    {
        var development = environment.IsDevelopment();

        // QueueLength is optional and defaults to 0 (reject immediately). A
        // non-zero value is permitted to be configured — Validate() then
        // refuses to start, because this limiter never queues an over-limit
        // request.
        var rawQueueLength = configuration[SectionName + ":QueueLength"];
        if (!string.IsNullOrWhiteSpace(rawQueueLength))
        {
            if (!int.TryParse(rawQueueLength, out var queueLength) || queueLength < 0)
            {
                throw new InvalidOperationException(
                    $"The '{SectionName}:QueueLength' configuration value must be a non-negative integer (exactly 0 to reject immediately).");
            }

            QueueLength = queueLength;
        }

        OrderLookupPerMinute = ResolvePerMinute(configuration, development, OrderLookupPerMinutePath, DefaultOrderLookupPerMinute);
        CartMutationPerMinute = ResolvePerMinute(configuration, development, CartMutationPerMinutePath, DefaultCartMutationPerMinute);
        CheckoutOrderPerMinute = ResolvePerMinute(configuration, development, CheckoutOrderPerMinutePath, DefaultCheckoutOrderPerMinute);
        PaymentInitiationPerMinute = ResolvePerMinute(configuration, development, PaymentInitiationPerMinutePath, DefaultPaymentInitiationPerMinute);

        MaxRequestBodyBytes = ResolveLong(configuration, development, MaxRequestBodyBytesPath, MaxJsonRequestBodyBytes, min: 1, max: MaxMediaUploadBytes);
    }

    /// <summary>
    /// The effective per-minute limit for a named policy.
    /// </summary>
    public int LimitFor(string policyName) => policyName switch
    {
        ShopRateLimitPolicies.OrderLookup => OrderLookupPerMinute,
        ShopRateLimitPolicies.CartMutation => CartMutationPerMinute,
        ShopRateLimitPolicies.CheckoutOrder => CheckoutOrderPerMinute,
        ShopRateLimitPolicies.Payment => PaymentInitiationPerMinute,
        _ => throw new InvalidOperationException($"Unknown Shop rate-limit policy '{policyName}'."),
    };

    /// <summary>
    /// Fails closed unless <see cref="QueueLength"/> is exactly zero. Called
    /// from <c>ShopConfig.ValidateConfiguration</c> so a misconfigured host
    /// refuses to start before any pipeline wiring happens.
    /// </summary>
    public void Validate()
    {
        if (QueueLength != 0)
        {
            throw new InvalidOperationException(
                $"The '{SectionName}:QueueLength' configuration value must be exactly 0 (the Shop limiter rejects over-limit requests immediately and never queues them).");
        }
    }

    private static int ResolvePerMinute(IConfiguration configuration, bool development, string path, int developmentDefault)
    {
        var rawValue = configuration[path];
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            if (development)
            {
                return developmentDefault;
            }

            throw new InvalidOperationException(
                $"The '{path}' configuration value is required in Production. Configure it to an integer from {MinPerMinute} through {MaxPerMinute}.");
        }

        if (!int.TryParse(rawValue, out var value) || value < MinPerMinute || value > MaxPerMinute)
        {
            throw new InvalidOperationException(
                $"The '{path}' configuration value must be an integer from {MinPerMinute} through {MaxPerMinute}.");
        }

        return value;
    }

    private static long ResolveLong(
        IConfiguration configuration, bool development, string path,
        long developmentDefault, long min, long max)
    {
        var rawValue = configuration[path];
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            if (development)
            {
                return developmentDefault;
            }

            throw new InvalidOperationException(
                $"The '{path}' configuration value is required in Production. Configure it to a positive integer no greater than {max}.");
        }

        if (!long.TryParse(rawValue, out var value) || value < min || value > max)
        {
            throw new InvalidOperationException(
                $"The '{path}' configuration value must be an integer from {min} through {max}.");
        }

        return value;
    }
}
