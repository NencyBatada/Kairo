using System.Net;
using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Access;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Live;

public sealed class LiveDeliveryTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_stream_is_an_event_stream()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        Assert.Equal(HttpStatusCode.OK, listener.Status);
        Assert.Equal("text/event-stream", listener.ContentType);
    }

    [Fact]
    public async Task An_event_published_outside_a_request_reaches_the_workspace_s_streams()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        await LivePublishing.PublishAsync(_api, brandId, "test.happened", new { Name = "Acme" });

        var message = await listener.NextAsync();
        Assert.Equal("test.happened", message.Type);
        Assert.False(string.IsNullOrEmpty(message.Id));
        Assert.Equal("Acme", message.Data.GetProperty("name").GetString());
    }

    [Fact]
    public async Task One_workspace_never_sees_another_workspace_s_events()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var first = await Workspaces.CreateAsync(owner.Browser);
        var second = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, first);

        await LivePublishing.PublishAsync(_api, second, "test.elsewhere");
        await LivePublishing.PublishAsync(_api, first, "test.here");

        Assert.Equal("test.here", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Role_and_user_audiences_reach_only_their_listeners()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await using var ownerStream = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await using var marketerStream = await LiveListener.OpenAsync(_api, marketer.Browser, brandId);

        await LivePublishing.PublishAsync(_api, brandId, "test.for_admins", audience: LiveAudience.Roles(BrandRoles.Admin));
        await LivePublishing.PublishAsync(_api, brandId, "test.for_marketer", audience: LiveAudience.Users(marketer.UserId));
        await LivePublishing.PublishAsync(_api, brandId, "test.for_everyone");

        var ownerFirst = await ownerStream.NextAsync();
        var ownerSecond = await ownerStream.NextAsync();
        var marketerFirst = await marketerStream.NextAsync();
        var marketerSecond = await marketerStream.NextAsync();
        Assert.Equal(("test.for_admins", "test.for_everyone"), (ownerFirst.Type, ownerSecond.Type));
        Assert.Equal(("test.for_marketer", "test.for_everyone"), (marketerFirst.Type, marketerSecond.Type));
    }

    [Fact]
    public async Task An_event_that_commits_after_a_later_one_still_arrives_without_holding_the_later_one_back()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        await using var slow = _api.Services.CreateAsyncScope();
        var database = slow.ServiceProvider.GetRequiredService<KairoDatabase>();
        var live = slow.ServiceProvider.GetRequiredService<ILiveUpdates>();
        await database.UseBrandAsync(brandId, Token);
        await database.BeginTransactionAsync(Token);
        await live.PublishAsync(new LiveEvent("test.slow", new { }, LiveAudience.Everyone), Token);

        await LivePublishing.PublishAsync(_api, brandId, "test.quick");
        Assert.Equal("test.quick", (await listener.NextAsync()).Type);

        await database.CommitAsync(Token);
        Assert.Equal("test.slow", (await listener.NextAsync()).Type);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
