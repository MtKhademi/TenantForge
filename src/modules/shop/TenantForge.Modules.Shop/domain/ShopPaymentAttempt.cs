using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal enum ShopPaymentAttemptStatus
{
    Initiated,
    Succeeded,
    Failed,
    Invalidated
}

internal sealed class ShopPaymentAttempt
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid OrderId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public ShopPaymentAttemptStatus Status { get; private set; } = ShopPaymentAttemptStatus.Initiated;

    /// <summary>
    /// The identifier the provider issues at initiation — for Sandbox a random
    /// hex string, for ZarinPal (B045) the authority. This column (not a
    /// separate "authority" column) is what B045 ships without a migration of
    /// its own.
    /// </summary>
    public string GatewayReference { get; private set; } = string.Empty;

    /// <summary>
    /// B044: the order's <c>GrandTotal</c> frozen at initiation time. A later
    /// price change can never change what this attempt is for; the completion
    /// service refuses any verification whose amount does not match this.
    /// </summary>
    public decimal AmountSnapshot { get; private set; }

    /// <summary>
    /// B044: SHA-256 (hex, lowercase) of the 32-byte raw callback token. Only
    /// the hash is ever stored — the raw token is returned exactly once in
    /// the initiation response and is never persisted or logged.
    /// </summary>
    public string CallbackTokenHash { get; private set; } = string.Empty;

    /// <summary>B044: stable reason code set when the attempt ends as <see cref="ShopPaymentAttemptStatus.Failed"/>.</summary>
    public string? FailureCode { get; private set; }

    /// <summary>
    /// B044: the reference the provider returns at verification (ZarinPal's
    /// RefId). Nullable — it does not exist until verification succeeds.
    /// Never holds the authority; that is <see cref="GatewayReference"/>.
    /// </summary>
    public string? ProviderReference { get; private set; }

    /// <summary>B044: set exactly once, when the attempt is resolved by the completion service.</summary>
    public DateTimeOffset? VerifiedAtUtc { get; private set; }

    /// <summary>
    /// B044: optimistic-concurrency / row-locking token. Bumped by exactly the
    /// mutation that resolves the attempt, inside the completion service's
    /// transaction — a racing duplicate re-reads the bumped row and cannot
    /// re-apply the transition.
    /// </summary>
    public int Version { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CallbackReceivedAtUtc { get; private set; }

    private ShopPaymentAttempt()
    {
    }

    public static ShopPaymentAttempt Create(
        Tsid orderId, string provider, string gatewayReference,
        decimal amountSnapshot, string callbackTokenHash, DateTimeOffset nowUtc)
    {
        if (TsidId.IsDefault(orderId))
        {
            throw new ArgumentException("Order id is required.", nameof(orderId));
        }

        return new ShopPaymentAttempt
        {
            Id = TsidId.NewId(),
            OrderId = orderId,
            Provider = provider,
            Status = ShopPaymentAttemptStatus.Initiated,
            GatewayReference = gatewayReference,
            AmountSnapshot = amountSnapshot,
            CallbackTokenHash = callbackTokenHash,
            CreatedAtUtc = nowUtc.ToUniversalTime(),
            Version = 0
        };
    }

    /// <summary>
    /// B044: the only transition out of <see cref="ShopPaymentAttemptStatus.Initiated"/>
    /// to a resolved state, and it is called exclusively by
    /// <c>ShopPaymentCompletionService</c> — inside its transaction, while it
    /// holds the row locks — so the attempt and its order can never resolve
    /// out of step. Returns false (no change) when the attempt was already
    /// resolved or invalidated: a duplicate provider callback is a replay,
    /// never a second write.
    /// </summary>
    public bool TryResolve(
        bool succeeded, string? providerReference, string? failureCode, DateTimeOffset nowUtc)
    {
        if (Status != ShopPaymentAttemptStatus.Initiated)
        {
            return false;
        }

        Status = succeeded ? ShopPaymentAttemptStatus.Succeeded : ShopPaymentAttemptStatus.Failed;
        ProviderReference = providerReference;
        FailureCode = failureCode;
        VerifiedAtUtc = nowUtc.ToUniversalTime();
        CallbackReceivedAtUtc = nowUtc.ToUniversalTime();
        Version++;
        return true;
    }

    /// <summary>
    /// B044: called inside a cancel's transaction for every attempt still in
    /// <see cref="ShopPaymentAttemptStatus.Initiated"/>. Moving the attempt to
    /// <see cref="ShopPaymentAttemptStatus.Invalidated"/> is what makes it
    /// un-completable — the completion service refuses any transition out of
    /// any non-<c>Initiated</c> status, and the order is no longer
    /// <c>PendingPayment</c> anyway. Already-resolved attempts return false
    /// and change nothing. No history row is ever deleted.
    /// </summary>
    public bool TryInvalidate(DateTimeOffset nowUtc)
    {
        if (Status != ShopPaymentAttemptStatus.Initiated)
        {
            return false;
        }

        Status = ShopPaymentAttemptStatus.Invalidated;
        VerifiedAtUtc = nowUtc.ToUniversalTime();
        CallbackReceivedAtUtc = nowUtc.ToUniversalTime();
        Version++;
        return true;
    }
}
