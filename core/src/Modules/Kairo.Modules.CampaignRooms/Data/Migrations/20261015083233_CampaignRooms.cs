using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.CampaignRooms.Data.Migrations
{
    internal partial class CampaignRooms : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "campaign_rooms");

            migrationBuilder.CreateTable(
                name: "Rooms",
                schema: "campaign_rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_BrandId_CreatedAt",
                schema: "campaign_rooms",
                table: "Rooms",
                columns: new[] { "BrandId", "CreatedAt" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_BrandId_OpportunityId",
                schema: "campaign_rooms",
                table: "Rooms",
                columns: new[] { "BrandId", "OpportunityId" },
                unique: true);

            migrationBuilder.CreateBrandAccessPredicate("campaign_rooms");
            migrationBuilder.AddBrandSeparation("campaign_rooms", "Rooms");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("campaign_rooms");

            migrationBuilder.DropTable(
                name: "Rooms",
                schema: "campaign_rooms");
        }
    }
}
