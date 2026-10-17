using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Measurement.Data.Migrations
{
    internal partial class ImpactLab : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LabAnalyses",
                schema: "measurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WhatChanged = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: false),
                    AffectsKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DataFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    DataTo = table.Column<DateOnly>(type: "date", nullable: true),
                    HistoryDays = table.Column<int>(type: "int", nullable: true),
                    ChangeDays = table.Column<int>(type: "int", nullable: true),
                    SeriesCount = table.Column<int>(type: "int", nullable: false),
                    Affected = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Comparison = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Candidates = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Fit = table.Column<double>(type: "float", nullable: true),
                    Detectable = table.Column<double>(type: "float", nullable: true),
                    Placebos = table.Column<int>(type: "int", nullable: false),
                    Warnings = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Conclusive = table.Column<bool>(type: "bit", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true),
                    Explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    InconclusiveReason = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
                    NextAdvice = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
                    Method = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PromptVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    ClaimedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Problem = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RunBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MemoryEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabAnalyses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LabDatasets",
                schema: "measurement",
                columns: table => new
                {
                    AnalysisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UploadFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    UploadTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Upload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    From = table.Column<DateOnly>(type: "date", nullable: true),
                    To = table.Column<DateOnly>(type: "date", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    Series = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabDatasets", x => x.AnalysisId);
                    table.ForeignKey(
                        name: "FK_LabDatasets_LabAnalyses_AnalysisId",
                        column: x => x.AnalysisId,
                        principalSchema: "measurement",
                        principalTable: "LabAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LabAnalyses_BrandId_CreatedAt",
                schema: "measurement",
                table: "LabAnalyses",
                columns: new[] { "BrandId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LabAnalyses_State_ClaimedUntil",
                schema: "measurement",
                table: "LabAnalyses",
                columns: new[] { "State", "ClaimedUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_LabDatasets_BrandId",
                schema: "measurement",
                table: "LabDatasets",
                column: "BrandId");
            migrationBuilder.AddBrandSeparation("measurement", "LabAnalyses");
            migrationBuilder.AddBrandSeparation("measurement", "LabDatasets");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoveBrandSeparation("measurement", "LabDatasets");
            migrationBuilder.RemoveBrandSeparation("measurement", "LabAnalyses");
            migrationBuilder.DropTable(
                name: "LabDatasets",
                schema: "measurement");

            migrationBuilder.DropTable(
                name: "LabAnalyses",
                schema: "measurement");
        }
    }
}
