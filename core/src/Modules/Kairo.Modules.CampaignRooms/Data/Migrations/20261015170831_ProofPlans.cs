using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.CampaignRooms.Data.Migrations
{
    internal partial class ProofPlans : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Input",
                schema: "campaign_rooms",
                table: "StageRuns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProofPlans",
                schema: "campaign_rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    BriefVersion = table.Column<int>(type: "int", nullable: false),
                    StageRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Method = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LockedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ChangedAfterLock = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProofPlans", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProofPlans_BrandId_RoomId_Version",
                schema: "campaign_rooms",
                table: "ProofPlans",
                columns: new[] { "BrandId", "RoomId", "Version" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.AddBrandSeparation("campaign_rooms", "ProofPlans");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoveBrandSeparation("campaign_rooms", "ProofPlans");

            migrationBuilder.DropTable(
                name: "ProofPlans",
                schema: "campaign_rooms");

            migrationBuilder.DropColumn(
                name: "Input",
                schema: "campaign_rooms",
                table: "StageRuns");
        }
    }
}
