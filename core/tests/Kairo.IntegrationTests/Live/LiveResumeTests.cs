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

    [Fact]
    public async Task A_stream_that_saw_no_events_resumes_from_its_opening_heartbeat()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        string resumeFrom;
        await using (var quiet = await LiveListener.OpenAsync(_api, owner.Browser, brandId))
        {
            resumeFrom = (await quiet.OpeningHeartbeatAsync()).Id!;
        }

        Assert.StartsWith("t", resumeFrom);
        await SeeOneEventAsync(owner.Browser, brandId, "test.while_away");
        await using var fresh = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId, resumeFrom);

        Assert.Equal("test.while_away", (await listener.NextAsync()).Type);
        await LivePublishing.PublishAsync(_api, brandId, "test.later");
        Assert.Equal("test.later", (await fresh.NextAsync()).Type);
    }

    [Fact]
    public async Task A_reset_tells_the_browser_where_to_resume()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        string resumeFrom;
        await using (var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId, long.MaxValue.ToString()))
        {
            var reset = await listener.NextAsync();
            Assert.Equal("reset", reset.Type);
            resumeFrom = reset.Id!;
        }

        Assert.StartsWith("t", resumeFrom);
        await SeeOneEventAsync(owner.Browser, brandId, "test.after_reset");
        await using var resumed = await LiveListener.OpenAsync(_api, owner.Browser, brandId, resumeFrom);

        Assert.Equal("test.after_reset", (await resumed.NextAsync()).Type);
    }

    [Fact]
    public async Task A_resume_point_older_than_a_day_gets_a_reset()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        string resumeFrom;
        await using (var quiet = await LiveListener.OpenAsync(_api, owner.Browser, brandId))
        {
            resumeFrom = (await quiet.OpeningHeartbeatAsync()).Id!;
        }

        _api.Clock.Advance(TimeSpan.FromHours(25));
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId, resumeFrom);

        Assert.Equal("reset", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task A_new_stream_is_not_sent_old_events_the_feed_has_not_read_yet()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await SeeOneEventAsync(owner.Browser, brandId, "test.start");

        await OutboxRows.InsertAsync(sqlServer.KairoConnectionString, brandId, "test.stale", 3, _api.Clock.GetUtcNow() - TimeSpan.FromHours(2));
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await LivePublishing.PublishAsync(_api, brandId, "test.fresh");

        Assert.Equal("test.fresh", (await listener.NextAsync()).Type);
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
