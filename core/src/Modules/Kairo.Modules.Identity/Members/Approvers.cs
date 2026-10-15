using Kairo.Contracts.Identity;
using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Members;

internal sealed class Approvers(IdentityDbContext db, KairoDatabase database) : IApprovers
{
    public async Task<IReadOnlyList<BrandMember>> ForAsync(Guid brandId, ApprovalDuty duty, CancellationToken cancellationToken)
    {
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await (
                    from assignment in db.DutyAssignments
                    where assignment.BrandId == brandId && assignment.Duty == duty
                    join membership in db.Memberships
                        on new { assignment.BrandId, assignment.UserId } equals new { membership.BrandId, membership.UserId }
                    join user in db.Users on membership.UserId equals user.Id
                    orderby user.Name
                    select new BrandMember(user.Id, user.Email, user.Name, membership.Roles))
                .ToListAsync(cancellationToken);
        }
    }
}
