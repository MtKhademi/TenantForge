using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

/// <summary>
/// B044: the durable record of one payment-initiation request and the
/// idempotency guarantee behind it. There is at most one row per
/// <c>(TenantId, IdempotencyKey)</c> — enforced by a unique database
/// constraint, so the same key can never be stored twice for the same tenant
/// and two racing first-calls resolve to exactly one winner (the unique index
/// is the last-resort backstop behind the order-row lock).
///
/// The row stores:
/// - the <see cref="OrderId"/> the key was used for and the
///   <see cref="AttemptId"/> it produced, so a replay rebuilds the exact
///   response for the attempt the key initiated;
/// - a <see cref="RequestFingerprint"/> — SHA-256 over the canonical request
///   (tenant id + order id; initiation carries no body) — so a retry with the
///   same key but a different canonical request is a conflict, not a silent
///   replay;
/// - a <see cref="TokenSeed"/> — 32 random bytes. The raw callback token is
///   the one-way value <c>SHA256(TokenSeed)</c>, so a same-key retry re-derives
///   the identical <c>resultToken</c> and replays a byte-identical response
///   WITHOUT the raw token ever being persisted (only the seed and the
///   attempt's hash of the token are stored — neither is the token).
///
/// When a new key arrives for an order that already has a live
/// <c>Initiated</c> attempt, the new key's row points at that existing attempt
/// and copies its <see cref="TokenSeed"/>, so every key that maps to the same
/// attempt replays the same token.
/// </summary>
internal sealed class ShopPaymentInitiation
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public Tsid OrderId { get; private set; }
    public Tsid AttemptId { get; private set; }

    /// <summary>The client-supplied idempotency key (a UUID string).</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>SHA-256 (hex, lowercase) over the canonical request: tenant id + order id.</summary>
    public string RequestFingerprint { get; private set; } = string.Empty;

    /// <summary>
    /// The <c>redirectUrl</c> the initiating gateway returned, stored verbatim
    /// so a same-key replay returns the identical response without re-running
    /// the gateway (Sandbox: the in-app bank page, ZarinPal in B045: an
    /// absolute <c>https://</c> URL — neither is assumed here).
    /// </summary>
    public string RedirectUrl { get; private set; } = string.Empty;

    /// <summary>32 random bytes; the raw callback token is SHA256 of this seed.</summary>
    public byte[] TokenSeed { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private ShopPaymentInitiation()
    {
    }

    public static ShopPaymentInitiation Create(
        Tsid tenantId, Tsid orderId, Tsid attemptId,
        string idempotencyKey, string requestFingerprint, string redirectUrl,
        byte[] tokenSeed, DateTimeOffset nowUtc)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (TsidId.IsDefault(orderId))
        {
            throw new ArgumentException("Order id is required.", nameof(orderId));
        }

        if (TsidId.IsDefault(attemptId))
        {
            throw new ArgumentException("Attempt id is required.", nameof(attemptId));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        }

        if (tokenSeed is null || tokenSeed.Length != 32)
        {
            throw new ArgumentException("Token seed must be exactly 32 bytes.", nameof(tokenSeed));
        }

        if (string.IsNullOrWhiteSpace(redirectUrl))
        {
            throw new ArgumentException("Redirect URL is required.", nameof(redirectUrl));
        }

        return new ShopPaymentInitiation
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            OrderId = orderId,
            AttemptId = attemptId,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = requestFingerprint,
            RedirectUrl = redirectUrl,
            TokenSeed = tokenSeed,
            CreatedAtUtc = nowUtc.ToUniversalTime()
        };
    }
}
