using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Measurement.Data.Migrations
{
    internal partial class Feeds : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "measurement");

            migrationBuilder.CreateTable(
                name: "FeedRuns",
                schema: "measurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Feed = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesSources",
                schema: "measurement",
                columns: table => new
                {
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TableRef = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DateColumn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    RegionColumn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    OrdersColumn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    RevenueColumn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SetBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SetAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesSources", x => x.BrandId);
                });

            migrationBuilder.CreateTable(
                name: "SalesUploads",
                schema: "measurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    FirstDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LastDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Days = table.Column<int>(type: "int", nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesUploads", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedRuns_BrandId_Feed_StartedAt",
                schema: "measurement",
                table: "FeedRuns",
                columns: new[] { "BrandId", "Feed", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedRuns_StartedAt",
                schema: "measurement",
                table: "FeedRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SalesUploads_BrandId_UploadedAt",
                schema: "measurement",
                table: "SalesUploads",
                columns: new[] { "BrandId", "UploadedAt" });

            migrationBuilder.CreateBrandAccessPredicate("measurement");
            migrationBuilder.AddBrandSeparation("measurement", "FeedRuns");
            migrationBuilder.AddBrandSeparation("measurement", "SalesUploads");
            migrationBuilder.AddBrandSeparation("measurement", "SalesSources");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("measurement");

            migrationBuilder.DropTable(
                name: "FeedRuns",
                schema: "measurement");

            migrationBuilder.DropTable(
                name: "SalesSources",
                schema: "measurement");

            migrationBuilder.DropTable(
                name: "SalesUploads",
                schema: "measurement");
        }
    }
}
