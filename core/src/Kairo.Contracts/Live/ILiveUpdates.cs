namespace Kairo.Contracts.Live;

public interface ILiveUpdates
{
    Task PublishAsync(LiveEvent liveEvent, CancellationToken cancellationToken);
}

public sealed record LiveEvent(string Type, object Payload, LiveAudience Audience);
