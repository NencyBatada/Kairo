using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Triggers;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.IntegrationTests.Opportunities;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Triggers;

public sealed class TriggerSuggestionTests(SqlServer sqlServer) : IAsyncLifetime
{
    private string _database = "";
    private KairoApiFactory _api = null!;
    private KairoWorkerFactory _worker = null!;
    private RadarBrand _brand = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _worker = new KairoWorkerFactory(_database, _api.GoogleApis, Triggered.Morning);
        new FakePlan().Attach(_worker.Agents);
        _brand = await Radar.BrandAsync(_api, connectAds: false);
    }

    [Fact]
    public async Task A_suggested_rule_waits_as_a_draft_until_an_admin_turns_it_on()
    {
        _api.GoogleApis.Weather.Rain["mumbai"] = [90];
        var memoryId = Guid.NewGuid();

        var result = await SuggestAsync(new SuggestedRule(
            "Rain lifts waterproof footwear",
            "weather",
            JsonSerializer.SerializeToElement(new { kind = "heavy_rain", threshold = 40 }),
            ["mumbai"],
            Radar.RainCategory,
            null,
            "Rain lifted waterproof footwear sales in Mumbai by 12% in the last test.",
            memoryId));

        Assert.Empty(result.Errors);
        var ruleId = result.RuleId!.Value;
        var draft = Assert.Single((await _brand.Admin.Browser.GetFromJsonAsync<JsonElement[]>($"{Triggered.Base(_brand.BrandId)}/rules?status=draft", Token))!);
        Assert.Equal((ruleId, "suggested", "draft"), (draft.GetProperty("id").GetGuid(), draft.GetProperty("origin").GetString(), draft.GetProperty("status").GetString()));
        Assert.StartsWith("Rain lifted", draft.GetProperty("evidence").GetString());
        var entry = await Workspaces.LatestAuditEntryAsync(_brand.Admin.Browser, _brand.BrandId);
        Assert.Equal(("trigger_rule.suggested", "Learn agent"), (entry.GetProperty("action").GetString(), entry.GetProperty("actorName").GetString()));

        await Triggered.CheckAsync(_worker, force: true);
        Assert.Empty(_api.GoogleApis.Weather.Calls);

        Assert.Equal(HttpStatusCode.OK, (await _brand.Admin.Browser.PostAsync($"{Triggered.Base(_brand.BrandId)}/rules/{ruleId}/activate", null, Token)).StatusCode);
        await Triggered.CheckAsync(_worker);

        Assert.Single(await Triggered.TriggeredCardsAsync(_brand.Marketer.Browser, _brand.BrandId));
    }

    [Fact]
    public async Task A_suggestion_is_checked_like_a_rule_and_needs_evidence()
    {
        var result = await SuggestAsync(new SuggestedRule(
            "Snow",
            "weather",
            JsonSerializer.SerializeToElement(new { kind = "snow" }),
            ["kerala"],
            Radar.RainCategory,
            null,
            " ",
            null));

        Assert.Null(result.RuleId);
        Assert.Equal(["condition.kind", "evidence", "regions"], result.Errors.Keys.Order());
        Assert.Empty((await _brand.Admin.Browser.GetFromJsonAsync<JsonElement[]>($"{Triggered.Base(_brand.BrandId)}/rules", Token))!);
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private async Task<SuggestionResult> SuggestAsync(SuggestedRule rule)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        await database.UseBrandAsync(_brand.BrandId, Token);
        await database.BeginTransactionAsync(Token);
        var result = await scope.ServiceProvider.GetRequiredService<ITriggerSuggestions>().SuggestAsync(rule, Token);
        await database.CommitAsync(Token);
        return result;
    }
}
