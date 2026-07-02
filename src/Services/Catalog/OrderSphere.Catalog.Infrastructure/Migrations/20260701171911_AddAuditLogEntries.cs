using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderSphere.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddAuditLogEntries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_log_entries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EntityType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                EntityId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ChangedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Changes = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_log_entries", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_EntityType_EntityId_OccurredAt",
            table: "audit_log_entries",
            columns: new[] { "EntityType", "EntityId", "OccurredAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");
    }
}
