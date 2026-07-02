using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderSphere.Ordering.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddTenantId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "TenantId",
            table: "return_requests",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<Guid>(
            name: "TenantId",
            table: "orders",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<Guid>(
            name: "TenantId",
            table: "order_items",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<Guid>(
            name: "TenantId",
            table: "coupons",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.UpdateData(
            table: "coupons",
            keyColumn: "Id",
            keyValue: new Guid("0192a000-0000-7000-8000-000000000001"),
            column: "TenantId",
            value: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.UpdateData(
            table: "coupons",
            keyColumn: "Id",
            keyValue: new Guid("0192a000-0000-7000-8000-000000000002"),
            column: "TenantId",
            value: new Guid("00000000-0000-0000-0000-000000000000"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "return_requests");

        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "order_items");

        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "coupons");

        migrationBuilder.UpdateData(
            table: "coupons",
            keyColumn: "Id",
            keyValue: new Guid("0192a000-0000-7000-8000-000000000001"),
            column: "scoped_category_ids",
            value: new List<Guid>());

        migrationBuilder.UpdateData(
            table: "coupons",
            keyColumn: "Id",
            keyValue: new Guid("0192a000-0000-7000-8000-000000000002"),
            column: "scoped_category_ids",
            value: new List<Guid>());
    }
}
