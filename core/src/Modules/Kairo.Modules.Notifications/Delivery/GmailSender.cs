using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MimeKit;

namespace Kairo.Modules.Notifications.Delivery;

internal sealed class GmailSender(IHttpClientFactory http, SenderToken token, IConfiguration configuration) : IEmailSender
{
    private const string SendEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/messages/send";

    public async Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var raw = Build(message, configuration[NotificationSettings.Sender]!);
        var accessToken = await token.GetAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint) { Content = JsonContent.Create(new { raw }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.CreateClient(SenderToken.ClientName).SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return SendResult.Sent;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            token.Forget();
            throw new EmailSendException("Gmail refused the sender's access token.", mayPass: true);
        }

        var status = (int)response.StatusCode;
        throw new EmailSendException($"Gmail answered {status}.", mayPass: response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500);
    }

    public static string Build(EmailMessage message, string sender)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Kairo", sender));
        mime.To.Add(new MailboxAddress(message.ToName, message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.Text, HtmlBody = message.Html }.ToMessageBody();
        using var stream = new MemoryStream();
        mime.WriteTo(stream);
        return Convert.ToBase64String(stream.ToArray()).Replace('+', '-').Replace('/', '_');
    }
}
