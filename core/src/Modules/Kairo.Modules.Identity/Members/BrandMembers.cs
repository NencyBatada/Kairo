using Kairo.Contracts.Identity;
using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Members;

internal sealed class BrandMembers(IdentityDbContext db, KairoDatabase database) : IBrandMembers
{
    public async Task<IReadOnlyList<BrandMember>> ListAsync(CancellationToken cancellationToken)
    {
        if (database.BrandId is null)
        {
            throw new InvalidOperationException("Listing members needs a workspace.");
        }

        return await (
                from membership in db.Memberships
                join user in db.Users on membership.UserId equals user.Id
                orderby user.Name
                select new BrandMember(user.Id, user.Email, user.Name, membership.Roles))
            .ToListAsync(cancellationToken);
    }

    public async Task<BrandMember?> FindAsync(Guid brandId, Guid userId, CancellationToken cancellationToken)
    {
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await (
                    from membership in db.Memberships
                    where membership.BrandId == brandId && membership.UserId == userId
                    join user in db.Users on membership.UserId equals user.Id
                    select new BrandMember(user.Id, user.Email, user.Name, membership.Roles))
                .SingleOrDefaultAsync(cancellationToken);
        }
    }
}
