using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderSphere.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddTenantId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "TenantId",
            table: "stock_reservations",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<Guid>(
            name: "TenantId",
            table: "products",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<Guid>(
            name: "TenantId",
            table: "product_reviews",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<Guid>(
            name: "TenantId",
            table: "categories",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<Guid>(
            name: "TenantId",
            table: "brands",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "stock_reservations");

        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "products");

        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "product_reviews");

        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "categories");

        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "brands");
    }
}
