using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Live.Data.Migrations
{
    internal partial class Outbox : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "live");

            migrationBuilder.CreateTable(
                name: "Outbox",
                schema: "live",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AudienceKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AudienceRoles = table.Column<int>(type: "int", nullable: false),
                    AudienceUsers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_BrandId_Id",
                schema: "live",
                table: "Outbox",
                columns: new[] { "BrandId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_CreatedAt",
                schema: "live",
                table: "Outbox",
                column: "CreatedAt");

            migrationBuilder.CreateBrandAccessPredicate("live");
            migrationBuilder.AddBrandSeparation("live", "Outbox");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("live");

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: "live");
        }
    }
}
