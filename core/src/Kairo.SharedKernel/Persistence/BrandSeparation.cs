using Microsoft.EntityFrameworkCore.Migrations;

namespace Kairo.SharedKernel.Persistence;

public static class BrandSeparation
{
    public static string CreatePredicateSql(string schema) => $"""
        CREATE FUNCTION [{schema}].[BrandAccess](@BrandId uniqueidentifier)
        RETURNS TABLE
        WITH SCHEMABINDING
        AS RETURN
            SELECT 1 AS Allowed
            WHERE @BrandId = CAST(SESSION_CONTEXT(N'kairo.brand_id') AS uniqueidentifier)
               OR CAST(SESSION_CONTEXT(N'kairo.all_brands') AS bit) = 1
        """;

    public static string AddTableSql(string schema, string table, string column = "BrandId")
    {
        var predicates = $"ADD FILTER PREDICATE [{schema}].[BrandAccess]([{column}]) ON [{schema}].[{table}], "
            + $"ADD BLOCK PREDICATE [{schema}].[BrandAccess]([{column}]) ON [{schema}].[{table}] AFTER INSERT, "
            + $"ADD BLOCK PREDICATE [{schema}].[BrandAccess]([{column}]) ON [{schema}].[{table}] AFTER UPDATE";

        return $"""
            IF OBJECT_ID(N'[{schema}].[BrandSeparation]', N'SP') IS NULL
                EXEC(N'CREATE SECURITY POLICY [{schema}].[BrandSeparation] {predicates} WITH (STATE = ON)');
            ELSE
                EXEC(N'ALTER SECURITY POLICY [{schema}].[BrandSeparation] {predicates}');
            """;
    }

    public static string RemoveTableSql(string schema, string table) =>
        $"ALTER SECURITY POLICY [{schema}].[BrandSeparation] "
        + $"DROP FILTER PREDICATE ON [{schema}].[{table}], "
        + $"DROP BLOCK PREDICATE ON [{schema}].[{table}] AFTER INSERT, "
        + $"DROP BLOCK PREDICATE ON [{schema}].[{table}] AFTER UPDATE;";

    public static string DropSql(string schema) => $"""
        DROP SECURITY POLICY IF EXISTS [{schema}].[BrandSeparation];
        DROP FUNCTION IF EXISTS [{schema}].[BrandAccess];
        """;

    public static void CreateBrandAccessPredicate(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(CreatePredicateSql(schema));

    public static void AddBrandSeparation(this MigrationBuilder migrationBuilder, string schema, string table, string column = "BrandId") =>
        migrationBuilder.Sql(AddTableSql(schema, table, column));

    public static void RemoveBrandSeparation(this MigrationBuilder migrationBuilder, string schema, string table) =>
        migrationBuilder.Sql(RemoveTableSql(schema, table));

    public static void DropBrandSeparation(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(DropSql(schema));
}
