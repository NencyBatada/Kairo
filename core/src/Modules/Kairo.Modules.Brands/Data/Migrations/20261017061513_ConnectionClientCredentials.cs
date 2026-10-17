using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Brands.Data.Migrations
{
    internal partial class ConnectionClientCredentials : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Connections_GoogleSubject",
                schema: "brands",
                table: "Connections");

            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                schema: "brands",
                table: "Connections",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ClientSecret",
                schema: "brands",
                table: "Connections",
                type: "varbinary(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Connections_GoogleSubject",
                schema: "brands",
                table: "Connections",
                column: "GoogleSubject")
                .Annotation("SqlServer:Include", new[] { "BrandId", "RefreshToken", "ClientId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Connections_GoogleSubject",
                schema: "brands",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "ClientId",
                schema: "brands",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "ClientSecret",
                schema: "brands",
                table: "Connections");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_GoogleSubject",
                schema: "brands",
                table: "Connections",
                column: "GoogleSubject")
                .Annotation("SqlServer:Include", new[] { "BrandId", "RefreshToken" });
        }
    }
}
