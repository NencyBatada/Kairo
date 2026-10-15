using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Triggers.Data.Migrations
{
    internal partial class Triggers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "triggers");

            migrationBuilder.CreateTable(
                name: "BrandStates",
                schema: "triggers",
                columns: table => new
                {
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FestivalsSeededAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandStates", x => x.BrandId);
                });

            migrationBuilder.CreateTable(
                name: "FestivalEvents",
                schema: "triggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Regions = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FestivalEvents", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "TriggerFirings",
                schema: "triggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Regions = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CooldownUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConditionEndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TriggerFirings", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "TriggerRules",
                schema: "triggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Regions = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(750)", maxLength: 750, nullable: false),
                    CooldownHours = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MemoryEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastCheckOutcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    LastImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastFiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Condition = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TriggerRules", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "WeatherForecasts",
                schema: "triggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegionId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Hour = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Days = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherForecasts", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FestivalEvents_BrandId_StartsOn",
                schema: "triggers",
                table: "FestivalEvents",
                columns: new[] { "BrandId", "StartsOn" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_TriggerFirings_BrandId_FiredAt",
                schema: "triggers",
                table: "TriggerFirings",
                columns: new[] { "BrandId", "FiredAt" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_TriggerFirings_BrandId_RuleId_FiredAt",
                schema: "triggers",
                table: "TriggerFirings",
                columns: new[] { "BrandId", "RuleId", "FiredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TriggerRules_BrandId_CreatedAt",
                schema: "triggers",
                table: "TriggerRules",
                columns: new[] { "BrandId", "CreatedAt" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_TriggerRules_BrandId_Status",
                schema: "triggers",
                table: "TriggerRules",
                columns: new[] { "BrandId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WeatherForecasts_RegionId_Hour",
                schema: "triggers",
                table: "WeatherForecasts",
                columns: new[] { "RegionId", "Hour" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateBrandAccessPredicate("triggers");
            migrationBuilder.AddBrandSeparation("triggers", "TriggerRules");
            migrationBuilder.AddBrandSeparation("triggers", "TriggerFirings");
            migrationBuilder.AddBrandSeparation("triggers", "FestivalEvents");
            migrationBuilder.AddBrandSeparation("triggers", "BrandStates");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("triggers");

            migrationBuilder.DropTable(
                name: "BrandStates",
                schema: "triggers");

            migrationBuilder.DropTable(
                name: "FestivalEvents",
                schema: "triggers");

            migrationBuilder.DropTable(
                name: "TriggerFirings",
                schema: "triggers");

            migrationBuilder.DropTable(
                name: "TriggerRules",
                schema: "triggers");

            migrationBuilder.DropTable(
                name: "WeatherForecasts",
                schema: "triggers");
        }
    }
}
