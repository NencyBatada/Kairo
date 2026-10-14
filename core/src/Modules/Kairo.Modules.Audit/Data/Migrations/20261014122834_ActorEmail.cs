using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Audit.Data.Migrations
{
    internal partial class ActorEmail : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorEmail",
                schema: "audit",
                table: "Entries",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActorEmail",
                schema: "audit",
                table: "Entries");
        }
    }
}
