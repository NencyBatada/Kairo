using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Api;

public sealed class ApprovalSettingsTests(SqlServer sqlServer) : IAsyncDisposable
{
    private static readonly string[] AllDuties =
    [
        "briefApprovers",
        "assetApprovers",
        "launchApprovers",
        "budgetOwners",
        "budgetMoveApprovers",
        "targetChangeApprovers",
        "googleSuggestionApprovers",
    ];

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_workspace_puts_its_creator_on_every_duty_with_no_budget_limit()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var settings = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/approval-settings", Token);

        foreach (var duty in AllDuties)
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
            budgetMoveApprovers = new[] { owner.UserId },
            targetChangeApprovers = new[] { owner.UserId },
            googleSuggestionApprovers = new[] { owner.UserId },
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
    public async Task An_admin_can_choose_who_approves_each_type_of_recommendation()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var meera = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "approver");
        var arjun = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "approver");

        var response = await SaveAsync(owner, brandId, budgetLimit: null, recommendations: new
        {
            budgetMoveApprovers = new[] { meera.UserId },
            targetChangeApprovers = new[] { meera.UserId, arjun.UserId },
            googleSuggestionApprovers = new[] { arjun.UserId },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await arjun.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/approval-settings", Token);
        Assert.Equal(meera.UserId, Assert.Single(settings.GetProperty("budgetMoveApprovers").EnumerateArray()).GetGuid());
        Assert.Equal(
            new[] { meera.UserId, arjun.UserId }.Order(),
            settings.GetProperty("targetChangeApprovers").EnumerateArray().Select(person => person.GetGuid()).Order());
        Assert.Equal(arjun.UserId, Assert.Single(settings.GetProperty("googleSuggestionApprovers").EnumerateArray()).GetGuid());
        Assert.Equal(owner.UserId, Assert.Single(settings.GetProperty("budgetOwners").EnumerateArray()).GetGuid());
    }

    [Fact]
    public async Task Recommendation_approver_changes_are_audited_with_before_and_after()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var approver = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "approver");

        await SaveAsync(owner, brandId, budgetLimit: null, recommendations: new
        {
            budgetMoveApprovers = new[] { approver.UserId },
        });

        var entry = await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId);
        Assert.Equal("approval_settings.changed", entry.GetProperty("action").GetString());
        Assert.Equal(owner.UserId, Assert.Single(entry.GetProperty("before").GetProperty("budgetMoveApprovers").EnumerateArray()).GetGuid());
        Assert.Equal(approver.UserId, Assert.Single(entry.GetProperty("after").GetProperty("budgetMoveApprovers").EnumerateArray()).GetGuid());
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
    public async Task Every_type_of_recommendation_needs_someone()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var response = await SaveAsync(owner, brandId, budgetLimit: null, recommendations: new
        {
            budgetMoveApprovers = Array.Empty<Guid>(),
            targetChangeApprovers = Array.Empty<Guid>(),
            googleSuggestionApprovers = (Guid[]?)null,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
        Assert.Equal(
            ["budgetMoveApprovers", "googleSuggestionApprovers", "targetChangeApprovers"],
            errors.EnumerateObject().Select(error => error.Name).Order(StringComparer.Ordinal));
        var unchanged = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/approval-settings", Token);
        Assert.Equal(owner.UserId, Assert.Single(unchanged.GetProperty("budgetMoveApprovers").EnumerateArray()).GetGuid());
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
        var withMarketerOnRecommendations = await SaveAsync(owner, brandId, budgetLimit: null, recommendations: new
        {
            googleSuggestionApprovers = new[] { marketer.UserId },
        });

        Assert.Equal(HttpStatusCode.BadRequest, withMarketer.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withStranger.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withMarketerOnRecommendations.StatusCode);
        Assert.True((await withMarketerOnRecommendations.Content.ReadFromJsonAsync<JsonElement>(Token))
            .GetProperty("errors").TryGetProperty("googleSuggestionApprovers", out _));
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

    [Fact]
    public async Task Other_modules_can_ask_who_approves_each_gate_and_type_of_recommendation()
    {
        var owner = await Workspaces.SignInAsync(_api, "Priya Owner");
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var meera = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "approver");
        await SaveAsync(owner, brandId, budgetLimit: null, recommendations: new
        {
            budgetMoveApprovers = new[] { meera.UserId, owner.UserId },
            targetChangeApprovers = new[] { meera.UserId },
        });
        await using var scope = _api.Services.CreateAsyncScope();
        var approvers = scope.ServiceProvider.GetRequiredService<IApprovers>();

        var briefs = await approvers.ForAsync(brandId, ApprovalDuty.Brief, Token);
        var budgetMoves = await approvers.ForAsync(brandId, ApprovalDuty.BudgetMoves, Token);
        var targetChanges = await approvers.ForAsync(brandId, ApprovalDuty.TargetChanges, Token);
        var googleSuggestions = await approvers.ForAsync(brandId, ApprovalDuty.GoogleSuggestions, Token);

        Assert.Equal(owner.UserId, Assert.Single(briefs).UserId);
        Assert.Equal(new[] { owner.UserId, meera.UserId }.Order(), budgetMoves.Select(member => member.UserId).Order());
        Assert.Equal(meera.UserId, Assert.Single(targetChanges).UserId);
        var google = Assert.Single(googleSuggestions);
        Assert.Equal((owner.UserId, owner.Account.Email, "Priya Owner"), (google.UserId, google.Email, google.Name));
        Assert.Empty(await approvers.ForAsync(Guid.NewGuid(), ApprovalDuty.BudgetMoves, Token));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static Task<HttpResponseMessage> SaveAsync(Member owner, Guid brandId, decimal? budgetLimit, Guid[]? brief = null, object? recommendations = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["briefApprovers"] = brief ?? [owner.UserId],
            ["assetApprovers"] = new[] { owner.UserId },
            ["launchApprovers"] = new[] { owner.UserId },
            ["budgetOwners"] = new[] { owner.UserId },
            ["budgetMoveApprovers"] = new[] { owner.UserId },
            ["targetChangeApprovers"] = new[] { owner.UserId },
            ["googleSuggestionApprovers"] = new[] { owner.UserId },
            ["budgetLimit"] = budgetLimit,
        };
        foreach (var property in JsonSerializer.SerializeToElement(recommendations ?? new { }).EnumerateObject())
        {
            body[property.Name] = property.Value;
        }

        return owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/approval-settings", body, Token);
    }
}
