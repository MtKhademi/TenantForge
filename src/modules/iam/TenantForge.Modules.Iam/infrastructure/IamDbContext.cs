using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Iam.Domain;

namespace TenantForge.Modules.Iam.Infrastructure;

internal sealed class IamDbContext(DbContextOptions<IamDbContext> options) : DbContext(options)
{
    internal DbSet<Account> Accounts => Set<Account>();
    internal DbSet<Tenant> Tenants => Set<Tenant>();
    internal DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AccountMap());
        modelBuilder.ApplyConfiguration(new TenantMap());
        modelBuilder.ApplyConfiguration(new TenantMembershipMap());
    }
}
