using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Live;

public sealed class LiveBacklogTests(SqlServer sqlServer) : IAsyncLifetime
{
    private string _connectionString = "";
    private KairoApiFactory _api = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_connectionString);
    }

    [Fact]
    public async Task A_new_event_is_not_held_behind_a_large_gap_still_being_read()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var now = _api.Clock.GetUtcNow();
        await InsertAsync(1, now.AddDays(1));
        await InsertAsync(5000, now.AddHours(-1));
        await InsertAsync(1, now);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await listener.OpeningHeartbeatAsync();

        await LivePublishing.PublishAsync(_api, brandId, "test.after_backlog");

        var message = await listener.NextOrEndAsync(TimeSpan.FromSeconds(1), maxSteps: 2);
        Assert.Equal("test.after_backlog", message?.Type);
    }

    [Fact]
    public async Task An_event_that_commits_late_arrives_even_with_old_events_below_it()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var now = _api.Clock.GetUtcNow();
        await InsertAsync(1, now.AddDays(1));
        await InsertAsync(5000, now.AddHours(-1));
        await using var slow = _api.Services.CreateAsyncScope();
        var database = slow.ServiceProvider.GetRequiredService<KairoDatabase>();
        await database.UseBrandAsync(brandId, Token);
        await database.BeginTransactionAsync(Token);
        await slow.ServiceProvider.GetRequiredService<ILiveUpdates>().PublishAsync(new LiveEvent("test.slow", new { }, LiveAudience.Everyone), Token);
        await LivePublishing.PublishAsync(_api, brandId, "test.quick");
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await listener.OpeningHeartbeatAsync();

        await database.CommitAsync(Token);

        var message = await listener.NextOrEndAsync(TimeSpan.FromSeconds(1), maxSteps: 2);
        Assert.Equal("test.slow", message?.Type);
    }

    [Fact]
    public async Task An_event_that_commits_late_arrives_even_with_a_lost_event_far_below_it()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await InsertAsync(1, _api.Clock.GetUtcNow());
        await using (var lost = _api.Services.CreateAsyncScope())
        {
            var lostDatabase = lost.ServiceProvider.GetRequiredService<KairoDatabase>();
            await lostDatabase.UseBrandAsync(brandId, Token);
            await lostDatabase.BeginTransactionAsync(Token);
            await lost.ServiceProvider.GetRequiredService<ILiveUpdates>().PublishAsync(new LiveEvent("test.lost", new { }, LiveAudience.Everyone), Token);
            await lostDatabase.RollbackAsync(Token);
        }

        await InsertAsync(5000, _api.Clock.GetUtcNow());
        await using var slow = _api.Services.CreateAsyncScope();
        var database = slow.ServiceProvider.GetRequiredService<KairoDatabase>();
        await database.UseBrandAsync(brandId, Token);
        await database.BeginTransactionAsync(Token);
        await slow.ServiceProvider.GetRequiredService<ILiveUpdates>().PublishAsync(new LiveEvent("test.slow", new { }, LiveAudience.Everyone), Token);
        await LivePublishing.PublishAsync(_api, brandId, "test.quick");
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await listener.OpeningHeartbeatAsync();

        await database.CommitAsync(Token);

        var message = await listener.NextOrEndAsync(TimeSpan.FromSeconds(1), maxSteps: 2);
        Assert.Equal("test.slow", message?.Type);
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private async Task InsertAsync(int count, DateTimeOffset createdAt)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            WITH numbers AS (SELECT TOP (@count) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO live.Outbox (BrandId, Type, Payload, AudienceKind, AudienceRoles, AudienceUsers, CreatedAt)
            SELECT @brand, N'test.backlog', N'{}', N'Everyone', 0, NULL, @createdAt FROM numbers ORDER BY n;
            """;
        command.Parameters.AddWithValue("@count", count);
        command.Parameters.AddWithValue("@brand", Guid.NewGuid());
        command.Parameters.AddWithValue("@createdAt", createdAt);
        await command.ExecuteNonQueryAsync(Token);
    }
}
