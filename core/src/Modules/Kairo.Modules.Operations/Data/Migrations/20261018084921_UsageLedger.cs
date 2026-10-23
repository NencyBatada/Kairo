using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Kairo.SharedKernel.Persistence;

#nullable disable

namespace Kairo.Modules.Operations.Data.Migrations
{
    internal partial class UsageLedger : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "operations");

            migrationBuilder.CreateTable(
                name: "Invocations",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Closed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderCalls",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Reservation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TextCalls = table.Column<int>(type: "int", nullable: false),
                    Images = table.Column<int>(type: "int", nullable: false),
                    VideoClips = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Report = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    ThoughtTokens = table.Column<long>(type: "bigint", nullable: true),
                    CachedTokens = table.Column<long>(type: "bigint", nullable: true),
                    GeneratedImages = table.Column<int>(type: "int", nullable: true),
                    VideoSeconds = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SpeechCharacters = table.Column<long>(type: "bigint", nullable: true),
                    SpeechSeconds = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    EstimatedUsd = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    RateVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCalls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCalls_BrandId_CampaignId",
                schema: "operations",
                table: "ProviderCalls",
                columns: new[] { "BrandId", "CampaignId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCalls_BrandId_InvocationId_RequestId",
                schema: "operations",
                table: "ProviderCalls",
                columns: new[] { "BrandId", "InvocationId", "RequestId" },
                unique: true);
            migrationBuilder.CreateBrandAccessPredicate("operations");
            migrationBuilder.AddBrandSeparation("operations", "Invocations");
            migrationBuilder.AddBrandSeparation("operations", "ProviderCalls");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("operations");
            migrationBuilder.DropTable(
                name: "Invocations",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "ProviderCalls",
                schema: "operations");
        }
    }
}
