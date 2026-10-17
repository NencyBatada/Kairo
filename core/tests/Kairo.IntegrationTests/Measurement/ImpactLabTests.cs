using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kairo.Contracts.BrandMemory;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Feeds;
using Kairo.IntegrationTests.Live;
using Kairo.IntegrationTests.Notifications;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Feeds;
using Kairo.Modules.Measurement.Lab;
using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Measurement;

public sealed class ImpactLabTests(SqlServer sqlServer) : IAsyncLifetime
{
    private const int History = 90;
    private const int ChangeDays = 21;

    private readonly FakeLab _lab = new();
    private string _database = "";
    private KairoApiFactory _api = null!;
    private Member _admin = null!;
    private Guid _brandId;
    private string _handle = "";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private DateOnly Today => FeedClock.Today(_api.Clock, FeedClock.IndiaTimeZone);

    private DateOnly Start => Today.AddDays(-ChangeDays - 7);

    private DateOnly End => Start.AddDays(ChangeDays - 1);

    private string Base => $"/api/brands/{_brandId}/impact-lab/analyses";

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _lab.Attach(_api.Agents);
        _admin = await Workspaces.SignInAsync(_api);
        _handle = Workspaces.NewHandle();
        _brandId = await Workspaces.CreateAsync(_admin.Browser, _handle);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task A_file_analysis_goes_from_setup_to_a_result_and_tells_the_person_who_ran_it()
    {
        var created = await CreateAsync(FileSource());
        Assert.Equal(("setup", "file", "units sold", "series"), (Text(created, "state"), Text(created.GetProperty("source"), "kind"),
            Text(created.GetProperty("source"), "metric"), Text(created.GetProperty("source"), "grain")));
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(new[] { "Upload a CSV file first." }, await DataErrorsAsync(await PostAsync($"{Base}/{id}/load", Version(created))));

        var uploaded = await UploadAsync(id, created);
        Assert.Equal(("setup", "units.csv"), (Text(uploaded, "state"), Text(uploaded.GetProperty("source"), "fileName")));

        var loaded = await LoadAsync(id, uploaded);
        Assert.Equal("data_check", Text(loaded, "state"));
        var data = loaded.GetProperty("data");
        Assert.Equal((Start.AddDays(-History), End, History, ChangeDays, ChangeDays),
            (Date(data, "from"), Date(data, "to"), data.GetProperty("historyDays").GetInt32(), data.GetProperty("changeDays").GetInt32(),
                data.GetProperty("plannedDays").GetInt32()));
        Assert.Equal(new[] { "Running", "Sandals", "Walking" }, data.GetProperty("series").EnumerateArray().Select(series => Text(series, "name")).ToArray());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("currency").ValueKind);

        var checkedView = await SelectAsync(id, loaded, ["running"], null);
        Assert.Equal(new[] { "running" }, Strings(checkedView, "affected"));
        Assert.Equal(new[] { "sandals", "walking" }, Strings(checkedView, "comparison"));
        Assert.Equal(0.85, checkedView.GetProperty("fit").GetDouble());
        var candidate = checkedView.GetProperty("candidates")[0];
        Assert.Equal(("sandals", "Sandals", true), (Text(candidate, "key"), Text(candidate, "name"), candidate.GetProperty("suggested").GetBoolean()));
        var check = Assert.Single(_lab.Checks);
        Assert.Equal((History + ChangeDays, History, ChangeDays, ChangeDays), (check.GetProperty("days").GetInt32(), check.GetProperty("start").GetInt32(),
            check.GetProperty("changeDays").GetInt32(), check.GetProperty("plannedDays").GetInt32()));
        Assert.Equal(3, check.GetProperty("series").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, check.GetProperty("comparison").ValueKind);

        using var running = await PostAsync($"{Base}/{id}/run", Version(checkedView));
        Assert.Equal(HttpStatusCode.Accepted, running.StatusCode);
        Assert.Equal("running", Text(await running.Content.ReadFromJsonAsync<JsonElement>(Token), "state"));
        Assert.Equal(1, await DispatchAsync());

