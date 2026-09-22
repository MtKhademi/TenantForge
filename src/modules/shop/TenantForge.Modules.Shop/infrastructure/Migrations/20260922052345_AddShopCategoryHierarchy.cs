using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Shop.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShopCategoryHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "parent_category_id",
                table: "shop_categories",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_shop_categories_parent_category_id",
                table: "shop_categories",
                column: "parent_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_shop_categories_tenant_parent_display_order",
                table: "shop_categories",
                columns: new[] { "tenant_id", "parent_category_id", "display_order" });

            migrationBuilder.AddForeignKey(
                name: "FK_shop_categories_shop_categories_parent_category_id",
                table: "shop_categories",
                column: "parent_category_id",
                principalTable: "shop_categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_shop_categories_shop_categories_parent_category_id",
                table: "shop_categories");

            migrationBuilder.DropIndex(
                name: "IX_shop_categories_parent_category_id",
                table: "shop_categories");

            migrationBuilder.DropIndex(
                name: "ix_shop_categories_tenant_parent_display_order",
                table: "shop_categories");

            migrationBuilder.DropColumn(
                name: "parent_category_id",
                table: "shop_categories");
        }
    }
}
