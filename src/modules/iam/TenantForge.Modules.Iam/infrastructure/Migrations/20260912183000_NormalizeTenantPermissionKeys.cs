using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Iam.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeTenantPermissionKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE iam_tenant_roles
                SET permission_keys = COALESCE((
                    SELECT array_agg(DISTINCT mapped_key ORDER BY mapped_key)
                    FROM unnest(permission_keys) AS existing_key
                    CROSS JOIN LATERAL (
                        SELECT CASE existing_key
                            WHEN 'IAM.Tenants.Create' THEN 'IAM.Roles.Manage'
                            WHEN 'IAM.Roles.Manage' THEN 'IAM.Roles.Manage'
                            WHEN 'IAM.Invitations.View' THEN 'IAM.Invitations.View'
                            WHEN 'IAM.Invitations.Create' THEN 'IAM.Invitations.Create'
                            WHEN 'IAM.Audit.View' THEN 'IAM.Audit.View'
                            ELSE NULL
                        END AS mapped_key
                    ) AS mapped
                    WHERE mapped_key IS NOT NULL
                ), ARRAY[]::text[])
                WHERE permission_keys IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE iam_tenant_roles
                SET permission_keys = COALESCE((
                    SELECT array_agg(DISTINCT mapped_key ORDER BY mapped_key)
                    FROM unnest(permission_keys) AS existing_key
                    CROSS JOIN LATERAL (
                        SELECT CASE existing_key
                            WHEN 'IAM.Roles.Manage' THEN 'IAM.Tenants.Create'
                            WHEN 'IAM.Invitations.View' THEN 'IAM.Invitations.View'
                            WHEN 'IAM.Invitations.Create' THEN 'IAM.Invitations.Create'
                            WHEN 'IAM.Audit.View' THEN 'IAM.Audit.View'
                            ELSE existing_key
                        END AS mapped_key
                    ) AS mapped
                    WHERE mapped_key IS NOT NULL
                ), ARRAY[]::text[])
                WHERE permission_keys IS NOT NULL;
                """);
        }
    }
}
