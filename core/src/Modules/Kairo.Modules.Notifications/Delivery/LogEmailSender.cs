using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Notifications.Delivery;

internal sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "No notification sender is set, so an email was only logged: {Kind} to user {UserId}, \"{Subject}\".",
            message.Kind,
            message.UserId,
            message.Subject);
        return Task.FromResult(SendResult.Logged);
    }
}
