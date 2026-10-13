using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class TeamTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_member_can_see_the_team()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var members = await marketer.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/members", Token);

        Assert.Equal(
            new[] { owner.UserId, marketer.UserId }.Order(),
            members.EnumerateArray().Select(member => member.GetProperty("userId").GetGuid()).Order());
    }

    [Fact]
    public async Task An_admin_can_change_someones_roles()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var response = await ChangeRolesAsync(owner, brandId, marketer.UserId, "marketer", "approver");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId);
        Assert.Equal("member.roles_changed", entry.GetProperty("action").GetString());
        Assert.Equal(2, entry.GetProperty("after").GetProperty("roles").GetArrayLength());
    }

    [Fact]
    public async Task Every_member_keeps_at_least_one_role()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var response = await ChangeRolesAsync(owner, brandId, marketer.UserId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_last_admin_cannot_be_demoted_removed_or_leave()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var demote = await ChangeRolesAsync(owner, brandId, owner.UserId, "marketer", "approver");
        var leave = await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{owner.UserId}", Token);

        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);
        Assert.Contains("at least one admin", (await demote.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Two_admins_demoting_each_other_at_the_same_time_leave_one_admin()
    {
        var workspaces = new List<(Guid BrandId, Member Owner, Member Other)>();
        for (var i = 0; i < 10; i++)
        {
            var owner = await Workspaces.SignInAsync(_api);
            var brandId = await Workspaces.CreateAsync(owner.Browser);
            var other = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
            workspaces.Add((brandId, owner, other));
        }

        await Task.WhenAll(workspaces.SelectMany(w => new[]
        {
            ChangeRolesAsync(w.Owner, w.BrandId, w.Other.UserId, "marketer"),
            ChangeRolesAsync(w.Other, w.BrandId, w.Owner.UserId, "approver"),
        }));

        foreach (var (brandId, owner, _) in workspaces)
        {
            var members = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/members", Token);
            var admins = members.EnumerateArray()
                .Count(member => member.GetProperty("roles").EnumerateArray().Any(role => role.GetString() == "admin"));
            Assert.Equal(1, admins);
        }
    }

    [Fact]
    public async Task The_only_person_on_a_duty_keeps_the_approver_role()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var secondAdmin = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");

        var response = await ChangeRolesAsync(secondAdmin, brandId, owner.UserId, "admin", "marketer");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("only brief approver", (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Taking_away_the_approver_role_takes_them_off_their_duties()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var approver = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "approver", "marketer");
        await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/approval-settings", new
        {
            briefApprovers = new[] { owner.UserId, approver.UserId },
            assetApprovers = new[] { owner.UserId },
            launchApprovers = new[] { owner.UserId },
            budgetOwners = new[] { owner.UserId },
            budgetLimit = (decimal?)null,
        }, Token);

        var response = await ChangeRolesAsync(owner, brandId, approver.UserId, "marketer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/approval-settings", Token);
        Assert.Equal(owner.UserId, Assert.Single(settings.GetProperty("briefApprovers").EnumerateArray()).GetGuid());
    }

    [Fact]
    public async Task An_admin_can_leave_once_someone_else_can_take_over()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var successor = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin", "approver");
        await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/approval-settings", new
        {
            briefApprovers = new[] { successor.UserId },
            assetApprovers = new[] { successor.UserId },
            launchApprovers = new[] { successor.UserId },
            budgetOwners = new[] { successor.UserId },
            budgetLimit = (decimal?)null,
        }, Token);

        var leave = await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{owner.UserId}", Token);

        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Browser.GetAsync($"/api/brands/{brandId}", Token)).StatusCode);
        Assert.Equal("member.left", (await Workspaces.LatestAuditEntryAsync(successor.Browser, brandId)).GetProperty("action").GetString());
    }

    [Fact]
    public async Task Members_who_are_not_admins_can_leave_but_cannot_remove_others()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var removeOwner = await marketer.Browser.DeleteAsync($"/api/brands/{brandId}/members/{owner.UserId}", Token);
        var leave = await marketer.Browser.DeleteAsync($"/api/brands/{brandId}/members/{marketer.UserId}", Token);

        Assert.Equal(HttpStatusCode.Forbidden, removeOwner.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
    }

    [Fact]
    public async Task A_removed_member_loses_access_straight_away()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var removed = await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{marketer.UserId}", Token);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await marketer.Browser.GetAsync($"/api/brands/{brandId}", Token)).StatusCode);
        Assert.Equal("member.removed", (await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId)).GetProperty("action").GetString());
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static Task<HttpResponseMessage> ChangeRolesAsync(Member admin, Guid brandId, Guid userId, params string[] roles) =>
        admin.Browser.PutAsJsonAsync($"/api/brands/{brandId}/members/{userId}/roles", new { roles }, Token);
}
