using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderSphere.Advisory.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddInboxForErasure : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "inbox_messages",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uuid", nullable: false),
                EventType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inbox_messages", x => x.EventId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_inbox_messages_ProcessedAt",
            table: "inbox_messages",
            column: "ProcessedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "inbox_messages");
    }
}
