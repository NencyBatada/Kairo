using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Launch.Data.Migrations
{
    internal partial class StockPauses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdPauses",
                schema: "launch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LaunchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CampaignResource = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AdResource = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProductIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PausedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdPauses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockWatches",
                schema: "launch",
                columns: table => new
                {
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NextCheckAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FeedUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StaleSince = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockWatches", x => x.BrandId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdPauses_BrandId_AdResource",
                schema: "launch",
                table: "AdPauses",
                columns: new[] { "BrandId", "AdResource" },
                unique: true,
                filter: "[State] IN (N'held', N'paused')");

            migrationBuilder.CreateIndex(
                name: "IX_AdPauses_BrandId_BatchId",
                schema: "launch",
                table: "AdPauses",
                columns: new[] { "BrandId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdPauses_BrandId_RoomId_CreatedAt",
                schema: "launch",
                table: "AdPauses",
                columns: new[] { "BrandId", "RoomId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockWatches_NextCheckAt",
                schema: "launch",
                table: "StockWatches",
                column: "NextCheckAt");
            migrationBuilder.AddBrandSeparation("launch", "AdPauses");
            migrationBuilder.AddBrandSeparation("launch", "StockWatches");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoveBrandSeparation("launch", "StockWatches");
            migrationBuilder.RemoveBrandSeparation("launch", "AdPauses");
            migrationBuilder.DropTable(
                name: "AdPauses",
                schema: "launch");

            migrationBuilder.DropTable(
                name: "StockWatches",
                schema: "launch");
        }
    }
}
