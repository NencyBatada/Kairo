using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Launch.Data.Migrations
{
    internal partial class CampaignLaunches : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "launch");

            migrationBuilder.CreateTable(
                name: "Launches",
                schema: "launch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Plan = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Step = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AttemptingStep = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Resources = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Uploads = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClaimToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClaimedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SuggestedFix = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Uncertain = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Launches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductAds",
                schema: "launch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CampaignType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CampaignResource = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AdResource = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Launches_BrandId_RoomId",
                schema: "launch",
                table: "Launches",
                columns: new[] { "BrandId", "RoomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Launches_Status_ClaimedUntil",
                schema: "launch",
                table: "Launches",
                columns: new[] { "Status", "ClaimedUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAds_BrandId_RoomId_ProductId_AdResource",
                schema: "launch",
                table: "ProductAds",
                columns: new[] { "BrandId", "RoomId", "ProductId", "AdResource" },
                unique: true);
            migrationBuilder.CreateBrandAccessPredicate("launch");
            migrationBuilder.AddBrandSeparation("launch", "Launches");
            migrationBuilder.AddBrandSeparation("launch", "ProductAds");

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("launch");
            migrationBuilder.DropTable(
                name: "Launches",
                schema: "launch");

            migrationBuilder.DropTable(
                name: "ProductAds",
                schema: "launch");
        }
    }
}
