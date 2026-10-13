using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Brands.Data.Migrations
{
    internal partial class Connections : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Connections",
                schema: "brands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Product = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Scopes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    GoogleSubject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    RefreshToken = table.Column<byte[]>(type: "varbinary(4096)", maxLength: 4096, nullable: true),
                    KeyVersion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ConnectedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReconnectReason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsentNonces",
                schema: "brands",
                columns: table => new
                {
                    Nonce = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentNonces", x => x.Nonce);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Connections_BrandId_Product",
                schema: "brands",
                table: "Connections",
                columns: new[] { "BrandId", "Product" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Connections_GoogleSubject",
                schema: "brands",
                table: "Connections",
                column: "GoogleSubject");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentNonces_ExpiresAt",
                schema: "brands",
                table: "ConsentNonces",
                column: "ExpiresAt");

            migrationBuilder.AddBrandSeparation("brands", "Connections");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoveBrandSeparation("brands", "Connections");

            migrationBuilder.DropTable(
                name: "Connections",
                schema: "brands");

            migrationBuilder.DropTable(
                name: "ConsentNonces",
                schema: "brands");
        }
    }
}
