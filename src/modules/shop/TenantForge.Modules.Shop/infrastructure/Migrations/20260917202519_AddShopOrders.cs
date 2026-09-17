using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Shop.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShopOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shop_orders",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    order_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tracking_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    customer_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    shipping_province = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    shipping_city = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    shipping_address_line = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    shipping_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sub_total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    shipping_cost = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    grand_total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shop_order_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    product_variant_id = table.Column<long>(type: "bigint", nullable: false),
                    product_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    variant_label_snapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_order_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_shop_order_items_shop_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "shop_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shop_order_items_order_id",
                table: "shop_order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_shop_orders_order_number",
                table: "shop_orders",
                column: "order_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_orders_tenant_tracking_phone",
                table: "shop_orders",
                columns: new[] { "tenant_id", "tracking_code", "customer_phone" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_orders_tracking_code",
                table: "shop_orders",
                column: "tracking_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shop_order_items");

            migrationBuilder.DropTable(
                name: "shop_orders");
        }
    }
}
