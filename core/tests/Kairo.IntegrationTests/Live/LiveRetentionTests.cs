using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Live;

public sealed class LiveRetentionTests(SqlServer sqlServer) : IAsyncLifetime
{
    private string _connectionString = "";
    private KairoApiFactory _api = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_connectionString);
    }

    [Fact]
    public async Task An_event_id_older_than_a_day_gets_a_reset()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        string lastSeen;
        await using (var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId))
        {
            await LivePublishing.PublishAsync(_api, brandId, "test.old");
            lastSeen = (await listener.NextAsync()).Id!;
        }

        _api.Clock.Advance(TimeSpan.FromHours(25));
        await using var reconnected = await LiveListener.OpenAsync(_api, owner.Browser, brandId, lastSeen);

        Assert.Equal("reset", (await reconnected.NextAsync()).Type);
    }

    [Fact]
    public async Task Events_older_than_a_day_are_deleted_and_newer_ones_kept()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await LivePublishing.PublishAsync(_api, brandId, "test.old");
        var old = long.Parse((await listener.NextAsync()).Id!);

        _api.Clock.Advance(TimeSpan.FromHours(25));
        await LivePublishing.PublishAsync(_api, brandId, "test.new");

        var ids = Array.Empty<long>();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            ids = (await OutboxRows.ReadAsync(_connectionString, brandId)).Select(row => row.Id).ToArray();
            if (!ids.Contains(old))
            {
                break;
            }

            _api.Clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.DoesNotContain(old, ids);
        Assert.Single(ids);
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();
}
