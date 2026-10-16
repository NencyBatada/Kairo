using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Approvals.Data.Migrations
{
    internal partial class ApprovalEvidence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "approvals");

            migrationBuilder.CreateTable(
                name: "Entries",
                schema: "approvals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Gate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Snapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Invalidated = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Entries_BrandId_RoomId_Gate_Id",
                schema: "approvals",
                table: "Entries",
                columns: new[] { "BrandId", "RoomId", "Gate", "Id" });
            migrationBuilder.CreateBrandAccessPredicate("approvals");
            migrationBuilder.AddBrandSeparation("approvals", "Entries");
            migrationBuilder.Sql("""
                CREATE TRIGGER [approvals].[Entries_PreventChanges] ON [approvals].[Entries]
                INSTEAD OF UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'Approval entries cannot be changed or deleted.', 1;
                END
                """);

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("approvals");
            migrationBuilder.DropTable(
                name: "Entries",
                schema: "approvals");
        }
    }
}
