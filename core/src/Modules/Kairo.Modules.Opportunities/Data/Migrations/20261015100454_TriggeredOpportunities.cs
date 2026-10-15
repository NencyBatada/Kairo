using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Opportunities.Data.Migrations
{
    internal partial class TriggeredOpportunities : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Brief",
                schema: "opportunities",
                table: "Opportunities",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BriefStatus",
                schema: "opportunities",
                table: "Opportunities",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Triggers",
                schema: "opportunities",
                table: "Opportunities",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.Sql("""
                DECLARE @allBrands sql_variant = SESSION_CONTEXT(N'kairo.all_brands');
                EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;

                UPDATE [opportunities].[Opportunities] SET [Triggers] = N'[]' WHERE [Triggers] IS NULL;

                EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = @allBrands;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Brief",
                schema: "opportunities",
                table: "Opportunities");

            migrationBuilder.DropColumn(
                name: "BriefStatus",
                schema: "opportunities",
                table: "Opportunities");

            migrationBuilder.DropColumn(
                name: "Triggers",
                schema: "opportunities",
                table: "Opportunities");
        }
    }
}
