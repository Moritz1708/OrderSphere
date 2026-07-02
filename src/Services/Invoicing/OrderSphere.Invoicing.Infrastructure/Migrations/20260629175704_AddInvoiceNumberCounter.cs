using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderSphere.Invoicing.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddInvoiceNumberCounter : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "InvoiceNumberCounters",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false),
                Value = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InvoiceNumberCounters", x => x.Id);
            });

        migrationBuilder.InsertData(
            table: "InvoiceNumberCounters",
            columns: new[] { "Id", "Value" },
            values: new object[] { 1, 0L });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "InvoiceNumberCounters");
    }
}
