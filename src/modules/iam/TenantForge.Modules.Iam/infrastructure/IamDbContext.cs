using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Iam.Domain;

namespace TenantForge.Modules.Iam.Infrastructure;

internal sealed class IamDbContext(DbContextOptions<IamDbContext> options) : DbContext(options)
{
    internal DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AccountMap());
    }
}
