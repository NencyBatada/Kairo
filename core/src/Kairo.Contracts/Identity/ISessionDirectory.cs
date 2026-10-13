namespace Kairo.Contracts.Identity;

public interface ISessionDirectory
{
    Task<bool> IsActiveAsync(string sessionKey, CancellationToken cancellationToken);
}
