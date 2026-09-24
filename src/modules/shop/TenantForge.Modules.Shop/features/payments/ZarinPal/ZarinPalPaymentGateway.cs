using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Features.Payments;

namespace TenantForge.Modules.Shop.Features.Payments.ZarinPal;

/// <summary>
/// B045: the real ZarinPal implementation of the gateway-neutral
/// <see cref="IShopPaymentGateway"/> seam.
///
/// <b>Initiation</b> calls the v4 <c>request</c> endpoint with the merchant id,
/// the checked integer amount in the configured currency, a description and the
/// server-owned callback URL (never a frontend URL). The official v4 envelope is
/// parsed; request code <c>100</c> with a non-empty <c>authority</c> is the only
/// success. The redirect is <c>GatewayBaseUrl</c> + the authority — an absolute
/// URL, and never a value that carries the merchant id.
///
/// <b>Verification</b> is server-to-server: the v4 <c>verify</c> endpoint is
/// called with the server-stored authority and the server-stored amount
/// (re-derived from the attempt's frozen total) — never values from the
/// callback's query string. Code <c>100</c> is success; code <c>101</c> is
/// returned as a success outcome whose <see cref="GatewayVerification.
/// ReferenceId"/> the caller (the callback feature / completion service)
/// reconciles against the attempt's already-stored success reference — a
/// mismatch there fails closed. Every other code is a failure with a stable
/// code. The provider's <c>card_pan</c> is never read, propagated, logged or
/// stored: only the stable code and the RefId leave this method.
///
/// A provider-unavailable response (timeout, connection failure, 5xx, malformed
/// body) throws <see cref="ZarinPalProviderException"/> with the stable code
/// <c>provider_unavailable</c>; the caller maps it to a safe 503 and never
/// marks an attempt Paid. The per-call timeout comes from
/// <see cref="ZarinPalOptions.TimeoutSeconds"/> (10 s) through a linked
/// cancellation, not the HttpClient's global timeout (which has a 100 s floor).
/// </summary>
internal sealed class ZarinPalPaymentGateway : IShopPaymentGateway
{
    public const string ProviderName = "ZarinPal";

    private const string ClientName = nameof(ZarinPalPaymentGateway);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ZarinPalOptions _options;
    private readonly ILogger<ZarinPalPaymentGateway> _logger;

    public ZarinPalPaymentGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<ZarinPalOptions> options,
        ILogger<ZarinPalPaymentGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string Provider => ProviderName;

    public async Task<GatewayInitiation> InitiateAsync(PaymentContext context, CancellationToken ct)
    {
        // The checked conversion happens before any provider call: an unpayable
        // amount (overflow, or not an exact integer in the configured currency)
        // is refused locally, never sent to the provider.
        long amountInProviderUnits;
        try
        {
            amountInProviderUnits = ZarinPalAmountConverter.ToProviderAmount(context.Amount, _options.Currency);
        }
        catch (InvalidOperationException)
        {
            throw new ZarinPalProviderException("amount_invalid");
        }

        // context.CallbackUri is the caller's fully-built, server-owned
        // callback URL (the feature already bound the signed state into it) —
        // the gateway never mints or signs anything itself, and never sees a
        // frontend URL.
        var body = new
        {
            merchantId = _options.MerchantId,
            amount = ZarinPalAmountConverter.FormatProviderAmount(amountInProviderUnits),
            currency = _options.Currency,
            description = $"TenantForge order {TsidId.Format(context.OrderId)}",
            callbackUrl = context.CallbackUri.ToString()
        };

        var requestData = await SendAsync<ZarinPalRequestData>(_options.RequestEndpoint, body, ct);

        if (requestData.Code != 100)
        {
            // A business rejection: stable code only, never the provider's
            // message (it may echo request fields).
            _logger.LogInformation(
                "ZarinPal request rejected: orderId={OrderId} providerCode={ProviderCode}.",
                context.OrderId.ToLong(), requestData.Code);
            throw new ZarinPalProviderException("provider_rejected");
        }

        if (string.IsNullOrWhiteSpace(requestData.Authority))
        {
            // Code 100 but no usable authority: a failure, by the Spec's rule.
            _logger.LogWarning(
                "ZarinPal request returned code 100 with an empty authority: orderId={OrderId}.",
                context.OrderId.ToLong());
            throw new ZarinPalProviderException("authority_missing");
        }

        // The redirect is GatewayBaseUrl + the authority — an absolute URL the
        // browser's redirect allowlist must vet (F062). The authority is the
        // provider's own path segment, so a leading-slash authority is treated
        // as a path-absolute target.
        var redirect = new Uri(_options.GatewayBaseUrl, requestData.Authority);
        return new GatewayInitiation(requestData.Authority, redirect);
    }

