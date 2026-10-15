using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Audit.Data.Migrations
{
    internal partial class TargetIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Entries_BrandId_TargetType_TargetId_Id",
                schema: "audit",
                table: "Entries",
                columns: new[] { "BrandId", "TargetType", "TargetId", "Id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Entries_BrandId_TargetType_TargetId_Id",
                schema: "audit",
                table: "Entries");
        }
    }
}
