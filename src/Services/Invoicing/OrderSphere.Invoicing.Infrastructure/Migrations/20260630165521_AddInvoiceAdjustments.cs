using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderSphere.Invoicing.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddInvoiceAdjustments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "NetAmount",
            table: "Invoices",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<string>(
            name: "Status",
            table: "Invoices",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Issued");

        migrationBuilder.AddColumn<decimal>(
            name: "TaxAmount",
            table: "Invoices",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "TaxRate",
            table: "Invoices",
            type: "numeric(5,4)",
            precision: 5,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.CreateTable(
            name: "InvoiceAdjustments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                AmountNet = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                AppliedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InvoiceAdjustments", x => x.Id);
                table.ForeignKey(
                    name: "FK_InvoiceAdjustments_Invoices_InvoiceId",
                    column: x => x.InvoiceId,
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_InvoiceAdjustments_InvoiceId",
            table: "InvoiceAdjustments",
            column: "InvoiceId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "InvoiceAdjustments");

        migrationBuilder.DropColumn(
            name: "NetAmount",
            table: "Invoices");

        migrationBuilder.DropColumn(
            name: "Status",
            table: "Invoices");

        migrationBuilder.DropColumn(
            name: "TaxAmount",
            table: "Invoices");

        migrationBuilder.DropColumn(
            name: "TaxRate",
            table: "Invoices");
    }
}
