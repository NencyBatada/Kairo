using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.CampaignRooms.Data.Migrations
{
    internal partial class CheckResults : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckResults",
                schema: "campaign_rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetVersion = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    Issues = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SettingsVersion = table.Column<int>(type: "int", nullable: false),
                    StageRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PromptVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FailedFixes = table.Column<int>(type: "int", nullable: false),
                    OverriddenBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OverriddenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OverrideReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckResults", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckResults_BrandId_RoomId_AssetId_AssetVersion_CreatedAt",
                schema: "campaign_rooms",
                table: "CheckResults",
                columns: new[] { "BrandId", "RoomId", "AssetId", "AssetVersion", "CreatedAt" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.AddBrandSeparation("campaign_rooms", "CheckResults");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoveBrandSeparation("campaign_rooms", "CheckResults");

            migrationBuilder.DropTable(
                name: "CheckResults",
                schema: "campaign_rooms");
        }
    }
}
