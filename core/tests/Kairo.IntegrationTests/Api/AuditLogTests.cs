using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kairo.Contracts.Audit;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Api;

public sealed partial class AuditLogTests(SqlServer sqlServer) : IAsyncDisposable
{
    private static readonly TimeZoneInfo Kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admins_see_who_did_what_newest_first()
    {
        var owner = await Workspaces.SignInAsync(_api, "Neha Rao");
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name = "Acme Footwear" }, Token);

        var page = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/audit-log", Token);

        var entries = page.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["brand.updated", "brand.created"], entries.Select(entry => entry.GetProperty("action").GetString()));
        Assert.All(entries, entry => Assert.Equal("Neha Rao", entry.GetProperty("actorName").GetString()));
        Assert.All(entries, entry => Assert.Equal("user", entry.GetProperty("actorType").GetString()));
        Assert.All(entries, entry => Assert.Equal(owner.Account.Email, entry.GetProperty("actorEmail").GetString()));
        Assert.Equal("Acme Shoes", entries[0].GetProperty("before").GetProperty("name").GetString());
        Assert.Equal("Acme Footwear", entries[0].GetProperty("after").GetProperty("name").GetString());
        Assert.Equal("INR", entries[1].GetProperty("after").GetProperty("currency").GetString());
    }

    [Fact]
    public async Task The_log_pages_with_a_cursor()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        foreach (var name in new[] { "One", "Two", "Three" })
        {
            await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name }, Token);
        }

        var first = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/audit-log?limit=3", Token);
        var before = first.GetProperty("nextBefore").GetString();
        var second = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/audit-log?limit=3&before={before}", Token);

        Assert.Equal(3, first.GetProperty("entries").GetArrayLength());
        Assert.Equal("brand.created", Assert.Single(second.GetProperty("entries").EnumerateArray()).GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextBefore").ValueKind);
    }

    [Fact]
    public async Task Each_workspace_sees_only_its_own_entries()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var first = await Workspaces.CreateAsync(owner.Browser);
        var second = await Workspaces.CreateAsync(owner.Browser);
        await owner.Browser.PatchAsJsonAsync($"/api/brands/{second}", new { name = "Second" }, Token);

        Assert.Equal(["brand.created"], await Workspaces.AuditActionsAsync(owner.Browser, first));
    }

    [Fact]
    public async Task The_log_filters_by_action_actor_and_actor_type()
    {
        var owner = await Workspaces.SignInAsync(_api, "Neha Rao");
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name = "Acme Footwear" }, Token);
        await RecordAsync(brandId, new AuditRecord("brand_kit.approved", "brand_kit", "3", Actor: AuditActor.Agent("Sense agent", "sense")));
        await RecordAsync(brandId, new AuditRecord("connection.needs_reconnect", "connection", "c1"));
        var log = $"/api/brands/{brandId}/audit-log";

        Assert.Equal(["brand.updated", "brand.created"], await ActionsAsync(owner.Browser, $"{log}?action=brand.created&action=brand.updated"));
        Assert.Equal(["brand.updated", "brand.created"], await ActionsAsync(owner.Browser, $"{log}?actor=neha"));
        Assert.Equal(["brand.updated", "brand.created"], await ActionsAsync(owner.Browser, $"{log}?actor={Uri.EscapeDataString(owner.Account.Email)}"));
        Assert.Equal(["brand.updated", "brand.created"], await ActionsAsync(owner.Browser, $"{log}?actor={owner.UserId}"));
        Assert.Equal(["brand_kit.approved"], await ActionsAsync(owner.Browser, $"{log}?actorType=agent"));
        Assert.Equal(["connection.needs_reconnect"], await ActionsAsync(owner.Browser, $"{log}?actorType=system"));
        Assert.Equal(["brand_kit.approved"], await ActionsAsync(owner.Browser, $"{log}?actor=sense&action=brand_kit.approved&action=brand.created"));
        Assert.Empty(await ActionsAsync(owner.Browser, $"{log}?actor=nobody"));

        var agent = (await owner.Browser.GetFromJsonAsync<JsonElement>($"{log}?actorType=agent", Token)).GetProperty("entries")[0];
        Assert.Equal("Sense agent", agent.GetProperty("actorName").GetString());
        Assert.Equal("brand_kit", agent.GetProperty("targetType").GetString());
    }

    [Fact]
    public async Task Dates_are_whole_days_in_the_workspace_time_zone()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var local = TimeZoneInfo.ConvertTime(_api.Clock.GetUtcNow(), Kolkata);
        var nextMidnight = new DateTimeOffset(local.Date.AddDays(1), local.Offset);
        if (nextMidnight - local < TimeSpan.FromHours(1))
        {
            nextMidnight = nextMidnight.AddDays(1);
        }

        _api.Clock.Advance(nextMidnight - _api.Clock.GetUtcNow() - TimeSpan.FromMinutes(30));
        await RecordAsync(brandId, new AuditRecord("brand.updated", "brand", "late"));
        _api.Clock.Advance(TimeSpan.FromHours(1));
        await RecordAsync(brandId, new AuditRecord("brand.updated", "brand", "early"));
        var day = nextMidnight.ToString("yyyy-MM-dd");
        var previousDay = nextMidnight.AddDays(-1).ToString("yyyy-MM-dd");
        var instant = Uri.EscapeDataString(nextMidnight.ToString("yyyy-MM-dd'T'HH:mm:sszzz"));
        var log = $"/api/brands/{brandId}/audit-log";

        Assert.Equal(["early"], await TargetsAsync(owner.Browser, $"{log}?from={day}&to={day}"));
        Assert.Equal(["late", brandId.ToString()], await TargetsAsync(owner.Browser, $"{log}?to={previousDay}"));
        Assert.Equal(["early"], await TargetsAsync(owner.Browser, $"{log}?from={instant}"));
        Assert.Equal(["late", brandId.ToString()], await TargetsAsync(owner.Browser, $"{log}?to={instant}"));
    }

    [Fact]
    public async Task Paging_keeps_the_filters()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await RecordManyAsync(brandId, 10, i => i % 2 == 0
            ? new AuditRecord("brand.updated", "brand", $"u{i / 2}")
            : new AuditRecord("member.removed", "member", $"m{i / 2}"));

        var log = $"/api/brands/{brandId}/audit-log?action=member.removed&limit=2";
        var targets = new List<string>();
        string? before = null;
        do
        {
            var page = await owner.Browser.GetFromJsonAsync<JsonElement>(before is null ? log : $"{log}&before={before}", Token);
            targets.AddRange(page.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("targetId").GetString()!));
            before = page.GetProperty("nextBefore").GetString();
        }
        while (before is not null);

        Assert.Equal(["m4", "m3", "m2", "m1", "m0"], targets);
    }

    [Theory]
    [InlineData("before=abc", "before")]
    [InlineData("before=-5", "before")]
    [InlineData("from=yesterday", "from")]
    [InlineData("to=2026-13-01", "to")]
    [InlineData("from=2026-10-14T09:00:00", "from")]
    [InlineData("from=2026-10-15&to=2026-10-14", "to")]
    [InlineData("actorType=robot", "actorType")]
    public async Task Bad_filters_are_explained(string query, string field)
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var paths = field == "before" ? new[] { "audit-log" } : ["audit-log", "audit-log/export"];
        foreach (var path in paths)
        {
            var response = await owner.Browser.GetAsync($"/api/brands/{brandId}/{path}?{query}", Token);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"{path}?{query} did not explain {field}.");
        }
    }

    [Fact]
    public async Task Export_streams_every_matching_entry()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await RecordManyAsync(brandId, 250, i => new AuditRecord("member.removed", "member", $"m{i}"));
        await RecordAsync(brandId, new AuditRecord("brand.updated", "brand", "other"));

        var response = await owner.Browser.GetAsync($"/api/brands/{brandId}/audit-log/export?action=member.removed", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        var bytes = await response.Content.ReadAsByteArrayAsync(Token);
        Assert.Equal(Encoding.UTF8.Preamble.ToArray(), bytes[..3]);
        var rows = Csv(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        Assert.Equal(
            ["Occurred at (UTC)", "Occurred at (Asia/Kolkata)", "Actor type", "Actor", "Actor email", "Action", "Action key", "Target type", "Target ID", "Before", "After"],
            rows[0]);
        Assert.Equal(250, rows.Count - 1);
        Assert.Equal("m249", rows[1][8]);
        Assert.Equal("m0", rows[^1][8]);
        Assert.Equal("Removed a member", rows[1][5]);
        Assert.Equal("system", rows[1][2]);
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", rows[1][0]);
        Assert.EndsWith("+05:30", rows[1][1]);
    }

    [Fact]
    public async Task Export_neutralises_spreadsheet_formulas()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        string[] names = ["=HYPERLINK(\"http://evil\")", "+1", "-2", "@SUM(A1)", "\tTab", "\rReturn", "Plain \"quoted\""];
        await RecordManyAsync(brandId, names.Length, i => new AuditRecord("brand.updated", "brand", "x", Actor: AuditActor.Agent(names[i])));

        var text = await owner.Browser.GetStringAsync($"/api/brands/{brandId}/audit-log/export?actorType=agent", Token);

        Assert.Contains("\"'=HYPERLINK(\"\"http://evil\"\")\"", text);
        var actors = Csv(text.TrimStart('\uFEFF')).Skip(1).Select(row => row[3]).Reverse().ToArray();
        Assert.Equal(["'=HYPERLINK(\"http://evil\")", "'+1", "'-2", "'@SUM(A1)", "'\tTab", "'\rReturn", "Plain \"quoted\""], actors);
    }

    [Fact]
    public async Task Export_and_actions_are_for_admins_of_that_workspace_only()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var other = await Workspaces.CreateAsync(owner.Browser);
        await RecordAsync(other, new AuditRecord("brand.updated", "brand", "elsewhere"));
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        var outsider = await Workspaces.SignInAsync(_api);

        foreach (var path in new[] { "audit-log/export", "audit-log/actions" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await marketer.Browser.GetAsync($"/api/brands/{brandId}/{path}", Token)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.Browser.GetAsync($"/api/brands/{brandId}/{path}", Token)).StatusCode);
        }

        var text = await owner.Browser.GetStringAsync($"/api/brands/{brandId}/audit-log/export", Token);
        Assert.DoesNotContain("elsewhere", text);
        Assert.Contains(brandId.ToString(), text);
    }

    [Fact]
    public async Task Actions_list_every_known_action_with_its_label()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var actions = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/audit-log/actions", Token);

        var labels = actions.EnumerateArray().ToDictionary(item => item.GetProperty("action").GetString()!, item => item.GetProperty("label").GetString());
        Assert.Equal(AuditActions.All.Count, labels.Count);
        Assert.Equal("Uploaded a Brand Kit file", labels["brand_kit.file_uploaded"]);
        Assert.Equal("Uploaded sales data", labels["analytics.sales_uploaded"]);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private Task RecordAsync(Guid brandId, AuditRecord record) => RecordManyAsync(brandId, 1, _ => record);

    private async Task RecordManyAsync(Guid brandId, int count, Func<int, AuditRecord> create)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        for (var i = 0; i < count; i++)
        {
            await audit.RecordAsync(create(i), Token);
        }
    }

    private static async Task<string[]> ActionsAsync(HttpClient browser, string path) =>
        (await browser.GetFromJsonAsync<JsonElement>(path, Token)).GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("action").GetString()!)
            .ToArray();

    private static async Task<string[]> TargetsAsync(HttpClient browser, string path) =>
        (await browser.GetFromJsonAsync<JsonElement>(path, Token)).GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("targetId").GetString()!)
            .ToArray();

    private static List<string[]> Csv(string text) =>
        CsvRow().Matches(text)
            .Select(row => CsvField().Matches(row.Value).Select(field => field.Groups["value"].Value.Replace("\"\"", "\"")).ToArray())
            .ToList();

    [GeneratedRegex("(?:\"(?:[^\"]|\"\")*\",)*\"(?:[^\"]|\"\")*\"\r\n")]
    private static partial Regex CsvRow();

    [GeneratedRegex("\"(?<value>(?:[^\"]|\"\")*)\"")]
    private static partial Regex CsvField();
}
