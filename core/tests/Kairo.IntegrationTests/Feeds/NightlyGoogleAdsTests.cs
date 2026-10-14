using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Measurement.Feeds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kairo.IntegrationTests.Feeds;

public sealed class NightlyGoogleAdsTests(SqlServer sqlServer) : IAsyncLifetime
{
    private const int FirstWindowDays = 395;

    private static readonly DateTimeOffset Night = new(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);

    private static readonly string[] AdsTables = ["ads_campaign_daily", "ads_ad_group_daily", "ads_ad_daily", "ads_region_daily"];

    private string _database = "";
    private KairoApiFactory _api = null!;
    private KairoWorkerFactory _worker = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FakeGoogleApis Google => _api.GoogleApis;

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _worker = new KairoWorkerFactory(_database, _api.GoogleApis, Night);
    }

    [Fact]
    public async Task A_nightly_run_loads_the_four_Google_Ads_tables()
    {
        var brandId = await BrandWithAdsAsync();

        var run = await RunAsync();

        Assert.Equal(1, run.GetProperty("brands").GetInt32());
        var outcome = Outcome(run, brandId);
        Assert.Equal("succeeded", outcome.GetProperty("status").GetString());
        Assert.Equal(FirstWindowDays * AdsTables.Length, outcome.GetProperty("rows").GetInt32());
        var dataset = AnalyticsOptions.DatasetFor(brandId);
        foreach (var table in AdsTables)
        {
            Assert.Equal(FirstWindowDays, Google.BigQuery.Rows(dataset, table).Count);
        }

        var campaign = Day(dataset, "ads_campaign_daily", "2026-10-13");
        Assert.Equal("1111111111", campaign["customer_id"]!.GetValue<string>());
        Assert.Equal("9001", campaign["campaign_id"]!.GetValue<string>());
        Assert.Equal("SEARCH", campaign["channel"]!.GetValue<string>());
        Assert.Equal("INR", campaign["currency"]!.GetValue<string>());
        Assert.Equal("2.5", campaign["cost"]!.GetValue<string>());
        Assert.Equal(1000, campaign["impressions"]!.GetValue<long>());
        Assert.Equal("Running shoes", Day(dataset, "ads_ad_group_daily", "2026-10-13")["ad_group_name"]!.GetValue<string>());
        Assert.Equal("RESPONSIVE_SEARCH_AD", Day(dataset, "ads_ad_daily", "2026-10-13")["ad_type"]!.GetValue<string>());
        var region = Day(dataset, "ads_region_daily", "2026-10-13");
        Assert.Equal("21342", region["region_id"]!.GetValue<string>());
        Assert.Equal("Maharashtra", region["region_name"]!.GetValue<string>());
        Assert.Equal("IN", region["country_code"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_first_run_replaces_one_month_at_a_time()
    {
        var brandId = await BrandWithAdsAsync();

        await RunAsync();

        var target = $"`{FakeBigQuery.KairoProject}.{AnalyticsOptions.DatasetFor(brandId)}.ads_campaign_daily`";
        var replaces = Google.BigQuery.Queries.Where(query => query.Sql.Contains($"DELETE FROM {target}", StringComparison.Ordinal)).ToList();
        Assert.Equal(14, replaces.Count);
        Assert.All(replaces, replace => Assert.InRange(replace.Parameters[0].GetProperty("parameterValue").GetProperty("arrayValues").GetArrayLength(), 1, 31));
        Assert.Equal(14, CampaignQueries().Count);
        Assert.Contains("segments.date BETWEEN '2025-09-14' AND '2025-09-30'", CampaignQueries()[0]);
    }

    [Fact]
    public async Task A_first_run_that_fails_part_way_keeps_the_months_it_loaded_and_starts_over_next_time()
    {
        var brandId = await BrandWithAdsAsync();
        Google.AdsFailFrom = "2026-03-01";

        var failed = await RunAsync();

        Assert.Equal("failed", Outcome(failed, brandId).GetProperty("status").GetString());
        var dataset = AnalyticsOptions.DatasetFor(brandId);
        Assert.Equal(168, Google.BigQuery.Rows(dataset, "ads_campaign_daily").Count);

        Google.AdsFailFrom = null;
        var retried = await RunAsync();

        Assert.Equal("succeeded", Outcome(retried, brandId).GetProperty("status").GetString());
        Assert.Equal(FirstWindowDays, Google.BigQuery.Rows(dataset, "ads_campaign_daily").Count);
        Assert.Equal(2, CampaignQueries().Count(query => query.Contains("segments.date BETWEEN '2025-09-14' AND '2025-09-30'", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_dataset_and_tables_are_created_once()
    {
        var brandId = await BrandWithAdsAsync();

        await RunAsync();
        await RunAsync();

        var dataset = AnalyticsOptions.DatasetFor(brandId);
        Assert.Equal(1, Google.BigQuery.Count($"datasets.insert {dataset}"));
        foreach (var table in AdsTables)
        {
            Assert.Equal(1, Google.BigQuery.Count($"tables.insert {dataset}.{table}"));
        }
    }

    [Fact]
    public async Task The_first_run_reaches_back_13_months_and_later_runs_cover_7_days()
    {
        var brandId = await BrandWithAdsAsync();

        await RunAsync();
        var first = CampaignQueries();
        await RunAsync();
        var second = CampaignQueries().Skip(first.Count).ToList();

        Assert.Contains("segments.date BETWEEN '2025-09-14' AND '2025-09-30'", first[0]);
        Assert.Contains("segments.date BETWEEN '2026-10-01' AND '2026-10-13'", first[^1]);
        Assert.Contains("segments.date BETWEEN '2026-10-07' AND '2026-10-13'", Assert.Single(second));
        var windows = (await FeedRunRows.ListAsync(_database, brandId))
            .Where(stored => stored.Feed == FeedNames.GoogleAds)
            .Select(stored => (stored.FromDate, stored.ToDate));
        Assert.Equal(
            new (DateOnly?, DateOnly?)[]
            {
                (new DateOnly(2025, 9, 14), new DateOnly(2026, 10, 13)),
                (new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 13)),
            },
            windows);
    }

    [Fact]
    public async Task The_window_follows_the_accounts_time_zone()
    {
        Google.AdsTimeZone = "America/Los_Angeles";
        await BrandWithAdsAsync();

        await RunAsync();

        Assert.Contains("segments.date BETWEEN '2025-09-13' AND '2025-09-30'", CampaignQueries()[0]);
        Assert.Contains("segments.date BETWEEN '2026-10-01' AND '2026-10-12'", CampaignQueries()[^1]);
    }

    [Fact]
    public async Task A_client_reached_through_a_manager_is_read_through_the_manager()
    {
        var brandId = await BrandWithAdsAsync("4444444444");

        var run = await RunAsync();

        Assert.Equal("succeeded", Outcome(run, brandId).GetProperty("status").GetString());
        var reports = Google.AdsReports.Where(report => report.Customer == "4444444444").ToList();
        Assert.Contains(reports, report => report.Query.Contains("FROM geo_target_constant", StringComparison.Ordinal));
        Assert.All(reports, report => Assert.Equal("2222222222", report.Login));
    }

    [Fact]
    public async Task A_brand_without_Google_Ads_is_recorded_as_skipped()
    {
        var (_, brandId) = await WorkspaceAsync();

        var run = await RunAsync();

        Assert.Equal("skipped", Outcome(run, brandId).GetProperty("status").GetString());
        var stored = Assert.Single(await FeedRunRows.ListAsync(_database, brandId), stored => stored.Feed == FeedNames.GoogleAds);
        Assert.Equal("not_connected", stored.Reason);
        Assert.Empty(Google.AdsReports);
    }

    [Fact]
    public async Task A_rejected_token_is_recorded_as_needing_a_reconnect()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var issued = await Connections.ConnectAsync(_api, admin.Browser, brandId, "google-ads");
        Google.Reject(issued.RefreshToken);

        await RunAsync();
        await RunAsync();

        var stored = (await FeedRunRows.ListAsync(_database, brandId)).Where(stored => stored.Feed == FeedNames.GoogleAds).ToList();
        Assert.Equal(new[] { "skipped", "skipped" }, stored.Select(run => run.Status));
        Assert.Equal(new[] { "needs_reconnect", "needs_reconnect" }, stored.Select(run => run.Reason));
        Assert.Equal("needsReconnect", (await Connections.GetAsync(admin.Browser, brandId, "google-ads")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_feed_that_is_already_running_is_skipped()
    {
        var brandId = await BrandWithAdsAsync();
        await FeedRunRows.InsertAsync(_database, brandId, FeedNames.GoogleAds, "running", Night.AddMinutes(-30));

        var run = await RunAsync();

        Assert.Equal("skipped", Outcome(run, brandId).GetProperty("status").GetString());
        Assert.Contains(await FeedRunRows.ListAsync(_database, brandId), stored => stored.Reason == "already_running");
        Assert.Empty(CampaignQueries());
    }

    [Fact]
    public async Task A_running_record_older_than_2_hours_does_not_block_the_feed()
    {
        var brandId = await BrandWithAdsAsync();
        await FeedRunRows.InsertAsync(_database, brandId, FeedNames.GoogleAds, "running", Night.AddHours(-3));

        var run = await RunAsync();

        Assert.Equal("succeeded", Outcome(run, brandId).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_run_for_one_brand_touches_only_that_brand()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var first = await BrandWithAdsAsync(admin: admin);
        var second = await BrandWithAdsAsync(admin: admin);

        var run = await RunAsync(first);

        Assert.Equal(1, run.GetProperty("brands").GetInt32());
        Assert.All(run.GetProperty("runs").EnumerateArray(), outcome => Assert.Equal(first, outcome.GetProperty("brandId").GetGuid()));
        Assert.Empty(Google.BigQuery.TableNames(AnalyticsOptions.DatasetFor(second)));
    }

    [Fact]
    public async Task An_unknown_brand_is_not_found()
    {
        using var response = await _worker.RunDailyAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task One_failing_brand_still_lets_the_others_finish()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var healthy = await BrandWithAdsAsync(admin: admin);
        var broken = await BrandWithAdsAsync("3333333333", admin);
        Google.AdsFailures.Add("3333333333");

        var run = await RunAsync();

        Assert.Equal("succeeded", Outcome(run, healthy).GetProperty("status").GetString());
        Assert.Equal("failed", Outcome(run, broken).GetProperty("status").GetString());
        var failed = Assert.Single(await FeedRunRows.ListAsync(_database, broken), stored => stored.Feed == FeedNames.GoogleAds);
        Assert.Equal("Google answered 500: Internal error encountered.", failed.Error);
        Assert.Equal(FirstWindowDays, Google.BigQuery.Rows(AnalyticsOptions.DatasetFor(healthy), "ads_campaign_daily").Count);
    }

    [Fact]
    public async Task Run_records_older_than_90_days_are_deleted()
    {
        var (_, brandId) = await WorkspaceAsync();
        await FeedRunRows.InsertAsync(_database, brandId, FeedNames.SalesTable, "succeeded", Night.AddDays(-91));
        await FeedRunRows.InsertAsync(_database, brandId, FeedNames.SalesTable, "succeeded", Night.AddDays(-89));

        await RunAsync();

        var old = (await FeedRunRows.ListAsync(_database, brandId)).Where(stored => stored.StartedAt < Night.AddDays(-1));
        Assert.Equal(new[] { Night.AddDays(-89) }, old.Select(stored => stored.StartedAt));
    }

    [Fact]
    public async Task An_interrupted_run_is_recorded_as_failed_rather_than_left_running()
    {
        var brandId = await BrandWithAdsAsync();
        Google.BeforeAdsReport = () => _worker.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

        try
        {
            using var response = await _worker.RunDailyAsync();
        }
        catch (Exception)
        {
        }

        var stored = Assert.Single(await FeedRunRows.ListAsync(_database, brandId), stored => stored.Feed == FeedNames.GoogleAds);
        Assert.Equal("failed", stored.Status);
        Assert.Equal("The run was interrupted before it finished.", stored.Error);
    }

    [Fact]
    public async Task Without_an_analytics_project_the_job_is_unavailable()
    {
        await using var worker = new KairoWorkerFactory(_database, Google, Night) { AnalyticsProject = null };

        using var response = await worker.RunDailyAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private async Task<JsonElement> RunAsync(Guid? brandId = null)
    {
        _worker.Clock.Advance(TimeSpan.FromSeconds(1));
        using var response = await _worker.RunDailyAsync(brandId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync(Member? admin = null)
    {
        admin ??= await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }

    private async Task<Guid> BrandWithAdsAsync(string account = "1111111111", Member? admin = null)
    {
        var (owner, brandId) = await WorkspaceAsync(admin);
        await Connections.CompleteAsync(_api, owner.Browser, await Connections.StartAsync(owner.Browser, brandId, "google-ads"));
        (await owner.Browser.PostAsJsonAsync($"/api/brands/{brandId}/connections/google-ads/account", new { externalId = account }, Token))
            .EnsureSuccessStatusCode();
        return brandId;
    }

    private List<string> CampaignQueries() => Google.AdsReports
        .Select(report => report.Query)
        .Where(query => query.Contains(" FROM campaign WHERE ", StringComparison.Ordinal))
        .ToList();

    private JsonObject Day(string dataset, string table, string date) =>
        Google.BigQuery.Rows(dataset, table).Single(row => row["date"]!.GetValue<string>() == date);

    private static JsonElement Outcome(JsonElement run, Guid brandId, string feed = FeedNames.GoogleAds) =>
        run.GetProperty("runs").EnumerateArray()
            .Single(outcome => outcome.GetProperty("brandId").GetGuid() == brandId && outcome.GetProperty("feed").GetString() == feed);
}
