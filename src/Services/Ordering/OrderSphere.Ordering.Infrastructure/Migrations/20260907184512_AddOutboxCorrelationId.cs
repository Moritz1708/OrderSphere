using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderSphere.Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxCorrelationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "outbox_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The scaffolder also emitted two UpdateData calls resetting the seeded coupons'
            // scoped_category_ids — an artifact of how it re-emits collection-valued seed data on
            // Down. Up does not touch coupons and the model snapshot shows no such change, so
            // they were removed: Down must undo exactly what Up did and nothing else.
            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "outbox_messages");
        }
    }
}
