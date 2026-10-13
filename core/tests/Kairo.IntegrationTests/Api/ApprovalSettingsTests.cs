using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class ApprovalSettingsTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_workspace_puts_its_creator_on_every_duty_with_no_budget_limit()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var settings = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/approval-settings", Token);

        foreach (var duty in new[] { "briefApprovers", "assetApprovers", "launchApprovers", "budgetOwners" })
        {
            Assert.Equal(owner.UserId, Assert.Single(settings.GetProperty(duty).EnumerateArray()).GetGuid());
        }

        Assert.Equal(JsonValueKind.Null, settings.GetProperty("budgetLimit").ValueKind);
        Assert.Equal("INR", settings.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task An_admin_can_share_duties_and_set_the_budget_limit()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var approver = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "approver");

        var response = await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/approval-settings", new
        {
            briefApprovers = new[] { owner.UserId, approver.UserId },
            assetApprovers = new[] { approver.UserId },
            launchApprovers = new[] { owner.UserId },
            budgetOwners = new[] { owner.UserId },
            budgetLimit = 200000m,
        }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await approver.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/approval-settings", Token);
        Assert.Equal(2, settings.GetProperty("briefApprovers").GetArrayLength());
        Assert.Equal(approver.UserId, Assert.Single(settings.GetProperty("assetApprovers").EnumerateArray()).GetGuid());
        Assert.Equal(200000m, settings.GetProperty("budgetLimit").GetDecimal());
        Assert.Equal("approval_settings.changed", (await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId)).GetProperty("action").GetString());
    }

    [Fact]
    public async Task Every_duty_needs_someone()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var response = await SaveAsync(owner, brandId, brief: [], budgetLimit: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors").TryGetProperty("briefApprovers", out _));
    }

    [Fact]
    public async Task Only_members_with_the_approver_role_can_hold_a_duty()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        var stranger = await Workspaces.SignInAsync(_api);

        var withMarketer = await SaveAsync(owner, brandId, brief: [marketer.UserId], budgetLimit: null);
        var withStranger = await SaveAsync(owner, brandId, brief: [stranger.UserId], budgetLimit: null);

        Assert.Equal(HttpStatusCode.BadRequest, withMarketer.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withStranger.StatusCode);
    }

    [Fact]
    public async Task The_budget_limit_cannot_be_negative()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var response = await SaveAsync(owner, brandId, brief: [owner.UserId], budgetLimit: -1m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors").TryGetProperty("budgetLimit", out _));
    }

    [Fact]
    public async Task The_budget_limit_can_be_cleared()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await SaveAsync(owner, brandId, brief: [owner.UserId], budgetLimit: 5000m);

        await SaveAsync(owner, brandId, brief: [owner.UserId], budgetLimit: null);

        var settings = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/approval-settings", Token);
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("budgetLimit").ValueKind);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static Task<HttpResponseMessage> SaveAsync(Member owner, Guid brandId, Guid[] brief, decimal? budgetLimit) =>
        owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/approval-settings", new
        {
            briefApprovers = brief,
            assetApprovers = new[] { owner.UserId },
            launchApprovers = new[] { owner.UserId },
            budgetOwners = new[] { owner.UserId },
            budgetLimit,
        }, Token);
}
