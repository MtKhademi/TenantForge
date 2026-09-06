using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Iam.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "iam_tenant_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    permission_keys = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iam_tenant_roles", x => x.id);
                    table.ForeignKey(
                        name: "FK_iam_tenant_roles_iam_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "iam_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_tenant_member_role_assignments",
                columns: table => new
                {
                    tenant_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iam_tenant_member_role_assignments", x => new { x.tenant_membership_id, x.tenant_role_id });
                    table.ForeignKey(
                        name: "FK_iam_tenant_member_role_assignments_iam_tenant_memberships_t~",
                        column: x => x.tenant_membership_id,
                        principalTable: "iam_tenant_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_iam_tenant_member_role_assignments_iam_tenant_roles_tenant_~",
                        column: x => x.tenant_role_id,
                        principalTable: "iam_tenant_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_iam_tenant_member_role_assignments_tenant_role_id",
                table: "iam_tenant_member_role_assignments",
                column: "tenant_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenant_roles_tenant_normalized_name",
                table: "iam_tenant_roles",
                columns: new[] { "tenant_id", "normalized_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "iam_tenant_member_role_assignments");

            migrationBuilder.DropTable(
                name: "iam_tenant_roles");
        }
    }
}
