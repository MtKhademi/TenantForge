namespace TenantForge.Modules.Iam;

/// <summary>
/// Names of the authorization policies the IAM module registers. Internal:
/// only features inside this module map endpoints against these policies.
/// </summary>
internal static class AuthorizationPolicyNames
{
    /// <summary>
    /// Requires an authenticated principal whose "isPlatformAdmin" claim is
    /// exactly "true". Unauthenticated callers get 401 (challenge); an
    /// authenticated caller that fails the claim requirement gets 403 (forbid).
    /// </summary>
    public const string PlatformAdmin = "PlatformAdmin";
}
