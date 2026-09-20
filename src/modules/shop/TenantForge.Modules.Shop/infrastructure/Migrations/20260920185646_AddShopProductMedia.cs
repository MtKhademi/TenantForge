using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Shop.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShopProductMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "gallery_version",
                table: "shop_products",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "shop_product_images",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    content_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    alt_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_product_images", x => x.id);
                    table.ForeignKey(
                        name: "FK_shop_product_images_shop_products_product_id",
                        column: x => x.product_id,
                        principalTable: "shop_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shop_product_images_tenant_product",
                table: "shop_product_images",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ux_shop_product_images_product_display_order",
                table: "shop_product_images",
                columns: new[] { "product_id", "display_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_shop_product_images_storage_key",
                table: "shop_product_images",
                column: "storage_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shop_product_images");

            migrationBuilder.DropColumn(
                name: "gallery_version",
                table: "shop_products");
        }
    }
}
