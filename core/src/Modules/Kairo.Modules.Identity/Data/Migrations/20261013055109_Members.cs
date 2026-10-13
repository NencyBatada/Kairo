using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Identity.Data.Migrations
{
    internal partial class Members : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BudgetLimits",
                schema: "identity",
                columns: table => new
                {
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetLimits", x => x.BrandId);
                });

            migrationBuilder.CreateTable(
                name: "Memberships",
                schema: "identity",
                columns: table => new
                {
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Roles = table.Column<int>(type: "int", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memberships", x => new { x.BrandId, x.UserId });
                    table.ForeignKey(
                        name: "FK_Memberships_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalDuties",
                schema: "identity",
                columns: table => new
                {
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Duty = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDuties", x => new { x.BrandId, x.Duty, x.UserId });
                    table.ForeignKey(
                        name: "FK_ApprovalDuties_Memberships_BrandId_UserId",
                        columns: x => new { x.BrandId, x.UserId },
                        principalSchema: "identity",
                        principalTable: "Memberships",
                        principalColumns: new[] { "BrandId", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDuties_BrandId_UserId",
                schema: "identity",
                table: "ApprovalDuties",
                columns: new[] { "BrandId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_UserId",
                schema: "identity",
                table: "Memberships",
                column: "UserId");

            migrationBuilder.CreateBrandAccessPredicate("identity");
            migrationBuilder.AddBrandSeparation("identity", "Memberships");
            migrationBuilder.AddBrandSeparation("identity", "ApprovalDuties");
            migrationBuilder.AddBrandSeparation("identity", "BudgetLimits");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("identity");

            migrationBuilder.DropTable(
                name: "ApprovalDuties",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "BudgetLimits",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Memberships",
                schema: "identity");
        }
    }
}