        var result = await GetAsync(id);
        Assert.Equal("result", Text(result, "state"));
        var answer = result.GetProperty("result");
        Assert.Equal(("ok", "rose", true), (Text(answer, "status"), Text(answer, "outcome"), answer.GetProperty("conclusive").GetBoolean()));
        Assert.Equal(0.18, answer.GetProperty("figures").GetProperty("lift").GetDouble());
        Assert.Equal("Running shoe price cut changed the outcome.", Text(answer, "summary"));
        Assert.Equal(("placebo-did/1", "gemini-pro-test", "prove-lab/1"), (Text(answer, "method"), Text(answer, "model"), Text(answer, "promptVersion")));
        var days = answer.GetProperty("daily").EnumerateArray().ToList();
        Assert.Equal((Start.AddDays(-1), false, Start, true), (Date(days[0], "date"), days[0].GetProperty("during").GetBoolean(), Date(days[1], "date"),
            days[1].GetProperty("during").GetBoolean()));

        var run = Assert.Single(_lab.Runs);
        Assert.Equal(("units sold", "file", "", "Running shoe price cut"), (Text(run, "metric"), Text(run, "source"), Text(run, "currency"), Text(run, "name")));
        Assert.Equal(new[] { "running", "sandals", "walking" }, run.GetProperty("series").EnumerateArray().Select(series => Text(series, "key")).Order().ToArray());

        var notice = Assert.Single(await NotificationRows.NotificationsAsync(_database, _brandId), row => row.Kind == "impact_lab.ready");
        Assert.Equal((_admin.UserId, $"/{_handle}/impact-lab/{id}", "Impact Lab result ready for Running shoe price cut"), (notice.UserId, notice.Link, notice.Title));
        var actions = await Workspaces.AuditActionsAsync(_admin.Browser, _brandId);
        Assert.Contains("impact_lab.analysis_created", actions);
        Assert.Contains("impact_lab.data_loaded", actions);
        Assert.Contains("impact_lab.result_ready", actions);

