using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Live;

public sealed class LiveResumeTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reconnecting_replays_what_was_missed_in_order()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var lastSeen = await SeeOneEventAsync(owner.Browser, brandId, "test.first");
        await using var witness = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        await LivePublishing.PublishAsync(_api, brandId, "test.second");
        await LivePublishing.PublishAsync(_api, brandId, "test.third");
        Assert.Equal("test.second", (await witness.NextAsync()).Type);
        Assert.Equal("test.third", (await witness.NextAsync()).Type);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId, lastSeen);

        Assert.Equal("test.second", (await listener.NextAsync()).Type);
        Assert.Equal("test.third", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Reconnecting_also_replays_an_event_that_committed_after_the_last_one_seen()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var slow = _api.Services.CreateAsyncScope();
        var database = slow.ServiceProvider.GetRequiredService<KairoDatabase>();
        var live = slow.ServiceProvider.GetRequiredService<ILiveUpdates>();
        await database.UseBrandAsync(brandId, Token);
        await database.BeginTransactionAsync(Token);
        await live.PublishAsync(new LiveEvent("test.slow", new { }, LiveAudience.Everyone), Token);

        var lastSeen = await SeeOneEventAsync(owner.Browser, brandId, "test.quick");
        await using var witness = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await database.CommitAsync(Token);
        Assert.Equal("test.slow", (await witness.NextAsync()).Type);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId, lastSeen);

        Assert.Equal("test.slow", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Another_workspace_s_event_id_gets_a_reset_and_nothing_from_that_workspace()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var first = await Workspaces.CreateAsync(owner.Browser);
        var second = await Workspaces.CreateAsync(owner.Browser);
        var foreignId = await SeeOneEventAsync(owner.Browser, second, "test.elsewhere");

        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, first, foreignId);
        Assert.Equal("reset", (await listener.NextAsync()).Type);

        await LivePublishing.PublishAsync(_api, first, "test.here");
        Assert.Equal("test.here", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task An_unknown_event_id_gets_a_reset()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId, long.MaxValue.ToString());

        Assert.Equal("reset", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Missing_more_than_a_thousand_events_gets_a_reset()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var lastSeen = await SeeOneEventAsync(owner.Browser, brandId, "test.first");

        await OutboxRows.InsertAsync(sqlServer.KairoConnectionString, brandId, "test.bulk", 1001, _api.Clock.GetUtcNow());
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId, lastSeen);

        Assert.Equal("reset", (await listener.NextAsync()).Type);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<string> SeeOneEventAsync(HttpClient browser, Guid brandId, string type)
    {
        await using var listener = await LiveListener.OpenAsync(_api, browser, brandId);
        await LivePublishing.PublishAsync(_api, brandId, type);
        var message = await listener.NextAsync();
        Assert.Equal(type, message.Type);
        return message.Id!;
    }
}
