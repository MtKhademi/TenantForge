namespace TenantForge.Modules.Iam.Features.Login;

/// <summary>
/// Verifies login credentials. The implementation is free to consult any
/// source (a persisted store today); it returns the authenticated account on
/// success and null on failure. Callers must not rely on any distinction
/// between failure reasons — the endpoint answers a single generic 401.
/// </summary>
public interface ICredentialChecker
{
    Task<AuthenticatedAccount?> AuthenticateAsync(string email, string password);
}
