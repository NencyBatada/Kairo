using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.BrandKit.Data.Migrations
{
    internal partial class Versions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "brand_kit");

            migrationBuilder.CreateTable(
                name: "KitFiles",
                schema: "brand_kit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KitVersions",
                schema: "brand_kit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RestoredFrom = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitVersions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KitFiles_BrandId_UploadedAt",
                schema: "brand_kit",
                table: "KitFiles",
                columns: new[] { "BrandId", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KitVersions_BrandId_Number",
                schema: "brand_kit",
                table: "KitVersions",
                columns: new[] { "BrandId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KitVersions_OneApproved",
                schema: "brand_kit",
                table: "KitVersions",
                column: "BrandId",
                unique: true,
                filter: "[Status] = N'Approved'");

            migrationBuilder.CreateIndex(
                name: "IX_KitVersions_OneDraft",
                schema: "brand_kit",
                table: "KitVersions",
                column: "BrandId",
                unique: true,
                filter: "[Status] = N'Draft'");

            migrationBuilder.CreateBrandAccessPredicate("brand_kit");
            migrationBuilder.AddBrandSeparation("brand_kit", "KitVersions");
            migrationBuilder.AddBrandSeparation("brand_kit", "KitFiles");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("brand_kit");

            migrationBuilder.DropTable(
                name: "KitFiles",
                schema: "brand_kit");

            migrationBuilder.DropTable(
                name: "KitVersions",
                schema: "brand_kit");
        }
    }
}