    public async Task<GatewayVerification> VerifyAsync(PaymentVerificationRequest request, CancellationToken ct)
    {
        if (request.Amount is not {} amount)
        {
            // The verify call needs the server-stored amount to re-derive the
            // exact integer the initiation sent. A missing amount is a caller
            // error, not a provider failure — fail closed with a stable code.
            return new GatewayVerification(GatewayOutcome.Failed, null, "verification_failed");
        }

        long amountInProviderUnits;
        try
        {
            amountInProviderUnits = ZarinPalAmountConverter.ToProviderAmount(amount, _options.Currency);
        }
        catch (InvalidOperationException)
        {
            return new GatewayVerification(GatewayOutcome.Failed, null, "verification_failed");
        }

        var body = new
        {
            merchantId = _options.MerchantId,
            amount = ZarinPalAmountConverter.FormatProviderAmount(amountInProviderUnits),
            authority = request.Authority
        };

        var verifyData = await SendAsync<ZarinPalVerifyData>(_options.VerifyEndpoint, body, ct);

        switch (verifyData.Code)
        {
            case 100:
            case 101:
                // Both are provider-side success codes. 100 is a fresh
                // success; 101 is "already verified" and MUST be reconciled by
                // the caller against a previously stored success reference for
                // this exact attempt — this method only reports the code, it
                // never decides that reconciliation itself. The card PAN is
                // deliberately dropped and never propagated.
                return new GatewayVerification(
                    GatewayOutcome.Succeeded, verifyData.RefId?.ToString(), null, verifyData.Code);

            default:
                // The authority is deliberately NOT logged: gateway authorities
                // are bearer credentials for the provider's APIs (the Spec's
                // logging rule) — only the stable provider code leaves here.
                _logger.LogInformation(
                    "ZarinPal verify failed: providerCode={ProviderCode}.",
                    verifyData.Code);
                return new GatewayVerification(GatewayOutcome.Failed, null, "verification_failed");
        }
    }

    private async Task<TData> SendAsync<TData>(Uri endpoint, object body, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var client = _httpClientFactory.CreateClient(ClientName);
            using var content = JsonContent.Create(body);
            using var response = await client.PostAsync(endpoint, content, timeoutCts.Token);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return ParseEnvelope<TData>(json);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // A timeout (or the HttpClient's own global timeout): the provider
            // is unavailable, NOT a rejection. Safe failure, stable code.
            throw new ZarinPalProviderException("provider_unavailable");
        }
        catch (HttpRequestException)
        {
            // Connection failure, DNS, 5xx, etc.: unavailable, not rejected.
            throw new ZarinPalProviderException("provider_unavailable");
        }
    }

    private static TData ParseEnvelope<TData>(string json)
    {
        // The official v4 envelope: every field is camelCase and the numeric
        // codes arrive as strings ("100"). RefId is a numeric string.
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (typeof(TData) == typeof(ZarinPalRequestData))
            {
                var code = int.Parse(root.GetProperty("code").GetString() ?? string.Empty);
                var authority = root.TryGetProperty("authority", out var authorityElement)
                    ? authorityElement.GetString() ?? string.Empty
                    : string.Empty;
                var message = root.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                return (TData)(object)new ZarinPalRequestData(code, authority, message);
            }

            if (typeof(TData) == typeof(ZarinPalVerifyData))
            {
                var code = int.Parse(root.GetProperty("code").GetString() ?? string.Empty);
                long? refId = null;
                if (root.TryGetProperty("refId", out var refIdElement)
                    && refIdElement.ValueKind == JsonValueKind.String
                    && long.TryParse(refIdElement.GetString(), out var parsedRefId))
                {
                    refId = parsedRefId;
                }
                var message = root.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                // card_pan is deliberately NOT read: it must never leave this
                // method, be logged, or be stored.
                return (TData)(object)new ZarinPalVerifyData(code, refId, null, message);
            }

            throw new InvalidOperationException($"Unsupported ZarinPal envelope type: {typeof(TData).Name}.");
        }
        catch (JsonException)
        {
            throw new ZarinPalProviderException("provider_unavailable");
        }
        catch (FormatException)
        {
            throw new ZarinPalProviderException("provider_unavailable");
        }
    }
}
