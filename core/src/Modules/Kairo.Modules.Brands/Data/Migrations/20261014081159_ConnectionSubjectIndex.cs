using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Brands.Data.Migrations
{
    internal partial class ConnectionSubjectIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Connections_GoogleSubject",
                schema: "brands",
                table: "Connections");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_GoogleSubject",
                schema: "brands",
                table: "Connections",
                column: "GoogleSubject")
                .Annotation("SqlServer:Include", new[] { "BrandId", "RefreshToken" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Connections_GoogleSubject",
                schema: "brands",
                table: "Connections");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_GoogleSubject",
                schema: "brands",
                table: "Connections",
                column: "GoogleSubject");
        }
    }
}
