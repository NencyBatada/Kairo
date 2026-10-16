namespace Kairo.Contracts.Launch;

public interface ILaunchRecovery
{
    Task<bool> CanReturnAsync(Guid roomId, CancellationToken cancellationToken);
    Task<bool> ReturnAsync(Guid roomId, CancellationToken cancellationToken);
}
