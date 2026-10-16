using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.BrandMemory;
using Kairo.Contracts.CampaignRooms;
using Kairo.IntegrationTests.Agents;
using Kairo.IntegrationTests.CampaignRooms;
using Kairo.IntegrationTests.Care;
using Kairo.IntegrationTests.Launch;
using Kairo.IntegrationTests.Measurement;
using Kairo.IntegrationTests.Opportunities;
using Kairo.Modules.BrandMemory.Data;
using Kairo.Modules.BrandMemory.Learning;
using Kairo.Modules.Launch;
using Kairo.Modules.Launch.Data;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Feeds;
using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.BrandMemory;

public sealed class LearnJobTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;
    private readonly FakeLaunchGoogle _launch = new();
    private FakeCareGoogle _google = null!;
    private readonly FakeLift _lift = new();
    private readonly FakeReport _report = new();
    private readonly List<JsonElement> _learnRequests = [];
    private int _learnFailures;
    private List<string> _adKeys = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _world = await CampaignWorld.CreateAsync(sqlServer);
        _world.LoadCityHistory();
        _google = new FakeCareGoogle(_launch);
        _world.Api.GoogleApis.LaunchResponse = _google.Respond;
        _world.Api.GoogleApis.LaunchRequest = _launch.RespondRequest;
        _lift.Attach(_world.Worker.Agents);
        _report.Attach(_world.Worker.Agents);
        var previous = _world.Worker.Agents.Respond;
        _world.Worker.Agents.Respond = (path, body) =>
        {
            if (path != "/learn/lessons")
            {
                return previous(path, body);
            }

            lock (_learnRequests)
            {
                _learnRequests.Add(body);
                if (_learnFailures > 0)
                {
                    _learnFailures--;
                    return new HttpResponseMessage(HttpStatusCode.BadGateway);
                }
            }

            var firstAd = body.GetProperty("ads")[0].GetProperty("id").GetString();
            return FakeAgents.Json(new
            {
                lessons = new object[]
                {
                    new { kind = "message", outcome = "worked", title = "Quick-dry beats price", lesson = "Lead with quick-dry.", adIds = new[] { firstAd }, regions = Array.Empty<string>(), languages = new[] { "en" }, evidence = new { lift = 0.12 } },
                    new { kind = "audience", outcome = "worked", title = "Commuters buy in the rain", lesson = "Reach commuters.", adIds = Array.Empty<string>(), regions = Array.Empty<string>(), languages = Array.Empty<string>(), evidence = new { lift = 0.12 } },
                },
                model = "gemini-flash-test",
                promptVersion = "learn-lessons/1",
            });
        };
    }

    public ValueTask DisposeAsync() => _world.DisposeAsync();

    [Fact]
    public async Task A_ready_impact_report_is_learned_from_and_the_campaign_closes()
    {
        var (room, test) = await LiveAsync();
        await FinishAsync(room, test);
        Assert.Equal("results_ready", await StageAsync(room));

        var sweep = await LearnJobAsync();

        Assert.Equal((1, 0, 0), (sweep.GetProperty("saved").GetInt32(), sweep.GetProperty("skipped").GetInt32(), sweep.GetProperty("failed").GetInt32()));
        Assert.Equal("closed", await StageAsync(room));
        var request = Assert.Single(_learnRequests);
        Assert.Equal(("worked", 0.12), (request.GetProperty("result").GetProperty("outcome").GetString(), request.GetProperty("result").GetProperty("lift").GetDouble()));
        Assert.Equal(test.Title, request.GetProperty("campaign").GetProperty("title").GetString());
        Assert.Contains(request.GetProperty("campaign").GetProperty("products").EnumerateArray(), product => product.GetProperty("category").GetString() == Radar.RainCategory);
        Assert.Contains("Commuters caught in the rain", request.GetProperty("campaign").GetProperty("audiences").EnumerateArray().Select(text => text.GetString()));
        Assert.Contains(request.GetProperty("regions").EnumerateArray(), region => region.GetProperty("test").GetBoolean() && region.GetProperty("lift").GetDouble() == 0.12);
        Assert.Contains(request.GetProperty("regions").EnumerateArray(), region => !region.GetProperty("test").GetBoolean());
        var ads = request.GetProperty("ads").EnumerateArray().ToList();
        Assert.Equal(_adKeys.Order(), ads.Select(ad => ad.GetProperty("id").GetString()!).Order());
        Assert.Contains(ads, ad => ad.GetProperty("headlines").GetArrayLength() > 0);
        Assert.All(ads, ad => Assert.True(ad.GetProperty("clicks").GetInt64() >= 100));

        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrandMemoryDbContext>();
            var entries = await db.Entries.AsNoTracking().OrderBy(entry => entry.Title).ToListAsync(Token);
            Assert.Equal(["Commuters buy in the rain", "Quick-dry beats price"], entries.Select(entry => entry.Title));
            Assert.All(entries, entry => Assert.Equal(room, entry.SourceRoomId));
            Assert.All(entries, entry => Assert.Contains(Radar.RainCategory, entry.Categories));
            var run = await db.LearnRuns.AsNoTracking().SingleAsync(Token);
            Assert.Equal((LearnOutcomes.Saved, 2, 1), (run.Outcome, run.Entries, run.Attempts));
            Assert.NotNull(run.ReportId);
            var relevant = await scope.ServiceProvider.GetRequiredService<IBrandMemory>().RelevantAsync(new MemoryQuery(Categories: [Radar.RainCategory]), Token);
            Assert.Equal(2, relevant.Count);
        }

        var again = await LearnJobAsync();

        Assert.Equal((0, 0, 0), (again.GetProperty("saved").GetInt32(), again.GetProperty("skipped").GetInt32(), again.GetProperty("failed").GetInt32()));
        Assert.Single(_learnRequests);
    }

    [Fact]
    public async Task An_inconclusive_report_saves_nothing_and_still_closes_the_campaign()
    {
        var (room, test) = await LiveAsync();
        _lift.Lift = 0.03;
        await FinishAsync(room, test);

        var sweep = await LearnJobAsync();

        Assert.Equal((0, 1, 0), (sweep.GetProperty("saved").GetInt32(), sweep.GetProperty("skipped").GetInt32(), sweep.GetProperty("failed").GetInt32()));
        Assert.Empty(_learnRequests);
        Assert.Equal("closed", await StageAsync(room));
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BrandMemoryDbContext>();
        Assert.Empty(await db.Entries.ToListAsync(Token));
        Assert.Equal(LearnOutcomes.SkippedInconclusive, (await db.LearnRuns.SingleAsync(Token)).Outcome);
    }

    [Fact]
    public async Task Failed_learning_retries_on_later_runs_and_stops_after_three_attempts()
    {
        var (room, test) = await LiveAsync();
        await FinishAsync(room, test);
        _learnFailures = LearnWork.MostAttempts;

        for (var run = 0; run < LearnWork.MostAttempts; run++)
            Assert.Equal(1, (await LearnJobAsync()).GetProperty("failed").GetInt32());
        var stopped = await LearnJobAsync();

        Assert.Equal(0, stopped.GetProperty("failed").GetInt32());
        Assert.Equal(LearnWork.MostAttempts, _learnRequests.Count);
        Assert.Equal("results_ready", await StageAsync(room));
        await using var scope = await ScopeAsync();
        var learned = await scope.ServiceProvider.GetRequiredService<BrandMemoryDbContext>().LearnRuns.SingleAsync(Token);
        Assert.Equal((LearnOutcomes.Failed, LearnWork.MostAttempts), (learned.Outcome, learned.Attempts));
    }

    [Fact]
    public async Task Failing_to_read_ad_results_counts_as_a_failed_attempt()
    {
        var (room, test) = await LiveAsync();
        await FinishAsync(room, test);
        _world.Api.GoogleApis.LaunchResponse = (uri, body) => body.Contains("FROM ad_group_ad", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":{\"code\":400,\"message\":\"Bad query\",\"status\":\"INVALID_ARGUMENT\"}}") }
            : _google.Respond(uri, body);

        Assert.Equal(1, (await LearnJobAsync()).GetProperty("failed").GetInt32());

        Assert.Empty(_learnRequests);
        Assert.Equal("results_ready", await StageAsync(room));
        await using var scope = await ScopeAsync();
        var learned = await scope.ServiceProvider.GetRequiredService<BrandMemoryDbContext>().LearnRuns.SingleAsync(Token);
        Assert.Equal((LearnOutcomes.Failed, 1), (learned.Outcome, learned.Attempts));
    }

    private async Task<JsonElement> LearnJobAsync()
    {
        using var client = _world.Worker.CreateClient();
        using var response = await client.PostAsync("/jobs/brand-memory/learn", null, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private async Task FinishAsync(Guid room, CampaignTest test)
    {
        var end = test.End!.Value;
        var days = end.DayNumber - test.Start!.Value.DayNumber + 1;
        _google.Record("search", test.Start.Value, days, 1400, 35);
        _google.Record("shopping", test.Start.Value, days, 1400, 35);
        await using (var scope = await ScopeAsync())
        {
            var resources = JsonSerializer.Deserialize<Dictionary<string, string>>(
                (await scope.ServiceProvider.GetRequiredService<LaunchDbContext>().Launches.SingleAsync(row => row.RoomId == room, Token)).Resources)!;
            var ads = resources.Where(pair => pair.Key.StartsWith("ad:", StringComparison.Ordinal)).ToList();
            _adKeys = [.. ads.Select(ad => ad.Key)];
            foreach (var (ad, index) in ads.Select((ad, index) => (ad, index)))
                _google.AdMetrics[ad.Value] = (100 + index * 2, 500m + index, 10m + index);
        }

        await AdvanceToAsync(end.AddDays(1));
        await RunJobAsync("/jobs/campaigns/test-ends");
        await CoverAsync(end);
        await RunJobAsync("/jobs/measurement/lift");
    }

    private async Task<(Guid Room, CampaignTest Test)> LiveAsync()
    {
        var room = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(room);
        await using (var scope = _world.Api.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LaunchDispatcher>().RunDueAsync(Token);
        Assert.Equal("live", await StageAsync(room));
        await using var testScope = await ScopeAsync();
        return (room, (await testScope.ServiceProvider.GetRequiredService<ICampaignTests>().GetAsync(room, Token))!);
    }

    private async Task<string> StageAsync(Guid room)
    {
        await using var scope = await ScopeAsync();
        return (await scope.ServiceProvider.GetRequiredService<ICampaignTests>().GetAsync(room, Token))!.Stage;
    }

    private async Task CoverAsync(DateOnly through)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<MeasurementDbContext>();
        db.FeedRuns.RemoveRange(await db.FeedRuns.ToListAsync(Token));
        db.FeedRuns.Add(new FeedRun
        {
            Id = Guid.NewGuid(), Feed = FeedNames.SalesTable, Status = RunStatuses.Succeeded, FromDate = through.AddDays(-7), ToDate = through,
            StartedAt = _world.Api.Clock.GetUtcNow(), FinishedAt = _world.Api.Clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(Token);
    }

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
        return scope;
    }

    private async Task AdvanceToAsync(DateOnly day)
    {
        var target = new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(5.5));
        while (_world.Api.Clock.GetUtcNow() < target)
        {
            var next = _world.Api.Clock.GetUtcNow() + TimeSpan.FromDays(1);
            var step = next < target ? next : target;
            _world.Api.Clock.SetUtcNow(step);
            if (_world.Worker.Clock.GetUtcNow() < step) _world.Worker.Clock.SetUtcNow(step);
            foreach (var browser in new[] { _world.Marketer, _world.Admin, _world.Approver.Browser })
                await browser.GetAsync($"/api/brands/{_world.BrandId}", Token);
        }
    }

    private async Task RunJobAsync(string path)
    {
        using var client = _world.Worker.CreateClient();
        using var response = await client.PostAsync(path, null, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
