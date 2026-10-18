using System.Net;
using Kairo.Contracts.Notifications;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Notifications.Delivery;
using Kairo.Modules.Notifications.Publishing;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Kairo.IntegrationTests.Notifications;

public sealed class OperationsAlertTests(SqlServer sqlServer) : IAsyncLifetime
{
    private const string Recipient = "operations@acme.example";
    private string _database = "";
    private KairoApiFactory _api = null!;
    private Guid _brand;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database, settings: new Dictionary<string, string>(GmailSenderTests.SenderSettings)
        {
            [OperationsAlerts.RecipientSetting] = Recipient,
        });
        _api.GoogleApis.AllowRefreshToken(GmailSenderTests.SenderRefreshToken);
        var owner = await Workspaces.SignInAsync(_api);
        _brand = await Workspaces.CreateAsync(owner.Browser);
    }

    [Theory]
    [InlineData("launch.failed")]
    [InlineData("stage.failed_twice")]
    [InlineData("mass_pause.held")]
    [InlineData("feed.stale")]
    [InlineData("connection.expired")]
    public async Task Each_critical_event_reaches_operations_with_only_its_safe_identifiers(string kind)
    {
        var alert = new OperationsAlert(Guid.NewGuid(), kind, Guid.NewGuid(), Guid.NewGuid());
        await QueueAsync(_api, _brand, alert);
        Assert.Equal(1, await SendAsync(_api));
        var message = Assert.Single(_api.GoogleApis.SentMail);
        Assert.Equal(Recipient, Assert.Single(message.To.Mailboxes).Address);
        Assert.Equal("Kairo operations: " + kind, message.Subject);
        Assert.Contains(_brand.ToString(), message.TextBody);
        Assert.Contains(alert.CampaignId.ToString()!, message.TextBody);
        Assert.Contains(alert.RunId.ToString()!, message.TextBody);
        Assert.Equal("Sent", Assert.Single(await RowsAsync()).Status);
        Assert.Equal(0, await SendAsync(_api));
    }

    [Fact]
    public async Task Concurrent_duplicate_events_make_one_durable_email_and_distinct_events_exceed_twenty_per_day()
    {
        var alert = new OperationsAlert(Guid.NewGuid(), "launch.failed");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => QueueAsync(_api, _brand, alert)));
        Assert.Single(await RowsAsync());
        foreach (var _ in Enumerable.Range(0, 24))
            await QueueAsync(_api, _brand, new OperationsAlert(Guid.NewGuid(), "launch.failed"));
        Assert.Equal(25, (await RowsAsync()).Count);
        Assert.Equal(10, await SendAsync(_api));
        Assert.Equal(10, await SendAsync(_api));
        Assert.Equal(5, await SendAsync(_api));
        Assert.Equal(25, _api.GoogleApis.SentMail.Count());
        Assert.All(await RowsAsync(), row => Assert.Equal("Sent", row.Status));
    }

    [Fact]
    public async Task The_same_event_key_in_two_brands_remains_separate()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var other = await Workspaces.CreateAsync(owner.Browser);
        var alert = new OperationsAlert(Guid.NewGuid(), "mass_pause.held");
        await Task.WhenAll(QueueAsync(_api, _brand, alert), QueueAsync(_api, other, alert));
        Assert.Single(await RowsAsync());
        Assert.Single(await NotificationRows.EmailsAsync(_database, other, "Operations"));
    }

    [Fact]
    public async Task A_rolled_back_business_transaction_cannot_send_an_operations_email()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        await database.UseBrandAsync(_brand, Token);
        await database.BeginTransactionAsync(Token);
        await scope.ServiceProvider.GetRequiredService<IOperationsAlerts>().QueueAsync(new OperationsAlert(Guid.NewGuid(), "launch.failed"), Token);
        await database.RollbackAsync(Token);
        Assert.Empty(await RowsAsync());
        Assert.Equal(0, await SendAsync(_api));
    }

    [Fact]
    public async Task Provider_quota_failures_retain_the_event_and_retry_without_creating_another_email()
    {
        await QueueAsync(_api, _brand, new OperationsAlert(Guid.NewGuid(), "launch.failed"));
        _api.GoogleApis.GmailSend = HttpStatusCode.TooManyRequests;
        Assert.Equal(1, await SendAsync(_api));
        Assert.Equal("Pending", Assert.Single(await RowsAsync()).Status);
        _api.GoogleApis.GmailSend = HttpStatusCode.OK;
        _api.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await SendAsync(_api));
        Assert.Equal(("Sent", 2), (Assert.Single(await RowsAsync()).Status, Assert.Single(await RowsAsync()).Attempts));
    }

    [Fact]
    public async Task An_unconfigured_sender_cannot_count_as_delivered()
    {
        await QueueAsync(_api, _brand, new OperationsAlert(Guid.NewGuid(), "launch.failed"));
        await using var unconfigured = new KairoApiFactory(_database);
        Assert.Equal(1, await SendAsync(unconfigured));
        var email = Assert.Single(await RowsAsync());
        Assert.Equal("Pending", email.Status);
        Assert.Null(email.SentAt);
        Assert.Contains("not configured", email.LastError);
    }

    [Fact]
    public async Task Missing_workspace_and_unknown_event_kinds_are_rejected()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        var alerts = scope.ServiceProvider.GetRequiredService<IOperationsAlerts>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => alerts.QueueAsync(new OperationsAlert(Guid.NewGuid(), "launch.failed"), Token));
        await Assert.ThrowsAsync<ArgumentException>(() => alerts.QueueAsync(new OperationsAlert(Guid.NewGuid(), "private text"), Token));
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task An_invalid_operations_mailbox_cannot_be_queued()
    {
        _api.Services.GetRequiredService<IConfiguration>()[OperationsAlerts.RecipientSetting] = "Name <user@example.com>";
        await Assert.ThrowsAsync<InvalidOperationException>(() => QueueAsync(_api, _brand, new OperationsAlert(Guid.NewGuid(), "launch.failed")));
        Assert.Empty(await RowsAsync());
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private Task<IReadOnlyList<EmailRow>> RowsAsync() => NotificationRows.EmailsAsync(_database, _brand, "Operations");

    private static async Task QueueAsync(KairoApiFactory api, Guid brand, OperationsAlert alert)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand, Token);
        await scope.ServiceProvider.GetRequiredService<IOperationsAlerts>().QueueAsync(alert, Token);
    }

    private static async Task<int> SendAsync(KairoApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().SendDueAsync(Token);
    }
}

internal static class OperationsMail
{
    public static void Enable(params IServiceProvider[] hosts)
    {
        foreach (var host in hosts)
            host.GetRequiredService<IConfiguration>()[OperationsAlerts.RecipientSetting] = "operations@acme.example";
    }
}
