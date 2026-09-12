using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TenantForge.Modules.Iam.Infrastructure;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Isolated migration coverage because the shared API fixture always migrates
/// to the latest version before tests can insert representative pre-migration rows.
/// </summary>
public sealed class PermissionKeyMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tenantforge_permission_migration_tests")
        .WithUsername("tenantforge")
        .WithPassword("tenantforge")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task NormalizePermissionKeys_MigratesOldGrantsAndPreservesRoleIdentityAndAssignments()
    {
        await using (var context = CreateContext())
        {
            await context.GetInfrastructure().GetRequiredService<IMigrator>()
                .MigrateAsync("20260906170721_AddInvitationsAndAudit");

            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO iam_accounts (id, email, normalized_email, display_name, password_hash, is_platform_admin, status, created_at_utc, updated_at_utc)
                VALUES ('11111111-1111-1111-1111-111111111111', 'member@tenantforge.local', 'MEMBER@TENANTFORGE.LOCAL', 'Member', 'hash', false, 'Active', now(), now());

                INSERT INTO iam_tenants (id, name, slug, normalized_slug, status, created_at_utc, updated_at_utc)
                VALUES ('22222222-2222-2222-2222-222222222222', 'Migration Tenant', 'migration-tenant', 'migration-tenant', 'Active', now(), now());

                INSERT INTO iam_tenant_memberships (id, tenant_id, account_id, role, created_at_utc)
                VALUES ('33333333-3333-3333-3333-333333333333', '22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 'Member', now());

                INSERT INTO iam_tenant_roles (id, tenant_id, name, normalized_name, description, kind, permission_keys, created_at_utc, updated_at_utc)
                VALUES ('44444444-4444-4444-4444-444444444444', '22222222-2222-2222-2222-222222222222', 'Legacy Manager', 'LEGACY MANAGER', 'legacy', 'custom',
                        ARRAY['IAM.Tenants.Create','IAM.Users.View','IAM.Users.Create','IAM.Dashboard.View','IAM.Invitations.View','IAM.Tenants.Create']::text[], now(), now());

                INSERT INTO iam_tenant_member_role_assignments (tenant_membership_id, tenant_role_id, created_at_utc)
                VALUES ('33333333-3333-3333-3333-333333333333', '44444444-4444-4444-4444-444444444444', now());
                """);
        }

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();

            var role = await context.TenantRoles.AsNoTracking().SingleAsync(role => role.Id == Guid.Parse("44444444-4444-4444-4444-444444444444"));
            Assert.Equal("Legacy Manager", role.Name);
            Assert.Equal([
                "IAM.Invitations.View",
                "IAM.Roles.Manage"
            ], role.PermissionKeys);

            Assert.True(await context.TenantMemberRoleAssignments.AnyAsync(assignment =>
                assignment.TenantMembershipId == Guid.Parse("33333333-3333-3333-3333-333333333333")
                && assignment.TenantRoleId == role.Id));
        }
    }

    private IamDbContext CreateContext() => new(
        new DbContextOptionsBuilder<IamDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}
