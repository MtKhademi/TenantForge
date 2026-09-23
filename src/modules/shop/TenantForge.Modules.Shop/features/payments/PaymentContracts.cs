namespace TenantForge.Modules.Shop.Features.Payments;

/// <summary>
/// B044: the response to <c>POST …/payments/initiate</c>. <see
/// cref="Provider"/> is the gateway name that handled the initiation
/// (<c>Sandbox</c> today, <c>ZarinPal</c> in B045). <see cref="RedirectUrl"/>
/// is where the browser must go next — for Sandbox a relative, same-origin
/// path to the in-app bank page (<c>/shop/{tenantId}/bank?authority=…</c>),
/// for a real provider an absolute URL the client's redirect allowlist must
/// vet. <see cref="ResultToken"/> is the raw callback token, returned exactly
/// once on a first-time initiation (and re-derived, byte-identically, on a
/// same-key idempotency replay) — it is never persisted, only its SHA-256
/// hash is, and it is what <c>GET …/payments/status</c> requires.
/// </summary>
public sealed record InitiatePaymentResponse(
    string Provider, string RedirectUrl, string ResultToken);

/// <summary>
/// B044: the response to <c>GET …/payments/status</c> (and the sandbox
/// resolve). Deliberately minimal — the order's <c>OrderNumber</c>, its
/// current <see cref="Status"/> and the provider's verification-time
/// <see cref="ProviderReference"/> (null until a provider returns one).
/// Nothing else about the order, its customer or its totals is exposed.
/// </summary>
public sealed record PaymentStatusResponse(
    string OrderNumber, string Status, string? ProviderReference);

/// <summary>
/// B044: the request to the Development-only sandbox resolve route.
/// <see cref="Authority"/> is the initiation identifier the fake bank page
/// carries; <see cref="Approved"/> is the customer's simulated decision.
/// This is the only browser-authored payment input in the module, and it can
/// only ever reach a Development host — the route is not mapped elsewhere and
/// Production startup with <c>Shop:Payments:Provider=Sandbox</c> fails closed.
/// </summary>
public sealed record ResolveSandboxPaymentRequest(string? Authority, bool Approved);
