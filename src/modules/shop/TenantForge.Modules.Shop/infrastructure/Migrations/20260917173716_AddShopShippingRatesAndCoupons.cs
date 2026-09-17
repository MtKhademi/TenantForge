using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Shop.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShopShippingRatesAndCoupons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shop_coupons",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normalized_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    discount_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    discount_value = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_coupons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shop_shipping_rates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    province_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    cost = table.Column<decimal>(type: "numeric(12,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_shipping_rates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shop_coupons_tenant_normalized_code",
                table: "shop_coupons",
                columns: new[] { "tenant_id", "normalized_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_shipping_rates_tenant_province",
                table: "shop_shipping_rates",
                columns: new[] { "tenant_id", "province_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shop_coupons");

            migrationBuilder.DropTable(
                name: "shop_shipping_rates");
        }
    }
}
