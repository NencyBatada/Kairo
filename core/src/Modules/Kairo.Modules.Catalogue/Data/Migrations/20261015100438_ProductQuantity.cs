using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Catalogue.Data.Migrations
{
    internal partial class ProductQuantity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                schema: "catalogue",
                table: "Products",
                type: "int",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Quantity",
                schema: "catalogue",
                table: "Products");
        }
    }
}
