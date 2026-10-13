namespace Kairo.Contracts.Identity;

public interface IWorkspaceFounding
{
    Task FoundAsync(Guid brandId, Guid founderId, string currency, CancellationToken cancellationToken);
}
