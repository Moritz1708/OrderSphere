using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderSphere.Advisory.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddTenantId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "TenantId",
            table: "Conversations",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<Guid>(
            name: "TenantId",
            table: "ConversationMessages",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "Conversations");

        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "ConversationMessages");
    }
}
