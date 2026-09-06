using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Iam.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationsAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "iam_audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    target = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iam_audit_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_iam_audit_events_iam_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalTable: "iam_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_iam_audit_events_iam_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "iam_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iam_tenant_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    role = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iam_tenant_invitations", x => x.id);
                    table.ForeignKey(
                        name: "FK_iam_tenant_invitations_iam_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "iam_tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_iam_audit_events_actor_account_id",
                table: "iam_audit_events",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_iam_audit_events_tenant_action",
                table: "iam_audit_events",
                columns: new[] { "tenant_id", "action" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_audit_events_tenant_created",
                table: "iam_audit_events",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_iam_tenant_invitations_active_email",
                table: "iam_tenant_invitations",
                columns: new[] { "tenant_id", "normalized_email", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "iam_audit_events");

            migrationBuilder.DropTable(
                name: "iam_tenant_invitations");
        }
    }
}
