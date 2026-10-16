using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.CampaignRooms.Data.Migrations
{
    internal partial class AssetProvenance : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                schema: "campaign_rooms",
                table: "AssetVersions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                schema: "campaign_rooms",
                table: "AssetVersions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provenance",
                schema: "campaign_rooms",
                table: "AssetVersions",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                schema: "campaign_rooms",
                table: "AssetVersions");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                schema: "campaign_rooms",
                table: "AssetVersions");

            migrationBuilder.DropColumn(
                name: "Provenance",
                schema: "campaign_rooms",
                table: "AssetVersions");
        }
    }
}
