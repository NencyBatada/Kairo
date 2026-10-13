using Kairo.Contracts.Identity;
using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Members;

internal sealed class MembershipDirectory(IdentityDbContext db, KairoDatabase database) : IMembershipDirectory
{
    public async Task<IReadOnlyList<BrandMembership>> GetForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await db.Memberships
                .Where(membership => membership.UserId == userId)
                .Select(membership => new BrandMembership(membership.BrandId, membership.Roles))
                .ToListAsync(cancellationToken);
        }
    }
}
