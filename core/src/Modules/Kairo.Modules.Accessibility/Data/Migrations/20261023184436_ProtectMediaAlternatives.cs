using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Accessibility.Data.Migrations
{
    /// <inheritdoc />
    internal partial class ProtectMediaAlternatives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateBrandAccessPredicate("accessibility");
            migrationBuilder.AddBrandSeparation("accessibility", "MediaAlternatives");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("accessibility");

        }
    }
}
