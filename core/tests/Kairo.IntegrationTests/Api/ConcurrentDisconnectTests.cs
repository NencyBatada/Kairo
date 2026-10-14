using Kairo.Modules.Brands.Connections;
using Kairo.SharedKernel.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Api;

public sealed class ConcurrentDisconnectTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Checking_whether_a_token_is_still_used_does_not_wait_on_other_accounts_connections()
    {
        var first = await Workspaces.SignInAsync(_api, "First Admin");
        var firstBrandId = await Workspaces.CreateAsync(first.Browser);
        await Connections.ConnectAsync(_api, first.Browser, firstBrandId, "google-ads", subject: $"first-{Guid.NewGuid():N}");
        var second = await Workspaces.SignInAsync(_api, "Second Admin");
        var secondBrandId = await Workspaces.CreateAsync(second.Browser);
        var secondSubject = $"second-{Guid.NewGuid():N}";
        await Connections.ConnectAsync(_api, second.Browser, secondBrandId, "google-ads", subject: secondSubject);

        await using var holder = new SqlConnection(sqlServer.KairoConnectionString);
        await holder.OpenAsync(Token);
        await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync(Token);
        await using (var command = holder.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
                DELETE FROM brands.Connections WHERE BrandId = @brand;
                """;
            command.Parameters.AddWithValue("@brand", firstBrandId);
            await command.ExecuteNonQueryAsync(Token);
        }

        using var waitLimit = CancellationTokenSource.CreateLinkedTokenSource(Token);
        waitLimit.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await using var scope = _api.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(secondBrandId, Token);

            await scope.ServiceProvider.GetRequiredService<GoogleRevocations>()
                .RevokeIfUnusedAsync(secondSubject, "unused-token", waitLimit.Token);

            Assert.DoesNotContain("unused-token", _api.GoogleApis.Revoked);
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
