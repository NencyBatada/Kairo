using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Measurement.Data.Migrations
{
    internal partial class SalesDateColumnType : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DateColumnType",
                schema: "measurement",
                table: "SalesSources",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "DATE");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateColumnType",
                schema: "measurement",
                table: "SalesSources");
        }
    }
}
