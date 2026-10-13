using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Notifications.Delivery;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Notifications;

public sealed class EmailLeaseTests(SqlServer sqlServer) : IAsyncLifetime
{
    private readonly ScriptedEmailSender _sender = new();
    private string _connectionString = "";
    private KairoApiFactory _api = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_connectionString, services => services.AddSingleton<IEmailSender>(_sender));
    }

    [Fact]
    public async Task A_claim_another_sender_took_over_is_left_to_that_sender()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        var theirs = _api.Clock.GetUtcNow() + TimeSpan.FromMinutes(10);
        _sender.OnSend = async _ =>
        {
            await NotificationRows.ExecuteAsync(
                _connectionString,
                "UPDATE notifications.OutboundEmails SET ClaimedUntil = @until WHERE BrandId = @brand",
                ("@until", theirs),
                ("@brand", brandId));
            return SendResult.Sent;
        };

        await SendDueAsync(Token);

        var email = Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId));
        Assert.Equal(("Pending", (DateTimeOffset?)theirs, (DateTimeOffset?)null), (email.Status, email.ClaimedUntil, email.SentAt));
    }

    [Fact]
    public async Task A_row_whose_lease_is_nearly_over_is_released_unsent()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        _sender.OnSend = _ =>
        {
            _api.Clock.Advance(EmailDispatcher.Lease - TimeSpan.FromSeconds(30));
            return Task.FromResult(SendResult.Sent);
        };

        Assert.Equal(2, await SendDueAsync(Token));

        Assert.Equal(1, _sender.Sends);
        var emails = await NotificationRows.EmailsAsync(_connectionString, brandId);
        Assert.Equal("Sent", emails[0].Status);
        Assert.Equal(("Pending", 0, (DateTimeOffset?)null), (emails[1].Status, emails[1].Attempts, emails[1].ClaimedUntil));
    }

    [Fact]
    public async Task A_sent_email_is_recorded_even_when_the_loop_is_stopping()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Token);
        _sender.OnSend = async _ =>
        {
            await stopping.CancelAsync();
            return SendResult.Sent;
        };

        try
        {
            await SendDueAsync(stopping.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal("Sent", Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId)).Status);
    }

    [Fact]
    public void Gmail_calls_give_up_well_inside_the_lease()
    {
        var client = _api.Services.GetRequiredService<IHttpClientFactory>().CreateClient("kairo-gmail");

        Assert.Equal(EmailDispatcher.SendTimeout, client.Timeout);
        Assert.True(EmailDispatcher.SendTimeout <= TimeSpan.FromSeconds(20));
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private async Task<(Member Owner, Guid BrandId)> WorkspaceAsync()
    {
        var owner = await Workspaces.SignInAsync(_api);
        return (owner, await Workspaces.CreateAsync(owner.Browser));
    }

    private async Task<int> SendDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().SendDueAsync(cancellationToken);
    }

    private sealed class ScriptedEmailSender : IEmailSender
    {
        public Func<EmailMessage, Task<SendResult>> OnSend { get; set; } = _ => Task.FromResult(SendResult.Sent);

        public int Sends { get; private set; }

        public async Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Sends++;
            return await OnSend(message);
        }
    }
}
