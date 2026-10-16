using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.CampaignRooms.Data.Migrations
{
    internal partial class CheckSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckSettings",
                schema: "campaign_rooms",
                columns: table => new
                {
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    PassScore = table.Column<int>(type: "int", nullable: false),
                    FixAttempts = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CustomRules = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rules = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckSettings", x => x.BrandId);
                });

            migrationBuilder.AddBrandSeparation("campaign_rooms", "CheckSettings");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoveBrandSeparation("campaign_rooms", "CheckSettings");

            migrationBuilder.DropTable(
                name: "CheckSettings",
                schema: "campaign_rooms");
        }
    }
}
