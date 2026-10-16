using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Launch.Data.Migrations
{
    internal partial class CampaignPacing : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pacing",
                schema: "launch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LaunchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Spend = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    PlannedSpend = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Conversions = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ConversionValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Impressions = table.Column<long>(type: "bigint", nullable: false),
                    Clicks = table.Column<long>(type: "bigint", nullable: false),
                    Deviation = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StreakAlerted = table.Column<bool>(type: "bit", nullable: false),
                    AlertedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CauseKind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Cause = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SuggestedFix = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pacing", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pacing_BrandId_LaunchId_CampaignType_Date",
                schema: "launch",
                table: "Pacing",
                columns: new[] { "BrandId", "LaunchId", "CampaignType", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pacing_BrandId_RoomId_Date",
                schema: "launch",
                table: "Pacing",
                columns: new[] { "BrandId", "RoomId", "Date" });
            migrationBuilder.AddBrandSeparation("launch", "Pacing");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoveBrandSeparation("launch", "Pacing");
            migrationBuilder.DropTable(
                name: "Pacing",
                schema: "launch");
        }
    }
}
