using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TenantForge.Modules.Shop.Features.RateLimiting;

/// <summary>
/// B046: the exact, closed set of named rate-limit policy names applied to the
/// anonymous Shop routes. The strings are the wire values the endpoint
/// metadata references via <c>RequireRateLimiting</c> and the names the
/// limiter registers its policies under — never aliases.
/// </summary>
internal static class ShopRateLimitPolicies
{
    internal const string OrderLookup = "shop-order-lookup";
    internal const string CartMutation = "shop-cart-mutation";
    internal const string CheckoutOrder = "shop-checkout-order";
    internal const string Payment = "shop-payment";
}

/// <summary>
/// B046: the single registration seam for the Shop rate limiter.
/// <see cref="AddShopRateLimiter"/> contains the host's one — and only —
/// <c>AddRateLimiter</c> call (a second call throws at DI validation) and is
/// invoked by <c>Program.cs</c> at the exact position this task's Spec names:
/// after both modules' registrations, before <c>builder.Build()</c>.
///
/// On .NET 10 the rate-limiting API is policy-per-endpoint: there is no
/// <c>AddPolicy(name, RateLimitPartition)</c> overload with a key selector, and
/// no global limiter is needed. Each named policy is registered with
/// <c>AddPolicy&lt;TKey&gt;(name, httpContext => …)</c>, whose lambda computes the
/// per-request partition key from the <c>HttpContext</c>; the framework then
/// keeps one fixed-window limiter per distinct key. Buckets are therefore
/// partitioned by normalized tenant ID plus the request's effective remote IP
/// (the value <c>UseForwardedHeaders</c> has already resolved through the
/// configured trusted-proxy allowlist). Because only the Shop endpoints carry
/// <c>RequireRateLimiting</c> metadata, every other route (IAM, admin, health,
/// the provider callback, public catalog reads) is left unlimited.
///
/// Every over-limit response is the one, generic RFC 7807 429: the policy name
/// and tenant ID are logged for operators, but the response body never reveals
/// which specific resource, phone number, tracking code, cart or order
/// triggered the limit.
/// </summary>
/// <remarks>
/// Public like the module's own <c>ShopModule</c> seam: the host (the
/// <c>TenantForge.Api</c> assembly) invokes
/// <see cref="AddShopRateLimiter"/> and <see cref="UseShopRequestBodySizeLimit"/>
/// directly. <see cref="ShopRateLimitPolicies"/> stays <c>internal</c> because it
/// is referenced only from within this assembly.
/// </remarks>
public static class ShopRateLimiterHostExtensions
{
    /// <summary>
    /// The exact string a Shop 429 response carries as its RFC 7807
    /// <c>type</c> — shared by every policy, for valid and invalid inputs
    /// alike.
    /// </summary>
    public const string ProblemType = "shop_rate_limit";

    /// <summary>
    /// The one generic, Persian-safe detail every Shop 429 carries. It names
    /// nothing specific — no tenant, no cart, no order, no tracking code, no
    /// phone number — so a caller cannot observe which input (if any) was
    /// "real" and which was not.
    /// </summary>
    private const string GenericDetail = "Too many requests. Please try again later.";

