using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;

namespace Kairo.IntegrationTests.Notifications;

public sealed class InboxTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_list_is_newest_first_and_pages_with_before()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyOwnerAsync(owner, brandId, 3);

        var first = await ListAsync(owner.Browser, brandId, "?limit=2");
        var ids = Ids(first);
        Assert.Equal(2, ids.Length);
        Assert.True(ids[0] > ids[1]);
        Assert.Equal(ids[1], first.GetProperty("nextBefore").GetInt64());
        Assert.Equal(3, first.GetProperty("unreadCount").GetInt32());

        var second = await ListAsync(owner.Browser, brandId, $"?limit=2&before={ids[1]}");
        var rest = Assert.Single(Ids(second));
        Assert.True(rest < ids[1]);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextBefore").ValueKind);
    }

    [Fact]
    public async Task Each_item_carries_what_the_screen_shows()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyOwnerAsync(owner, brandId, 1);

        var item = (await ListAsync(owner.Browser, brandId)).GetProperty("items")[0];

        Assert.Equal("connection.problem", item.GetProperty("kind").GetString());
        Assert.Equal("Google Ads needs reconnecting", item.GetProperty("title").GetString());
        Assert.Equal("Reconnect it so refreshes and changes can continue.", item.GetProperty("body").GetString());
        Assert.Equal("/settings/connections", item.GetProperty("link").GetString());
        Assert.Equal(_api.Clock.GetUtcNow(), item.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("readAt").ValueKind);
    }

    [Fact]
    public async Task A_limit_outside_1_to_100_is_brought_into_range()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyOwnerAsync(owner, brandId, 2);

        Assert.Single(Ids(await ListAsync(owner.Browser, brandId, "?limit=0")));
        Assert.Equal(2, Ids(await ListAsync(owner.Browser, brandId, "?limit=500")).Length);
    }

    [Fact]
    public async Task Reading_one_marks_it_read_once_and_tells_the_reader_s_other_tabs()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyOwnerAsync(owner, brandId, 2);
        var id = Ids(await ListAsync(owner.Browser, brandId))[0];

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Browser.PostAsync($"/api/brands/{brandId}/notifications/{id}/read", null, Token)).StatusCode);
        var readAt = _api.Clock.GetUtcNow();
        _api.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Browser.PostAsync($"/api/brands/{brandId}/notifications/{id}/read", null, Token)).StatusCode);

        var list = await ListAsync(owner.Browser, brandId);
        Assert.Equal(1, list.GetProperty("unreadCount").GetInt32());
        Assert.Equal(readAt, list.GetProperty("items")[0].GetProperty("readAt").GetDateTimeOffset());
        var read = Assert.Single(await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId), row => row.Type == "notifications.read");
        Assert.Equal("{}", read.Payload);
        Assert.Equal([owner.UserId], JsonSerializer.Deserialize<Guid[]>(read.AudienceUsers!)!);
    }

    [Fact]
    public async Task Reading_all_marks_every_one_read_and_tells_the_reader_s_other_tabs()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyOwnerAsync(owner, brandId, 3);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Browser.PostAsync($"/api/brands/{brandId}/notifications/read-all", null, Token)).StatusCode);

        var list = await ListAsync(owner.Browser, brandId);
        Assert.Equal(0, list.GetProperty("unreadCount").GetInt32());
        Assert.All(list.GetProperty("items").EnumerateArray(), item => Assert.Equal(JsonValueKind.String, item.GetProperty("readAt").ValueKind));
        Assert.Single(await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId), row => row.Type == "notifications.read");
    }

    [Fact]
    public async Task Another_member_s_notification_is_not_found_and_stays_unread()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await NotifyOwnerAsync(owner, brandId, 1);
        var id = Ids(await ListAsync(owner.Browser, brandId))[0];

        var response = await marketer.Browser.PostAsync($"/api/brands/{brandId}/notifications/{id}/read", null, Token);
        (await marketer.Browser.PostAsync($"/api/brands/{brandId}/notifications/read-all", null, Token)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(Ids(await ListAsync(marketer.Browser, brandId)));
        Assert.Equal(1, (await ListAsync(owner.Browser, brandId)).GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task Another_workspace_s_notifications_never_appear()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var otherBrandId = await Workspaces.CreateAsync(owner.Browser);
        await NotifyOwnerAsync(owner, brandId, 1);
        var id = Ids(await ListAsync(owner.Browser, brandId))[0];

        var other = await ListAsync(owner.Browser, otherBrandId);
        var response = await owner.Browser.PostAsync($"/api/brands/{otherBrandId}/notifications/{id}/read", null, Token);

        Assert.Empty(Ids(other));
        Assert.Equal(0, other.GetProperty("unreadCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Only_signed_in_members_reach_the_inbox()
    {
        var (_, brandId) = await WorkspaceAsync();
        var outsider = await Workspaces.SignInAsync(_api);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateBrowser().GetAsync($"/api/brands/{brandId}/notifications", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Browser.GetAsync($"/api/brands/{brandId}/notifications", Token)).StatusCode);
    }

    [Fact]
    public async Task Marking_read_needs_the_request_header()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyOwnerAsync(owner, brandId, 1);
        var bare = _api.CreateBrowser(withRequestHeader: false);
        await _api.SignInAsync(bare, owner.Account, "/");

        var response = await bare.PostAsync($"/api/brands/{brandId}/notifications/read-all", null, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, (await ListAsync(owner.Browser, brandId)).GetProperty("unreadCount").GetInt32());
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(Member Owner, Guid BrandId)> WorkspaceAsync()
    {
        var owner = await Workspaces.SignInAsync(_api);
        return (owner, await Workspaces.CreateAsync(owner.Browser));
    }

    private async Task NotifyOwnerAsync(Member owner, Guid brandId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        }
    }

    private static Task<JsonElement> ListAsync(HttpClient browser, Guid brandId, string query = "") =>
        browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/notifications{query}", Token);

    private static long[] Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt64()).ToArray();
}
