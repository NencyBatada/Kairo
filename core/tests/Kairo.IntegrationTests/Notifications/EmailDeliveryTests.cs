using System.Net;
using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Notifications.Delivery;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Notifications;

public sealed class EmailDeliveryTests(SqlServer sqlServer) : IAsyncLifetime
{
    private string _connectionString = "";
    private KairoApiFactory _api = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_connectionString, settings: GmailSenderTests.SenderSettings);
        _api.GoogleApis.AllowRefreshToken(GmailSenderTests.SenderRefreshToken);
    }

    [Fact]
    public async Task A_due_email_is_sent_to_the_recipient_with_a_link_to_act()
    {
        var handle = Workspaces.NewHandle();
        var (owner, brandId) = await WorkspaceAsync(handle);
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));

        Assert.Equal(1, await SendDueAsync(_api));

        var mail = Assert.Single(_api.GoogleApis.SentMail);
        Assert.Equal(owner.Account.Email, Assert.Single(mail.To.Mailboxes).Address);
        Assert.Equal("Google Ads needs reconnecting", mail.Subject);
        Assert.Contains($"{KairoApiFactory.PublicOrigin}/{handle}/settings/connections", mail.HtmlBody);
        var email = Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId));
        Assert.Equal(("Sent", 1, (DateTimeOffset?)null, (string?)null), (email.Status, email.Attempts, email.ClaimedUntil, email.LastError));
        Assert.Equal(_api.Clock.GetUtcNow(), email.SentAt);
    }

    [Fact]
    public async Task A_sent_email_is_never_sent_again()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        await SendDueAsync(_api);

        _api.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, await SendDueAsync(_api));
        Assert.Single(_api.GoogleApis.SentMail);
    }

    [Fact]
    public async Task Without_a_sender_the_email_is_logged_instead()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        await using var unconfigured = new KairoApiFactory(_connectionString);

        Assert.Equal(1, await SendDueAsync(unconfigured));

        Assert.Equal("Logged", Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId)).Status);
        Assert.Empty(_api.GoogleApis.SentMail);
        Assert.Empty(unconfigured.GoogleApis.Requests);
    }

    [Fact]
    public async Task A_failure_that_may_pass_is_retried_on_the_schedule_and_the_sixth_gives_up()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        _api.GoogleApis.GmailSend = HttpStatusCode.InternalServerError;

        foreach (var wait in new[] { 1, 5, 30, 120, 360 }.Select(minutes => TimeSpan.FromMinutes(minutes)))
        {
            Assert.Equal(1, await SendDueAsync(_api));
            var pending = Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId));
            Assert.Equal(("Pending", "Gmail answered 500."), (pending.Status, pending.LastError));
            Assert.Equal(_api.Clock.GetUtcNow() + wait, pending.NextAttemptAt);

            _api.Clock.Advance(wait - TimeSpan.FromSeconds(1));
            Assert.Equal(0, await SendDueAsync(_api));
            _api.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(1, await SendDueAsync(_api));
        var failed = Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId));
        Assert.Equal(("Failed", 6, (DateTimeOffset?)null), (failed.Status, failed.Attempts, failed.SentAt));
        _api.GoogleApis.GmailSend = HttpStatusCode.OK;
        _api.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, await SendDueAsync(_api));
    }

    [Fact]
    public async Task A_rejected_message_fails_at_once()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        _api.GoogleApis.GmailSend = HttpStatusCode.BadRequest;

        await SendDueAsync(_api);

        var email = Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId));
        Assert.Equal(("Failed", 1, "Gmail answered 400."), (email.Status, email.Attempts, email.LastError));
    }

    [Fact]
    public async Task A_recipient_who_left_the_workspace_is_skipped()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await NotificationRows.ExecuteAsync(_connectionString, "DELETE FROM notifications.OutboundEmails WHERE Type = N'Invitation'");
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(marketer.UserId)));
        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{marketer.UserId}", Token)).EnsureSuccessStatusCode();

        Assert.Equal(1, await SendDueAsync(_api));

        Assert.Equal("Skipped", Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId)).Status);
        Assert.Empty(_api.GoogleApis.SentMail);
    }

    [Fact]
    public async Task The_email_uses_the_current_address_and_handle()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        var newHandle = Workspaces.NewHandle();
        await NotificationRows.ExecuteAsync(_connectionString, "UPDATE [identity].Users SET Email = @email WHERE Id = @id", ("@email", "priya.new@acme.example"), ("@id", owner.UserId));
        await NotificationRows.ExecuteAsync(_connectionString, "UPDATE brands.Brands SET Handle = @handle WHERE Id = @id", ("@handle", newHandle), ("@id", brandId));

        await SendDueAsync(_api);

        var mail = Assert.Single(_api.GoogleApis.SentMail);
        Assert.Equal("priya.new@acme.example", Assert.Single(mail.To.Mailboxes).Address);
        Assert.Contains($"{KairoApiFactory.PublicOrigin}/{newHandle}/settings/connections", mail.HtmlBody);
    }

    [Fact]
    public async Task Two_senders_at_once_send_each_email_once()
    {
        var (owner, brandId) = await WorkspaceAsync();
        for (var i = 0; i < 15; i++)
        {
            await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        }

        var claimed = 0;
        int[] round;
        do
        {
            round = await Task.WhenAll(SendDueAsync(_api), SendDueAsync(_api));
            claimed += round.Sum();
        }
        while (round.Sum() > 0);

        Assert.Equal(15, claimed);
        Assert.Equal(15, _api.GoogleApis.SentMail.Count);
        Assert.All(await NotificationRows.EmailsAsync(_connectionString, brandId), email => Assert.Equal(("Sent", 1), (email.Status, email.Attempts)));
    }

    [Fact]
    public async Task A_claim_whose_lease_ran_out_is_claimed_again()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        await NotificationRows.ExecuteAsync(
            _connectionString,
            "UPDATE notifications.OutboundEmails SET Attempts = 1, ClaimedUntil = @until WHERE BrandId = @brand",
            ("@until", _api.Clock.GetUtcNow() + EmailDispatcher.Lease),
            ("@brand", brandId));

        Assert.Equal(0, await SendDueAsync(_api));
        _api.Clock.Advance(EmailDispatcher.Lease + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await SendDueAsync(_api));

        var email = Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId));
        Assert.Equal(("Sent", 2), (email.Status, email.Attempts));
    }

    [Fact]
    public async Task At_most_ten_emails_are_claimed_at_a_time()
    {
        var (owner, brandId) = await WorkspaceAsync();
        for (var i = 0; i < 12; i++)
        {
            await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        }

        Assert.Equal(EmailDispatcher.BatchSize, await SendDueAsync(_api));
        Assert.Equal(2, await SendDueAsync(_api));
    }

    [Fact]
    public async Task The_loop_in_the_API_sends_due_emails_every_five_seconds()
    {
        await using var api = new KairoApiFactory(
            _connectionString,
            settings: new Dictionary<string, string>(GmailSenderTests.SenderSettings) { ["Kairo:Notifications:DeliveryLoop"] = "on" });
        api.GoogleApis.AllowRefreshToken(GmailSenderTests.SenderRefreshToken);
        var owner = await Workspaces.SignInAsync(api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        await Notifying.NotifyAsync(api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        for (var step = 0; step < 100 && api.GoogleApis.GmailMessages.IsEmpty; step++)
        {
            api.Clock.Advance(EmailDelivery.Interval);
            await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
        }

        Assert.Single(api.GoogleApis.SentMail);
        Assert.Equal("Sent", Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId)).Status);
    }

    [Fact]
    public async Task The_test_hosts_leave_the_loop_off()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));

        for (var step = 0; step < 10; step++)
        {
            _api.Clock.Advance(EmailDelivery.Interval);
            await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
        }

        Assert.Equal("Pending", Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId)).Status);
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private async Task<(Member Owner, Guid BrandId)> WorkspaceAsync(string? handle = null)
    {
        var owner = await Workspaces.SignInAsync(_api);
        return (owner, await Workspaces.CreateAsync(owner.Browser, handle));
    }

    private static async Task<int> SendDueAsync(KairoApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().SendDueAsync(Token);
    }
}
