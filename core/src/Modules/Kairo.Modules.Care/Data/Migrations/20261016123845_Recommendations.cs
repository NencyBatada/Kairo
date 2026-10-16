using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Care.Data.Migrations
{
    internal partial class Recommendations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "care");

            migrationBuilder.CreateTable(
                name: "RecommendationRuns",
                schema: "care",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: false),
                    Drafted = table.Column<int>(type: "int", nullable: false),
                    DraftedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recommendations",
                schema: "care",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CampaignType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExpectedEffect = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Reasoning = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Change = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Baseline = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GoogleResource = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Problem = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DecidedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DismissReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NotifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recommendations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationRuns_BrandId_RoomId",
                schema: "care",
                table: "RecommendationRuns",
                columns: new[] { "BrandId", "RoomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_BrandId_GoogleResource",
                schema: "care",
                table: "Recommendations",
                columns: new[] { "BrandId", "GoogleResource" },
                unique: true,
                filter: "[GoogleResource] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_BrandId_RoomId_CreatedAt",
                schema: "care",
                table: "Recommendations",
                columns: new[] { "BrandId", "RoomId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_BrandId_State",
                schema: "care",
                table: "Recommendations",
                columns: new[] { "BrandId", "State" });
            migrationBuilder.CreateBrandAccessPredicate("care");
            migrationBuilder.AddBrandSeparation("care", "Recommendations");
            migrationBuilder.AddBrandSeparation("care", "RecommendationRuns");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("care");
            migrationBuilder.DropTable(
                name: "RecommendationRuns",
                schema: "care");

            migrationBuilder.DropTable(
                name: "Recommendations",
                schema: "care");
        }
    }
}
