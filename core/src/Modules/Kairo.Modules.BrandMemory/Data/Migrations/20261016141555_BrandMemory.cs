using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.BrandMemory.Data.Migrations
{
    internal partial class BrandMemory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "brand_memory");

            migrationBuilder.CreateTable(
                name: "LearnRuns",
                schema: "brand_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Entries = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PromptVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MemoryEntries",
                schema: "brand_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Lesson = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Products = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Categories = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Regions = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Languages = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceRoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EditedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EditedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RemovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MemoryUses",
                schema: "brand_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsedIn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryUses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LearnRuns_BrandId_RoomId",
                schema: "brand_memory",
                table: "LearnRuns",
                columns: new[] { "BrandId", "RoomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemoryEntries_BrandId_SourceRoomId",
                schema: "brand_memory",
                table: "MemoryEntries",
                columns: new[] { "BrandId", "SourceRoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryEntries_BrandId_State_CreatedAt",
                schema: "brand_memory",
                table: "MemoryEntries",
                columns: new[] { "BrandId", "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryUses_BrandId_EntryId_UsedIn_TargetId",
                schema: "brand_memory",
                table: "MemoryUses",
                columns: new[] { "BrandId", "EntryId", "UsedIn", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemoryUses_BrandId_RoomId_UsedIn",
                schema: "brand_memory",
                table: "MemoryUses",
                columns: new[] { "BrandId", "RoomId", "UsedIn" });
            migrationBuilder.CreateBrandAccessPredicate("brand_memory");
            migrationBuilder.AddBrandSeparation("brand_memory", "MemoryEntries");
            migrationBuilder.AddBrandSeparation("brand_memory", "MemoryUses");
            migrationBuilder.AddBrandSeparation("brand_memory", "LearnRuns");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("brand_memory");
            migrationBuilder.DropTable(
                name: "LearnRuns",
                schema: "brand_memory");

            migrationBuilder.DropTable(
                name: "MemoryEntries",
                schema: "brand_memory");

            migrationBuilder.DropTable(
                name: "MemoryUses",
                schema: "brand_memory");
        }
    }
}
