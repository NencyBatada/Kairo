namespace Kairo.Contracts.Launch;

public interface ILaunchQueue
{
    Task EnqueueAsync(Guid roomId, CancellationToken cancellationToken);
}
