using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Opportunities.Data.Migrations
{
    internal partial class Opportunities : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "opportunities");

            migrationBuilder.CreateTable(
                name: "Opportunities",
                schema: "opportunities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TopicKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Keywords = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Growth = table.Column<double>(type: "float", nullable: false),
                    Mom = table.Column<double>(type: "float", nullable: true),
                    Yoy = table.Column<double>(type: "float", nullable: true),
                    LastMonthSearches = table.Column<long>(type: "bigint", nullable: false),
                    AvgMonthlySearches = table.Column<long>(type: "bigint", nullable: false),
                    MatchedCategories = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MatchedProductIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FitReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReasonsStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PromptVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MissedRefreshes = table.Column<int>(type: "int", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DismissReason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DismissNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    GrowthAtClose = table.Column<double>(type: "float", nullable: true),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Breakdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reasons = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Regions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Trend = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Opportunities", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "RefreshRuns",
                schema: "opportunities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Keywords = table.Column<int>(type: "int", nullable: false),
                    Created = table.Column<int>(type: "int", nullable: false),
                    Updated = table.Column<int>(type: "int", nullable: false),
                    Expired = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshRuns", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "WatchTopics",
                schema: "opportunities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Phrase = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchTopics", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_BrandId_FirstSeenAt",
                schema: "opportunities",
                table: "Opportunities",
                columns: new[] { "BrandId", "FirstSeenAt" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_BrandId_Status_Score",
                schema: "opportunities",
                table: "Opportunities",
                columns: new[] { "BrandId", "Status", "Score" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshRuns_BrandId_StartedAt",
                schema: "opportunities",
                table: "RefreshRuns",
                columns: new[] { "BrandId", "StartedAt" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchTopics_BrandId_Phrase",
                schema: "opportunities",
                table: "WatchTopics",
                columns: new[] { "BrandId", "Phrase" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateBrandAccessPredicate("opportunities");
            migrationBuilder.AddBrandSeparation("opportunities", "Opportunities");
            migrationBuilder.AddBrandSeparation("opportunities", "WatchTopics");
            migrationBuilder.AddBrandSeparation("opportunities", "RefreshRuns");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("opportunities");

            migrationBuilder.DropTable(
                name: "Opportunities",
                schema: "opportunities");

            migrationBuilder.DropTable(
                name: "RefreshRuns",
                schema: "opportunities");

            migrationBuilder.DropTable(
                name: "WatchTopics",
                schema: "opportunities");
        }
    }
}
