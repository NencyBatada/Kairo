using Kairo.Modules.Brands.Data;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Brands.Connections;

internal sealed class GoogleRevocations(BrandsDbContext db, KairoDatabase database, GoogleOAuth oauth, ILogger<GoogleRevocations> logger)
{
    public async Task RevokeIfUnusedAsync(string subject, string refreshToken, CancellationToken cancellationToken)
    {
        bool inUse;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            inUse = await db.Connections.AnyAsync(
                connection => connection.GoogleSubject == subject && connection.ClientId == null && connection.RefreshToken != null,
                cancellationToken);
        }

        if (!inUse)
        {
            await oauth.RevokeAsync(refreshToken, cancellationToken);
        }
    }

    public Task RevokeAfterCommitAsync(string subject, string refreshToken, CancellationToken cancellationToken) =>
        database.AfterCommitAsync(async _ =>
        {
            try
            {
                await RevokeIfUnusedAsync(subject, refreshToken, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not revoke a Google token after its connection was removed.");
            }
        }, cancellationToken);
}
