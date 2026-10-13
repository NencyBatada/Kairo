namespace Kairo.Modules.Notifications.Delivery;

internal sealed record EmailMessage(Guid UserId, string Kind, string To, string ToName, string Subject, string Html, string Text);

internal enum SendResult
{
    Sent,
    Logged,
}

internal interface IEmailSender
{
    Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

internal sealed class EmailSendException(string message, bool mayPass) : Exception(message)
{
    public bool MayPass { get; } = mayPass;
}
