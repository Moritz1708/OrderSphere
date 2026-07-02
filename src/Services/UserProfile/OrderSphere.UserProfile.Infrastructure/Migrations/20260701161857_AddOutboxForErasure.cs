using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderSphere.UserProfile.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddOutboxForErasure : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Content = table.Column<string>(type: "text", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true),
                RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                TraceParent = table.Column<string>(type: "character varying(55)", maxLength: 55, nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedAt_OccurredAt",
            table: "outbox_messages",
            columns: new[] { "ProcessedAt", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_RetryCount",
            table: "outbox_messages",
            column: "RetryCount");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "outbox_messages");
    }
}
