using System.Net;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Live;

public sealed class LiveAccessTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_stream_needs_a_session()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        await using var listener = await LiveListener.OpenAsync(_api, _api.CreateBrowser(), brandId);

        Assert.Equal(HttpStatusCode.Unauthorized, listener.Status);
    }

    [Fact]
    public async Task Someone_outside_the_workspace_gets_not_found()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var outsider = await Workspaces.SignInAsync(_api);

        await using var listener = await LiveListener.OpenAsync(_api, outsider.Browser, brandId);

        Assert.Equal(HttpStatusCode.NotFound, listener.Status);
    }

    [Fact]
    public async Task A_removed_member_s_stream_ends()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await using var listener = await LiveListener.OpenAsync(_api, marketer.Browser, brandId);

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{marketer.UserId}", Token)).EnsureSuccessStatusCode();

        Assert.Null(await listener.NextOrEndAsync(TimeSpan.FromSeconds(5), maxSteps: 20));
    }

    [Fact]
    public async Task Signing_out_ends_the_stream()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        (await owner.Browser.PostAsync("/api/auth/sign-out", null, Token)).EnsureSuccessStatusCode();

        Assert.Null(await listener.NextOrEndAsync(TimeSpan.FromSeconds(5), maxSteps: 20));
    }

    [Fact]
    public async Task A_stream_closes_after_fifteen_minutes()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        await Assert.ThrowsAsync<TimeoutException>(() => listener.NextOrEndAsync(TimeSpan.FromSeconds(30), maxSteps: 28));
        Assert.Null(await listener.NextOrEndAsync(TimeSpan.FromSeconds(30), maxSteps: 4));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
