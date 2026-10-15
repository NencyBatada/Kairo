using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.CampaignRooms.Data.Migrations
{
    internal partial class CampaignRoomLifecycle : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscardReason",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscardedFrom",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GateRequestedAt",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GateRequestedBy",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSendBack",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "PendingGate",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Products",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunStatus",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StateEnteredAt",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "campaign_rooms",
                table: "Rooms",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                DECLARE @allBrands sql_variant = SESSION_CONTEXT(N'kairo.all_brands');
                EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;

                UPDATE [campaign_rooms].[Rooms]
                SET [OwnerId] = [CreatedBy],
                    [Status] = N'in_progress',
                    [StateEnteredAt] = [CreatedAt],
                    [UpdatedAt] = [CreatedAt],
                    [Products] = CASE WHEN ISJSON([Evidence]) = 1 THEN COALESCE(
                        (SELECT JSON_VALUE([product].[value], '$.id') AS [Id], JSON_VALUE([product].[value], '$.title') AS [Title]
                         FROM OPENJSON([Evidence], '$.products') AS [product]
                         FOR JSON PATH),
                        N'[]') ELSE N'[]' END
                WHERE [Status] = N'';

                EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = @allBrands;
                """);

            migrationBuilder.CreateTable(
                name: "Briefs",
                schema: "campaign_rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StageRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PromptVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Briefs", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateTable(
                name: "StageRuns",
                schema: "campaign_rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Agent = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    ClaimedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QueuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PromptVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InputTokens = table.Column<int>(type: "int", nullable: true),
                    OutputTokens = table.Column<int>(type: "int", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Output = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageRuns", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_BrandId_State",
                schema: "campaign_rooms",
                table: "Rooms",
                columns: new[] { "BrandId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_BrandId_Status",
                schema: "campaign_rooms",
                table: "Rooms",
                columns: new[] { "BrandId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Briefs_BrandId_RoomId_Version",
                schema: "campaign_rooms",
                table: "Briefs",
                columns: new[] { "BrandId", "RoomId", "Version" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_StageRuns_BrandId_RoomId_QueuedAt",
                schema: "campaign_rooms",
                table: "StageRuns",
                columns: new[] { "BrandId", "RoomId", "QueuedAt" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_StageRuns_Status_NextAttemptAt",
                schema: "campaign_rooms",
                table: "StageRuns",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.AddBrandSeparation("campaign_rooms", "StageRuns");
            migrationBuilder.AddBrandSeparation("campaign_rooms", "Briefs");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoveBrandSeparation("campaign_rooms", "Briefs");
            migrationBuilder.RemoveBrandSeparation("campaign_rooms", "StageRuns");

            migrationBuilder.DropTable(
                name: "Briefs",
                schema: "campaign_rooms");

            migrationBuilder.DropTable(
                name: "StageRuns",
                schema: "campaign_rooms");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_BrandId_State",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_BrandId_Status",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "DiscardReason",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "DiscardedFrom",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "GateRequestedAt",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "GateRequestedBy",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "LastSendBack",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "PendingGate",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "Products",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "RunStatus",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "StateEnteredAt",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "campaign_rooms",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "campaign_rooms",
                table: "Rooms");
        }
    }
}
