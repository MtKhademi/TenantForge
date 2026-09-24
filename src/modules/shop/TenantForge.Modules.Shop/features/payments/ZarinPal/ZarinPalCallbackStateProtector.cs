using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using TenantForge.Modules.Shop.Features.Payments;

namespace TenantForge.Modules.Shop.Features.Payments.ZarinPal;

/// <summary>
/// B045: mints and validates the signed, expiring <c>state</c> value the
/// callback query string carries. The token is an ASP.NET Core Data Protection
/// payload under the fixed purpose <see cref="Purpose"/>; it binds the tenant
/// id, order id, attempt id and the B044 callback token, so a callback query
/// string can only ever be meaningful for the attempt this server itself
/// signed — it cannot be forged, copied from another tenant, or replayed
/// after <see cref="Lifetime"/> (30 minutes) have passed.
///
/// The state is carried in the query string and is deliberately NOT persisted
/// (no column, no table). Unprotecting a tampered, expired or absent token
/// returns null; the caller answers its one generic 404 and never reveals which
/// half was wrong.
/// </summary>
internal sealed class ZarinPalCallbackStateProtector(IDataProtectionProvider dataProtection, TimeProvider timeProvider)
{
    private const string Purpose = "TenantForge.Shop.ZarinPal.Callback.v1";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly IDataProtector _protector = dataProtection.CreateProtector(Purpose);

    /// <summary>The protected token for the <paramref name="state"/> — safe to put in a URL.</summary>
    public string Protect(ZarinPalCallbackState state)
    {
        var expiresAtUtc = timeProvider.GetUtcNow().Add(Lifetime);
        var payload = new CallbackStateEnvelope(
            state.TenantId, state.OrderId, state.AttemptId, state.CallbackToken, expiresAtUtc);
        return _protector.Protect(JsonSerializer.Serialize(payload));
    }

    /// <summary>
    /// Unprotects a callback <c>state</c> token. Returns null for a missing,
    /// tampered, unparsable or expired token — the caller cannot distinguish
    /// the cases.
    /// </summary>
    public ZarinPalCallbackState? Unprotect(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string payload;
        try
        {
            payload = _protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            // Tampered or issued by another key ring — a miss, like any other.
            return null;
        }

        CallbackStateEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CallbackStateEnvelope>(payload)
                ?? throw new JsonException("Empty callback state payload.");
        }
        catch (JsonException)
        {
            return null;
        }

        if (timeProvider.GetUtcNow() >= envelope.ExpiresAtUtc)
        {
            return null; // the 30-minute window has closed
        }

        return new ZarinPalCallbackState(
            envelope.TenantId, envelope.OrderId, envelope.AttemptId, envelope.CallbackToken);
    }

    /// <summary>The protected payload — the state plus its expiry stamp.</summary>
    private sealed record CallbackStateEnvelope(
        string TenantId, string OrderId, string AttemptId, string CallbackToken,
        DateTimeOffset ExpiresAtUtc);
}
