using Kairo.Contracts.Live;

namespace Kairo.Contracts.Notifications;

public interface INotifications
{
    Task NotifyAsync(Notification notification, CancellationToken cancellationToken);
}

public sealed record Notification(string Kind, LiveAudience Audience, string Title, string Body, string Link);
