using System.Net.Http.Json;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Live;

public sealed class LiveEventsTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Renaming_the_workspace_publishes_workspace_changed()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        (await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name = "Acme Footwear" }, Token)).EnsureSuccessStatusCode();

        Assert.Equal("workspace.changed", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Saving_approval_settings_publishes_approval_settings_changed()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        var duty = new[] { owner.UserId };

        (await owner.Browser.PutAsJsonAsync(
            $"/api/brands/{brandId}/approval-settings",
            new { briefApprovers = duty, assetApprovers = duty, launchApprovers = duty, budgetOwners = duty, budgetMoveApprovers = duty, targetChangeApprovers = duty, googleSuggestionApprovers = duty, budgetLimit = 50000m },
            Token)).EnsureSuccessStatusCode();

        Assert.Equal("approval_settings.changed", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Saving_check_settings_publishes_check_settings_changed()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        (await owner.Browser.PutAsJsonAsync(
            $"/api/brands/{brandId}/check-settings",
            new { version = 0, passScore = 75, fixAttempts = 2 },
            Token)).EnsureSuccessStatusCode();

        Assert.Equal("check_settings.changed", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Invitation_changes_reach_admins_only()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await using var ownerStream = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await using var marketerStream = await LiveListener.OpenAsync(_api, marketer.Browser, brandId);

        var invitationId = await Workspaces.InviteAsync(owner.Browser, brandId, $"new-{Guid.NewGuid():N}@example.com", "marketer");
        (await owner.Browser.PostAsync($"/api/brands/{brandId}/invitations/{invitationId}/revoke", null, Token)).EnsureSuccessStatusCode();
        (await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name = "Acme Footwear" }, Token)).EnsureSuccessStatusCode();

        Assert.Equal("invitations.changed", (await ownerStream.NextAsync()).Type);
        Assert.Equal("invitations.changed", (await ownerStream.NextAsync()).Type);
        Assert.Equal("workspace.changed", (await marketerStream.NextAsync()).Type);
    }

    [Fact]
    public async Task Accepting_an_invitation_publishes_the_new_member()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        var member = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        Assert.Equal("invitations.changed", (await listener.NextAsync()).Type);
        var joined = await listener.NextAsync();
        Assert.Equal("team.changed", joined.Type);
        Assert.Equal(member.UserId, joined.Data.GetProperty("userId").GetGuid());
        Assert.False(joined.Data.GetProperty("removed").GetBoolean());
        Assert.Equal("invitations.changed", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Changing_roles_publishes_team_changed_for_that_member()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        (await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/members/{marketer.UserId}/roles", new { roles = new[] { "marketer", "approver" } }, Token)).EnsureSuccessStatusCode();

        var message = await listener.NextAsync();
        Assert.Equal("team.changed", message.Type);
        Assert.Equal(marketer.UserId, message.Data.GetProperty("userId").GetGuid());
        Assert.False(message.Data.GetProperty("removed").GetBoolean());
    }

    [Fact]
    public async Task Removing_a_member_ends_their_stream_straight_away()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await using var ownerStream = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await using var marketerStream = await LiveListener.OpenAsync(_api, marketer.Browser, brandId);

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{marketer.UserId}", Token)).EnsureSuccessStatusCode();

        Assert.Null(await marketerStream.NextOrEndAsync(TimeSpan.FromSeconds(1), maxSteps: 10));
        var removed = await ownerStream.NextAsync();
        Assert.Equal("team.changed", removed.Type);
        Assert.True(removed.Data.GetProperty("removed").GetBoolean());
        await using var reconnect = await LiveListener.OpenAsync(_api, marketer.Browser, brandId);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, reconnect.Status);
    }

    [Fact]
    public async Task A_demoted_admin_stops_seeing_admin_events_without_reconnecting()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var deputy = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        await using var listener = await LiveListener.OpenAsync(_api, deputy.Browser, brandId);

        (await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/members/{deputy.UserId}/roles", new { roles = new[] { "marketer" } }, Token)).EnsureSuccessStatusCode();
        Assert.Equal("team.changed", (await listener.NextAsync()).Type);

        await Workspaces.InviteAsync(owner.Browser, brandId, $"new-{Guid.NewGuid():N}@example.com", "marketer");
        (await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name = "Acme Footwear" }, Token)).EnsureSuccessStatusCode();

        Assert.Equal("workspace.changed", (await listener.NextAsync(maxSteps: 10)).Type);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
