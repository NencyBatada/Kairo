using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Notifications.Data.Migrations
{
    internal partial class NotificationDelivery : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                schema: "notifications",
                table: "OutboundEmails",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<long>(
                name: "NotificationId",
                schema: "notifications",
                table: "OutboundEmails",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                schema: "notifications",
                table: "OutboundEmails",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Payload",
                schema: "notifications",
                table: "OutboundEmails",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                schema: "notifications",
                table: "OutboundEmails",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Notification");

            migrationBuilder.AddColumn<bool>(
                name: "AwaitsDigest",
                schema: "notifications",
                table: "Notifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "DigestEmailId",
                schema: "notifications",
                table: "Notifications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemindAt",
                schema: "notifications",
                table: "Notifications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResolvedAt",
                schema: "notifications",
                table: "Notifications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                schema: "notifications",
                table: "Notifications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HeldNotifications",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Link = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeldNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundEmails_BrandId_Type_UserId_CreatedAt",
                schema: "notifications",
                table: "OutboundEmails",
                columns: new[] { "BrandId", "Type", "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_AwaitingDigest",
                schema: "notifications",
                table: "Notifications",
                columns: new[] { "AwaitsDigest", "BrandId" },
                filter: "[AwaitsDigest] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_DigestEmailId",
                schema: "notifications",
                table: "Notifications",
                column: "DigestEmailId",
                filter: "[DigestEmailId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Reminders",
                schema: "notifications",
                table: "Notifications",
                column: "RemindAt",
                filter: "[RemindAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Unresolved",
                schema: "notifications",
                table: "Notifications",
                columns: new[] { "BrandId", "Subject" },
                filter: "[Subject] IS NOT NULL AND [ResolvedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HeldNotifications_BrandId_CreatedAt",
                schema: "notifications",
                table: "HeldNotifications",
                columns: new[] { "BrandId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HeldNotifications_CreatedAt",
                schema: "notifications",
                table: "HeldNotifications",
                column: "CreatedAt");

            migrationBuilder.AddBrandSeparation("notifications", "HeldNotifications");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoveBrandSeparation("notifications", "HeldNotifications");

            migrationBuilder.DropTable(
                name: "HeldNotifications",
                schema: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_OutboundEmails_BrandId_Type_UserId_CreatedAt",
                schema: "notifications",
                table: "OutboundEmails");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_AwaitingDigest",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_DigestEmailId",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_Reminders",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_Unresolved",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Address",
                schema: "notifications",
                table: "OutboundEmails");

            migrationBuilder.DropColumn(
                name: "Payload",
                schema: "notifications",
                table: "OutboundEmails");

            migrationBuilder.DropColumn(
                name: "Type",
                schema: "notifications",
                table: "OutboundEmails");

            migrationBuilder.DropColumn(
                name: "AwaitsDigest",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "DigestEmailId",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "RemindAt",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Subject",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                schema: "notifications",
                table: "OutboundEmails",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "NotificationId",
                schema: "notifications",
                table: "OutboundEmails",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
