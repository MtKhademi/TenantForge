using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Shop.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShopOrderOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at_utc",
                table: "shop_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fulfilled_at_utc",
                table: "shop_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "inventory_released_at_utc",
                table: "shop_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shop_order_operations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    response_snapshot = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_order_operations", x => x.id);
                    table.ForeignKey(
                        name: "FK_shop_order_operations_shop_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "shop_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shop_order_operations_order_id",
                table: "shop_order_operations",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_shop_order_operations_tenant_idempotency_key",
                table: "shop_order_operations",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shop_order_operations");

            migrationBuilder.DropColumn(
                name: "cancelled_at_utc",
                table: "shop_orders");

            migrationBuilder.DropColumn(
                name: "fulfilled_at_utc",
                table: "shop_orders");

            migrationBuilder.DropColumn(
                name: "inventory_released_at_utc",
                table: "shop_orders");
        }
    }
}
