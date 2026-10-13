using Kairo.Contracts.Identity;
using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Sessions;

internal sealed class SessionDirectory(IdentityDbContext db, TimeProvider time) : ISessionDirectory
{
    public Task<bool> IsActiveAsync(string sessionKey, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return db.Sessions.AnyAsync(session => session.Key == sessionKey && session.ExpiresAt > now, cancellationToken);
    }
}
