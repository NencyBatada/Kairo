using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.CampaignRooms.Data.Migrations
{
    public partial class BriefApprovalLock : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GateBriefVersion",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GatePlanVersion",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GateRoute",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ApprovedWithWarnings",
                schema: "campaign_rooms",
                table: "ProofPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ChangeReason",
                schema: "campaign_rooms",
                table: "ProofPlans",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChangedBy",
                schema: "campaign_rooms",
                table: "ProofPlans",
                type: "uniqueidentifier",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GateBriefVersion",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "GatePlanVersion",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "GateRoute",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "ApprovedWithWarnings",
                schema: "campaign_rooms",
                table: "ProofPlans");

            migrationBuilder.DropColumn(
                name: "ChangeReason",
                schema: "campaign_rooms",
                table: "ProofPlans");

            migrationBuilder.DropColumn(
                name: "ChangedBy",
                schema: "campaign_rooms",
                table: "ProofPlans");
        }
    }
}
