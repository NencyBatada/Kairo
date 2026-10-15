using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Opportunities;

public sealed class OpportunityDecisionTests(SqlServer sqlServer) : IAsyncLifetime
{
    private string _database = "";
    private KairoApiFactory _api = null!;
    private KairoWorkerFactory _worker = null!;
    private RadarBrand _brand = null!;
    private Guid _id;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HttpClient Marketer => _brand.Marketer.Browser;

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _worker = new KairoWorkerFactory(_database, _api.GoogleApis, _api.Clock.GetUtcNow());
        new FakeSense().Attach(_worker.Agents);
        Radar.PlanDemand(_api.GoogleApis.Keywords);
        _brand = await Radar.BrandAsync(_api);
        await Radar.RefreshAsync(_worker);
        _id = (await Radar.OnlyCardAsync(Marketer, _brand.BrandId)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Accepting_opens_a_campaign_room_with_the_evidence()
    {
        var response = await Radar.DecideAsync(Marketer, _brand.BrandId, _id, "accept");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        var roomId = accepted.GetProperty("roomId").GetGuid();
        var opportunity = accepted.GetProperty("opportunity");
        Assert.Equal("accepted", opportunity.GetProperty("status").GetString());
        Assert.Equal(roomId, opportunity.GetProperty("roomId").GetGuid());
        var room = await RoomAsync(roomId);
        Assert.Equal(("Monsoon footwear", "brief_draft", _id, _brand.Marketer.UserId), (room.Title, room.State, room.OpportunityId, room.CreatedBy));
        using var evidence = JsonDocument.Parse(room.Evidence);
        Assert.Equal("Monsoon footwear", evidence.RootElement.GetProperty("topic").GetString());
        Assert.Equal("sourced", evidence.RootElement.GetProperty("reasonsStatus").GetString());
        Assert.Equal(2, evidence.RootElement.GetProperty("products").GetArrayLength());
        Assert.Empty(await Radar.CardsAsync(Marketer, _brand.BrandId));
        Assert.Single(await Radar.CardsAsync(Marketer, _brand.BrandId, "?status=accepted"));
        var actions = await Workspaces.AuditActionsAsync(_brand.Admin.Browser, _brand.BrandId);
        Assert.Equal(["opportunity.accepted", "campaign_room.opened"], actions.Take(2));
    }

    [Fact]
    public async Task An_opportunity_can_be_accepted_only_once()
    {
        (await Radar.DecideAsync(Marketer, _brand.BrandId, _id, "accept")).EnsureSuccessStatusCode();

        var again = await Radar.DecideAsync(Marketer, _brand.BrandId, _id, "accept");
        var dismiss = await Radar.DecideAsync(Marketer, _brand.BrandId, _id, "dismiss", new { reason = "too_small" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("This opportunity is already accepted.", (await again.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Conflict, dismiss.StatusCode);
        Assert.Equal(1, await RoomCountAsync());
    }

    [Fact]
    public async Task Saved_opportunities_stay_in_the_saved_list_until_unsaved()
    {
        var save = await Radar.DecideAsync(Marketer, _brand.BrandId, _id, "save");
        var saveAgain = await Radar.DecideAsync(Marketer, _brand.BrandId, _id, "save");
        var saved = await Radar.OnlyCardAsync(Marketer, _brand.BrandId, "?status=saved");
        var open = await Radar.OnlyCardAsync(Marketer, _brand.BrandId);
        var unsave = await Radar.DecideAsync(Marketer, _brand.BrandId, _id, "unsave");

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, saveAgain.StatusCode);
        Assert.Equal("saved", saved.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, saved.GetProperty("savedAt").ValueKind);
        Assert.Equal(_id, open.GetProperty("id").GetGuid());
        Assert.Equal("new", (await unsave.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("status").GetString());
        Assert.Empty(await Radar.CardsAsync(Marketer, _brand.BrandId, "?status=saved"));
        var actions = await Workspaces.AuditActionsAsync(_brand.Admin.Browser, _brand.BrandId);
        Assert.Equal(["opportunity.unsaved", "opportunity.saved"], actions.Take(2));
    }

    [Fact]
    public async Task Dismissing_needs_a_reason_from_the_list()
    {
        var none = await Radar.DecideAsync(Marketer, _brand.BrandId, _id, "dismiss", new { });
        var unknown = await Radar.DecideAsync(Marketer, _brand.BrandId, _id, "dismiss", new { reason = "boring" });
        var otherWithoutNote = await Radar.DecideAsync(Marketer, _brand.BrandId, _id, "dismiss", new { reason = "other" });
        var longNote = await Radar.DecideAsync(Marketer, _brand.BrandId, _id, "dismiss", new { reason = "other", note = new string('x', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(["Say why you're dismissing it."], await ErrorsAsync(otherWithoutNote, "note"));
        Assert.Equal(["Use up to 500 characters."], await ErrorsAsync(longNote, "note"));
        Assert.Single(await Radar.CardsAsync(Marketer, _brand.BrandId));
    }

    [Fact]
    public async Task A_dismissed_opportunity_keeps_its_reason()
    {
        var response = await Radar.DecideAsync(Marketer, _brand.BrandId, _id, "dismiss", new { reason = "other", note = "  We don't sell boots in the rain.  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dismissed = await Radar.DetailAsync(Marketer, _brand.BrandId, _id);
        Assert.Equal("dismissed", dismissed.GetProperty("status").GetString());
        Assert.Equal("other", dismissed.GetProperty("dismissReason").GetString());
        Assert.Equal("We don't sell boots in the rain.", dismissed.GetProperty("dismissNote").GetString());
        Assert.NotEqual(JsonValueKind.Null, dismissed.GetProperty("closedAt").ValueKind);
        Assert.Single(await Radar.CardsAsync(Marketer, _brand.BrandId, "?status=dismissed"));
        var entry = await Workspaces.LatestAuditEntryAsync(_brand.Admin.Browser, _brand.BrandId);
        Assert.Equal("opportunity.dismissed", entry.GetProperty("action").GetString());
    }

    [Fact]
    public async Task Only_marketers_decide_but_everyone_can_look()
    {
        var approver = await Workspaces.JoinAsync(_api, _brand.Admin.Browser, _brand.BrandId, "approver");
        var admin = await Workspaces.JoinAsync(_api, _brand.Admin.Browser, _brand.BrandId, "admin");

        var adminAccept = await Radar.DecideAsync(admin.Browser, _brand.BrandId, _id, "accept");
        var approverSave = await Radar.DecideAsync(approver.Browser, _brand.BrandId, _id, "save");
        var approverList = await Radar.CardsAsync(approver.Browser, _brand.BrandId);
        var outsider = await Workspaces.SignInAsync(_api);
        var outsiderList = await outsider.Browser.GetAsync(Radar.Base(_brand.BrandId), Token);

        Assert.Equal(HttpStatusCode.Forbidden, adminAccept.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, approverSave.StatusCode);
        Assert.Single(approverList);
        Assert.Equal(HttpStatusCode.NotFound, outsiderList.StatusCode);
    }

    [Fact]
    public async Task Another_workspace_cant_see_or_decide_on_the_opportunity()
    {
        var other = await Radar.BrandAsync(_api, connectAds: false, catalogue: false);

        var detail = await other.Marketer.Browser.GetAsync($"{Radar.Base(other.BrandId)}/{_id}", Token);
        var accept = await Radar.DecideAsync(other.Marketer.Browser, other.BrandId, _id, "accept");

        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
    }

    [Fact]
    public async Task Decisions_are_pushed_as_live_updates()
    {
        (await Radar.DecideAsync(Marketer, _brand.BrandId, _id, "save")).EnsureSuccessStatusCode();

        var rows = await OutboxRows.ReadAsync(_database, _brand.BrandId);
        Assert.Contains(rows, row => row.Type == "opportunities.refreshed");
        var change = JsonDocument.Parse(Assert.Single(rows, row => row.Type == "opportunity.changed").Payload).RootElement;
        Assert.Equal(_id, change.GetProperty("opportunityId").GetGuid());
        Assert.Equal("saved", change.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("?status=everything", "status")]
    [InlineData("?minScore=101", "minScore")]
    public async Task Bad_filters_are_rejected(string query, string field)
    {
        var response = await Marketer.GetAsync($"{Radar.Base(_brand.BrandId)}{query}", Token);

        Assert.Single(await ErrorsAsync(response, field));
    }

    [Fact]
    public async Task The_list_filters_by_category()
    {
        Assert.Single(await Radar.CardsAsync(Marketer, _brand.BrandId, "?category=shoes%20%3E%20rain"));
        Assert.Empty(await Radar.CardsAsync(Marketer, _brand.BrandId, "?category=Shoes%20%3E%20School"));
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private static async Task<string[]> ErrorsAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        return problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(error => error.GetString()!).ToArray();
    }

    private async Task<(string Title, string State, Guid OpportunityId, Guid CreatedBy, string Evidence)> RoomAsync(Guid roomId)
    {
        await using var connection = new SqlConnection(_database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            SELECT Title, State, OpportunityId, CreatedBy, Evidence FROM campaign_rooms.Rooms WHERE Id = @id;
            """;
        command.Parameters.AddWithValue("@id", roomId);
        await using var reader = await command.ExecuteReaderAsync(Token);
        Assert.True(await reader.ReadAsync(Token));
        return (reader.GetString(0), reader.GetString(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetString(4));
    }

    private async Task<int> RoomCountAsync()
    {
        await using var connection = new SqlConnection(_database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            SELECT COUNT(*) FROM campaign_rooms.Rooms WHERE OpportunityId = @id;
            """;
        command.Parameters.AddWithValue("@id", _id);
        return (int)(await command.ExecuteScalarAsync(Token))!;
    }
}
