using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Launch.Data.Migrations
{
    internal partial class LaunchRetryHealth : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Launches_BrandId_RoomId",
                schema: "launch",
                table: "Launches");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                schema: "launch",
                table: "Launches",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingBuild",
                schema: "launch",
                table: "Launches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                schema: "launch",
                table: "Launches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "launch",
                table: "Launches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Health",
                schema: "launch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LaunchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Alerts = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Cause = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Health", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Steps",
                schema: "launch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LaunchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Steps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Launches_BrandId_RoomId",
                schema: "launch",
                table: "Launches",
                columns: new[] { "BrandId", "RoomId" },
                unique: true,
                filter: "[Status] <> N'superseded'");

            migrationBuilder.CreateIndex(
                name: "IX_Health_LaunchId",
                schema: "launch",
                table: "Health",
                column: "LaunchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Health_Status_DueAt",
                schema: "launch",
                table: "Health",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Steps_BrandId_LaunchId_Name_Attempt",
                schema: "launch",
                table: "Steps",
                columns: new[] { "BrandId", "LaunchId", "Name", "Attempt" },
                unique: true);
            migrationBuilder.AddBrandSeparation("launch", "Steps");
            migrationBuilder.AddBrandSeparation("launch", "Health");
            migrationBuilder.Sql("""
                DECLARE @prior sql_variant = SESSION_CONTEXT(N'kairo.all_brands');
                EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
                INSERT INTO launch.Health (Id, BrandId, LaunchId, DueAt, Attempts, Status, Alerts)
                SELECT NEWID(), BrandId, Id, DATEADD(hour, 24, ActivatedAt), 0, N'pending', N'[]'
                FROM launch.Launches WHERE Status = N'live' AND ActivatedAt IS NOT NULL;
                EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = @prior;
                """);
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoveBrandSeparation("launch", "Health");
            migrationBuilder.RemoveBrandSeparation("launch", "Steps");
            migrationBuilder.DropTable(
                name: "Health",
                schema: "launch");

            migrationBuilder.DropTable(
                name: "Steps",
                schema: "launch");

            migrationBuilder.DropIndex(
                name: "IX_Launches_BrandId_RoomId",
                schema: "launch",
                table: "Launches");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                schema: "launch",
                table: "Launches");

            migrationBuilder.DropColumn(
                name: "PendingBuild",
                schema: "launch",
                table: "Launches");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                schema: "launch",
                table: "Launches");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "launch",
                table: "Launches");

            migrationBuilder.CreateIndex(
                name: "IX_Launches_BrandId_RoomId",
                schema: "launch",
                table: "Launches",
                columns: new[] { "BrandId", "RoomId" },
                unique: true);
        }
    }
}
