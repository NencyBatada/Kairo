using System.Net;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Notifications.Delivery;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace Kairo.IntegrationTests.Notifications;

public sealed class GmailSenderTests(SqlServer sqlServer) : IAsyncDisposable
{
    public const string Sender = "kairo-alerts@acme.example";
    public const string SenderRefreshToken = "sender-refresh-token";

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString, settings: SenderSettings);

    public static IReadOnlyDictionary<string, string> SenderSettings { get; } = new Dictionary<string, string>
    {
        ["Kairo:Notifications:Sender"] = Sender,
        ["Kairo:Notifications:SenderRefreshToken"] = SenderRefreshToken,
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_email_goes_through_Gmail_from_the_sender_mailbox()
    {
        _api.GoogleApis.AllowRefreshToken(SenderRefreshToken);

        var result = await SendAsync(Message());

        Assert.Equal(SendResult.Sent, result);
        var mail = Assert.Single(_api.GoogleApis.SentMail);
        var from = Assert.Single(mail.From.Mailboxes);
        var to = Assert.Single(mail.To.Mailboxes);
        Assert.Equal(("Kairo", Sender), (from.Name, from.Address));
        Assert.Equal(("Priya Shah", "priya@acme.example"), (to.Name, to.Address));
        Assert.Equal("Google Ads needs reconnecting", mail.Subject);
        Assert.Equal("Reconnect Google Ads.", mail.TextBody!.Trim());
        Assert.Equal("<p>Reconnect Google Ads.</p>", mail.HtmlBody!.Trim());
    }

    [Fact]
    public async Task Markup_and_non_english_text_arrive_as_plain_text()
    {
        _api.GoogleApis.AllowRefreshToken(SenderRefreshToken);

        await SendAsync(Message() with { Subject = "<b>Pacing</b> ₹ अलर्ट", Text = "बजट ₹ <b>" });

        var mail = Assert.Single(_api.GoogleApis.SentMail);
        Assert.Equal("<b>Pacing</b> ₹ अलर्ट", mail.Subject);
        Assert.Equal("बजट ₹ <b>", mail.TextBody!.Trim());
    }

    [Fact]
    public async Task The_access_token_is_reused_until_shortly_before_it_expires()
    {
        _api.GoogleApis.AllowRefreshToken(SenderRefreshToken);

        await SendAsync(Message());
        _api.Clock.Advance(TimeSpan.FromMinutes(58));
        await SendAsync(Message());

        Assert.Equal(1, _api.GoogleApis.Refreshes);
        Assert.Equal(2, _api.GoogleApis.SentMail.Count);
    }

    [Fact]
    public async Task An_expired_access_token_is_refreshed_before_sending()
    {
        _api.GoogleApis.AllowRefreshToken(SenderRefreshToken);

        await SendAsync(Message());
        _api.Clock.Advance(TimeSpan.FromMinutes(60));
        var result = await SendAsync(Message());

        Assert.Equal(SendResult.Sent, result);
        Assert.Equal(2, _api.GoogleApis.Refreshes);
        Assert.Equal(2, _api.GoogleApis.SentMail.Count);
    }

    [Fact]
    public async Task A_rejected_message_fails_for_good()
    {
        _api.GoogleApis.AllowRefreshToken(SenderRefreshToken);
        _api.GoogleApis.GmailSend = HttpStatusCode.BadRequest;

        var failure = await Assert.ThrowsAsync<EmailSendException>(() => SendAsync(Message()));

        Assert.False(failure.MayPass);
        Assert.Equal("Gmail answered 400.", failure.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Rate_limits_and_outages_may_pass(HttpStatusCode status)
    {
        _api.GoogleApis.AllowRefreshToken(SenderRefreshToken);
        _api.GoogleApis.GmailSend = status;

        var failure = await Assert.ThrowsAsync<EmailSendException>(() => SendAsync(Message()));

        Assert.True(failure.MayPass);
        Assert.Single(_api.GoogleApis.Requests, request => request == "POST gmail.googleapis.com/gmail/v1/users/me/messages/send");
    }

    [Fact]
    public async Task A_refused_access_token_is_dropped_and_may_pass()
    {
        _api.GoogleApis.AllowRefreshToken(SenderRefreshToken);
        _api.GoogleApis.GmailSend = HttpStatusCode.Unauthorized;

        var failure = await Assert.ThrowsAsync<EmailSendException>(() => SendAsync(Message()));
        _api.GoogleApis.GmailSend = HttpStatusCode.OK;
        await SendAsync(Message());

        Assert.True(failure.MayPass);
        Assert.Equal(2, _api.GoogleApis.Refreshes);
    }

    [Fact]
    public async Task A_sender_that_needs_signing_in_again_may_pass()
    {
        var failure = await Assert.ThrowsAsync<EmailSendException>(() => SendAsync(Message()));

        Assert.True(failure.MayPass);
        Assert.Equal("The notification sender mailbox needs signing in again.", failure.Message);
        Assert.Empty(_api.GoogleApis.SentMail);
    }

    [Fact]
    public async Task Without_a_sender_emails_are_only_logged()
    {
        await using var api = new KairoApiFactory(sqlServer.KairoConnectionString);
        await using var scope = api.Services.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(Message(), Token);

        Assert.Equal(SendResult.Logged, result);
        Assert.Empty(api.GoogleApis.Requests);
    }

    [Fact]
    public void The_raw_message_is_base64url_without_unsafe_characters()
    {
        var raw = GmailSender.Build(Message() with { Text = new string('?', 300) }, Sender);

        Assert.DoesNotContain('+', raw);
        Assert.DoesNotContain('/', raw);
        Assert.Equal("Google Ads needs reconnecting", MimeMessage.Load(new MemoryStream(Convert.FromBase64String(raw.Replace('-', '+').Replace('_', '/'))), Token).Subject);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static EmailMessage Message() => new(
        Guid.NewGuid(),
        "connection.problem",
        "priya@acme.example",
        "Priya Shah",
        "Google Ads needs reconnecting",
        "<p>Reconnect Google Ads.</p>",
        "Reconnect Google Ads.");

    private async Task<SendResult> SendAsync(EmailMessage message)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message, Token);
    }
}
