using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Identity.Data.Migrations
{
    internal partial class RecommendationApprovers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @allBrands sql_variant = SESSION_CONTEXT(N'kairo.all_brands');
                EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;

                INSERT INTO [identity].[ApprovalDuties] ([BrandId], [Duty], [UserId])
                SELECT [owner].[BrandId], [duty].[Name], [owner].[UserId]
                FROM [identity].[ApprovalDuties] AS [owner]
                CROSS JOIN (VALUES (N'BudgetMoves'), (N'TargetChanges'), (N'GoogleSuggestions')) AS [duty]([Name])
                WHERE [owner].[Duty] = N'BudgetOwner'
                  AND NOT EXISTS (
                      SELECT 1 FROM [identity].[ApprovalDuties] AS [existing]
                      WHERE [existing].[BrandId] = [owner].[BrandId]
                        AND [existing].[Duty] = [duty].[Name]
                        AND [existing].[UserId] = [owner].[UserId]);

                EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = @allBrands;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @allBrands sql_variant = SESSION_CONTEXT(N'kairo.all_brands');
                EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;

                DELETE FROM [identity].[ApprovalDuties]
                WHERE [Duty] IN (N'BudgetMoves', N'TargetChanges', N'GoogleSuggestions');

                EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = @allBrands;
                """);
        }
    }
}
