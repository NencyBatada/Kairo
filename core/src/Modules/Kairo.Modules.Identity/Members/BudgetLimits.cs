using Kairo.Contracts.Identity;
using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Members;

internal sealed class BudgetLimits(IdentityDbContext db, KairoDatabase database) : IBudgetLimit
{
    public async Task<BudgetCap> GetAsync(Guid brandId, CancellationToken cancellationToken)
    {
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            var limit = await db.BudgetLimits.AsNoTracking().SingleOrDefaultAsync(row => row.BrandId == brandId, cancellationToken);
            return new BudgetCap(limit?.Amount, limit?.Currency ?? "");
        }
    }
}
