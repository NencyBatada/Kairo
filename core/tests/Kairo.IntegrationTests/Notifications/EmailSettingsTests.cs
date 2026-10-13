using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Access;

namespace Kairo.IntegrationTests.Notifications;

public sealed class EmailSettingsTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Settings_list_only_the_email_kinds_a_member_s_roles_usually_receive()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        var approver = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "approver");

        var forMarketer = Kinds(await GetAsync(marketer.Browser, brandId));
        var forApprover = Kinds(await GetAsync(approver.Browser, brandId));
        var forOwner = Kinds(await GetAsync(owner.Browser, brandId));

        Assert.Contains("trigger.fired", forMarketer);
        Assert.DoesNotContain("connection.problem", forMarketer);
        Assert.DoesNotContain("approval.needed", forMarketer);
        Assert.Equal(new[] { "approval.needed", "pacing.alert", "report.ready" }, forApprover.Order());
        Assert.Contains("connection.problem", forOwner);
        Assert.DoesNotContain("stock.pause_changed", forOwner);
        Assert.DoesNotContain("impact_lab.ready", forOwner);
        Assert.Equal(11, forOwner.Length);
    }

    [Fact]
    public async Task Each_kind_says_whether_it_emails_and_whether_it_can_be_turned_off()
    {
        var (owner, brandId) = await WorkspaceAsync();

        var settings = (await GetAsync(owner.Browser, brandId)).GetProperty("kinds").EnumerateArray().ToList();

        var connection = settings.Single(kind => kind.GetProperty("kind").GetString() == "connection.problem");
        Assert.Equal("Connection or data problem", connection.GetProperty("name").GetString());
        Assert.Equal("A connected account needs reconnecting, or a data feed failed.", connection.GetProperty("description").GetString());
        Assert.True(connection.GetProperty("email").GetBoolean());
        Assert.True(connection.GetProperty("canTurnOff").GetBoolean());
        var approval = settings.Single(kind => kind.GetProperty("kind").GetString() == "approval.needed");
        Assert.False(approval.GetProperty("canTurnOff").GetBoolean());
    }

    [Fact]
    public async Task Turning_a_kind_off_stops_its_emails_but_not_its_notifications()
    {
        var (owner, brandId) = await WorkspaceAsync();

        var saved = await PutAsync(owner.Browser, brandId, ("connection.problem", false));
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.False(EmailOn(await saved.Content.ReadFromJsonAsync<JsonElement>(Token), "connection.problem"));
        Assert.False(EmailOn(await GetAsync(owner.Browser, brandId), "connection.problem"));
        Assert.Single(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId));
        Assert.Empty(await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, brandId));
    }

    [Fact]
    public async Task Turning_a_kind_back_on_resumes_its_emails()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await PutAsync(owner.Browser, brandId, ("connection.problem", false));

        (await PutAsync(owner.Browser, brandId, ("connection.problem", true))).EnsureSuccessStatusCode();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));

        Assert.True(EmailOn(await GetAsync(owner.Browser, brandId), "connection.problem"));
        Assert.Single(await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, brandId));
    }

    [Fact]
    public async Task One_member_turning_a_kind_off_leaves_the_others_emails_on()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var admin = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");

        await PutAsync(admin.Browser, brandId, ("connection.problem", false));
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Roles(BrandRoles.Admin)));

        var email = Assert.Single(await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, brandId));
        Assert.Equal(owner.UserId, email.UserId);
    }

    [Fact]
    public async Task Email_settings_belong_to_one_workspace()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var otherBrandId = await Workspaces.CreateAsync(owner.Browser);

        await PutAsync(owner.Browser, brandId, ("connection.problem", false));
        await Notifying.NotifyAsync(_api, otherBrandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));

        Assert.True(EmailOn(await GetAsync(owner.Browser, otherBrandId), "connection.problem"));
        Assert.Single(await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, otherBrandId));
    }

    [Theory]
    [InlineData("approval.needed", false)]
    [InlineData("launch.failed", false)]
    [InlineData("stock.pause_changed", false)]
    [InlineData("impact_lab.ready", true)]
    [InlineData("connection.unknown", false)]
    public async Task Required_in_app_only_and_unknown_kinds_are_refused(string kind, bool email)
    {
        var (owner, brandId) = await WorkspaceAsync();

        var response = await PutAsync(owner.Browser, brandId, ("connection.problem", false), (kind, email));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(EmailOn(await GetAsync(owner.Browser, brandId), "connection.problem"));
    }

    [Fact]
    public async Task A_required_kind_can_be_saved_as_on()
    {
        var (owner, brandId) = await WorkspaceAsync();

        var response = await PutAsync(owner.Browser, brandId, ("approval.needed", true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_change_is_audited_with_before_and_after()
    {
        var (owner, brandId) = await WorkspaceAsync();

        await PutAsync(owner.Browser, brandId, ("connection.problem", false));

        var entry = await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId);
        Assert.Equal("notifications.email_settings_changed", entry.GetProperty("action").GetString());
        Assert.Equal("user", entry.GetProperty("targetType").GetString());
        Assert.Equal(owner.UserId.ToString(), entry.GetProperty("targetId").GetString());
        Assert.True(EmailOn(entry.GetProperty("before"), "connection.problem"));
        Assert.False(EmailOn(entry.GetProperty("after"), "connection.problem"));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(Member Owner, Guid BrandId)> WorkspaceAsync()
    {
        var owner = await Workspaces.SignInAsync(_api);
        return (owner, await Workspaces.CreateAsync(owner.Browser));
    }

    private static Task<JsonElement> GetAsync(HttpClient browser, Guid brandId) =>
        browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/notifications/email-settings", Token);

    private static Task<HttpResponseMessage> PutAsync(HttpClient browser, Guid brandId, params (string Kind, bool Email)[] changes) =>
        browser.PutAsJsonAsync(
            $"/api/brands/{brandId}/notifications/email-settings",
            new { kinds = changes.Select(change => new { kind = change.Kind, email = change.Email }) },
            Token);

    private static string[] Kinds(JsonElement settings) =>
        settings.GetProperty("kinds").EnumerateArray().Select(kind => kind.GetProperty("kind").GetString()!).ToArray();

    private static bool EmailOn(JsonElement settings, string kind) =>
        settings.GetProperty("kinds").EnumerateArray().Single(row => row.GetProperty("kind").GetString() == kind).GetProperty("email").GetBoolean();
}
