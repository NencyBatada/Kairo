using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Members;

internal sealed class BrandAccess(IdentityDbContext db, KairoDatabase database) : IBrandAccess
{
    public async Task<BrandRoles?> FindRolesAsync(Guid brandId, Guid userId, CancellationToken cancellationToken)
    {
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await db.Memberships
                .Where(membership => membership.BrandId == brandId && membership.UserId == userId)
                .Select(membership => (BrandRoles?)membership.Roles)
                .SingleOrDefaultAsync(cancellationToken);
        }
    }
}
