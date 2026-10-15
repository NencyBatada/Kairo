using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Measurement.Data.Migrations
{
    internal partial class FeedRunTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tables",
                schema: "measurement",
                table: "FeedRuns",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tables",
                schema: "measurement",
                table: "FeedRuns");
        }
    }
}
