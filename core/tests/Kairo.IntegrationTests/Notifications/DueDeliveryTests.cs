using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Notifications.Delivery;
using Kairo.Modules.Notifications.Scheduling;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Notifications;

public sealed class DueDeliveryTests(SqlServer sqlServer) : IAsyncLifetime
{
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);
    private static readonly DateTimeOffset SundayNight = new(2030, 1, 6, 22, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MondayMorning = new(2030, 1, 7, 9, 30, 0, Ist);

    private string _connectionString = "";
    private KairoApiFactory _api = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_connectionString, settings: GmailSenderTests.SenderSettings);
        _api.GoogleApis.AllowRefreshToken(GmailSenderTests.SenderRefreshToken);
        _api.Clock.SetUtcNow(SundayNight);
    }

    [Fact]
    public async Task An_unresolved_approval_gets_one_reminder_after_four_working_hours()
    {
        _api.Clock.SetUtcNow(MondayMorning);
        var (owner, brandId) = await WorkspaceAsync();
        var approver = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "approver");
        await NotifyAsync(brandId, Approval(approver.UserId));

        var original = Assert.Single(await NotificationsAsync(brandId));
        Assert.Equal(new DateTimeOffset(2030, 1, 7, 13, 30, 0, Ist), original.RemindAt);

        _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 7, 13, 29, 0, Ist));
        Assert.Equal(0, (await RunDueAsync()).Reminders);

        _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 7, 13, 30, 0, Ist));
        Assert.Equal(1, (await RunDueAsync()).Reminders);

        var rows = await NotificationsAsync(brandId);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal((approver.UserId, "approval.needed", "approval:brief-1", "/campaigns/1/approval", (DateTimeOffset?)null), (row.UserId, row.Kind, row.Subject, row.Link, row.RemindAt)));
        Assert.Equal("Reminder: Approve the brief for Diwali", rows[1].Title);
        Assert.Equal(_api.Clock.GetUtcNow(), rows[1].CreatedAt);
        var emails = await NotificationRows.EmailsAsync(_connectionString, brandId);
        Assert.Equal(rows.Select(row => row.Id), emails.Select(email => email.NotificationId));
        Assert.Equal(2, (await OutboxRows.ReadAsync(_connectionString, brandId)).Count(row => row.Type == "notification.created"));

        _api.Clock.Advance(TimeSpan.FromDays(3));
        Assert.Equal(0, (await RunDueAsync()).Reminders);
        Assert.Equal(2, (await NotificationsAsync(brandId)).Count);
    }

    [Fact]
    public async Task A_friday_afternoon_approval_is_reminded_on_monday_morning()
    {
        _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 11, 16, 0, 0, Ist));
        var (owner, brandId) = await WorkspaceAsync();

        await NotifyAsync(brandId, Approval(owner.UserId));

        Assert.Equal(new DateTimeOffset(2030, 1, 14, 11, 0, 0, Ist), Assert.Single(await NotificationsAsync(brandId)).RemindAt);
    }

    [Fact]
    public async Task Resolving_an_approval_stops_its_reminder_and_clears_it_for_everyone()
    {
        _api.Clock.SetUtcNow(MondayMorning);
        var (owner, brandId) = await WorkspaceAsync();
        var approver = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "approver");
        await NotifyAsync(brandId, Approval(owner.UserId, approver.UserId));
        await NotifyAsync(brandId, Approval(owner.UserId) with { Subject = "approval:brief-2" });
        _api.Clock.Advance(TimeSpan.FromMinutes(10));

        await ResolveAsync(brandId, "approval:brief-1");
        await ResolveAsync(brandId, "approval:unknown");

        var rows = await NotificationsAsync(brandId);
        var resolved = rows.Where(row => row.Subject == "approval:brief-1").ToList();
        Assert.Equal(2, resolved.Count);
        Assert.All(resolved, row => Assert.Equal(
            ((DateTimeOffset?)_api.Clock.GetUtcNow(), (DateTimeOffset?)_api.Clock.GetUtcNow(), (DateTimeOffset?)null),
            (row.ResolvedAt, row.ReadAt, row.RemindAt)));
        var other = Assert.Single(rows, row => row.Subject == "approval:brief-2");
        Assert.Equal(((DateTimeOffset?)null, (DateTimeOffset?)null), (other.ResolvedAt, other.ReadAt));
        var emails = await NotificationRows.EmailsAsync(_connectionString, brandId);
        Assert.All(emails.Where(email => resolved.Any(row => row.Id == email.NotificationId)), email => Assert.Equal("Skipped", email.Status));
        Assert.Equal("Pending", Assert.Single(emails, email => email.NotificationId == other.Id).Status);
        var read = Assert.Single(await OutboxRows.ReadAsync(_connectionString, brandId), row => row.Type == "notifications.read");
        Assert.Equal(new[] { owner.UserId, approver.UserId }.Order(), JsonSerializer.Deserialize<Guid[]>(read.AudienceUsers!)!.Order());

        _api.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, (await RunDueAsync()).Reminders);
        Assert.DoesNotContain(await NotificationsAsync(brandId), row => row.Title.StartsWith("Reminder:", StringComparison.Ordinal) && row.Subject == "approval:brief-1");
    }

    [Fact]
    public async Task Resolving_a_reminded_approval_resolves_the_reminder_too()
    {
        _api.Clock.SetUtcNow(MondayMorning);
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyAsync(brandId, Approval(owner.UserId));
        _api.Clock.Advance(TimeSpan.FromDays(1));
        await RunDueAsync();

        await ResolveAsync(brandId, "approval:brief-1");

        Assert.All(await NotificationsAsync(brandId), row => Assert.NotNull(row.ResolvedAt));
    }

    [Fact]
    public async Task Stock_pauses_are_held_then_delivered_as_one_grouped_notification_each_hour()
    {
        _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 7, 4, 0, 0, TimeSpan.Zero));
        var (owner, brandId) = await WorkspaceAsync();
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        foreach (var (minutes, product) in new[] { (5, "Red sneakers"), (20, "Blue boots"), (40, "Green sandals") })
        {
            _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 7, 4, minutes, 0, TimeSpan.Zero));
            await NotifyAsync(brandId, Stock(marketer.UserId, product) with { Link = $"/campaigns/{minutes}" });
        }

        Assert.Empty(await NotificationsAsync(brandId));
        Assert.Equal(3, (await NotificationRows.HeldAsync(_connectionString, brandId)).Count);
        Assert.DoesNotContain(await OutboxRows.ReadAsync(_connectionString, brandId), row => row.Type == "notification.created");

        _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 7, 4, 59, 0, TimeSpan.Zero));
        Assert.Equal(0, (await RunDueAsync()).Groups);

        _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 7, 5, 0, 0, TimeSpan.Zero));
        Assert.Equal(1, (await RunDueAsync()).Groups);

        var grouped = Assert.Single(await NotificationsAsync(brandId));
        Assert.Equal((marketer.UserId, "stock.pause_changed", "Ad paused or resumed for stock: 3 updates", "/campaigns"), (grouped.UserId, grouped.Kind, grouped.Title, grouped.Link));
        Assert.Equal("Paused ads for Red sneakers\nPaused ads for Blue boots\nPaused ads for Green sandals", grouped.Body);
        Assert.Empty(await NotificationRows.HeldAsync(_connectionString, brandId));
        Assert.Empty(await NotificationRows.EmailsAsync(_connectionString, brandId));
        Assert.Single(await OutboxRows.ReadAsync(_connectionString, brandId), row => row.Type == "notification.created");

        Assert.Equal(0, (await RunDueAsync()).Groups);
        Assert.Single(await NotificationsAsync(brandId));
    }

    [Fact]
    public async Task One_held_update_arrives_as_it_is_and_long_groups_list_the_first_five()
    {
        _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 7, 4, 0, 0, TimeSpan.Zero));
        var (owner, brandId) = await WorkspaceAsync();
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await NotifyAsync(brandId, Stock(owner.UserId, "Red sneakers"));
        for (var i = 1; i <= 7; i++)
        {
            await NotifyAsync(brandId, Stock(marketer.UserId, $"Shoe {i}") with { Link = "/campaigns/7" });
        }

        _api.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, (await RunDueAsync()).Groups);

        var rows = await NotificationsAsync(brandId);
        var single = Assert.Single(rows, row => row.UserId == owner.UserId);
        Assert.Equal(("Paused ads for Red sneakers", "Kairo paused 2 ads.", "/campaigns/1"), (single.Title, single.Body, single.Link));
        var grouped = Assert.Single(rows, row => row.UserId == marketer.UserId);
        Assert.Equal("Ad paused or resumed for stock: 7 updates", grouped.Title);
        Assert.Equal("/campaigns/7", grouped.Link);
        Assert.Equal("Paused ads for Shoe 1\nPaused ads for Shoe 2\nPaused ads for Shoe 3\nPaused ads for Shoe 4\nPaused ads for Shoe 5\nAnd 2 more.", grouped.Body);
    }

    [Fact]
    public async Task New_top_opportunities_show_at_once_and_are_emailed_in_one_daily_digest()
    {
        _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 7, 5, 0, 0, Ist));
        var handle = Workspaces.NewHandle();
        var (owner, brandId) = await WorkspaceAsync(handle);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await NotifyAsync(brandId, Opportunity(marketer.UserId, "Monsoon footwear is rising", "/opportunities/1"));
        await NotifyAsync(brandId, Opportunity(marketer.UserId, "School shoes are rising", "/opportunities/2"));

        var waiting = await NotificationsAsync(brandId);
        Assert.Equal(2, waiting.Count);
        Assert.All(waiting, row => Assert.True(row.AwaitsDigest));
        Assert.Empty(await NotificationRows.EmailsAsync(_connectionString, brandId));
        Assert.Equal(2, (await OutboxRows.ReadAsync(_connectionString, brandId)).Count(row => row.Type == "notification.created"));

        _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 7, 6, 59, 0, Ist));
        Assert.Equal(0, (await RunDueAsync()).Digests);

        _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 7, 7, 0, 0, Ist));
        Assert.Equal(1, (await RunDueAsync()).Digests);

        var digest = Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId, "Digest"));
        Assert.Equal((marketer.UserId, "Pending"), (digest.UserId, digest.Status));
        Assert.All(await NotificationsAsync(brandId), row => Assert.Equal((false, (long?)digest.Id), (row.AwaitsDigest, row.DigestEmailId)));

        await SendDueAsync();
        var mail = Assert.Single(_api.GoogleApis.SentMail, sent => sent.Subject?.StartsWith("Your daily digest", StringComparison.Ordinal) == true);
        Assert.Equal(marketer.Account.Email, Assert.Single(mail.To.Mailboxes).Address);
        Assert.Equal("Your daily digest: 2 updates", mail.Subject);
        Assert.Contains("Monsoon footwear is rising", mail.HtmlBody);
        Assert.Contains($"{KairoApiFactory.PublicOrigin}/{handle}/opportunities/2", mail.HtmlBody);
        Assert.Equal("Sent", Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId, "Digest")).Status);

        _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 7, 8, 0, 0, Ist));
        await NotifyAsync(brandId, Opportunity(marketer.UserId, "Rain jackets are rising", "/opportunities/3"));
        Assert.Equal(0, (await RunDueAsync()).Digests);

        _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 8, 7, 15, 0, Ist));
        Assert.Equal(1, (await RunDueAsync()).Digests);
        var second = (await NotificationRows.EmailsAsync(_connectionString, brandId, "Digest"))[1];
        Assert.Equal("Rain jackets are rising", Assert.Single(await NotificationsAsync(brandId), row => row.DigestEmailId == second.Id).Title);
    }

    [Fact]
    public async Task Turning_off_the_email_before_the_digest_goes_out_skips_it()
    {
        _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 7, 5, 0, 0, Ist));
        var (owner, brandId) = await WorkspaceAsync();
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await NotifyAsync(brandId, Opportunity(marketer.UserId, "Monsoon footwear is rising", "/opportunities/1"));

        var saved = await marketer.Browser.PutAsJsonAsync(
            $"/api/brands/{brandId}/notifications/email-settings",
            new { kinds = new[] { new { kind = "opportunities.ready", email = false } } },
            Token);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        _api.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 7, 7, 30, 0, Ist));

        Assert.Equal(0, (await RunDueAsync()).Digests);
        Assert.Empty(await NotificationRows.EmailsAsync(_connectionString, brandId, "Digest"));
        Assert.False(Assert.Single(await NotificationsAsync(brandId)).AwaitsDigest);

        await NotifyAsync(brandId, Opportunity(marketer.UserId, "School shoes are rising", "/opportunities/2"));
        Assert.All(await NotificationsAsync(brandId), row => Assert.False(row.AwaitsDigest));
    }

    [Fact]
    public async Task An_invited_person_is_emailed_a_link_to_join()
    {
        _api.Clock.SetUtcNow(MondayMorning);
        var owner = await Workspaces.SignInAsync(_api, "Neha Rao");
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var address = $"priya-{Guid.NewGuid():N}@example.com";

        await Workspaces.InviteAsync(owner.Browser, brandId, address, "marketer", "approver");

        var queued = Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId, "Invitation"));
        Assert.Equal((address, Guid.Empty, 0L, "Pending"), (queued.Address, queued.UserId, queued.NotificationId, queued.Status));
        Assert.Equal(1, await SendDueAsync());
        var mail = Assert.Single(_api.GoogleApis.SentMail);
        Assert.Equal(address, Assert.Single(mail.To.Mailboxes).Address);
        Assert.Equal("Neha Rao invited you to Acme Shoes on Kairo", mail.Subject);
        Assert.Contains($"href=\"{KairoApiFactory.PublicOrigin}/welcome\"", mail.HtmlBody);
        Assert.Contains("with the Marketer and Approver roles", mail.TextBody);
        Assert.Contains("21 January 2030", mail.TextBody);
        Assert.Equal("Sent", Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId, "Invitation")).Status);
    }

    [Fact]
    public async Task The_worker_delivers_due_notifications_on_its_job_endpoint()
    {
        _api.Clock.SetUtcNow(MondayMorning);
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyAsync(brandId, Approval(owner.UserId));
        await using var worker = new KairoWorkerFactory(_connectionString, _api.GoogleApis, new DateTimeOffset(2030, 1, 7, 14, 0, 0, Ist));

        using var client = worker.CreateClient();
        var response = await client.PostAsync("/jobs/notifications/due", null, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal((1, 0, 1, 0, 0), (
            run.GetProperty("brands").GetInt32(),
            run.GetProperty("failed").GetInt32(),
            run.GetProperty("reminders").GetInt32(),
            run.GetProperty("groups").GetInt32(),
            run.GetProperty("digests").GetInt32()));
        Assert.Equal(2, (await NotificationsAsync(brandId)).Count);
    }

    [Fact]
    public async Task Nothing_due_touches_no_workspace()
    {
        _api.Clock.SetUtcNow(MondayMorning);
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyAsync(brandId, Approval(owner.UserId));

        Assert.Equal(new DueRun(0, 0, 0, 0, 0), await RunDueAsync());
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private static Notification Approval(params Guid[] users) =>
        new("approval.needed", LiveAudience.Users(users), "Approve the brief for Diwali", "Priya asked you to approve it.", "/campaigns/1/approval", "approval:brief-1");

    private static Notification Stock(Guid user, string product) =>
        new("stock.pause_changed", LiveAudience.Users(user), $"Paused ads for {product}", "Kairo paused 2 ads.", "/campaigns/1");

    private static Notification Opportunity(Guid user, string title, string link) =>
        new("opportunities.ready", LiveAudience.Users(user), title, "Searches grew this week.", link);

    private async Task<(Member Owner, Guid BrandId)> WorkspaceAsync(string? handle = null)
    {
        var owner = await Workspaces.SignInAsync(_api);
        return (owner, await Workspaces.CreateAsync(owner.Browser, handle));
    }

    private Task<IReadOnlyList<NotificationRow>> NotificationsAsync(Guid brandId) => NotificationRows.NotificationsAsync(_connectionString, brandId);

    private Task NotifyAsync(Guid brandId, Notification notification) => Notifying.NotifyAsync(_api, brandId, notification);

    private async Task ResolveAsync(Guid brandId, string subject)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        await scope.ServiceProvider.GetRequiredService<INotifications>().ResolveAsync(subject, Token);
    }

    private async Task<DueRun> RunDueAsync()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DueDeliveries>().RunAsync(Token);
    }

    private async Task<int> SendDueAsync()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().SendDueAsync(Token);
    }
}
