using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class BriefTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await CampaignWorld.CreateAsync(sqlServer);

    [Fact]
    public async Task The_drafted_brief_has_every_field_with_its_reason_and_evidence()
    {
        var roomId = await _world.OpenWithBriefAsync();

        var detail = await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId);

        Assert.True(detail.GetProperty("editable").GetBoolean());
        var brief = detail.GetProperty("brief");
        Assert.Equal((1, "drafted", "plan-brief/1"), (brief.GetProperty("version").GetInt32(), brief.GetProperty("origin").GetString(), brief.GetProperty("promptVersion").GetString()));
        Assert.Equal(("Monsoon runners", "orders"), (brief.GetProperty("title").GetString(), brief.GetProperty("kpi").GetString()));
        Assert.Equal(["search", "shopping"], Strings(brief.GetProperty("campaignTypes")));
        Assert.Equal((4000m, "INR"), (brief.GetProperty("budget").GetProperty("amount").GetDecimal(), brief.GetProperty("budget").GetProperty("currency").GetString()));
        var start = DateOnly.Parse(brief.GetProperty("startDate").GetString()!);
        var end = DateOnly.Parse(brief.GetProperty("endDate").GetString()!);
        Assert.Equal(13, end.DayNumber - start.DayNumber);
        Assert.Equal(3, brief.GetProperty("regions").GetArrayLength());
        var product = Assert.Single(brief.GetProperty("products").EnumerateArray());
        Assert.True(product.GetProperty("inStock").GetBoolean());
        Assert.Equal([product.GetProperty("link").GetString()], Strings(brief.GetProperty("landingPages")));
        Assert.Equal(["en"], Strings(brief.GetProperty("languages")));

        var reasons = brief.GetProperty("reasons").EnumerateArray().ToDictionary(reason => reason.GetProperty("field").GetString()!);
        Assert.Equal(
            ["objective", "kpi", "campaignTypes", "budget", "dates", "regions", "audiences", "keyMessages", "products", "landingPages", "languages"],
            reasons.Keys);
        Assert.Equal("Searches and bids point to this budget.", reasons["budget"].GetProperty("reason").GetString());
        var budgetEvidence = reasons["budget"].GetProperty("evidence").EnumerateArray()
            .ToDictionary(item => item.GetProperty("label").GetString()!, item => item.GetProperty("value").GetString());
        Assert.Equal("₹2,000 to ₹6,000", budgetEvidence["Suggested range"]);
        Assert.Equal("₹12.29 to ₹36.86", budgetEvidence["Top-of-page bid range"]);
        Assert.Equal("7,000", budgetEvidence["Searches last month"]);
        Assert.Contains(reasons["regions"].GetProperty("evidence").EnumerateArray(), item => item.GetProperty("value").GetString()!.Contains("searches last month"));

        var warnings = detail.GetProperty("warnings").EnumerateArray().Select(warning => warning.GetProperty("code").GetString()).ToList();
        Assert.Equal(["merchant_center_not_linked"], warnings);
        Assert.Equal(200000m, detail.GetProperty("budgetLimit").GetProperty("amount").GetDecimal());
        Assert.Equal(11, detail.GetProperty("options").GetProperty("languages").GetArrayLength());
        Assert.Single(detail.GetProperty("history").EnumerateArray());

        var request = Assert.Single(_world.Plan.Requests);
        var guide = request.GetProperty("budget");
        Assert.Equal((2000m, 6000m, 4000m, 14), (guide.GetProperty("low").GetDecimal(), guide.GetProperty("high").GetDecimal(), guide.GetProperty("suggested").GetDecimal(), guide.GetProperty("days").GetInt32()));
        Assert.Equal(7000, request.GetProperty("opportunity").GetProperty("lastMonthSearches").GetInt64());
        Assert.Equal(JsonValueKind.Null, request.GetProperty("dates").GetProperty("latestEnd").ValueKind);

        var room = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        var summary = room.GetProperty("brief");
        Assert.Equal("Sell waterproof shoes while monsoon searches peak.", summary.GetProperty("goal").GetString());
        Assert.Equal(["search", "shopping"], Strings(summary.GetProperty("channels")));
        Assert.Equal("Stay dry on the way to work.", summary.GetProperty("keyMessage").GetString());
    }

    [Fact]
    public async Task The_agent_cannot_take_the_budget_or_dates_outside_the_guides()
    {
        _world.Plan.Answer = request =>
        {
            var draft = JsonSerializer.SerializeToNode(FakeBriefPlan.Draft(request), JsonSerializerOptions.Web)!.AsObject();
            draft["budget"] = 9_000_000;
            draft["startDate"] = "2020-01-01";
            draft["kpi"] = "likes";
            draft["regions"] = new System.Text.Json.Nodes.JsonArray("Atlantis");
            return draft;
        };
        var roomId = await _world.OpenWithBriefAsync();

        var brief = (await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("brief");

        Assert.Equal(6000m, brief.GetProperty("budget").GetProperty("amount").GetDecimal());
        Assert.Equal("orders", brief.GetProperty("kpi").GetString());
        Assert.True(DateOnly.Parse(brief.GetProperty("startDate").GetString()!) > new DateOnly(2020, 1, 1));
        Assert.Equal(3, brief.GetProperty("regions").GetArrayLength());
    }

    [Fact]
    public async Task A_marketer_edits_fields_directly_and_the_brief_rechecks_itself()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var detail = await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId);

        using var response = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new
        {
            version = detail.GetProperty("campaignVersion").GetInt32(),
            briefVersion = 1,
            budget = 250000,
            languages = new[] { "en", "hi" },
            keyMessages = new[] { "Dries in minutes.", "Stay dry on the way to work." },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        var brief = edited.GetProperty("brief");
        Assert.Equal((2, "edit"), (brief.GetProperty("version").GetInt32(), brief.GetProperty("origin").GetString()));
        Assert.Equal(250000m, brief.GetProperty("budget").GetProperty("amount").GetDecimal());
        Assert.Equal(["en", "hi"], Strings(brief.GetProperty("languages")));
        var budgetReason = brief.GetProperty("reasons").EnumerateArray().Single(reason => reason.GetProperty("field").GetString() == "budget");
        Assert.StartsWith("Changed by ", budgetReason.GetProperty("reason").GetString());
        Assert.NotEmpty(budgetReason.GetProperty("evidence").EnumerateArray());
        var overLimit = edited.GetProperty("warnings").EnumerateArray().Single(warning => warning.GetProperty("code").GetString() == "over_budget_limit");
        Assert.Equal(("info", "budget"), (overLimit.GetProperty("severity").GetString(), overLimit.GetProperty("field").GetString()));
        Assert.Equal(detail.GetProperty("campaignVersion").GetInt32() + 1, edited.GetProperty("campaignVersion").GetInt32());
        var history = edited.GetProperty("history").EnumerateArray().ToList();
        Assert.Equal([2, 1], history.Select(entry => entry.GetProperty("version").GetInt32()));
        Assert.Equal(JsonValueKind.Object, history[0].GetProperty("by").ValueKind);

        var activity = await Campaigns.ActivityAsync(_world.Marketer, _world.BrandId, roomId);
        var latest = activity.GetProperty("entries")[0];
        Assert.Equal("brief.edited", latest.GetProperty("action").GetString());
        Assert.Equal(["budget", "keyMessages", "languages"], Strings(latest.GetProperty("details").GetProperty("fields")));
    }

    [Fact]
    public async Task Changing_the_products_updates_the_landing_pages()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var detail = await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId);
        var products = detail.GetProperty("options").GetProperty("products").EnumerateArray().ToList();

        using var response = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new
        {
            version = detail.GetProperty("campaignVersion").GetInt32(),
            briefVersion = 1,
            productIds = products.Select(product => product.GetProperty("id").GetString()).ToArray(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var brief = (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("brief");
        Assert.Equal(products.Select(product => product.GetProperty("link").GetString()), Strings(brief.GetProperty("landingPages")));
    }

    [Fact]
    public async Task Edits_outside_the_options_are_refused_with_field_errors()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var version = (await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("campaignVersion").GetInt32();

        using var response = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new
        {
            version,
            briefVersion = 1,
            kpi = "likes",
            regions = new[] { "Atlantis" },
            productIds = new[] { Guid.NewGuid().ToString() },
            startDate = "2020-01-01",
            languages = new[] { "fr" },
            budget = 0,
            landingPages = new[] { "javascript:alert(1)" },
            audiences = Array.Empty<string>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
        Assert.Equal(
            ["audiences", "budget", "kpi", "landingPages", "languages", "productIds", "regions", "startDate"],
            errors.EnumerateObject().Select(error => error.Name).Order());
        Assert.Single((await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task Stale_or_empty_edits_and_edits_by_non_marketers_are_refused()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var version = (await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("campaignVersion").GetInt32();

        using var staleRoom = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new { version = version - 1, briefVersion = 1, title = "New" });
        using var staleBrief = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new { version, briefVersion = 2, title = "New" });
        using var nothing = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new { version, briefVersion = 1, title = "Monsoon runners" });
        using var approver = await Campaigns.EditBriefAsync(_world.Approver.Browser, _world.BrandId, roomId, new { version, briefVersion = 1, title = "New" });
        using var missing = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new { title = "New" });

        Assert.Equal(HttpStatusCode.Conflict, staleRoom.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, staleBrief.StatusCode);
        Assert.Equal("The brief changed since you opened it. Refresh and try again.", await Campaigns.TitleAsync(staleBrief));
        Assert.Equal(HttpStatusCode.Conflict, nothing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, approver.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task The_brief_cannot_change_while_approval_is_pending()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var room = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);
        using var requested = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "approval-requests", new { gate = "brief", version = Campaigns.Version(room) });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var version = Campaigns.Version(await requested.Content.ReadFromJsonAsync<JsonElement>(Token));

        using var edit = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new { version, briefVersion = 1, title = "New" });
        using var instruct = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "brief/instructions", new { version, briefVersion = 1, instruction = "add Hindi" });

        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal("Approval was requested, so the brief can't change until it's approved or sent back.", await Campaigns.TitleAsync(edit));
        Assert.Equal(HttpStatusCode.Conflict, instruct.StatusCode);
        Assert.False((await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("editable").GetBoolean());
        Assert.Empty(_world.Plan.Instructions);
    }

    [Fact]
    public async Task An_instruction_shows_its_changes_before_they_apply()
    {
        _world.Plan.Changes = _ => new
        {
            languages = new[] { "en", "hi" },
            keyMessages = new[] { "Dries in minutes.", "Stay dry on the way to work." },
            regions = new[] { "Atlantis" },
            reasons = new { languages = "You asked for Hindi.", keyMessages = "Quick-dry now leads." },
            summary = "Adds Hindi and leads with quick-dry.",
        };
        var roomId = await _world.OpenWithBriefAsync();
        var version = (await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("campaignVersion").GetInt32();

        using var previewed = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "brief/instructions", new { version, briefVersion = 1, instruction = " add Hindi and lead with quick-dry " });

        Assert.Equal(HttpStatusCode.OK, previewed.StatusCode);
        var proposal = await previewed.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(("add Hindi and lead with quick-dry", "Adds Hindi and leads with quick-dry."), (proposal.GetProperty("instruction").GetString(), proposal.GetProperty("summary").GetString()));
        var changes = proposal.GetProperty("changes").EnumerateArray().ToList();
        Assert.Equal(["keyMessages", "languages"], changes.Select(change => change.GetProperty("field").GetString()));
        Assert.Equal(("English", "English, Hindi", "You asked for Hindi."), (changes[1].GetProperty("before").GetString(), changes[1].GetProperty("after").GetString(), changes[1].GetProperty("reason").GetString()));
        Assert.Equal(["en", "hi"], Strings(proposal.GetProperty("preview").GetProperty("languages")));
        Assert.Single((await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("history").EnumerateArray());

        var sent = Assert.Single(_world.Plan.Instructions);
        Assert.Equal("add Hindi and lead with quick-dry", sent.GetProperty("instruction").GetString());
        Assert.Equal(["p1"], Strings(sent.GetProperty("brief").GetProperty("productIds")));
        Assert.Equal("INR", sent.GetProperty("brief").GetProperty("currency").GetString());

        var id = proposal.GetProperty("id").GetGuid();
        using var applied = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, $"brief/proposals/{id}/apply", new { version });

        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var detail = await applied.Content.ReadFromJsonAsync<JsonElement>(Token);
        var brief = detail.GetProperty("brief");
        Assert.Equal((2, "instruction", "plan-brief-instruction/1"), (brief.GetProperty("version").GetInt32(), brief.GetProperty("origin").GetString(), brief.GetProperty("promptVersion").GetString()));
        Assert.Equal(["Dries in minutes.", "Stay dry on the way to work."], Strings(brief.GetProperty("keyMessages")));
        Assert.Equal("add Hindi and lead with quick-dry", detail.GetProperty("history")[0].GetProperty("instruction").GetString());
        var latest = (await Campaigns.ActivityAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("entries")[0];
        Assert.Equal("brief.instruction_applied", latest.GetProperty("action").GetString());

        using var again = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, $"brief/proposals/{id}/apply", new { version = detail.GetProperty("campaignVersion").GetInt32() });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_preview_is_refused_once_the_brief_has_moved_on_or_it_expired()
    {
        _world.Plan.Changes = _ => new { languages = new[] { "en", "hi" }, reasons = new { }, summary = "Adds Hindi." };
        var roomId = await _world.OpenWithBriefAsync();
        var version = (await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("campaignVersion").GetInt32();
        var first = await ProposeAsync(roomId, version);
        var second = await ProposeAsync(roomId, version);

        using var edited = await Campaigns.EditBriefAsync(_world.Marketer, _world.BrandId, roomId, new { version, briefVersion = 1, title = "Rain runners" });
        var moved = (await edited.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("campaignVersion").GetInt32();
        using var stale = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, $"brief/proposals/{first}/apply", new { version = moved });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("The brief changed since this preview. Type the instruction again.", await Campaigns.TitleAsync(stale));

        var third = await ProposeAsync(roomId, moved, 2);
        _world.Api.Clock.Advance(TimeSpan.FromMinutes(31));
        using var expired = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, $"brief/proposals/{third}/apply", new { version = moved });
        using var unknown = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, $"brief/proposals/{Guid.NewGuid()}/apply", new { version = moved });

        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        Assert.Equal("This preview has expired. Type the instruction again.", await Campaigns.TitleAsync(expired));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task An_instruction_that_changes_nothing_cannot_be_applied()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var version = (await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("campaignVersion").GetInt32();

        using var previewed = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "brief/instructions", new { version, briefVersion = 1, instruction = "make it better" });
        var proposal = await previewed.Content.ReadFromJsonAsync<JsonElement>(Token);
        using var applied = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, $"brief/proposals/{proposal.GetProperty("id").GetGuid()}/apply", new { version });

        Assert.Empty(proposal.GetProperty("changes").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, applied.StatusCode);
    }

    [Fact]
    public async Task A_failing_agent_or_a_blank_instruction_is_reported()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var version = (await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("campaignVersion").GetInt32();
        _world.Plan.Fails = true;

        using var failed = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "brief/instructions", new { version, briefVersion = 1, instruction = "add Hindi" });
        using var blank = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "brief/instructions", new { version, briefVersion = 1, instruction = " " });

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
    }

    [Fact]
    public async Task A_brief_saved_before_smart_briefs_still_reads()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await SetContentAsync(roomId, """
            {"Title":"Rain brief","Goal":"Sell","Audience":"Commuters","KeyMessage":"Stay dry","Offer":null,"Regions":["Mumbai"],"ProductIds":["6f9619ff-8b86-d011-b42d-00c04fc964ff"],"Channels":["search"],"Timing":"This week"}
            """);

        var detail = await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId);
        var room = await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId);

        var brief = detail.GetProperty("brief");
        Assert.Equal(("Rain brief", "Sell"), (brief.GetProperty("title").GetString(), brief.GetProperty("objective").GetString()));
        Assert.Equal(["Commuters"], Strings(brief.GetProperty("audiences")));
        Assert.Contains(detail.GetProperty("warnings").EnumerateArray(), warning => warning.GetProperty("field").GetString() == "kpi");
        Assert.Equal(("Sell", "This week"), (room.GetProperty("brief").GetProperty("goal").GetString(), room.GetProperty("brief").GetProperty("timing").GetString()));
    }

    [Fact]
    public async Task No_transaction_stays_open_while_the_agent_works_on_an_instruction()
    {
        var roomId = await _world.OpenWithBriefAsync();
        var version = (await Campaigns.BriefAsync(_world.Marketer, _world.BrandId, roomId)).GetProperty("campaignVersion").GetInt32();
        int? idleTransactions = null;
        _world.Plan.WhileInstructing = () => idleTransactions = IdleTransactionsAsync().GetAwaiter().GetResult();

        using var previewed = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "brief/instructions", new { version, briefVersion = 1, instruction = "add Hindi" });

        Assert.Equal(HttpStatusCode.OK, previewed.StatusCode);
        Assert.Equal(0, idleTransactions);
        Assert.Equal(1, await CountAsync("BriefProposals", roomId));
    }

    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private async Task<int> IdleTransactionsAsync()
    {
        await using var connection = new SqlConnection(_world.Database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sys.dm_exec_sessions
            WHERE database_id = DB_ID() AND session_id <> @@SPID AND status = N'sleeping' AND open_transaction_count > 0;
            """;
        return (int)(await command.ExecuteScalarAsync(Token))!;
    }

    private async Task<int> CountAsync(string table, Guid roomId)
    {
        await using var connection = new SqlConnection(_world.Database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            SELECT COUNT(*) FROM campaign_rooms.{table} WHERE RoomId = @room;
            """;
        command.Parameters.AddWithValue("@room", roomId);
        return (int)(await command.ExecuteScalarAsync(Token))!;
    }

    private async Task<Guid> ProposeAsync(Guid roomId, int version, int briefVersion = 1)
    {
        using var response = await Campaigns.PostAsync(_world.Marketer, _world.BrandId, roomId, "brief/instructions", new { version, briefVersion, instruction = "add Hindi" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetGuid();
    }

    private async Task SetContentAsync(Guid roomId, string content)
    {
        await using var connection = new SqlConnection(_world.Database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            UPDATE campaign_rooms.Briefs SET Content = @content WHERE RoomId = @room;
            """;
        command.Parameters.AddWithValue("@room", roomId);
        command.Parameters.AddWithValue("@content", content);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static string?[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString())];
}
