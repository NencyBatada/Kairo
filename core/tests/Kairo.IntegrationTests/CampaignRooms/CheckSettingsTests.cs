using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.CampaignRooms.Checks;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class CheckSettingsTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_workspace_starts_with_the_default_checks()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var settings = await GetAsync(owner.Browser, brandId);

        Assert.Equal(0, settings.GetProperty("version").GetInt32());
        Assert.Equal(80, settings.GetProperty("passScore").GetInt32());
        Assert.Equal(3, settings.GetProperty("fixAttempts").GetInt32());
        Assert.Equal(0, settings.GetProperty("customRules").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("updatedBy").ValueKind);
        var rules = settings.GetProperty("rules").EnumerateArray().ToList();
        Assert.Equal(CheckRules.All.Select(rule => rule.Id), rules.Select(rule => rule.GetProperty("id").GetString()));
        var political = rules.Single(rule => rule.GetProperty("id").GetString() == CheckRules.Political);
        Assert.Equal("block", political.GetProperty("severity").GetString());
        Assert.False(political.GetProperty("adjustable").GetBoolean());
        Assert.False(political.GetProperty("overridable").GetBoolean());
        Assert.Equal(["video"], rules.Single(rule => rule.GetProperty("id").GetString() == CheckRules.AbcdDirect)
            .GetProperty("appliesTo").EnumerateArray().Select(target => target.GetString()));
    }

    [Fact]
    public async Task An_admin_tunes_the_checks_and_every_member_sees_them()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var response = await SaveAsync(owner.Browser, brandId, Body(0,
            rules: new Dictionary<string, string> { [CheckRules.Voice] = "off", [CheckRules.AbcdDirect] = "block" },
            customRules: [new { text = "Never show bare feet", appliesTo = "image", severity = "block" }],
            passScore: 70,
            fixAttempts: 2));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await GetAsync(marketer.Browser, brandId);
        Assert.Equal(1, settings.GetProperty("version").GetInt32());
        Assert.Equal(70, settings.GetProperty("passScore").GetInt32());
        Assert.Equal(2, settings.GetProperty("fixAttempts").GetInt32());
        Assert.Equal(owner.UserId, settings.GetProperty("updatedBy").GetGuid());
        Assert.Equal("off", Severity(settings, CheckRules.Voice));
        Assert.Equal("block", Severity(settings, CheckRules.AbcdDirect));
        Assert.Equal("warn", Severity(settings, CheckRules.Visual));
        var custom = Assert.Single(settings.GetProperty("customRules").EnumerateArray());
        Assert.Equal("Never show bare feet", custom.GetProperty("text").GetString());
        Assert.StartsWith("custom.", custom.GetProperty("id").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_again_keeps_custom_rule_ids_and_bumps_the_version()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await SaveAsync(owner.Browser, brandId, Body(0, customRules: [new { text = "Never show bare feet", appliesTo = "image", severity = "block" }]));
        var id = (await GetAsync(owner.Browser, brandId)).GetProperty("customRules")[0].GetProperty("id").GetString();

        var response = await SaveAsync(owner.Browser, brandId, Body(1, customRules: [new { id, text = "Never show bare feet indoors", appliesTo = "all", severity = "warn" }]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(2, settings.GetProperty("version").GetInt32());
        var custom = Assert.Single(settings.GetProperty("customRules").EnumerateArray());
        Assert.Equal(id, custom.GetProperty("id").GetString());
        Assert.Equal("all", custom.GetProperty("appliesTo").GetString());
    }

    [Fact]
    public async Task A_save_from_an_old_version_is_refused()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await SaveAsync(owner.Browser, brandId, Body(0, passScore: 90));

        var response = await SaveAsync(owner.Browser, brandId, Body(0, passScore: 60));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(90, (await GetAsync(owner.Browser, brandId)).GetProperty("passScore").GetInt32());
    }

    [Fact]
    public async Task Invalid_settings_are_refused_and_nothing_changes()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var response = await SaveAsync(owner.Browser, brandId, Body(0,
            rules: new Dictionary<string, string> { [CheckRules.Political] = "off", ["brand.made_up"] = "warn" },
            passScore: 120,
            fixAttempts: 9));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
        Assert.Equal(
            ["fixAttempts", "passScore", "rules.brand.made_up", "rules.policy.political"],
            errors.EnumerateObject().Select(error => error.Name).Order(StringComparer.Ordinal));
        Assert.Equal(0, (await GetAsync(owner.Browser, brandId)).GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Only_admins_can_change_the_checks()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        var approver = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "approver");

        Assert.Equal(HttpStatusCode.Forbidden, (await SaveAsync(marketer.Browser, brandId, Body(0))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SaveAsync(approver.Browser, brandId, Body(0))).StatusCode);
        Assert.Equal(0, (await GetAsync(approver.Browser, brandId)).GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Changes_are_audited_with_before_and_after()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        await SaveAsync(owner.Browser, brandId, Body(0, rules: new Dictionary<string, string> { [CheckRules.Voice] = "block" }, passScore: 65));

        var entry = await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId);
        Assert.Equal("check_settings.changed", entry.GetProperty("action").GetString());
        Assert.Equal(80, entry.GetProperty("before").GetProperty("passScore").GetInt32());
        Assert.Equal(65, entry.GetProperty("after").GetProperty("passScore").GetInt32());
        Assert.Equal("warn", Severity(entry.GetProperty("before"), CheckRules.Voice));
        Assert.Equal("block", Severity(entry.GetProperty("after"), CheckRules.Voice));
    }

    [Fact]
    public async Task Each_brand_has_its_own_settings()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var first = await Workspaces.CreateAsync(owner.Browser);
        var second = await Workspaces.CreateAsync(owner.Browser);

        await SaveAsync(owner.Browser, first, Body(0, passScore: 95));

        Assert.Equal(95, (await GetAsync(owner.Browser, first)).GetProperty("passScore").GetInt32());
        Assert.Equal(80, (await GetAsync(owner.Browser, second)).GetProperty("passScore").GetInt32());
        Assert.Equal(95, (await StoredAsync(first)).PassScore);
        Assert.Equal(0, (await StoredAsync(second)).Version);
    }

    [Fact]
    public async Task The_store_gives_the_checks_the_settings_they_run_with()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await SaveAsync(owner.Browser, brandId, Body(0,
            rules: new Dictionary<string, string> { [CheckRules.ProductAccuracy] = "warn" },
            customRules: [new { text = "Say monsoon, not rain", appliesTo = "text", severity = "warn" }],
            fixAttempts: 1));

        var settings = await StoredAsync(brandId);

        Assert.Equal(1, settings.Version);
        Assert.Equal(1, settings.FixAttempts);
        Assert.Equal(CheckSeverities.Warn, settings.Severity(CheckRules.ProductAccuracy));
        Assert.Equal(CheckSeverities.Block, settings.Severity(CheckRules.Political));
        Assert.Equal("Say monsoon, not rain", Assert.Single(settings.CustomOn(CheckTargets.Text)).Text);
        Assert.Empty(settings.CustomOn(CheckTargets.Image));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static object Body(
        int version,
        Dictionary<string, string>? rules = null,
        object[]? customRules = null,
        int passScore = 80,
        int fixAttempts = 3) =>
        new { version, rules, customRules, passScore, fixAttempts };

    private static Task<HttpResponseMessage> SaveAsync(HttpClient browser, Guid brandId, object body) =>
        browser.PutAsJsonAsync($"/api/brands/{brandId}/check-settings", body, Token);

    private static Task<JsonElement> GetAsync(HttpClient browser, Guid brandId) =>
        browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/check-settings", Token);

    private static string? Severity(JsonElement settings, string id) =>
        settings.GetProperty("rules").EnumerateArray()
            .Single(rule => rule.GetProperty("id").GetString() == id)
            .GetProperty("severity").GetString();

    private async Task<EffectiveCheckSettings> StoredAsync(Guid brandId)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        return await scope.ServiceProvider.GetRequiredService<CheckSettingsStore>().GetAsync(brandId, Token);
    }
}
