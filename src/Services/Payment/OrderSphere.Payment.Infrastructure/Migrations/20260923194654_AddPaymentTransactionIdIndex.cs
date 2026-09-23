using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderSphere.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentTransactionIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_payments_TransactionId",
                table: "payments",
                column: "TransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_TransactionId",
                table: "payments");
        }
    }
}
