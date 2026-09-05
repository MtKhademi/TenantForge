using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Iam.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "iam_tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iam_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iam_tenant_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iam_tenant_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_iam_tenant_memberships_iam_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "iam_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_iam_tenant_memberships_iam_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "iam_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenant_memberships_account_id",
                table: "iam_tenant_memberships",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenant_memberships_tenant_account",
                table: "iam_tenant_memberships",
                columns: new[] { "tenant_id", "account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenants_normalized_slug",
                table: "iam_tenants",
                column: "normalized_slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "iam_tenant_memberships");

            migrationBuilder.DropTable(
                name: "iam_tenants");
        }
    }
}
