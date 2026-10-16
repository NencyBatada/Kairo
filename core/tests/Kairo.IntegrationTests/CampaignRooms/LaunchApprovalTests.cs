using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Launch;
using Kairo.Contracts.Approvals;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class LaunchApprovalTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public async ValueTask InitializeAsync()
    {
        _world = await CampaignWorld.CreateAsync(sqlServer);
        _world.LoadCityHistory();
    }

    public ValueTask DisposeAsync() => _world.DisposeAsync();

    [Fact]
    public async Task Launch_approval_matches_the_complete_plan()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(roomId);
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
        var plans = scope.ServiceProvider.GetRequiredService<ILaunchPlans>();
        Assert.True(await plans.IsApprovedAsync(roomId, Token));
        var plan = Assert.IsType<LaunchPlan>(await plans.GetAsync(roomId, Token));
        Assert.Equal(plan.Budget, plan.Settings.Channels.Sum(channel => channel.Budget));
        Assert.NotEmpty(plan.Holdouts);
        Assert.All(plan.Assets, asset => Assert.Contains(asset.CheckState, new[] { "passed", "overridden" }));
        Assert.DoesNotContain(plan.Assets, asset => asset.FileKey?.StartsWith("drafts/", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Launch_settings_require_the_marketer_role_and_cannot_change_during_approval()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(roomId);
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        var body = new { version = Campaigns.Version(detail), settings = new { channels = Array.Empty<object>() } };
        using var denied = await _world.Approver.Browser.PutAsJsonAsync($"{Campaigns.Base(_world.BrandId)}/{roomId}/launch-plan", body, Token);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var locked = await _world.Marketer.PutAsJsonAsync($"{Campaigns.Base(_world.BrandId)}/{roomId}/launch-plan", body, Token);
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
    }

    [Fact]
    public async Task Legacy_or_invalidated_prior_approvals_cannot_authorize_a_launch()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var ready = await _world.ReadyForLaunchAsync(roomId);
        await using (var scope = _world.Api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
            await scope.ServiceProvider.GetRequiredService<IApprovalLocks>().InvalidateAsync(roomId, ["brief"], Token);
        }

        using var refused = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "launch", version = Campaigns.Version(ready) });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("no approval evidence", await Campaigns.TitleAsync(refused));
    }

    [Fact]
    public async Task Invalid_allocations_roll_back_and_stale_writes_do_not_replace_the_plan()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var ready = await _world.ReadyForLaunchAsync(roomId);
        var url = $"{Campaigns.Base(_world.BrandId)}/{roomId}/launch-plan";
        var before = await _world.Marketer.GetFromJsonAsync<JsonElement>(url, Token);
        using var invalid = await _world.Marketer.PutAsJsonAsync(url, new { version = Campaigns.Version(ready), settings = new { channels = new[] { new { campaignType = "search", budget = 1, biddingStrategy = "maximize_clicks" } } } }, Token);
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        using var stale = await _world.Marketer.PutAsJsonAsync(url, new { version = Campaigns.Version(ready) - 1, settings = new { channels = Array.Empty<object>() } }, Token);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var after = await _world.Marketer.GetFromJsonAsync<JsonElement>(url, Token);
        Assert.Equal(before.GetProperty("hash").GetString(), after.GetProperty("hash").GetString());
        Assert.Equal(before.GetProperty("version").GetInt32(), after.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Lowering_the_budget_limit_requires_a_budget_owner_at_launch()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var ready = await _world.ReadyForLaunchAsync(roomId);
        var owner = await Workspaces.JoinAsync(_world.Api, _world.Admin, _world.BrandId, "approver");
        var approver = _world.Approver.UserId;
        using var settings = await _world.Admin.PutAsJsonAsync($"/api/brands/{_world.BrandId}/approval-settings", new
        {
            briefApprovers = new[] { approver }, assetApprovers = new[] { approver }, launchApprovers = new[] { approver },
            budgetOwners = new[] { owner.UserId }, budgetMoveApprovers = new[] { owner.UserId },
            targetChangeApprovers = new[] { owner.UserId }, googleSuggestionApprovers = new[] { owner.UserId }, budgetLimit = 1m,
        }, Token);
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        using var request = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "launch", version = Campaigns.Version(ready) });
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        var pending = await request.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("budget_owner", pending.GetProperty("pendingApproval").GetProperty("route").GetString());
        var body = new { gate = "launch", version = Campaigns.Version(pending) };
        using var refused = await Campaigns.PostAsync(_world.Approver.Browser, _world.BrandId, roomId, "approve", body);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var approved = await Campaigns.PostAsync(owner.Browser, _world.BrandId, roomId, "approve", body);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    }
}
