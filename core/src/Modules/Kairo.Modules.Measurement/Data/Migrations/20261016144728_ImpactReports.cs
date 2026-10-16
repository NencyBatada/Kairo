using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Measurement.Data.Migrations
{
    internal partial class ImpactReports : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImpactReports",
                schema: "measurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReadingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Problem = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kpi = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Spend = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Results = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    CostPerResult = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    CostPerExtra = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    CostPerExtraLow = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    CostPerExtraHigh = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    TargetCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NextStep = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NextTest = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Flags = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true),
                    NextStepReasoning = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    InconclusiveReason = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
                    NextTestAdvice = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PromptVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReadyAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImpactReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImpactReports_BrandId_RoomId",
                schema: "measurement",
                table: "ImpactReports",
                columns: new[] { "BrandId", "RoomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImpactReports_BrandId_Status_ReadyAt",
                schema: "measurement",
                table: "ImpactReports",
                columns: new[] { "BrandId", "Status", "ReadyAt" });
            migrationBuilder.AddBrandSeparation("measurement", "ImpactReports");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoveBrandSeparation("measurement", "ImpactReports");
            migrationBuilder.DropTable(
                name: "ImpactReports",
                schema: "measurement");
        }
    }
}
