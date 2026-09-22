using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Shop.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShopCartReservationExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at_utc",
                table: "shop_carts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at_utc",
                table: "shop_carts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_touched_at_utc",
                table: "shop_carts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "shop_carts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE shop_carts
                SET status = 'Active',
                    last_touched_at_utc = now(),
                    expires_at_utc = now() + interval '30 minutes'
                WHERE status IS NULL;
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "expires_at_utc",
                table: "shop_carts",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "last_touched_at_utc",
                table: "shop_carts",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "shop_carts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_carts_status_expires_at_utc",
                table: "shop_carts",
                columns: new[] { "status", "expires_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_shop_carts_status_expires_at_utc",
                table: "shop_carts");

            migrationBuilder.DropColumn(
                name: "closed_at_utc",
                table: "shop_carts");

            migrationBuilder.DropColumn(
                name: "expires_at_utc",
                table: "shop_carts");

            migrationBuilder.DropColumn(
                name: "last_touched_at_utc",
                table: "shop_carts");

            migrationBuilder.DropColumn(
                name: "status",
                table: "shop_carts");
        }
    }
}
