using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Catalogue.Data.Migrations
{
    internal partial class Catalogue : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalogue");

            migrationBuilder.CreateTable(
                name: "Imports",
                schema: "catalogue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FeedUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    Valid = table.Column<int>(type: "int", nullable: false),
                    Invalid = table.Column<int>(type: "int", nullable: false),
                    Added = table.Column<int>(type: "int", nullable: false),
                    Updated = table.Column<int>(type: "int", nullable: false),
                    Removed = table.Column<int>(type: "int", nullable: false),
                    Unchanged = table.Column<int>(type: "int", nullable: false),
                    Problems = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    StorageKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StartedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Imports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                schema: "catalogue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Link = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ImageLink = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AdditionalImageLinks = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    SalePrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    Availability = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(750)", maxLength: 750, nullable: true),
                    GoogleCategory = table.Column<string>(type: "nvarchar(750)", maxLength: 750, nullable: true),
                    Brand = table.Column<string>(type: "nvarchar(70)", maxLength: 70, nullable: true),
                    Gtin = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: true),
                    ItemGroupId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Missing = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Fixes = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sources",
                schema: "catalogue",
                columns: table => new
                {
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FeedUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SetBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SetAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSucceededAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailuresInRow = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sources", x => x.BrandId);
                });

            migrationBuilder.CreateTable(
                name: "StockEvents",
                schema: "catalogue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    From = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    To = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Imports_BrandId_StartedAt",
                schema: "catalogue",
                table: "Imports",
                columns: new[] { "BrandId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_BrandId_OfferId",
                schema: "catalogue",
                table: "Products",
                columns: new[] { "BrandId", "OfferId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_BrandId_Status_Missing",
                schema: "catalogue",
                table: "Products",
                columns: new[] { "BrandId", "Status", "Missing" });

            migrationBuilder.CreateIndex(
                name: "IX_StockEvents_BrandId_ProductId_At",
                schema: "catalogue",
                table: "StockEvents",
                columns: new[] { "BrandId", "ProductId", "At" });

            migrationBuilder.CreateBrandAccessPredicate("catalogue");
            migrationBuilder.AddBrandSeparation("catalogue", "Products");
            migrationBuilder.AddBrandSeparation("catalogue", "Sources");
            migrationBuilder.AddBrandSeparation("catalogue", "Imports");
            migrationBuilder.AddBrandSeparation("catalogue", "StockEvents");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("catalogue");

            migrationBuilder.DropTable(
                name: "Imports",
                schema: "catalogue");

            migrationBuilder.DropTable(
                name: "Products",
                schema: "catalogue");

            migrationBuilder.DropTable(
                name: "Sources",
                schema: "catalogue");

            migrationBuilder.DropTable(
                name: "StockEvents",
                schema: "catalogue");
        }
    }
}
