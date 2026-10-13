using Kairo.Modules.Brands.Data;

namespace Kairo.Modules.Brands.Connections;

internal sealed class GoogleRevocations(BrandsDbContext db, KairoDatabase database, GoogleOAuth oauth)
{
    public async Task RevokeIfUnusedAsync(string subject, string refreshToken, CancellationToken cancellationToken)
    {
        bool inUse;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            inUse = await db.Connections.AnyAsync(
                connection => connection.GoogleSubject == subject && connection.RefreshToken != null,
                cancellationToken);
        }

        if (!inUse)
        {
            await oauth.RevokeAsync(refreshToken, cancellationToken);
        }
    }
}
