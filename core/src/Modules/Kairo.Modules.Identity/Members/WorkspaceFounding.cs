using Kairo.Contracts.Identity;
using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Members;

internal sealed class WorkspaceFounding(IdentityDbContext db, TimeProvider time) : IWorkspaceFounding
{
    public async Task FoundAsync(Guid brandId, Guid founderId, string currency, CancellationToken cancellationToken)
    {
        db.Memberships.Add(new Membership
        {
            BrandId = brandId,
            UserId = founderId,
            Roles = BrandRoles.All,
            JoinedAt = time.GetUtcNow(),
        });

        foreach (var duty in Enum.GetValues<ApprovalDuty>())
        {
            db.DutyAssignments.Add(new DutyAssignment { BrandId = brandId, Duty = duty, UserId = founderId });
        }

        db.BudgetLimits.Add(new BudgetLimit { BrandId = brandId, Currency = currency });
        await db.SaveChangesAsync(cancellationToken);
    }
}
