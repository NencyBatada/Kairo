using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Contracts.BrandMemory;
using Kairo.IntegrationTests.Agents;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.CampaignRooms;
using Kairo.IntegrationTests.Launch;
using Kairo.IntegrationTests.Opportunities;
using Kairo.Modules.BrandMemory.Data;
using Kairo.Modules.BrandMemory.Learning;
using Kairo.Modules.Launch;
using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.BrandMemory;

public sealed class BrandMemoryTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;
    private readonly FakeLaunchGoogle _launch = new();
    private readonly List<JsonElement> _learnRequests = [];
    private Func<JsonElement, object> _lessons = _ => new { lessons = Array.Empty<object>() };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _world = await CampaignWorld.CreateAsync(sqlServer);
        var previous = _world.Api.Agents.Respond;
        _world.Api.Agents.Respond = (path, body) =>
        {
            if (path != "/learn/lessons")
            {
                return previous(path, body);
            }

            lock (_learnRequests)
            {
                _learnRequests.Add(body);
            }

            var answer = JsonSerializer.SerializeToNode(_lessons(body))!.AsObject();
            answer["model"] = "gemini-flash-test";
            answer["promptVersion"] = "learn-lessons/1";
            return FakeAgents.Json(answer);
        };
    }

    public ValueTask DisposeAsync() => _world.DisposeAsync();

    [Fact]
    public async Task Learn_saves_the_proven_lessons_audits_them_and_closes_the_campaign()
    {
        var room = await ResultsReadyAsync();
        _lessons = _ => new
        {
            lessons = new object[]
            {
                Lesson("message", "worked", "Quick-dry beats price", adIds: ["ad:search:1"], languages: ["en"]),
                Lesson("audience", "worked", "Commuters buy in the rain", regions: ["Mumbai", "Goa"]),
                Lesson("message", "worked", "Thin ad", adIds: ["ad:search:2"]),
                Lesson("audience", "didnt_work", "Wrong side"),
                Lesson("opinion", "worked", "Unknown kind"),
            },
        };

        var run = await LearnAsync(Input(room, "worked"));

        Assert.Equal((LearnOutcomes.Saved, 2, 1), (run.Outcome, run.Entries, run.Attempts));
        Assert.Equal(("gemini-flash-test", "learn-lessons/1"), (run.Model, run.PromptVersion));
        var entries = await EntriesAsync();
        Assert.Equal(["Commuters buy in the rain", "Quick-dry beats price"], entries.Select(entry => entry.Title).Order());
        var message = entries.Single(entry => entry.Kind == MemoryKinds.Message);
        Assert.Equal(["p-1"], message.Products);
        Assert.Equal([Radar.RainCategory], message.Categories);
        Assert.Equal(["Mumbai", "Pune"], message.Regions);
        Assert.Equal(["en"], message.Languages);
        Assert.Equal((room, "learn"), (message.SourceRoomId!.Value, message.CreatedBy));
        Assert.Contains("\"lift\":0.12", message.Evidence);
        Assert.Equal(["Mumbai"], entries.Single(entry => entry.Kind == MemoryKinds.Audience).Regions);
        Assert.Equal("closed", Campaigns.Stage(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room)));
        Assert.Equal(2, (await AuditAsync("brand_memory.entry_saved")).Length);
        var request = Assert.Single(_learnRequests);
        Assert.Equal("worked", request.GetProperty("result").GetProperty("outcome").GetString());
        Assert.Equal("ad:search:1", request.GetProperty("ads")[0].GetProperty("id").GetString());

        var again = await LearnAsync(Input(room, "worked"));

        Assert.Equal(run.Id, again.Id);
        Assert.Single(_learnRequests);
        Assert.Equal(2, (await EntriesAsync()).Count);
    }

    [Fact]
    public async Task An_inconclusive_result_saves_nothing_but_still_closes_the_campaign()
    {
        var room = await ResultsReadyAsync();

        var run = await LearnAsync(Input(room, "inconclusive"));

        Assert.Equal((LearnOutcomes.SkippedInconclusive, 0), (run.Outcome, run.Entries));
        Assert.Empty(_learnRequests);
        Assert.Empty(await EntriesAsync());
        Assert.Empty(await AuditAsync("brand_memory.entry_saved"));
        Assert.Equal("closed", Campaigns.Stage(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room)));
    }

    [Fact]
    public async Task A_failed_Learn_call_is_recorded_and_tried_again()
    {
        var room = await ResultsReadyAsync();
        _world.Api.Agents.Failures = 1;
        _lessons = _ => new { lessons = new object[] { Lesson("audience", "worked", "Commuters buy in the rain") } };

        var failed = await LearnAsync(Input(room, "worked"));

        Assert.Equal((LearnOutcomes.Failed, 1), (failed.Outcome, failed.Attempts));
        Assert.False(string.IsNullOrEmpty(failed.Error));
        Assert.Equal("results_ready", Campaigns.Stage(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room)));

        var saved = await LearnAsync(Input(room, "worked"));

        Assert.Equal((failed.Id, LearnOutcomes.Saved, 2, 1), (saved.Id, saved.Outcome, saved.Attempts, saved.Entries));
        Assert.Null(saved.Error);
        Assert.Equal("closed", Campaigns.Stage(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room)));
    }

    [Fact]
    public async Task Learn_waits_until_the_test_has_ended()
    {
        var room = await ResultsReadyAsync(endTest: false);

        Assert.Null(await TryLearnAsync(Input(room, "worked")));
        Assert.Null(await TryLearnAsync(Input(Guid.NewGuid(), "inconclusive")));

        Assert.Empty(_learnRequests);
        await using var scope = await BrandScopeAsync();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<BrandMemoryDbContext>().LearnRuns.ToListAsync(Token));
        Assert.Equal("live", Campaigns.Stage(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room)));
    }

    [Fact]
    public async Task Learn_stops_trying_after_three_failed_attempts()
    {
        var room = await ResultsReadyAsync();
        _world.Api.Agents.Failures = LearnWork.MostAttempts;

        for (var attempt = 0; attempt < LearnWork.MostAttempts + 1; attempt++)
            await LearnAsync(Input(room, "worked"));

        var run = await LearnAsync(Input(room, "worked"));
        Assert.Equal((LearnOutcomes.Failed, LearnWork.MostAttempts), (run.Outcome, run.Attempts));
        Assert.Equal(LearnWork.MostAttempts, _world.Api.Agents.Requests.Count(request => request.Url.EndsWith("/learn/lessons", StringComparison.Ordinal)));
        Assert.Equal("results_ready", Campaigns.Stage(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room)));
    }

    [Fact]
    public async Task People_can_view_edit_and_remove_entries_and_removed_ones_are_no_longer_used()
    {
        var kept = await SeedAsync(MemoryKinds.Message, MemoryOutcomes.Worked, "Quick-dry beats price", categories: [Radar.RainCategory]);
        var removed = await SeedAsync(MemoryKinds.Region, MemoryOutcomes.DidntWork, "Goa barely responded", categories: [Radar.RainCategory]);

        var list = await _world.Approver.Browser.GetFromJsonAsync<JsonElement>(Base, Token);
        Assert.Equal(2, list.GetProperty("entries").GetArrayLength());
        var detail = await _world.Approver.Browser.GetFromJsonAsync<JsonElement>($"{Base}/{kept}", Token);
        Assert.Equal(("Quick-dry beats price", 1, 0), (Text(detail.GetProperty("entry"), "title"), detail.GetProperty("entry").GetProperty("version").GetInt32(), detail.GetProperty("uses").GetArrayLength()));
        using (var filtered = await _world.Marketer.GetAsync($"{Base}?kind=message&outcome=worked", Token))
            Assert.Equal(1, (await filtered.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("entries").GetArrayLength());
        using (var invalid = await _world.Marketer.GetAsync($"{Base}?kind=colour", Token))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var notFound = await _world.Marketer.GetAsync($"{Base}/{Guid.NewGuid()}", Token))
            Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);

        using (var approver = await _world.Approver.Browser.PatchAsJsonAsync($"{Base}/{kept}", new { version = 1, lesson = "No." }, Token))
            Assert.Equal(HttpStatusCode.Forbidden, approver.StatusCode);
        using (var stale = await _world.Marketer.PatchAsJsonAsync($"{Base}/{kept}", new { version = 2, lesson = "No." }, Token))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var empty = await _world.Marketer.PatchAsJsonAsync($"{Base}/{kept}", new { version = 1, title = " " }, Token))
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using (var edited = await _world.Marketer.PatchAsJsonAsync($"{Base}/{kept}", new { version = 1, lesson = "Lead with quick-dry, not price." }, Token))
        {
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            var entry = (await edited.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("entry");
            Assert.Equal(("Lead with quick-dry, not price.", "Quick-dry beats price", 2), (Text(entry, "lesson"), Text(entry, "title"), entry.GetProperty("version").GetInt32()));
            Assert.Equal(_world.Brand.Marketer.UserId, entry.GetProperty("editedBy").GetGuid());
        }

        using (var adminRemoves = await _world.Admin.DeleteAsync($"{Base}/{removed}", Token))
            Assert.Equal(HttpStatusCode.NoContent, adminRemoves.StatusCode);
        using (var again = await _world.Marketer.DeleteAsync($"{Base}/{removed}", Token))
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        using (var approverRemoves = await _world.Approver.Browser.DeleteAsync($"{Base}/{kept}", Token))
            Assert.Equal(HttpStatusCode.Forbidden, approverRemoves.StatusCode);
        using (var editRemoved = await _world.Marketer.PatchAsJsonAsync($"{Base}/{removed}", new { version = 2, lesson = "No." }, Token))
            Assert.Equal(HttpStatusCode.Conflict, editRemoved.StatusCode);

        var active = await _world.Marketer.GetFromJsonAsync<JsonElement>(Base, Token);
        Assert.Equal([kept], active.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("id").GetGuid()));
        var gone = await _world.Marketer.GetFromJsonAsync<JsonElement>($"{Base}?state=removed", Token);
        Assert.Equal([removed], gone.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("id").GetGuid()));
        var relevant = await RelevantAsync(new MemoryQuery(Categories: [Radar.RainCategory]));
        Assert.Equal([kept], relevant.Select(note => note.Id));
        Assert.Single(await AuditAsync("brand_memory.entry_edited"));
        Assert.Single(await AuditAsync("brand_memory.entry_removed"));
    }

    [Fact]
    public async Task Relevant_entries_rank_by_overlap_then_recency_and_uses_are_recorded_once()
    {
        var region = await SeedAsync(MemoryKinds.Region, MemoryOutcomes.Worked, "Mumbai responds", regions: ["Mumbai"]);
        var product = await SeedAsync(MemoryKinds.Message, MemoryOutcomes.Worked, "Runner copy", products: ["p-1"]);
        var general = await SeedAsync(MemoryKinds.Audience, MemoryOutcomes.Worked, "Brand-wide lesson");
        await SeedAsync(MemoryKinds.Message, MemoryOutcomes.Worked, "Other products", products: ["p-9"]);

        var relevant = await RelevantAsync(new MemoryQuery(Products: ["P-1"], Regions: ["mumbai"]));
        Assert.Equal([product, region, general], relevant.Select(note => note.Id));
        Assert.Equal([product], (await RelevantAsync(new MemoryQuery(Products: ["p-1"], Kinds: [MemoryKinds.Message]))).Select(note => note.Id));
        Assert.Equal(4, (await RelevantAsync(new MemoryQuery())).Count);

        var target = Guid.NewGuid();
        await using (var scope = await BrandScopeAsync())
        {
            var memory = scope.ServiceProvider.GetRequiredService<IBrandMemory>();
            await memory.RecordUsesAsync([product, product, region, Guid.NewGuid()], MemoryUsedIn.Brief, target, null, Token);
            await memory.RecordUsesAsync([product], MemoryUsedIn.Brief, target, null, Token);
        }

        Assert.Equal(2, await UsesAsync(MemoryUsedIn.Brief));
    }

    [Fact]
    public async Task A_drafted_brief_gets_relevant_lessons_and_records_the_ones_it_used()
    {
        var used = await SeedAsync(MemoryKinds.Message, MemoryOutcomes.Worked, "Quick-dry beats price", categories: [Radar.RainCategory]);
        await SeedAsync(MemoryKinds.Message, MemoryOutcomes.Worked, "School shoes in June", categories: ["Shoes > School"]);
        var dropped = await SeedAsync(MemoryKinds.Message, MemoryOutcomes.Worked, "Removed lesson", categories: [Radar.RainCategory]);
        using (var removed = await _world.Marketer.DeleteAsync($"{Base}/{dropped}", Token))
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        _world.Plan.Answer = body =>
        {
            var draft = JsonSerializer.SerializeToNode(FakeBriefPlan.Draft(body))!.AsObject();
            draft["memoryUsed"] = new JsonArray("m1", "m7");
            return draft;
        };

        var room = await _world.OpenWithBriefAsync();

        var request = Assert.Single(_world.Plan.Requests);
        var memory = request.GetProperty("memory").EnumerateArray().ToList();
        var lesson = Assert.Single(memory);
        Assert.Equal(("m1", "Worked (message): Quick-dry beats price"), (Text(lesson, "id"), Text(lesson, "text")));
        var brief = await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, room);
        var shown = Assert.Single(brief.GetProperty("memory").EnumerateArray());
        Assert.Equal((used, "Quick-dry beats price", false), (shown.GetProperty("id").GetGuid(), Text(shown, "title"), shown.GetProperty("removed").GetBoolean()));
        Assert.Equal(1, await UsesAsync(MemoryUsedIn.Brief));
        var entry = await _world.Marketer.GetFromJsonAsync<JsonElement>($"{Base}/{used}", Token);
        var use = Assert.Single(entry.GetProperty("uses").EnumerateArray());
        Assert.Equal(("brief", room), (Text(use, "usedIn"), use.GetProperty("campaignId").GetGuid()));
    }

    [Fact]
    public async Task Generated_copy_gets_relevant_lessons_and_shows_them_in_the_asset_history()
    {
        await SeedAsync(MemoryKinds.Message, MemoryOutcomes.Worked, "Quick-dry beats price", categories: [Radar.RainCategory]);
        await SeedAsync(MemoryKinds.Region, MemoryOutcomes.Worked, "Mumbai responds", categories: [Radar.RainCategory]);
        var room = await _world.OpenWithBriefAsync();
        await _world.ApproveBriefAsync(room);
        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(1, await _world.RunStagesAsync());
        Assert.Equal(1, await _world.RunStagesAsync());

        var request = _world.Create.Requests.First();
        Assert.Equal(["Worked (message): Quick-dry beats price"], request.GetProperty("memory").EnumerateArray().Select(text => text.GetString()));
        var ad = Campaigns.Asset(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, room), "search_ad");
        using var response = await _world.Marketer.GetAsync($"{Campaigns.Base(_world.BrandId)}/{room}/assets/{ad.GetProperty("id").GetGuid()}/history", Token);
        var version = (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("versions")[0];
        Assert.Equal(["Quick-dry beats price"], version.GetProperty("memoryUses").EnumerateArray().Select(title => title.GetString()));
        var assets = Campaigns.AllAssets(await Campaigns.AssetsAsync(_world.Marketer, _world.BrandId, room));
        var titles = assets.Where(asset => Text(asset, "kind") == "shopping_title").Select(asset => asset.GetProperty("id").GetGuid()).ToList();
        Assert.NotEmpty(titles);
        using var titleResponse = await _world.Marketer.GetAsync($"{Campaigns.Base(_world.BrandId)}/{room}/assets/{titles[0]}/history", Token);
        Assert.Empty((await titleResponse.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("versions")[0].GetProperty("memoryUses").EnumerateArray());
        await using var scope = await BrandScopeAsync();
        var targets = await scope.ServiceProvider.GetRequiredService<BrandMemoryDbContext>().Uses.Where(use => use.UsedIn == MemoryUsedIn.Asset).Select(use => use.TargetId).ToListAsync(Token);
        Assert.Equal(assets.Length - titles.Count, targets.Count);
        Assert.DoesNotContain(targets, titles.Contains);
    }

    [Fact]
    public async Task Opportunity_rankings_use_lessons_about_the_same_products()
    {
        var card = await Radar.OnlyCardAsync(_world.Marketer, _world.BrandId);
        var before = await Radar.DetailAsync(_world.Marketer, _world.BrandId, card.GetProperty("id").GetGuid());
        var lesson = await SeedAsync(MemoryKinds.Message, MemoryOutcomes.Worked, "Quick-dry beats price", categories: [Radar.RainCategory]);
        await SeedAsync(MemoryKinds.Message, MemoryOutcomes.DidntWork, "School shoes in June", categories: ["Shoes > School"]);

        var refreshed = await Radar.RefreshAsync(_world.Worker, _world.BrandId);
        Assert.Equal("succeeded", Radar.Outcome(refreshed, _world.BrandId), StringComparer.Ordinal);

        var after = await Radar.DetailAsync(_world.Marketer, _world.BrandId, before.GetProperty("id").GetGuid());
        var breakdown = after.GetProperty("scoreBreakdown");
        Assert.Equal(1.15, breakdown.GetProperty("memory").GetDouble());
        Assert.Equal(["Brand Memory: Quick-dry beats price"], breakdown.GetProperty("memoryReasons").EnumerateArray().Select(reason => reason.GetString()));
        var raw = (breakdown.GetProperty("growthWeight").GetDouble() * breakdown.GetProperty("growthScore").GetDouble())
            + (breakdown.GetProperty("volumeWeight").GetDouble() * breakdown.GetProperty("volumeScore").GetDouble())
            + (breakdown.GetProperty("fitWeight").GetDouble() * breakdown.GetProperty("fitScore").GetDouble());
        var expected = (int)Math.Clamp(Math.Round(raw * breakdown.GetProperty("feedback").GetDouble() * 1.15, MidpointRounding.AwayFromZero), 0, 100);
        Assert.Equal(expected, after.GetProperty("score").GetInt32());
        Assert.Equal(1.0, before.GetProperty("scoreBreakdown").GetProperty("memory").GetDouble());
        var entry = await _world.Marketer.GetFromJsonAsync<JsonElement>($"{Base}/{lesson}", Token);
        var use = Assert.Single(entry.GetProperty("uses").EnumerateArray());
        Assert.Equal(("opportunity", before.GetProperty("id").GetGuid()), (Text(use, "usedIn"), use.GetProperty("targetId").GetGuid()));

        await Radar.RefreshAsync(_world.Worker, _world.BrandId);

        Assert.Equal(1, await UsesAsync(MemoryUsedIn.Opportunity));
    }

    private string Base => $"/api/brands/{_world.BrandId}/brand-memory";

    private static object Lesson(string kind, string outcome, string title, string[]? adIds = null, string[]? regions = null, string[]? languages = null) => new
    {
        kind,
        outcome,
        title,
        lesson = $"{title}.",
        adIds = adIds ?? [],
        regions = regions ?? [],
        languages = languages ?? [],
        evidence = new { lift = 0.12, low = 0.06, high = 0.18 },
    };

    private static LearnInput Input(Guid room, string outcome) => new(
        room,
        Guid.NewGuid(),
        new LearnRequest(
            "Acme Shoes",
            "INR",
            new LearnCampaign("Monsoon runners", "orders", "Sell waterproof shoes.", ["Commuters"], ["Dries in minutes."], ["en"],
                [new LearnProduct("p-1", "Monsoon Waterproof Runner", Radar.RainCategory)]),
            new LearnResult(outcome, 0.12, 0.06, 0.18, 420, 210, 630, 310, 130_200, 3900),
            [new LearnRegion("Mumbai", true, 2400, 2100), new LearnRegion("Pune", true, 1500, 1380), new LearnRegion("Nagpur", false, null, null)],
            [
                new LearnAd("ad:search:1", "search", "en", ["Dries in minutes"], [], null, 20_000, 1000, 60, 9000),
                new LearnAd("ad:search:2", "search", "en", ["Lowest prices"], [], null, 2000, 40, 1, 400),
            ]));

    private async Task<Guid> ResultsReadyAsync(bool endTest = true)
    {
        _world.LoadCityHistory();
        _world.Api.GoogleApis.LaunchResponse = _launch.Respond;
        _world.Api.GoogleApis.LaunchRequest = _launch.RespondRequest;
        var room = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(room);
        await using (var scope = _world.Api.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LaunchDispatcher>().RunDueAsync(Token);
        var detail = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, room);
        Assert.Equal("live", Campaigns.Stage(detail));
        if (!endTest)
        {
            return room;
        }

        using var ended = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, room, "end-test", new { version = Campaigns.Version(detail) });
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        return room;
    }

    private async Task<LearnRun> LearnAsync(LearnInput input) =>
        await TryLearnAsync(input) ?? throw new InvalidOperationException("Learn didn't run.");

    private async Task<LearnRun?> TryLearnAsync(LearnInput input)
    {
        await using var scope = await BrandScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<LearnWork>().LearnAsync(input, Token);
    }

    private async Task<Guid> SeedAsync(string kind, string outcome, string title, string[]? products = null, string[]? categories = null, string[]? regions = null)
    {
        await using var scope = await BrandScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BrandMemoryDbContext>();
        var entry = new MemoryEntry
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Outcome = outcome,
            Title = title,
            Lesson = title,
            Products = [.. products ?? []],
            Categories = [.. categories ?? []],
            Regions = [.. regions ?? []],
            CreatedBy = LearnWork.Creator,
            CreatedAt = _world.Api.Clock.GetUtcNow(),
        };
        db.Entries.Add(entry);
        await db.SaveChangesAsync(Token);
        _world.Api.Clock.Advance(TimeSpan.FromSeconds(1));
        return entry.Id;
    }

    private async Task<IReadOnlyList<MemoryNote>> RelevantAsync(MemoryQuery query)
    {
        await using var scope = await BrandScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IBrandMemory>().RelevantAsync(query, Token);
    }

    private async Task<List<MemoryEntry>> EntriesAsync()
    {
        await using var scope = await BrandScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<BrandMemoryDbContext>().Entries.AsNoTracking().ToListAsync(Token);
    }

    private async Task<int> UsesAsync(string usedIn)
    {
        await using var scope = await BrandScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<BrandMemoryDbContext>().Uses.CountAsync(use => use.UsedIn == usedIn, Token);
    }

    private async Task<AsyncServiceScope> BrandScopeAsync()
    {
        var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
        return scope;
    }

    private async Task<JsonElement[]> AuditAsync(string action) =>
        [.. (await _world.Admin.GetFromJsonAsync<JsonElement>($"/api/brands/{_world.BrandId}/audit-log?action={action}", Token)).GetProperty("entries").EnumerateArray()];

    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString()!;
}
