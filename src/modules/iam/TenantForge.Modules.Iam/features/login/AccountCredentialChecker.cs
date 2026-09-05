using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam.Features.Login;

/// <summary>
/// Verifies a login by looking up the persisted account by normalized email and
/// verifying the supplied password against the stored hash. Returns the account
/// on success, or null when the account is missing, the password is wrong, or
/// the account is not active. Callers treat a null result as a generic
/// "invalid credentials" 401 so that account existence and account state are
/// never leaked.
/// </summary>
internal sealed class AccountCredentialChecker(
    IamDbContext db,
    IPasswordHasher<global::TenantForge.Modules.Iam.Domain.Account> passwordHasher) : ICredentialChecker
{
    public async Task<AuthenticatedAccount?> AuthenticateAsync(string email, string password)
    {
        var normalized = global::TenantForge.Modules.Iam.Domain.Account.NormalizeEmail(email);

        var account = await db.Accounts
            .FirstOrDefaultAsync(a => a.NormalizedEmail == normalized);

        // A missing account and a disabled account both yield null; the caller
        // cannot distinguish them, so neither existence nor state is disclosed.
        if (account is null || account.Status != global::TenantForge.Modules.Iam.Domain.AccountStatus.Active)
        {
            return null;
        }

        var result = passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (result != PasswordVerificationResult.Success)
        {
            return null;
        }

        return new AuthenticatedAccount(
            account.Id.ToString(),
            account.Email,
            account.DisplayName,
            account.IsPlatformAdmin);
    }
}
