using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Ask.Data.Migrations
{
    internal partial class AskConversations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ask");

            migrationBuilder.CreateTable(
                name: "Conversations",
                schema: "ask",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                schema: "ask",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Context = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Segments = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TokenExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ToolCalls = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PromptVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Proposals",
                schema: "ask",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Target = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Revision = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Changes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Preview = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Lease = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Proposals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_BrandId_UserId_UpdatedAt",
                schema: "ask",
                table: "Conversations",
                columns: new[] { "BrandId", "UserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_BrandId_ConversationId_CreatedAt",
                schema: "ask",
                table: "Messages",
                columns: new[] { "BrandId", "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_BrandId_ConversationId_RequestId",
                schema: "ask",
                table: "Messages",
                columns: new[] { "BrandId", "ConversationId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_BrandId_UserId_ConversationId",
                schema: "ask",
                table: "Proposals",
                columns: new[] { "BrandId", "UserId", "ConversationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_Status_DueAt",
                schema: "ask",
                table: "Proposals",
                columns: new[] { "Status", "DueAt" });
            migrationBuilder.CreateBrandAccessPredicate("ask");
            migrationBuilder.AddBrandSeparation("ask", "Conversations");
            migrationBuilder.AddBrandSeparation("ask", "Messages");
            migrationBuilder.AddBrandSeparation("ask", "Proposals");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("ask");
            migrationBuilder.DropTable(
                name: "Conversations",
                schema: "ask");

            migrationBuilder.DropTable(
                name: "Messages",
                schema: "ask");

            migrationBuilder.DropTable(
                name: "Proposals",
                schema: "ask");
        }
    }
}
