using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kairo.IntegrationTests.Api;

public sealed class WorkspaceTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creating_a_workspace_returns_where_to_find_it()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();

        var response = await owner.Browser.PostAsJsonAsync("/api/brands", Workspaces.NewWorkspace(handle), Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(handle, created.GetProperty("handle").GetString());
        Assert.Equal($"/api/brands/{created.GetProperty("id").GetGuid()}", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_creator_holds_every_role()
    {
        var owner = await Workspaces.SignInAsync(_api);
        await Workspaces.CreateAsync(owner.Browser);

        var mine = await owner.Browser.GetFromJsonAsync<JsonElement>("/api/brands", Token);

        var roles = Assert.Single(mine.EnumerateArray()).GetProperty("roles").EnumerateArray().Select(role => role.GetString());
        Assert.Equal(["admin", "marketer", "approver"], roles);
    }

    [Fact]
    public async Task People_see_only_their_own_workspaces()
    {
        var first = await Workspaces.SignInAsync(_api);
        var second = await Workspaces.SignInAsync(_api);
        var firstId = await Workspaces.CreateAsync(first.Browser);
        await Workspaces.CreateAsync(second.Browser);

        var mine = await first.Browser.GetFromJsonAsync<JsonElement>("/api/brands", Token);

        Assert.Equal(firstId, Assert.Single(mine.EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Someone_outside_the_workspace_gets_not_found()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var stranger = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Browser.GetAsync($"/api/brands/{brandId}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name = "Mine now" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Browser.GetAsync($"/api/brands/{Guid.NewGuid()}", Token)).StatusCode);
    }

    [Fact]
    public async Task Details_include_the_time_zone_currency_and_my_roles()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var details = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}", Token);

        Assert.Equal("Acme Shoes", details.GetProperty("name").GetString());
        Assert.Equal("Asia/Kolkata", details.GetProperty("timeZone").GetString());
        Assert.Equal("INR", details.GetProperty("currency").GetString());
        Assert.Equal(3, details.GetProperty("roles").GetArrayLength());
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("1acme")]
    [InlineData("acme--shoes")]
    [InlineData("acme-")]
    [InlineData("Acme")]
    [InlineData("acme_shoes")]
    [InlineData("sign-in")]
    [InlineData("api")]
    [InlineData("welcome")]
    [InlineData("settings")]
    public async Task Handles_must_follow_the_rules(string handle)
    {
        var owner = await Workspaces.SignInAsync(_api);

        var response = await owner.Browser.PostAsJsonAsync("/api/brands", Workspaces.NewWorkspace(handle), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors").TryGetProperty("handle", out _));
    }

    [Fact]
    public async Task A_taken_handle_is_refused()
    {
        var first = await Workspaces.SignInAsync(_api);
        var second = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        await Workspaces.CreateAsync(first.Browser, handle);

        var response = await second.Browser.PostAsJsonAsync("/api/brands", Workspaces.NewWorkspace(handle), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
        Assert.Equal("That handle is taken.", errors.GetProperty("handle")[0].GetString());
    }

    [Theory]
    [InlineData("timeZone", "Mars/Olympus")]
    [InlineData("timeZone", "India Standard Time")]
    [InlineData("currency", "XYZ")]
    [InlineData("currency", "inr")]
    [InlineData("name", "")]
    public async Task Names_time_zones_and_currencies_are_checked(string field, string value)
    {
        var owner = await Workspaces.SignInAsync(_api);
        var request = new Dictionary<string, string>
        {
            ["name"] = "Acme Shoes",
            ["handle"] = Workspaces.NewHandle(),
            ["timeZone"] = "Asia/Kolkata",
            ["currency"] = "INR",
            [field] = value,
        };

        var response = await owner.Browser.PostAsJsonAsync("/api/brands", request, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task An_admin_can_rename_the_workspace_and_change_its_time_zone()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var response = await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name = "Acme Footwear", timeZone = "Europe/London" }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}", Token);
        Assert.Equal(("Acme Footwear", "Europe/London"), (details.GetProperty("name").GetString(), details.GetProperty("timeZone").GetString()));
    }

    [Fact]
    public async Task Updates_are_checked_too()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var response = await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { timeZone = "Nowhere/Special" }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_failure_part_way_through_leaves_nothing_behind()
    {
        await using var failing = new KairoApiFactory(
            sqlServer.KairoConnectionString,
            services => services.Replace(ServiceDescriptor.Scoped<IAuditLog, FailingAuditLog>()));
        var account = GoogleUser.Create();
        var handle = Workspaces.NewHandle();

        var failed = await (await failing.SignInAsync(account)).PostAsJsonAsync("/api/brands", Workspaces.NewWorkspace(handle), Token);

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var browser = await _api.SignInAsync(account);
        Assert.Empty((await browser.GetFromJsonAsync<JsonElement>("/api/brands", Token)).EnumerateArray());
        Assert.Equal(HttpStatusCode.Created, (await browser.PostAsJsonAsync("/api/brands", Workspaces.NewWorkspace(handle), Token)).StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private sealed class FailingAuditLog : IAuditLog
    {
        public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The audit log is down.");
    }
}
