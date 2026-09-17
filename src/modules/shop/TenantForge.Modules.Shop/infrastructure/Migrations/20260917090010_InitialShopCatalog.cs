using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Shop.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialShopCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shop_categories",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shop_products",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    category_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    base_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    compare_at_price = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_products", x => x.id);
                    table.ForeignKey(
                        name: "FK_shop_products_shop_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "shop_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shop_product_variants",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    color = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    size = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sku = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    stock_quantity = table.Column<int>(type: "integer", nullable: false),
                    price_override = table.Column<decimal>(type: "numeric(12,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_product_variants", x => x.id);
                    table.ForeignKey(
                        name: "FK_shop_product_variants_shop_products_product_id",
                        column: x => x.product_id,
                        principalTable: "shop_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shop_size_guide_columns",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_size_guide_columns", x => x.id);
                    table.ForeignKey(
                        name: "FK_shop_size_guide_columns_shop_products_product_id",
                        column: x => x.product_id,
                        principalTable: "shop_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shop_size_guide_rows",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    size_label = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_size_guide_rows", x => x.id);
                    table.ForeignKey(
                        name: "FK_shop_size_guide_rows_shop_products_product_id",
                        column: x => x.product_id,
                        principalTable: "shop_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shop_size_guide_cells",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    row_id = table.Column<long>(type: "bigint", nullable: false),
                    column_id = table.Column<long>(type: "bigint", nullable: false),
                    value = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_size_guide_cells", x => x.id);
                    table.ForeignKey(
                        name: "FK_shop_size_guide_cells_shop_size_guide_columns_column_id",
                        column: x => x.column_id,
                        principalTable: "shop_size_guide_columns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_shop_size_guide_cells_shop_size_guide_rows_row_id",
                        column: x => x.row_id,
                        principalTable: "shop_size_guide_rows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shop_categories_tenant_slug",
                table: "shop_categories",
                columns: new[] { "tenant_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_product_variants_product_color_size",
                table: "shop_product_variants",
                columns: new[] { "product_id", "color", "size" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_products_category_id",
                table: "shop_products",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_shop_products_tenant_slug",
                table: "shop_products",
                columns: new[] { "tenant_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shop_size_guide_cells_column_id",
                table: "shop_size_guide_cells",
                column: "column_id");

            migrationBuilder.CreateIndex(
                name: "ix_shop_size_guide_cells_row_column",
                table: "shop_size_guide_cells",
                columns: new[] { "row_id", "column_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_size_guide_columns_product_id",
                table: "shop_size_guide_columns",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_shop_size_guide_rows_product_id",
                table: "shop_size_guide_rows",
                column: "product_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shop_product_variants");

            migrationBuilder.DropTable(
                name: "shop_size_guide_cells");

            migrationBuilder.DropTable(
                name: "shop_size_guide_columns");

            migrationBuilder.DropTable(
                name: "shop_size_guide_rows");

            migrationBuilder.DropTable(
                name: "shop_products");

            migrationBuilder.DropTable(
                name: "shop_categories");
        }
    }
}
