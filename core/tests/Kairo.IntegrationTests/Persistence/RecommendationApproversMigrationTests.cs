using Kairo.Contracts.Identity;
using Kairo.Migrator;
using Kairo.Modules.Identity.Data;
using Kairo.SharedKernel.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Persistence;

public sealed class RecommendationApproversMigrationTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Existing_workspaces_give_recommendations_to_their_budget_owners()
    {
        var connectionString = await sqlServer.CreateDatabaseAsync("approvers");
        await using var host = MigratorHost.Build([$"--ConnectionStrings:kairo={connectionString}"]);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261013055439_Invitations", Token);
        var (brandId, owner, approver) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await SeedAsync(connectionString, brandId, owner, approver);

        await migrator.MigrateAsync(cancellationToken: Token);

        await using (await scope.ServiceProvider.GetRequiredService<KairoDatabase>().EnterSystemScopeAsync(Token))
        {
            var assignments = (await db.DutyAssignments
                    .Where(assignment => assignment.BrandId == brandId)
                    .Select(assignment => new { assignment.Duty, assignment.UserId })
                    .ToListAsync(Token))
                .Select(assignment => (assignment.Duty, assignment.UserId))
                .ToHashSet();

            Assert.Equal(
                new HashSet<(ApprovalDuty, Guid)>
                {
                    (ApprovalDuty.Brief, approver),
                    (ApprovalDuty.BudgetOwner, owner),
                    (ApprovalDuty.BudgetMoves, owner),
                    (ApprovalDuty.TargetChanges, owner),
                    (ApprovalDuty.GoogleSuggestions, owner),
                },
                assignments);
        }
    }

    private static async Task SeedAsync(string connectionString, Guid brandId, Guid owner, Guid approver)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            INSERT INTO [identity].[Users] ([Id], [GoogleSubject], [Email], [Name], [CreatedAt], [LastSignedInAt])
            VALUES (@owner, N'owner', N'owner@example.com', N'Owner', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
                   (@approver, N'approver', N'approver@example.com', N'Approver', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            INSERT INTO [identity].[Memberships] ([BrandId], [UserId], [Roles], [JoinedAt])
            VALUES (@brand, @owner, 7, SYSDATETIMEOFFSET()), (@brand, @approver, 4, SYSDATETIMEOFFSET());
            INSERT INTO [identity].[ApprovalDuties] ([BrandId], [Duty], [UserId])
            VALUES (@brand, N'BudgetOwner', @owner), (@brand, N'Brief', @approver);
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@owner", owner);
        command.Parameters.AddWithValue("@approver", approver);
        await command.ExecuteNonQueryAsync(Token);
    }
}
