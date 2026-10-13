using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Audit.Data.Migrations
{
    internal partial class Entries : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "Entries",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Before = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    After = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TraceId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Entries_BrandId_Id",
                schema: "audit",
                table: "Entries",
                columns: new[] { "BrandId", "Id" });

            migrationBuilder.Sql("""
                CREATE TRIGGER [audit].[Entries_PreventChanges] ON [audit].[Entries]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'Audit entries cannot be changed or deleted.', 1;
                END
                """);

            migrationBuilder.CreateBrandAccessPredicate("audit");
            migrationBuilder.AddBrandSeparation("audit", "Entries");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("audit");

            migrationBuilder.DropTable(
                name: "Entries",
                schema: "audit");
        }
    }
}