        var list = await _admin.Browser.GetFromJsonAsync<JsonElement>(Base, Token);
        var summary = Assert.Single(list.GetProperty("analyses").EnumerateArray());
        Assert.Equal(("result", "rose", 0.18), (Text(summary, "state"), Text(summary, "outcome"), summary.GetProperty("lift").GetDouble()));
    }

    [Fact]
    public async Task Region_data_comes_from_the_nightly_tables_once_a_feed_has_run()
    {
        var created = await CreateAsync(new { kind = "google_ads", metric = "conversions", grain = "state" }, "regions");
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(new[] { "Kairo hasn't loaded any data from this source yet." }, await DataErrorsAsync(await PostAsync($"{Base}/{id}/load", Version(created))));

        await FeedRunRows.InsertAsync(_database, _brandId, FeedNames.GoogleAds, "succeeded", _api.Clock.GetUtcNow(), Today.AddDays(-1));
        _api.GoogleApis.BigQuery.Selects["ads_region_daily"] = [.. Days(Start.AddDays(-History), End).SelectMany(day => new[]
        {
            new[] { Iso(day), "Maharashtra", "Maharashtra", "5.5", "INR" },
            new[] { Iso(day), "Karnataka", "Karnataka", "3", "INR" },
            new[] { Iso(day), "(not set)", "(not set)", "9", "INR" },
        })];

        var loaded = await LoadAsync(id, created);

        var series = loaded.GetProperty("data").GetProperty("series").EnumerateArray().ToList();
        Assert.Equal(new[] { "karnataka", "maharashtra" }, series.Select(item => Text(item, "key")).Order().ToArray());
        Assert.Equal(5.5 * (History + ChangeDays), series.Single(item => Text(item, "key") == "maharashtra").GetProperty("total").GetDouble(), 6);
        var query = _api.GoogleApis.BigQuery.Queries.Last(entry => entry.Sql.Contains("ads_region_daily", StringComparison.Ordinal));
        Assert.Contains($"FROM `{FakeBigQuery.KairoProject}.{AnalyticsOptions.DatasetFor(_brandId)}.ads_region_daily`", query.Sql);
        Assert.Contains("SUM(conversions)", query.Sql);
    }

    [Fact]
    public async Task Ga4_item_categories_are_read_live_from_the_data_api()
    {
        await Connections.ConnectAsync(_api, _admin.Browser, _brandId, "ga4");
        foreach (var day in Days(Start.AddDays(-History), End))
        {
            var date = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            _api.GoogleApis.Ga4ItemRows.Add((date, "Running shoes", "12"));
            _api.GoogleApis.Ga4ItemRows.Add((date, "Walking shoes", "8"));
            _api.GoogleApis.Ga4ItemRows.Add((date, "(not set)", "3"));
        }

        var created = await CreateAsync(new { kind = "ga4", metric = "item_revenue", grain = "category" });
        var loaded = await LoadAsync(created.GetProperty("id").GetGuid(), created);

        var data = loaded.GetProperty("data");
        Assert.Equal(new[] { "running shoes", "walking shoes" }, data.GetProperty("series").EnumerateArray().Select(item => Text(item, "key")).ToArray());
        Assert.Equal("INR", Text(data, "currency"));
        var request = _api.GoogleApis.Ga4Requests.Last();
        Assert.Equal(new[] { "date", "itemCategory" }, request.GetProperty("dimensions").EnumerateArray().Select(item => Text(item, "name")).ToArray());
        Assert.Equal("itemRevenue", Text(request.GetProperty("metrics")[0], "name"));
        Assert.Equal(Iso(Start.AddDays(-LabWork.HistoryDays)), Text(request.GetProperty("dateRanges")[0], "startDate"));
        Assert.Equal(Iso(End), Text(request.GetProperty("dateRanges")[0], "endDate"));
    }

    [Fact]
    public async Task Google_Ads_products_are_read_live_from_the_shopping_report()
    {
        await Connections.ConnectAsync(_api, _admin.Browser, _brandId, "google-ads");
        foreach (var day in Days(Start.AddDays(-History), End))
        {
            _api.GoogleApis.AdsProductRows.Add(new
            {
                segments = new { date = Iso(day), productItemId = "SKU-1", productTitle = "Trail runner" },
                metrics = new { clicks = "40", conversions = 2.0, conversionsValue = 3000.5 },
            });
            _api.GoogleApis.AdsProductRows.Add(new
            {
                segments = new { date = Iso(day), productItemId = "SKU-2", productTitle = "Road runner" },
                metrics = new { clicks = "20", conversions = 1.0, conversionsValue = 1500.0 },
            });
        }

        var created = await CreateAsync(new { kind = "google_ads", metric = "conversion_value", grain = "product" });
        var loaded = await LoadAsync(created.GetProperty("id").GetGuid(), created);

        var data = loaded.GetProperty("data");
        Assert.Equal(new[] { ("sku-2", "Road runner"), ("sku-1", "Trail runner") },
            data.GetProperty("series").EnumerateArray().Select(item => (Text(item, "key"), Text(item, "name"))).ToArray());
        Assert.Equal(3000.5 * (History + ChangeDays), data.GetProperty("series")[1].GetProperty("total").GetDouble(), 4);
        Assert.Equal("INR", Text(data, "currency"));
        Assert.Contains(_api.GoogleApis.AdsReports, report => report.Query.Contains("FROM shopping_performance_view", StringComparison.Ordinal)
            && report.Query.Contains("segments.product_item_id, segments.product_title", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_brand_table_is_summed_by_day_and_series_with_checked_columns()
    {
        await Connections.ConnectAsync(_api, _admin.Browser, _brandId, "bigquery");
        foreach (var day in Days(Start.AddDays(-History), End))
        {
            _api.GoogleApis.BigQuery.LabTableRows.Add((Iso(day), "Running", "14"));
            _api.GoogleApis.BigQuery.LabTableRows.Add((Iso(day), "Walking", "6"));
        }

        var created = await CreateAsync(TableSource("units"));
        var loaded = await LoadAsync(created.GetProperty("id").GetGuid(), created);

        Assert.Equal(new[] { "running", "walking" }, loaded.GetProperty("data").GetProperty("series").EnumerateArray().Select(item => Text(item, "key")).ToArray());
        var query = _api.GoogleApis.BigQuery.Queries.Last(entry => entry.Project == FakeBigQuery.BrandProject);
        Assert.Contains("FROM `acme-data.shop.daily_units`", query.Sql);
        Assert.Contains("CAST(`product_line` AS STRING) AS series, CAST(SUM(`units`) AS FLOAT64) AS value", query.Sql);
        Assert.Contains("WHERE DATE(`sold_on`) BETWEEN @from AND @to", query.Sql);
        Assert.StartsWith("access-", query.AccessToken);

        var wrong = await CreateAsync(TableSource("tags"));
        Assert.Equal(new[] { "tags is a STRING column, but the value needs INTEGER, INT64, NUMERIC, BIGNUMERIC, FLOAT, FLOAT64." },
            await DataErrorsAsync(await PostAsync($"{Base}/{wrong.GetProperty("id").GetGuid()}/load", Version(wrong))));
    }

    [Fact]
    public async Task Loading_without_the_connection_asks_for_it()
    {
        var created = await CreateAsync(new { kind = "ga4", metric = "items_purchased", grain = "product" });

        using var response = await PostAsync($"{Base}/{created.GetProperty("id").GetGuid()}/load", Version(created));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Connect Google Analytics 4 before loading this data.", Text(await response.Content.ReadFromJsonAsync<JsonElement>(Token), "title"));
    }

    [Theory]
    [InlineData("\"source\":{\"kind\":\"tv\",\"metric\":\"x\"}", "source.kind")]
    [InlineData("\"source\":{\"kind\":\"ga4\",\"metric\":\"transactions\",\"grain\":\"campaign\"}", "source.grain")]
    [InlineData("\"source\":{\"kind\":\"ga4\",\"metric\":\"clicks\",\"grain\":\"state\"}", "source.metric")]
    [InlineData("\"source\":{\"kind\":\"bigquery\",\"metric\":\"units\",\"table\":\"shop;drop\",\"dateColumn\":\"a\",\"valueColumn\":\"b\",\"seriesColumn\":\"c\"}", "source.table")]
    [InlineData("\"source\":{\"kind\":\"bigquery\",\"metric\":\"units\",\"table\":\"acme-data.shop.daily_units\",\"dateColumn\":\"a`b\",\"valueColumn\":\"b\",\"seriesColumn\":\"c\"}", "source.dateColumn")]
    public async Task Bad_sources_are_refused(string source, string field)
    {
        var body = $"{{\"name\":\"Change\",\"whatChanged\":\"Something\",\"start\":\"{Iso(Start)}\",\"end\":\"{Iso(End)}\",\"affectsKind\":\"products\",{source}}}";

        using var response = await _admin.Browser.PostAsync(Base, new StringContent(body, Encoding.UTF8, "application/json"), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Change_dates_must_make_sense()
    {
        var errors = async (DateOnly start, DateOnly end) =>
        {
            using var response = await _admin.Browser.PostAsJsonAsync(Base, Setup(FileSource(), "products", start, end), Token);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
        };

        Assert.True((await errors(Today, Today)).TryGetProperty("start", out _));
        Assert.True((await errors(Start, Start.AddDays(-1))).TryGetProperty("end", out _));
        Assert.True((await errors(Today.AddDays(-200), Today.AddDays(-10))).TryGetProperty("end", out _));
    }

    [Fact]
    public async Task Marketers_can_run_analyses_and_approvers_can_only_read_them()
    {
        var marketer = await Workspaces.JoinAsync(_api, _admin.Browser, _brandId, "marketer");
        var approver = await Workspaces.JoinAsync(_api, _admin.Browser, _brandId, "approver");

        using var made = await marketer.Browser.PostAsJsonAsync(Base, Setup(FileSource()), Token);
        Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        var created = await made.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(marketer.UserId, created.GetProperty("createdBy").GetGuid());
        using var refused = await approver.Browser.PostAsJsonAsync(Base, Setup(FileSource()), Token);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var load = await approver.Browser.PostAsJsonAsync($"{Base}/{created.GetProperty("id").GetGuid()}/load", Version(created), Token);
        Assert.Equal(HttpStatusCode.Forbidden, load.StatusCode);
        Assert.Single((await approver.Browser.GetFromJsonAsync<JsonElement>(Base, Token)).GetProperty("analyses").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await approver.Browser.GetAsync($"{Base}/{created.GetProperty("id").GetGuid()}", Token)).StatusCode);
    }

    [Fact]
    public async Task Data_check_warnings_are_kept_and_explained()
    {
        _lab.Warnings = [new { code = "short_history", value = 30.0 }, new { code = "noisy", value = 0.27 }, new { code = "weak_comparison", value = 0.55 }];
        var (id, loaded) = await LoadedFileAsync();

        var checkedView = await SelectAsync(id, loaded, ["running"], ["walking"]);

        Assert.Equal(new[] { "walking" }, Strings(checkedView, "comparison"));
        Assert.Equal(new[] { "walking" }, Strings(Assert.Single(_lab.Checks), "comparison"));
        var warnings = checkedView.GetProperty("warnings").EnumerateArray().ToDictionary(warning => Text(warning, "code"), warning => Text(warning, "message"));
        Assert.Equal("There are only 30 days of data before the change. Kairo needs at least 56 days (8 weeks) for a reliable answer.", warnings["short_history"]);
        Assert.Equal("The data is noisy, so only changes bigger than about 27% can be measured reliably.", warnings["noisy"]);
        Assert.Equal("The comparison only loosely tracked the outcome before the change (fit 0.55; aim for 0.70 or more).", warnings["weak_comparison"]);
        Assert.Equal(3, (await GetAsync(id)).GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public async Task Picks_must_come_from_the_loaded_data()
    {
        var (id, loaded) = await LoadedFileAsync();

        foreach (var (affected, comparison) in new (string[], string[]?)[] { ([], null), (["nope"], null), (["running"], ["running"]), (["running"], ["nope"]) })
        {
            using var response = await _admin.Browser.PutAsJsonAsync($"{Base}/{id}/selection", new { version = VersionOf(loaded), affected, comparison }, Token);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Empty(_lab.Checks);
        using var early = await PostAsync($"{Base}/{id}/run", Version(loaded));
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
    }

    [Fact]
    public async Task A_failing_run_is_retried_then_marked_failed_and_can_run_again()
    {
        var (id, ready) = await ReadyToRunAsync();
        _lab.RunFailures = 3;
        await PostAsync($"{Base}/{id}/run", Version(ready));

        Assert.Equal(1, await DispatchAsync());
        Assert.Equal("running", Text(await GetAsync(id), "state"));
        Assert.Equal(0, await DispatchAsync());
        _api.Clock.Advance(LabRunner.Backoff + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await DispatchAsync());
        _api.Clock.Advance(LabRunner.Backoff + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await DispatchAsync());

        var failed = await GetAsync(id);
        Assert.Equal("failed", Text(failed, "state"));
        Assert.StartsWith(LabRunner.FailedMessage, Text(failed, "problem"));
        Assert.Equal(3, _lab.Runs.Count);
        var notice = Assert.Single(await NotificationRows.NotificationsAsync(_database, _brandId), row => row.Kind == "impact_lab.ready");
        Assert.Equal("Impact Lab couldn't finish Running shoe price cut", notice.Title);
        Assert.Contains("impact_lab.analysis_failed", await Workspaces.AuditActionsAsync(_admin.Browser, _brandId));

        using var again = await PostAsync($"{Base}/{id}/run", Version(failed));
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(1, await DispatchAsync());
        Assert.Equal("result", Text(await GetAsync(id), "state"));
    }

    [Fact]
    public async Task Only_the_person_who_ran_it_is_told_the_result()
    {
        var marketer = await Workspaces.JoinAsync(_api, _admin.Browser, _brandId, "marketer");
        var (id, ready) = await ReadyToRunAsync();

        using var run = await marketer.Browser.PostAsJsonAsync($"{Base}/{id}/run", Version(ready), Token);
        Assert.Equal(HttpStatusCode.Accepted, run.StatusCode);
        await DispatchAsync();

        var notice = Assert.Single(await NotificationRows.NotificationsAsync(_database, _brandId), row => row.Kind == "impact_lab.ready");
        Assert.Equal(marketer.UserId, notice.UserId);
        Assert.Equal(marketer.UserId, (await GetAsync(id)).GetProperty("runBy").GetGuid());
    }

    [Fact]
    public async Task A_conclusive_result_can_be_saved_to_Brand_Memory_once()
    {
        var (id, ready) = await ReadyToRunAsync();
        await PostAsync($"{Base}/{id}/run", Version(ready));
        await DispatchAsync();
        var result = await GetAsync(id);

        using var saved = await PostAsync($"{Base}/{id}/memory", Version(result));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var view = await saved.Content.ReadFromJsonAsync<JsonElement>(Token);
        var entryId = view.GetProperty("memoryEntryId").GetGuid();

        var memory = await _admin.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{_brandId}/brand-memory?kind=change", Token);
        var entry = Assert.Single(memory.GetProperty("entries").EnumerateArray());
        Assert.Equal((entryId, "change", "worked", "Running shoe price cut", "Running shoe price cut changed the outcome.", "impact_lab"),
            (entry.GetProperty("id").GetGuid(), Text(entry, "kind"), Text(entry, "outcome"), Text(entry, "title"), Text(entry, "lesson"), Text(entry, "createdBy")));
        Assert.Equal(new[] { "Running" }, Strings(entry, "products"));
        Assert.Equal((id, 0.18), (entry.GetProperty("evidence").GetProperty("labAnalysisId").GetGuid(), entry.GetProperty("evidence").GetProperty("lift").GetDouble()));
        var actions = await Workspaces.AuditActionsAsync(_admin.Browser, _brandId);
        Assert.Contains("impact_lab.saved_to_memory", actions);
        Assert.Contains("brand_memory.entry_saved", actions);

        using var twice = await PostAsync($"{Base}/{id}/memory", Version(view));
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal(LabEndpoints.AlreadySaved, Text(await twice.Content.ReadFromJsonAsync<JsonElement>(Token), "title"));

        await PostAsync($"{Base}/{id}/run", Version(view));
        await DispatchAsync();
        var rerun = await GetAsync(id);
        Assert.Equal(entryId, rerun.GetProperty("memoryEntryId").GetGuid());
        using var again = await PostAsync($"{Base}/{id}/memory", Version(rerun));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Single((await _admin.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{_brandId}/brand-memory", Token)).GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task An_inconclusive_result_is_never_saved_as_a_lesson()
    {
        _lab.Conclusive = false;
        var (id, ready) = await ReadyToRunAsync();
        await PostAsync($"{Base}/{id}/run", Version(ready));
        await DispatchAsync();
        var result = await GetAsync(id);
        Assert.Equal(("inconclusive", false), (Text(result.GetProperty("result"), "outcome"), result.GetProperty("result").GetProperty("conclusive").GetBoolean()));
        Assert.Equal("Measure a longer change.", Text(result.GetProperty("result"), "nextAdvice"));

        using var saved = await PostAsync($"{Base}/{id}/memory", Version(result));

        Assert.Equal(HttpStatusCode.Conflict, saved.StatusCode);
        Assert.Equal(LabEndpoints.NotConclusive, Text(await saved.Content.ReadFromJsonAsync<JsonElement>(Token), "title"));
        Assert.Empty((await _admin.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{_brandId}/brand-memory", Token)).GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task Saved_changes_reach_rankings_and_briefs_but_not_copy()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_brandId, Token);
        var memory = scope.ServiceProvider.GetRequiredService<IBrandMemory>();
        var id = await memory.SaveAsync(
            new MemoryDraft(MemoryKinds.Change, MemoryOutcomes.Worked, "Price cut", "Sales rose.", "{}", [], ["Running shoes", "running shoes"], [], "impact_lab"), Token);

        var ranked = await memory.RelevantAsync(new MemoryQuery(Categories: ["Running shoes"]), Token);
        var copy = await memory.RelevantAsync(new MemoryQuery(Categories: ["Running shoes"], Kinds: [MemoryKinds.Message, MemoryKinds.Visual, MemoryKinds.Audience]), Token);

        var note = Assert.Single(ranked);
        Assert.Equal((id, "change"), (note.Id, note.Kind));
        Assert.Equal(new[] { "Running shoes" }, note.Categories);
        Assert.Empty(copy);
        await Assert.ThrowsAsync<ArgumentException>(() => memory.SaveAsync(
            new MemoryDraft("tv", MemoryOutcomes.Worked, "Price cut", "Sales rose.", "{}", [], [], [], "impact_lab"), Token));
    }

    [Fact]
    public async Task Stale_or_running_analyses_cannot_be_changed()
    {
        var (id, ready) = await ReadyToRunAsync();

        using var unversioned = await PostAsync($"{Base}/{id}/run", new { });
        Assert.Equal(HttpStatusCode.BadRequest, unversioned.StatusCode);
        Assert.Equal(LabEndpoints.NeedsVersion, Text(await unversioned.Content.ReadFromJsonAsync<JsonElement>(Token), "title"));

        using var stale = await _admin.Browser.PutAsJsonAsync($"{Base}/{id}", Setup(FileSource(), version: 1), Token);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(LabEndpoints.Changed, Text(await stale.Content.ReadFromJsonAsync<JsonElement>(Token), "title"));

        using var running = await PostAsync($"{Base}/{id}/run", Version(ready));
        var current = await running.Content.ReadFromJsonAsync<JsonElement>(Token);
        using var busy = await _admin.Browser.PutAsJsonAsync($"{Base}/{id}", Setup(FileSource(), version: VersionOf(current)), Token);
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        Assert.Equal(LabEndpoints.Busy, Text(await busy.Content.ReadFromJsonAsync<JsonElement>(Token), "title"));
    }

    [Fact]
    public async Task Changing_the_dates_clears_the_data_but_renaming_keeps_it()
    {
        var (id, ready) = await ReadyToRunAsync();

        using var renamed = await _admin.Browser.PutAsJsonAsync($"{Base}/{id}", Setup(FileSource(), version: VersionOf(ready), name: "Price cut"), Token);
        var kept = await renamed.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(("data_check", "Price cut", 1), (Text(kept, "state"), Text(kept, "name"), kept.GetProperty("affected").GetArrayLength()));

        using var moved = await _admin.Browser.PutAsJsonAsync($"{Base}/{id}", Setup(FileSource(), start: Start.AddDays(1), version: VersionOf(kept)), Token);
        var reset = await moved.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(("setup", JsonValueKind.Null, 0), (Text(reset, "state"), reset.GetProperty("data").ValueKind, reset.GetProperty("affected").GetArrayLength()));
        Assert.Equal("units.csv", Text(reset.GetProperty("source"), "fileName"));
        Assert.Equal("data_check", Text(await LoadAsync(id, reset), "state"));
    }

    [Fact]
    public async Task Bad_files_are_refused_with_their_row_errors()
    {
        var created = await CreateAsync(FileSource());

        using var response = await _admin.Browser.PostAsync(
            $"{Base}/{created.GetProperty("id").GetGuid()}/file?version={VersionOf(created)}",
            SalesFiles.Csv("date,series,value\n2026-13-01,Running,1\n2026-01-01,,2\n2026-01-02,Running,-1\n", "bad.csv"),
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            new[] { "row 2: date must be YYYY-MM-DD", "row 3: series is empty", "row 4: value must be a number, 0 or more" },
            (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors").GetProperty("file").EnumerateArray().Select(error => error.GetString()!).ToArray());
    }

    [Fact]
    public async Task Other_brands_cannot_see_an_analysis()
    {
        var created = await CreateAsync(FileSource());
        var outsider = await Workspaces.SignInAsync(_api);
        var otherBrand = await Workspaces.CreateAsync(outsider.Browser);

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Browser.GetAsync($"/api/brands/{otherBrand}/impact-lab/analyses/{created.GetProperty("id").GetGuid()}", Token)).StatusCode);
        Assert.Empty((await outsider.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{otherBrand}/impact-lab/analyses", Token)).GetProperty("analyses").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Browser.GetAsync($"{Base}/{created.GetProperty("id").GetGuid()}", Token)).StatusCode);
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(otherBrand, Token);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().LabAnalyses.ToListAsync(Token));
    }

    [Fact]
    public async Task The_worker_job_runs_waiting_analyses()
    {
        var (id, ready) = await ReadyToRunAsync();
        await PostAsync($"{Base}/{id}/run", Version(ready));
        await using var worker = new KairoWorkerFactory(_database, _api.GoogleApis, _api.Clock.GetUtcNow());
        _lab.Attach(worker.Agents);
        using var client = worker.CreateClient();

        using var response = await client.PostAsync("/jobs/impact-lab/run", null, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("ran").GetInt32());
        Assert.Equal("result", Text(await GetAsync(id), "state"));
    }

    [Fact]
    public async Task The_additive_migration_keeps_earlier_measurement_tables()
    {
        await CreateAsync(FileSource());
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_brandId, Token);
        var migrator = scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().GetService<IMigrator>();
        await migrator.MigrateAsync("20261016144728_ImpactReports", Token);
        await migrator.MigrateAsync(cancellationToken: Token);

        Assert.Empty(await scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().LabAnalyses.ToListAsync(Token));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().ImpactReports.ToListAsync(Token));
    }

    private async Task<(Guid Id, JsonElement View)> LoadedFileAsync()
    {
        var created = await CreateAsync(FileSource());
        var id = created.GetProperty("id").GetGuid();
        return (id, await LoadAsync(id, await UploadAsync(id, created)));
    }

    private async Task<(Guid Id, JsonElement View)> ReadyToRunAsync()
    {
        var (id, loaded) = await LoadedFileAsync();
        return (id, await SelectAsync(id, loaded, ["running"], null));
    }

    private async Task<JsonElement> CreateAsync(object source, string affects = "products")
    {
        using var response = await _admin.Browser.PostAsJsonAsync(Base, Setup(source, affects), Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private async Task<JsonElement> UploadAsync(Guid id, JsonElement current)
    {
        var csv = new StringBuilder("date,series,value\n");
        foreach (var (day, index) in Days(Start.AddDays(-History), End).Select((day, index) => (day, index)))
        {
            csv.Append(CultureInfo.InvariantCulture, $"{Iso(day)},Running,{40 + (index % 7)}\n");
            csv.Append(CultureInfo.InvariantCulture, $"{Iso(day)},Walking,{30 + (index % 5)}\n");
            csv.Append(CultureInfo.InvariantCulture, $"{Iso(day)},\"Sandals\",{20.5}\n");
        }

        using var response = await _admin.Browser.PostAsync($"{Base}/{id}/file?version={VersionOf(current)}", SalesFiles.Csv(csv.ToString(), "units.csv"), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private async Task<JsonElement> LoadAsync(Guid id, JsonElement current)
    {
        using var response = await PostAsync($"{Base}/{id}/load", Version(current));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private async Task<JsonElement> SelectAsync(Guid id, JsonElement current, string[] affected, string[]? comparison)
    {
        using var response = await _admin.Browser.PutAsJsonAsync($"{Base}/{id}/selection", new { version = VersionOf(current), affected, comparison }, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private async Task<JsonElement> GetAsync(Guid id) => await _admin.Browser.GetFromJsonAsync<JsonElement>($"{Base}/{id}", Token);

    private Task<HttpResponseMessage> PostAsync(string url, object body) => _admin.Browser.PostAsJsonAsync(url, body, Token);

    private async Task<int> DispatchAsync()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LabDispatcher>().RunDueAsync(Token);
    }

    private object Setup(object source, string affects = "products", DateOnly? start = null, DateOnly? end = null, int? version = null, string name = "Running shoe price cut") => new
    {
        version,
        name,
        whatChanged = "A 10% price cut on running shoes",
        start = start ?? Start,
        end = end ?? End,
        affectsKind = affects,
        source,
    };

    private static object FileSource() => new { kind = "file", metric = "units sold" };

    private static object TableSource(string value) => new
    {
        kind = "bigquery",
        metric = "units",
        table = "acme-data.shop.daily_units",
        dateColumn = "sold_on",
        valueColumn = value,
        seriesColumn = "product_line",
    };

    private static async Task<string[]> DataErrorsAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors").GetProperty("data").EnumerateArray()
                .Select(error => error.GetString()!).ToArray();
        }
    }

    private static int VersionOf(JsonElement view) => view.GetProperty("version").GetInt32();

    private static object Version(JsonElement view) => new { version = VersionOf(view) };

    private static IEnumerable<DateOnly> Days(DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            yield return day;
        }
    }

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()!;

    private static string[] Strings(JsonElement element, string name) => element.GetProperty(name).EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static DateOnly Date(JsonElement element, string name) => DateOnly.ParseExact(Text(element, name), "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
