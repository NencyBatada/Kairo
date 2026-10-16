using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.CampaignRooms.Data.Migrations
{
    internal partial class LaunchPlans : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GateHash",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LaunchPlans",
                schema: "campaign_rooms",
                columns: table => new
                {
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Settings = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LaunchPlans", x => x.RoomId);
                });
            migrationBuilder.AddBrandSeparation("campaign_rooms", "LaunchPlans");

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoveBrandSeparation("campaign_rooms", "LaunchPlans");
            migrationBuilder.DropTable(
                name: "LaunchPlans",
                schema: "campaign_rooms");

            migrationBuilder.DropColumn(
                name: "GateHash",
                schema: "campaign_rooms",
                table: "Rooms");
        }
    }
}
