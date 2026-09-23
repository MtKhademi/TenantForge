using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Shop.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShopCouponRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "maximum_discount_amount",
                table: "shop_coupons",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "minimum_subtotal",
                table: "shop_coupons",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "redeemed_count",
                table: "shop_coupons",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "redemption_limit",
                table: "shop_coupons",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "shop_coupons",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "maximum_discount_amount",
                table: "shop_coupons");

            migrationBuilder.DropColumn(
                name: "minimum_subtotal",
                table: "shop_coupons");

            migrationBuilder.DropColumn(
                name: "redeemed_count",
                table: "shop_coupons");

            migrationBuilder.DropColumn(
                name: "redemption_limit",
                table: "shop_coupons");

            migrationBuilder.DropColumn(
                name: "version",
                table: "shop_coupons");
        }
    }
}
