using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Measurement.Data.Migrations
{
    internal partial class LiftReadings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LiftReadings",
                schema: "measurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Early = table.Column<bool>(type: "bit", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ThroughDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kpi = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Days = table.Column<int>(type: "int", nullable: false),
                    PlannedDays = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Conclusive = table.Column<bool>(type: "bit", nullable: false),
                    Lift = table.Column<double>(type: "float", nullable: true),
                    Low = table.Column<double>(type: "float", nullable: true),
                    High = table.Column<double>(type: "float", nullable: true),
                    Actual = table.Column<double>(type: "float", nullable: true),
                    Expected = table.Column<double>(type: "float", nullable: true),
                    Extra = table.Column<double>(type: "float", nullable: true),
                    ExtraLow = table.Column<double>(type: "float", nullable: true),
                    ExtraHigh = table.Column<double>(type: "float", nullable: true),
                    Placebos = table.Column<int>(type: "int", nullable: false),
                    Spread = table.Column<double>(type: "float", nullable: true),
                    Revenue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Regions = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Daily = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiftReadings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LiftReadings_BrandId_RoomId_Early_ThroughDate",
                schema: "measurement",
                table: "LiftReadings",
                columns: new[] { "BrandId", "RoomId", "Early", "ThroughDate" },
                unique: true);
            migrationBuilder.AddBrandSeparation("measurement", "LiftReadings");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoveBrandSeparation("measurement", "LiftReadings");
            migrationBuilder.DropTable(
                name: "LiftReadings",
                schema: "measurement");
        }
    }
}
