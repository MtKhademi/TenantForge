using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Features.Payments;

/// <summary>
/// B044: the gateway-neutral payment context. This is everything a provider
/// needs to start a payment — and nothing else: the gateway never sees the
/// order aggregate, never reads the database and never sees the raw callback
/// token. <see cref="Amount"/> is the server-derived order total (the
/// caller's job to freeze as the attempt's <c>AmountSnapshot</c>);
/// <see cref="CallbackUri"/> is the server-owned URL the provider is told to
/// redirect back to (ZarinPal's return/failure URLs in B045).
/// </summary>
internal sealed record PaymentContext(Tsid TenantId, Tsid OrderId, decimal Amount, Uri CallbackUri);

/// <summary>
/// B044: a provider-neutral initiation result. <see cref="Authority"/> is the
/// identifier the provider issues at initiation (Sandbox: a random hex string,
/// ZarinPal: its authority) — it is stored in the attempt's existing
/// <c>GatewayReference</c> column, which is why B045 needs no migration of its
/// own. <see cref="RedirectUri"/> is where the browser must go next.
/// </summary>
internal sealed record GatewayInitiation(string Authority, Uri RedirectUri);

/// <summary>
/// B044: everything the server hands a provider to verify one payment. The
/// browser never declares success — it only carries the <see cref="Authority"/>
/// it was redirected with plus whatever callback values the provider's page
/// handed back (<see cref="CallbackValues"/>). A real provider (ZarinPal in
/// B045) uses these to make its own server-to-server verification call; the
/// sandbox derives its outcome directly from them.
/// </summary>
internal sealed record PaymentVerificationRequest(
    string Authority, IReadOnlyDictionary<string, string> CallbackValues);

/// <summary>
/// B044: exactly the two outcomes a gateway verification can produce. The
/// names deliberately mirror the two resolved <c>ShopPaymentAttemptStatus</c>
/// values so the mapping from gateway outcome to attempt status stays
/// obvious. A third member (e.g. "unknown") is deliberately not part of this
/// type: a verification the provider cannot complete is a <c>Failed</c>
/// outcome with a stable <see cref="GatewayVerification.ErrorCode"/>, not a
/// new state.
/// </summary>
internal enum GatewayOutcome
{
    Succeeded,
    Failed
}

/// <summary>
/// B044: the result of one gateway verification. <see cref="ReferenceId"/> is
/// the reference the provider returns at verification (ZarinPal's RefId) — it
/// is stored only on a success, and never holds the authority.
/// <see cref="ErrorCode"/> is a stable, lower-snake-case reason present on
/// every failure (e.g. <c>payment_declined</c>, <c>verification_failed</c>)
/// and null on success — it is what the completion service persists as the
/// attempt's <c>FailureCode</c>, so it must be safe to log and store.
/// </summary>
internal sealed record GatewayVerification(GatewayOutcome Outcome, string? ReferenceId, string? ErrorCode);

/// <summary>
/// B044: the seam a real provider implements. <see cref="Provider"/> is the
/// exact configuration value (<c>Sandbox</c> / <c>ZarinPal</c>) that selects
/// this implementation — the resolver matches on it, never on registration
/// order. Neither method may mutate the order or the attempt: the initiation
/// row is minted by the feature and the resolution runs exclusively through
/// <c>ShopPaymentCompletionService</c>.
/// </summary>
internal interface IShopPaymentGateway
{
    string Provider { get; }

    Task<GatewayInitiation> InitiateAsync(PaymentContext context, CancellationToken ct);

    Task<GatewayVerification> VerifyAsync(PaymentVerificationRequest request, CancellationToken ct);
}
