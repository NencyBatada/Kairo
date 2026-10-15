using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Opportunities.Data.Migrations
{
    internal partial class OpportunityBids : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bids",
                schema: "opportunities",
                table: "Opportunities",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bids",
                schema: "opportunities",
                table: "Opportunities");
        }
    }
}
