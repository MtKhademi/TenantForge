using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Shop.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShopPaymentLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "amount_snapshot",
                table: "shop_payment_attempts",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "callback_token_hash",
                table: "shop_payment_attempts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "failure_code",
                table: "shop_payment_attempts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_reference",
                table: "shop_payment_attempts",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "verified_at_utc",
                table: "shop_payment_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "shop_payment_attempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "shop_payment_initiations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    attempt_id = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    redirect_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    token_seed = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_payment_initiations", x => x.id);
                    table.ForeignKey(
                        name: "FK_shop_payment_initiations_shop_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "shop_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shop_payment_initiations_order_id",
                table: "shop_payment_initiations",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_shop_payment_initiations_tenant_idempotency_key",
                table: "shop_payment_initiations",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shop_payment_initiations");

            migrationBuilder.DropColumn(
                name: "amount_snapshot",
                table: "shop_payment_attempts");

            migrationBuilder.DropColumn(
                name: "callback_token_hash",
                table: "shop_payment_attempts");

            migrationBuilder.DropColumn(
                name: "failure_code",
                table: "shop_payment_attempts");

            migrationBuilder.DropColumn(
                name: "provider_reference",
                table: "shop_payment_attempts");

            migrationBuilder.DropColumn(
                name: "verified_at_utc",
                table: "shop_payment_attempts");

            migrationBuilder.DropColumn(
                name: "version",
                table: "shop_payment_attempts");
        }
    }
}