    private const int RetryAfterSeconds = 1;

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IServiceCollection AddShopRateLimiter(this IServiceCollection services)
    {
        services.AddOptions<ShopRateLimitOptions>()
            .Configure<IConfiguration, IHostEnvironment>((options, configuration, environment) => options.Bind(environment, configuration));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            RegisterPolicy(options, ShopRateLimitPolicies.OrderLookup);
            RegisterPolicy(options, ShopRateLimitPolicies.CartMutation);
            RegisterPolicy(options, ShopRateLimitPolicies.CheckoutOrder);
            RegisterPolicy(options, ShopRateLimitPolicies.Payment);

            options.OnRejected = WriteRejectionAsync;
        });

        return services;
    }

    /// <summary>
    /// The host-level request-body bound. Only Shop paths are scoped in
    /// (public <c>/api/shop/…</c> and tenant-scoped <c>/api/tenants/{id}/shop/…</c>);
    /// IAM and other hosts' routes are untouched. A JSON body above
    /// <c>MaxJsonRequestBodyBytes</c>, or any body above <c>MaxMediaUploadBytes</c>
    /// (the hard ceiling, which also covers the media upload), is rejected with
    /// a generic 413 before any model-binding or validation work runs. Called
    /// by <c>Program.cs</c> immediately after <c>UseRateLimiter</c>, before the
    /// module activation maps the endpoints.
    /// </summary>
    public static IApplicationBuilder UseShopRequestBodySizeLimit(this IApplicationBuilder app)
    {
        var shopOptions = app.ApplicationServices.GetRequiredService<IOptions<ShopRateLimitOptions>>().Value;

        app.Use(async (context, next) =>
        {
            if (IsShopPath(context.Request)
                && context.Request.ContentLength is long length
                && length > LimitForRequest(context.Request, shopOptions))
            {
                await WriteOversizedBodyAsync(context);
                return;
            }

            await next();
        });

        return app;
    }

    private static void RegisterPolicy(RateLimiterOptions options, string policyName)
    {
        // On .NET 10 the per-request key is computed inside this lambda from the
        // HttpContext; the framework keeps one fixed-window limiter per distinct
        // key (tenant id + effective remote ip). QueueLimit is 0: an over-limit
        // request is rejected immediately, never queued.
        options.AddPolicy<string>(policyName, httpContext =>
        {
            var shopOptions = httpContext.RequestServices
                .GetRequiredService<IOptions<ShopRateLimitOptions>>().Value;
            return RateLimitPartition.GetFixedWindowLimiter<string>(
                GetPartitionKey(httpContext),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = shopOptions.LimitFor(policyName),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
        });
    }

    /// <summary>
    /// The rate-limit partition key: the normalized tenant ID (the route
    /// segment, lowercased) plus the request's effective remote IP. The IP is
    /// whatever <c>UseForwardedHeaders</c> already resolved — honoring the
    /// configured trusted-proxy allowlist, so a request that did not arrive
    /// through a trusted proxy is partitioned by its direct remote IP, never
    /// by a client-supplied <c>X-Forwarded-For</c>.
    /// </summary>
    private static string GetPartitionKey(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var tenantId = (string?)context.Request.RouteValues["tenantId"];
        return string.IsNullOrEmpty(tenantId) ? remoteIp : $"{tenantId.ToLowerInvariant()}|{remoteIp}";
    }

    /// <summary>
    /// The one 429 response every Shop over-limit request receives: an RFC
    /// 7807 body with <c>type</c> set to the single shared
    /// <see cref="ProblemType"/>, the one generic <see cref="GenericDetail"/>,
    /// an integer <c>Retry-After</c> header, and the matching integer
    /// <c>retryAfter</c> in the body. The policy name and tenant ID are
    /// logged; the phone number, tracking code, coupon code and gateway
    /// authority never are.
    /// </summary>
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        var policyName = httpContext.GetEndpoint()?.Metadata
            .GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "unknown";
        var tenantId = (string?)httpContext.Request.RouteValues["tenantId"] ?? "unknown";

        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("TenantForge.Modules.Shop.Features.RateLimiting");
        logger.LogWarning(
            "Shop rate limit exceeded: policy={Policy} tenantId={TenantId}.",
            policyName, tenantId);

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.Headers["Retry-After"] = RetryAfterSeconds.ToString();
        httpContext.Response.ContentType = "application/problem+json";

        var problem = new
        {
            type = ProblemType,
            title = "Too many requests",
            detail = GenericDetail,
            status = StatusCodes.Status429TooManyRequests,
            retryAfter = RetryAfterSeconds
        };

        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(problem, CamelCaseOptions), cancellationToken);
    }

    private static bool IsShopPath(HttpRequest request)
    {
        var path = request.Path;
        return path.StartsWithSegments("/api/shop")
            || (path.StartsWithSegments("/api/tenants") && path.Value?.Contains("/shop/", StringComparison.Ordinal) == true);
    }

    private static long LimitForRequest(HttpRequest request, ShopRateLimitOptions options)
    {
        // JSON Shop bodies get the tighter bound; anything else (e.g. the
        // multipart media upload) gets the hard ceiling.
        var contentType = request.ContentType ?? string.Empty;
        var isJson = contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
        return isJson ? options.MaxRequestBodyBytes : ShopRateLimitOptions.MaxMediaUploadBytes;
    }

    /// <summary>
    /// The generic 413 for an over-limit request body. The message names only
    /// the size bound — never the tenant, route, or any request value.
    /// </summary>
    private static Task WriteOversizedBodyAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.ContentType = "application/problem+json";

        var problem = new
        {
            type = "shop_request_too_large",
            title = "Request too large",
            detail = "The request body exceeds the permitted size.",
            status = StatusCodes.Status413PayloadTooLarge
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(problem, CamelCaseOptions));
    }
}
