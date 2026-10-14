using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.BrandKit.Data.Migrations
{
    internal partial class DraftRuns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DraftRuns",
                schema: "brand_kit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WebsiteUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Pages = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Ads = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PromptVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    ClaimedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VersionNumber = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftRuns_BrandId_RequestedAt",
                schema: "brand_kit",
                table: "DraftRuns",
                columns: new[] { "BrandId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftRuns_OneInProgress",
                schema: "brand_kit",
                table: "DraftRuns",
                column: "BrandId",
                unique: true,
                filter: "[Status] IN (N'Pending', N'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_DraftRuns_Status_ClaimedUntil",
                schema: "brand_kit",
                table: "DraftRuns",
                columns: new[] { "Status", "ClaimedUntil" });

            migrationBuilder.AddBrandSeparation("brand_kit", "DraftRuns");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoveBrandSeparation("brand_kit", "DraftRuns");

            migrationBuilder.DropTable(
                name: "DraftRuns",
                schema: "brand_kit");
        }
    }
}
