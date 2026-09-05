using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam.Features.Login;

/// <summary>
/// Seeds the single platform administrator exactly once. Idempotent: when the
/// normalized email is already present it does nothing. Concurrency-safe: if
/// two processes race and both pass the "not present" check, the unique index
/// on <c>normalized_email</c> makes the second insert fail, and that failure is
/// treated as "already seeded" rather than an error, so the account is never
/// duplicated.
/// </summary>
internal sealed class PlatformAdminSeeder(
    IamDbContext db,
    IPasswordHasher<global::TenantForge.Modules.Iam.Domain.Account> passwordHasher)
{
    public async Task<bool> SeedAsync(SeedAdminOptions options, DateTimeOffset nowUtc)
    {
        var normalized = global::TenantForge.Modules.Iam.Domain.Account.NormalizeEmail(options.Email!);

        var existing = await db.Accounts
            .FirstOrDefaultAsync(a => a.NormalizedEmail == normalized);
        if (existing is not null)
        {
            // Already seeded (by a previous startup); leave it untouched.
            return false;
        }

        // The default PBKDF2 hasher salts each hash independently and does not
        // read the user instance, so null is a valid (if awkwardly annotated)
        // first argument here.
        var hash = passwordHasher.HashPassword(null!, options.Password!);
        var account = global::TenantForge.Modules.Iam.Domain.Account.CreatePlatformAdmin(
            options.Email!,
            options.DisplayName!,
            hash,
            nowUtc);

        db.Accounts.Add(account);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            // Lost the race: another startup inserted the same normalized email
            // between our lookup and our insert. The unique index enforced
            // single-row semantics, so treat this as already seeded.
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
