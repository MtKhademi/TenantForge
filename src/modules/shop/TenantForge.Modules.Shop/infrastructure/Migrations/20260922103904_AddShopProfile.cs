using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Shop.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShopProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shop_profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tagline = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    support_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    instagram_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    about_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    shipping_policy = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: false),
                    payment_policy = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: false),
                    return_policy = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: false),
                    privacy_policy = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_profiles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shop_profiles_tenant_id",
                table: "shop_profiles",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shop_profiles");
        }
    }
}
