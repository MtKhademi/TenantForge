namespace TenantForge.Modules.Shop.Features.Payments.ZarinPal;

/// <summary>
/// B045: the official ZarinPal v4 request response envelope.
/// Request code 100 is the only success code.
/// </summary>
internal sealed record ZarinPalRequestData(int Code, string Authority, string? Message);

/// <summary>
/// B045: the official ZarinPal v4 verification response envelope.
/// Code 100 is success; 101 is "already verified".
/// </summary>
internal sealed record ZarinPalVerifyData(int Code, long? RefId, string? CardPan, string? Message);

/// <summary>
/// B045: the signed, expiring state token that binds the callback query string
/// to the server's authority and attempt records.
/// </summary>
internal sealed record ZarinPalCallbackState(
    string TenantId,
    string OrderId,
    string AttemptId,
    string CallbackToken);
